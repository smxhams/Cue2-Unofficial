using Godot;
using System;
using System.Linq;
using Cue2.Shared;
using Cue2.UI.Utilities;
using Godot.Collections;

namespace Cue2.Base.Classes.Devices;

public partial class VideoOutputDevice : Window, IDisposable
{
    private const int DEFAULT_WIDTH = 1920;
    private const int DEFAULT_HEIGHT = 1080;

    /// <summary>
    /// Unique ID for the output device.
    /// </summary>
    public int OutputId { get; set; }

    /// <summary>
    /// Name of the output device.
    /// </summary>
    public string OutputName { get; set; } = "Unnamed Output";

    /// <summary>
    /// Position on the canvas (top-left corner).
    /// </summary>
    public Vector2I CanvasPosition { get; set; } = Vector2I.Zero;

    /// <summary>
    /// Size of the output region on the canvas.
    /// </summary>
    public Vector2I OutputSize { get; set; } = new Vector2I(DEFAULT_WIDTH, DEFAULT_HEIGHT);

    /// <summary>
    /// Sentinel value for a virtual (non-physical) screen output.
    /// </summary>
    public const int VirtualMonitorIndex = -1;

    /// <summary>
    /// Target display monitor index. Use <see cref="VirtualMonitorIndex"/> for Virtual Output.
    /// </summary>
    public int TargetMonitor { get; set; } = VirtualMonitorIndex;

    /// <summary>
    /// Whether this screen is assigned to Virtual Output (no physical display).
    /// </summary>
    public bool IsVirtual => TargetMonitor < 0;

    /// <summary>
    /// Whether the output window is transparent.
    /// </summary>
    public bool OutputTransparent { get; set; } = false;

    /// <summary>
    /// Pixel offset of the output window relative to the target display's origin (home position).
    /// Applied after canvas clipping when placing the window on a physical monitor.
    /// </summary>
    public Vector2I DisplayOffset { get; set; } = Vector2I.Zero;

    /// <summary>
    /// When true, changing one size dimension updates the other to preserve aspect ratio.
    /// </summary>
    public bool KeepAspect { get; set; } = false;
    
    private static int _nextOutputId = 0;

    public static void SetNextOutputId(int id)
    {
        _nextOutputId = id;
    }

    private Control _sceneRoot;

    private TestPattern _testPattern;
    private Dictionary<int, TestPattern> _layerTestPatterns = new();
    
    private PackedScene _displayLayerPackedScene = SceneLoader.LoadPackedScene("uid://dwnssjgckgb8p", out _);
    private Dictionary<Control, int> _activeLayers = new();
    

    /// <summary>
    /// Cached last clipped rectangle to avoid unnecessary updates.
    /// </summary>
    private Rect2 _lastClippedRect = new Rect2(-1, -1, 0, 0);

    /// <summary>
    /// Cached display offset used with <see cref="_lastClippedRect"/> so offset-only changes still refresh.
    /// </summary>
    private Vector2I _lastDisplayOffset = new Vector2I(int.MinValue, int.MinValue);

    /// <summary>
    /// Last applied global window position.
    /// </summary>
    private Vector2I _lastWindowPos = new Vector2I(int.MinValue, int.MinValue);

    /// <summary>
    /// Last applied window size.
    /// </summary>
    private Vector2I _lastWindowSize = new Vector2I(int.MinValue, int.MinValue);

    public VideoOutputDevice()
    {
        OutputId = _nextOutputId++;
        Mode = ModeEnum.Windowed;
        Borderless = true;
        DisplayServer.ScreenSetKeepOn(true);

        InitSceneRoot();
    }

