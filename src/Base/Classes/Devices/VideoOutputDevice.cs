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
        _sceneRoot = new Control();
        AddChild(_sceneRoot);
        _sceneRoot.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _sceneRoot.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
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
        _sceneRoot.AddChild(displayLayer);
        var outputLayer = displayLayer.GetNode<TextureRect>("%LayerOutput");
        outputLayer.Position = (Vector2)(layer.CanvasPosition - CanvasPosition);
        outputLayer.Size = (Vector2)layer.Size;


        return displayLayer;
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
    /// Leaves exclusive/fullscreen once so the window can be freely placed on a target monitor.
    /// Uses the proven Size.X-1 nudge only when already exclusive — not every frame.
    /// </summary>
    private void ExitExclusiveFullscreenIfNeeded()
    {
        if (Mode != ModeEnum.ExclusiveFullscreen && Mode != ModeEnum.Fullscreen)
            return;

        // Proven Godot/Windows path to leave exclusive mode without getting stuck.
        Borderless = false;
        Vector2I s = Size;
        if (s.X > 1)
            Size = new Vector2I(s.X - 1, s.Y);
        Mode = ModeEnum.Windowed;
        Borderless = true;
        InvalidateGeometryCache();
    }

    /// <summary>
    /// Updates the output to show the correct region of the canvas.
    /// Physical screens are borderless windows placed via DisplayServer on the target monitor.
    /// </summary>
    public void UpdateOutputRegion()
    {
        GD.Print($"VideoOutputDevice:UpdateOutputRegion - Updating output region '{OutputName}' (monitor={TargetMonitor}).");

        if (OutputSize.X <= 0 || OutputSize.Y <= 0)
        {
            GD.Print("VideoOutputDevice:UpdateOutputRegion - Invalid output size, must be positive.");
            return;
        }

        if (IsVirtual)
        {
            HideScreenWindow();
            GD.Print($"VideoOutputDevice:UpdateOutputRegion - '{OutputName}' is virtual; window hidden.");
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
            var windowPos = monitorPos + DisplayOffset + (Vector2I)(clippedRect.Position - (Vector2)CanvasPosition);
            var windowSize = (Vector2I)clippedRect.Size;

            // Skip only when geometry is already applied and we are not stuck in exclusive mode
            if (_lastClippedRect == clippedRect
                && _lastDisplayOffset == DisplayOffset
                && _lastWindowPos == windowPos
                && _lastWindowSize == windowSize
                && Visible
                && Mode == ModeEnum.Windowed
                && Borderless)
            {
                return;
            }

            // Leave exclusive fullscreen before placing (only when currently exclusive)
            ExitExclusiveFullscreenIfNeeded();

            Transparent = OutputTransparent;
            Borderless = true;
            Mode = ModeEnum.Windowed;

            // Pin to the correct screen before show/place
            CurrentScreen = TargetMonitor;

            if (!Visible)
                Show();

            ApplyNativeWindowGeometry(windowPos, windowSize);

            // If the platform promoted us to exclusive after covering the whole monitor,
            // exit once and re-apply. Cache prevents repeating every call.
            if (Mode == ModeEnum.ExclusiveFullscreen || Mode == ModeEnum.Fullscreen)
            {
                ExitExclusiveFullscreenIfNeeded();
                Mode = ModeEnum.Windowed;
                Borderless = true;
                CurrentScreen = TargetMonitor;
                ApplyNativeWindowGeometry(windowPos, windowSize);
            }

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
                     $"monitor={TargetMonitor} pos={windowPos} size={windowSize} clipped={clippedRect}");
        }
        catch (Exception ex)
        {
            GD.Print($"VideoOutputDevice:UpdateOutputRegion - Error: {ex.Message}. Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Places the window using DisplayServer when a native id exists (correct multi-monitor),
    /// falling back to Window properties. Skips no-op updates.
    /// </summary>
    private void ApplyNativeWindowGeometry(Vector2I windowPos, Vector2I windowSize)
    {
        if (windowSize.X <= 0 || windowSize.Y <= 0)
            return;

        if (_lastWindowPos == windowPos && _lastWindowSize == windowSize
            && Position == windowPos && Size == windowSize)
            return;

        if (TryGetNativeWindowId(out int windowId))
        {
            // DisplayServer uses absolute desktop coordinates — required for multi-monitor.
            if (_lastWindowPos != windowPos || Position != windowPos)
                DisplayServer.WindowSetPosition(windowPos, windowId);

            if (_lastWindowSize != windowSize || Size != windowSize)
                DisplayServer.WindowSetSize(windowSize, windowId);
        }
        else
        {
            Position = windowPos;
            Size = windowSize;
        }

        // Keep Godot Window state aligned (without re-applying if DisplayServer already did)
        if (Position != windowPos)
            Position = windowPos;
        if (Size != windowSize)
            Size = windowSize;

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