    private void InitSceneRoot()
    {
        // Full-rect root so layer content always fills the Window content viewport.
        // SizeFlags alone do not layout against Window (not a container) — anchors are required.
        _sceneRoot = new Control
        {
            Name = "OutputRoot",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_sceneRoot);
        _sceneRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    /// <summary>
    /// Sets the reference to the parent canvas.
    /// </summary>
    /// <param name="canvas">The canvas this output belongs to.</param>
    public void SetCanvasReference(Canvas canvas)
    {
        UpdateOutputRegion();
    }


    public Control AddLayer(int LayerId)
    {
        var layer = DisplaysManager.GetLayerById(LayerId);
        var displayLayer = _displayLayerPackedScene.Instantiate<Control>();
        // Host = layer rectangle on this screen (not full output). Clip so Fill mode crops.
        displayLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
        displayLayer.ClipContents = true;
        _sceneRoot.AddChild(displayLayer);
        var outputLayer = displayLayer.GetNode<TextureRect>("%LayerOutput");
        ApplyLayerRectToHost(displayLayer, outputLayer, layer);
        _activeLayers[displayLayer] = LayerId;
        ApplyLayerDrawOrder();

        return displayLayer;
    }

    /// <summary>
    /// Applies target-layer stack order so first/highest ZIndex draws on top.
    /// </summary>
    public void ApplyLayerDrawOrder()
    {
        // Assign Control.ZIndex from layer data (higher = on top).
        foreach (var kvp in _activeLayers.ToList())
        {
            var host = kvp.Key;
            if (host == null || !IsInstanceValid(host))
            {
                _activeLayers.Remove(host);
                continue;
            }

            var layer = DisplaysManager.GetLayerById(kvp.Value);
            host.ZIndex = layer?.ZIndex ?? 0;
        }

        // Also reorder children so draw order is deterministic even without z-index.
        // Bottom of stack first, top last (later siblings paint above).
        if (_sceneRoot == null || !IsInstanceValid(_sceneRoot))
            return;

        var ordered = _activeLayers
            .Where(kv => kv.Key != null && IsInstanceValid(kv.Key))
            .OrderBy(kv =>
            {
                var layer = DisplaysManager.GetLayerById(kv.Value);
                return layer?.ZIndex ?? 0;
            })
            .Select(kv => kv.Key)
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
            _sceneRoot.MoveChild(ordered[i], i);
    }

    /// <summary>
    /// Updates the on-screen TextureRect position/size for a target layer (e.g. after canvas editor edits).
    /// Live video playback uses these same rects, so they must move with the layer data.
    /// </summary>
    /// <param name="layerId">Target layer id to refresh on this output.</param>
    public void UpdateLayerDisplayRect(int layerId)
    {
        var layer = DisplaysManager.GetLayerById(layerId);
        if (layer == null)
            return;

        foreach (var kvp in _activeLayers.ToList())
        {
            var host = kvp.Key;
            if (kvp.Value != layerId)
                continue;

            if (host == null || !IsInstanceValid(host))
            {
                _activeLayers.Remove(host);
                continue;
            }

            var outputLayer = host.GetNodeOrNull<TextureRect>("%LayerOutput");
            if (outputLayer == null)
                continue;

            ApplyLayerRectToHost(host, outputLayer, layer);
        }
    }

    /// <summary>
    /// Refreshes display rects for every active layer on this output (after screen canvas position changes).
    /// </summary>
    public void UpdateAllLayerDisplayRects()
    {
        foreach (var layerId in _activeLayers.Values.Distinct().ToList())
            UpdateLayerDisplayRect(layerId);
    }

    /// <summary>
    /// Positions the layer host on this screen and makes LayerOutput fill it so Fit/Fill/Stretch work.
    /// StretchMode is left alone if already set by <see cref="VideoComponent.ApplyTextureLayout"/>.
    /// </summary>
    private void ApplyLayerRectToHost(Control host, TextureRect outputLayer, VideoTargetLayer layer)
    {
        if (host == null || outputLayer == null || layer == null)
            return;

        // Layer rect in output-window space: canvas coords minus this screen's canvas origin.
        host.Position = (Vector2)(layer.CanvasPosition - CanvasPosition);
        host.Size = (Vector2)layer.Size;
        host.ClipContents = true;

        // Texture fills the host; VideoDisplayMode stretch mode maps the frame inside.
        outputLayer.Position = Vector2.Zero;
        outputLayer.Size = host.Size;
        outputLayer.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
    }

    

    /// <summary>
    /// Returns true when this Window has a native DisplayServer window id that can be resized/moved.
    /// Virtual screens never create a usable native window until assigned to a physical output.
    /// </summary>
    private bool TryGetNativeWindowId(out int windowId)
    {
        windowId = -1;
        if (!IsInsideTree() || !GodotObject.IsInstanceValid(this))
            return false;

        try
        {
            windowId = GetWindowId();
            // DisplayServer.InvalidWindowId is -1; also reject ids that are not registered yet.
            if (windowId == DisplayServer.InvalidWindowId || windowId < 0)
                return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Hides a virtual / invalid screen window.
    /// </summary>
    private void HideScreenWindow()
    {
        try
        {
            if (Visible)
                Hide();
        }
        catch
        {
            /* ignore */
        }

        InvalidateGeometryCache();
    }

    private void InvalidateGeometryCache()
    {
        _lastClippedRect = new Rect2(-1, -1, 0, 0);
        _lastDisplayOffset = new Vector2I(int.MinValue, int.MinValue);
        _lastWindowPos = new Vector2I(int.MinValue, int.MinValue);
        _lastWindowSize = new Vector2I(int.MinValue, int.MinValue);
    }

    /// <summary>
    /// True when the intended window rect fully covers the target monitor (no offset / partial size).
    /// Exact match is when Godot/Windows most often promotes borderless windows to ExclusiveFullscreen,
    /// which causes black frame flashes — especially on multi-monitor setups with continuous video present.
    /// </summary>
    private bool IsFullMonitorCoverage(Vector2I windowPos, Vector2I windowSize)
    {
        if (TargetMonitor < 0 || TargetMonitor >= DisplayServer.GetScreenCount())
            return false;

        var monPos = DisplayServer.ScreenGetPosition(TargetMonitor);
        var monSize = DisplayServer.ScreenGetSize(TargetMonitor);
        return windowPos == monPos && windowSize == monSize;
    }

    /// <summary>
    /// Whether the window is currently in ExclusiveFullscreen (DXGI exclusive — black flashes).
    /// </summary>
    private bool IsExclusiveFullscreen() => Mode == ModeEnum.ExclusiveFullscreen;

    /// <summary>
    /// Whether the window is in any fullscreen-like mode we do not want for free placement.
    /// </summary>
    private bool IsFullscreenLike() =>
        Mode == ModeEnum.ExclusiveFullscreen || Mode == ModeEnum.Fullscreen;

    /// <summary>
    /// Forces the window out of ExclusiveFullscreen / Fullscreen into borderless Windowed.
    /// Exclusive mode must never stick on video outputs — it causes black flickering.
    /// </summary>
    /// <remarks>
    /// On Windows, Godot can stick in exclusive unless we briefly leave borderless and nudge size
    /// before setting Mode back to Windowed. Then we reinforce via DisplayServer.
    /// </remarks>
    private void ForceBorderlessWindowed()
    {
        bool wasExclusive = Mode == ModeEnum.ExclusiveFullscreen;

        if (wasExclusive)
        {
            // Proven Godot/Windows path to leave exclusive mode without getting stuck.
            Borderless = false;
            Vector2I s = Size;
            if (s.X > 1)
                Size = new Vector2I(s.X - 1, Mathf.Max(1, s.Y));
        }

        Mode = ModeEnum.Windowed;
        Borderless = true;

        if (TryGetNativeWindowId(out int windowId))
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed, windowId);
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true, windowId);
        }

        if (wasExclusive)
            InvalidateGeometryCache();
    }

    /// <summary>
    /// When a window is sized/positioned exactly to a monitor, Windows/Godot often promotes it to
    /// ExclusiveFullscreen. Expanding width by 1px (off the right edge) breaks the exact match
    /// while still fully covering the display — no visible gap on the target screen.
    /// </summary>
    private static Vector2I AntiExclusivePlacementSize(Vector2I intendedSize, bool fullCoverage)
    {
        if (!fullCoverage || intendedSize.X <= 0)
            return intendedSize;
        return new Vector2I(intendedSize.X + 1, intendedSize.Y);
    }

    /// <summary>
    /// Updates the output to show the correct region of the canvas.
    /// Physical screens are always borderless <b>windowed</b> windows placed via DisplayServer.
    /// </summary>
    /// <remarks>
    /// Never use ExclusiveFullscreen for outputs (black frame flashes). Exact full-monitor geometry
    /// is placed 1px wider so the engine cannot promote the window to exclusive mode.
    /// Partial sizes must stay true Windowed with Window.Size in sync, or the surface goes grey.
    /// </remarks>
    public void UpdateOutputRegion()
    {
        if (OutputSize.X <= 0 || OutputSize.Y <= 0)
        {
            GD.Print("VideoOutputDevice:UpdateOutputRegion - Invalid output size, must be positive.");
            return;
        }

        if (IsVirtual)
        {
            HideScreenWindow();
            return;
        }

        if (TargetMonitor < 0 || TargetMonitor >= DisplayServer.GetScreenCount())
        {
            GD.PrintErr($"VideoOutputDevice:UpdateOutputRegion - Target monitor {TargetMonitor} is out of bounds (screen_count = {DisplayServer.GetScreenCount()})");
            HideScreenWindow();
            return;
        }

        try
        {
            var canvas = DisplaysManager.Canvas;

            Rect2 canvasRect = new Rect2(0, 0, canvas.CanvasSize.X, canvas.CanvasSize.Y);
            Rect2 outputRect = new Rect2(CanvasPosition, OutputSize);
            Rect2 clippedRect = canvasRect.Intersection(outputRect);

            if (clippedRect.Size.X <= 0 || clippedRect.Size.Y <= 0)
            {
                HideScreenWindow();
                GD.Print($"VideoOutputDevice:UpdateOutputRegion - No valid region for output '{OutputName}' within canvas bounds.");
                return;
            }

            // Display home origin + DisplayOffset + canvas clip adjustment (global desktop coords)
            var monitorPos = DisplayServer.ScreenGetPosition(TargetMonitor);
            var intendedPos = monitorPos + DisplayOffset + (Vector2I)(clippedRect.Position - (Vector2)CanvasPosition);
            var intendedSize = (Vector2I)clippedRect.Size;
            bool fullCoverage = IsFullMonitorCoverage(intendedPos, intendedSize);

            // Exact full-monitor match → +1px width so we never land in ExclusiveFullscreen.
            var placePos = intendedPos;
            var placeSize = AntiExclusivePlacementSize(intendedSize, fullCoverage);

            bool modeOk = Mode == ModeEnum.Windowed && !IsExclusiveFullscreen();

            // Skip only when geometry + safe windowed mode are already applied.
            if (_lastClippedRect == clippedRect
                && _lastDisplayOffset == DisplayOffset
                && _lastWindowPos == placePos
                && _lastWindowSize == placeSize
                && Visible
                && Borderless
                && modeOk
                && Size == placeSize)
            {
                return;
            }

            bool wasFullscreenLike = IsFullscreenLike();

            Transparent = OutputTransparent;
            Unresizable = true;

            // Always demote exclusive/fullscreen before free placement (never leave exclusive active).
            if (wasFullscreenLike || Mode != ModeEnum.Windowed)
                ForceBorderlessWindowed();
            else
            {
                Mode = ModeEnum.Windowed;
                Borderless = true;
            }

            CurrentScreen = TargetMonitor;

            if (!Visible)
                Show();

            // When leaving exclusive/fullscreen, bounce visibility so the content surface
            // is recreated cleanly (prevents grey blank windows after mode exit).
            if (wasFullscreenLike && Visible)
            {
                Hide();
                Show();
                ForceBorderlessWindowed();
            }

            ApplyNativeWindowGeometry(placePos, placeSize);
            EnsureContentLayout(intendedSize);
            RefreshLayerHostSizes();

            // Final safety: if engine still promoted to exclusive (race after Show/size), demote and re-place.
            if (IsExclusiveFullscreen() || Mode != ModeEnum.Windowed)
            {
                GD.Print($"VideoOutputDevice:UpdateOutputRegion - '{OutputName}' demoting Mode={Mode} (exclusive/fullscreen not allowed on outputs).");
                ForceBorderlessWindowed();
                CurrentScreen = TargetMonitor;
                // Ensure anti-exclusive size even if fullCoverage detection raced
                var safeSize = placeSize;
                if (safeSize == DisplayServer.ScreenGetSize(TargetMonitor)
                    && placePos == DisplayServer.ScreenGetPosition(TargetMonitor))
                {
                    safeSize = AntiExclusivePlacementSize(safeSize, true);
                }

                ApplyNativeWindowGeometry(placePos, safeSize);
                EnsureContentLayout(intendedSize);
                RefreshLayerHostSizes();
            }

            // Deferred re-check: promotion sometimes happens a frame later on Windows.
            CallDeferred(nameof(DeferredDemoteExclusiveIfNeeded));

            _lastClippedRect = clippedRect;
            _lastDisplayOffset = DisplayOffset;

            // In-place test pattern update (no destroy/recreate)
            if (_testPattern != null)
            {
                _testPattern.PatternSize = OutputSize;
                _testPattern.PatternPosition = Vector2I.Zero;
                _testPattern.QueueRedraw();
            }

            GD.Print($"VideoOutputDevice:UpdateOutputRegion - '{OutputName}' Mode={Mode} Borderless={Borderless} " +
                     $"monitor={TargetMonitor} pos={placePos} size={placeSize} intended={intendedSize} " +
                     $"full={fullCoverage} exclusivePrevent={fullCoverage} clipped={clippedRect}");
        }
        catch (Exception ex)
        {
            GD.Print($"VideoOutputDevice:UpdateOutputRegion - Error: {ex.Message}. Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// One-frame later guard: Windows/Godot may promote exact-size borderless windows to exclusive
    /// after the initial placement call returns.
    /// </summary>
    private void DeferredDemoteExclusiveIfNeeded()
    {
        if (!GodotObject.IsInstanceValid(this) || IsVirtual)
            return;

        if (!IsExclusiveFullscreen() && Mode == ModeEnum.Windowed)
            return;

        GD.Print($"VideoOutputDevice:DeferredDemoteExclusiveIfNeeded - '{OutputName}' Mode={Mode}, forcing borderless windowed.");
        ForceBorderlessWindowed();

        if (_lastWindowPos.X != int.MinValue && _lastWindowSize.X > 0)
        {
            var pos = _lastWindowPos;
            var size = _lastWindowSize;
            // If cached size still exactly matches the monitor, widen by 1px.
            if (TargetMonitor >= 0 && TargetMonitor < DisplayServer.GetScreenCount())
            {
                var monPos = DisplayServer.ScreenGetPosition(TargetMonitor);
                var monSize = DisplayServer.ScreenGetSize(TargetMonitor);
                if (pos == monPos && (size == monSize || size == new Vector2I(monSize.X, monSize.Y)))
                    size = AntiExclusivePlacementSize(monSize, true);
            }

            CurrentScreen = TargetMonitor;
            ApplyNativeWindowGeometry(pos, size);
            EnsureContentLayout(size);
            RefreshLayerHostSizes();
        }
    }

    /// <summary>
    /// Keeps the content root filling the window content viewport after size changes.
    /// </summary>
    private void EnsureContentLayout(Vector2I windowSize)
    {
        if (_sceneRoot == null || !IsInstanceValid(_sceneRoot))
            return;

        _sceneRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        // Explicit size backup if anchors have not resolved yet this frame
        if (_sceneRoot.Size.X < 1 || _sceneRoot.Size.Y < 1)
            _sceneRoot.Size = windowSize;
    }

    /// <summary>
    /// Keeps per-layer host controls sized to the current output region after resizes.
    /// </summary>
    private void RefreshLayerHostSizes()
    {
        UpdateAllLayerDisplayRects();
    }

    /// <summary>
    /// Places the window as borderless windowed. Window.Size is the content-viewport authority;
    /// DisplayServer reinforces absolute multi-monitor desktop coordinates and windowed mode.
    /// </summary>
    private void ApplyNativeWindowGeometry(Vector2I windowPos, Vector2I windowSize)
    {
        if (windowSize.X <= 0 || windowSize.Y <= 0)
            return;

        // Never apply geometry while exclusive — exit first or content surface can go grey/black.
        if (IsExclusiveFullscreen())
            ForceBorderlessWindowed();

        Mode = ModeEnum.Windowed;
        Borderless = true;

        // Always drive Godot Window.Size first — this is what the content viewport uses.
        // DisplayServer-only size changes (without Size) are a common cause of grey blank windows.
        if (Size != windowSize)
            Size = windowSize;
        if (Position != windowPos)
            Position = windowPos;

        if (TryGetNativeWindowId(out int windowId))
        {
            // Force windowed + borderless at the OS level so exact full-screen rects do not
            // stick as ExclusiveFullscreen (black flashes on mode entry / multi-monitor).
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed, windowId);
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true, windowId);
            DisplayServer.WindowSetPosition(windowPos, windowId);
            DisplayServer.WindowSetSize(windowSize, windowId);
        }

        _lastWindowPos = windowPos;
        _lastWindowSize = windowSize;
    }

    public void ToggleTestPattern(bool toggle)
    {
        SetTestPatternRect(toggle, new Rect2(Vector2.Zero, OutputSize));
    }

    public void SetTestPatternRect(bool enable, Rect2 rect)
    {
        if (enable)
        {
            if (_testPattern == null)
            {
                GD.Print($"VideoOutputDevice:SetTestPatternRect - Adding test pattern to {OutputName} at {rect}.");
                _testPattern = new TestPattern((Vector2I)rect.Size, (Vector2I)rect.Position, OutputName);
                AddChild(_testPattern);
            }
            else
            {
                _testPattern.PatternSize = (Vector2I)rect.Size;
                _testPattern.PatternPosition = (Vector2I)rect.Position;
                _testPattern.QueueRedraw();
                GD.Print($"VideoOutputDevice:SetTestPatternRect - Updating test pattern to {rect}.");
            }
        }
        else
        {
            if (_testPattern != null)
            {
                RemoveChild(_testPattern);
                _testPattern.QueueFree();
                _testPattern = null;
                GD.Print($"VideoOutputDevice:SetTestPatternRect - Removing test pattern from {OutputName}.");
            }
        }
    }

    public void AddLayerTestPattern(int layerId, string layerName, Rect2 rect)
    {
        if (!_layerTestPatterns.ContainsKey(layerId))
        {
            GD.Print($"VideoOutputDevice:AddLayerTestPattern - Adding layer test pattern '{layerName}' to {OutputName} at {rect}.");
            var tp = new TestPattern((Vector2I)rect.Size, (Vector2I)rect.Position, layerName);
            AddChild(tp);
            _layerTestPatterns[layerId] = tp;
        }
        else
        {
            var tp = _layerTestPatterns[layerId];
            tp.PatternSize = (Vector2I)rect.Size;
            tp.PatternPosition = (Vector2I)rect.Position;
            tp.Position = (Vector2I)rect.Position;
            tp.QueueRedraw();
            GD.Print($"VideoOutputDevice:AddLayerTestPattern - Updating layer test pattern '{layerName}' to {rect}.");
        }
    }

    public void RemoveLayerTestPattern(int layerId)
    {
        if (_layerTestPatterns.TryGetValue(layerId, out var tp))
        {
            RemoveChild(tp);
            tp.QueueFree();
            _layerTestPatterns.Remove(layerId);
            GD.Print($"VideoOutputDevice:RemoveLayerTestPattern - Removing layer test pattern for layer ID {layerId} from {OutputName}.");
        }
    }

    public bool TestPatternStatus()
    {
        if (_testPattern != null) return true;
        return false;
    }

    public void SetTransparent(bool state)
    {
        OutputTransparent = state;
        this.Transparent = state;
    }

    /// <summary>
    /// Serializes the output device data.
    /// </summary>
    /// <returns>Dictionary containing output data.</returns>
    public Godot.Collections.Dictionary GetData()
    {
        var data = new Godot.Collections.Dictionary();
        data.Add("OutputId", OutputId);
        data.Add("OutputName", OutputName);
        data.Add("CanvasPositionX", CanvasPosition.X);
        data.Add("CanvasPositionY", CanvasPosition.Y);
        data.Add("OutputSizeX", OutputSize.X);
        data.Add("OutputSizeY", OutputSize.Y);
        data.Add("TargetMonitor", TargetMonitor);
        data.Add("Transparent", OutputTransparent);
        data.Add("DisplayOffsetX", DisplayOffset.X);
        data.Add("DisplayOffsetY", DisplayOffset.Y);
        data.Add("KeepAspect", KeepAspect);
        return data;
    }

    /// <summary>
    /// Loads the output device data from a dictionary.
    /// </summary>
    /// <param name="data">Dictionary containing output data.</param>
    public void LoadFromData(Godot.Collections.Dictionary data)
    {
        OutputId = (int)data["OutputId"];
        OutputName = (string)data["OutputName"];
        TargetMonitor = (int)data["TargetMonitor"];

        var canvPosX = data.ContainsKey("CanvasPositionX") ? (int)data["CanvasPositionX"] : 0;
        var canvPosY = data.ContainsKey("CanvasPositionY") ? (int)data["CanvasPositionY"] : 0;
        CanvasPosition = new Vector2I(canvPosX, canvPosY);

        var outSizeX = data.ContainsKey("OutputSizeX") ? (int)data["OutputSizeX"] : 1920;
        var outSizeY = data.ContainsKey("OutputSizeY") ? (int)data["OutputSizeY"] : 1080;
        OutputSize = new Vector2I(outSizeX, outSizeY);

        OutputTransparent = data.ContainsKey("Transparent") ? (bool)data["Transparent"] : false;

        var offX = data.ContainsKey("DisplayOffsetX") ? (int)data["DisplayOffsetX"] : 0;
        var offY = data.ContainsKey("DisplayOffsetY") ? (int)data["DisplayOffsetY"] : 0;
        DisplayOffset = new Vector2I(offX, offY);

        KeepAspect = data.ContainsKey("KeepAspect") && (bool)data["KeepAspect"];
    }
    
    private bool _disposed;

    public override void _ExitTree()
    {
        // Only clean children here — do not QueueFree self (already exiting tree)
        DisposeContents();
        base._ExitTree();
    }

    public new void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DisposeContents();

        if (IsInsideTree())
            QueueFree();
        else if (IsInstanceValid(this))
            Free();

        GD.Print($"VideoOutputDevice:Dispose - Disposed output device '{OutputName}'.");
    }

    private void DisposeContents()
    {
        try { Hide(); } catch { /* ignore */ }

        if (_testPattern != null && IsInstanceValid(_testPattern))
        {
            try
            {
                if (_testPattern.GetParent() == this)
                    RemoveChild(_testPattern);
                _testPattern.QueueFree();
            }
            catch { /* ignore */ }
            _testPattern = null;
        }

        foreach (var kvp in _layerTestPatterns.ToList())
        {
            var tp = kvp.Value;
            if (tp != null && IsInstanceValid(tp))
            {
                try
                {
                    if (tp.GetParent() == this)
                        RemoveChild(tp);
                    tp.QueueFree();
                }
                catch { /* ignore */ }
            }
        }
        _layerTestPatterns.Clear();

        foreach (var layer in _activeLayers.Keys.ToList())
        {
            if (layer != null && IsInstanceValid(layer))
            {
                try
                {
                    if (layer.GetParent() != null)
                        layer.GetParent().RemoveChild(layer);
                    layer.QueueFree();
                }
                catch { /* ignore */ }
            }
        }
        _activeLayers.Clear();
    }

}