using Godot;
using System;
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
    /// Target display monitor index (for multi-monitor setups).
    /// </summary>
    public int TargetMonitor { get; set; } = 0;

    /// <summary>
    /// Whether the output window is transparent.
    /// </summary>
    public bool OutputTransparent { get; set; } = false;

    /// <summary>
    /// Reference to the parent canvas.
    /// </summary>
    private Canvas _canvas;

    /// <summary>
    /// TextureRect to display the cropped canvas region.
    /// </summary>
    private TextureRect _outputRect;

    private static int _nextOutputId = 0;

    private TestPattern _testPattern;
    private Dictionary<int, TestPattern> _layerTestPatterns = new();

    /// <summary>
    /// Cached last clipped rectangle to avoid unnecessary updates.
    /// </summary>
    private Rect2 _lastClippedRect = new Rect2(-1, -1, 0, 0);

    public VideoOutputDevice()
    {
        OutputId = _nextOutputId++;
        Mode = ModeEnum.Windowed; // Windowed for proper sizing and positioning
        _outputRect = new TextureRect();
        _outputRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _outputRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        //AlwaysOnTop = true;
        AddChild(_outputRect);
        Borderless = true;
        DisplayServer.ScreenSetKeepOn(true);
        GD.Print($"VideoOutputDevice:Constructor - Initialized output device '{OutputName}' with ID {OutputId}.");
    }
    

    /// <summary>
    /// Sets the reference to the parent canvas.
    /// </summary>
    /// <param name="canvas">The canvas this output belongs to.</param>
    public void SetCanvasReference(Canvas canvas)
    {
        _canvas = canvas;
        UpdateOutputRegion();
    }

    

    /// <summary>
    /// Updates the output to show the correct region of the canvas.
    /// </summary>
    public void UpdateOutputRegion()
    {
        GD.Print($"VideoOutputDevice:UpdateOutputRegion - Updating output region '{OutputName}'.");
        if (_canvas == null)
        {
            GD.Print("VideoOutputDevice:UpdateOutputRegion - No canvas reference set.");
            return;
        }

        // Validation
        if (OutputSize.X <= 0 || OutputSize.Y <= 0)
        {
            GD.Print("VideoOutputDevice:UpdateOutputRegion - Invalid output size, must be positive.");
            return;
        }

        try
        {
            var canvasTexture = _canvas.GetCanvasTexture();
            if (canvasTexture == null)
            {
                GD.Print("VideoOutputDevice:UpdateOutputRegion - Canvas texture not available.");
                return;
            }

            // Calculate clipped region within canvas bounds
            Rect2 canvasRect = new Rect2(0, 0, _canvas.CanvasSize.X, _canvas.CanvasSize.Y);
            Rect2 outputRect = new Rect2(CanvasPosition, OutputSize);
            Rect2 clippedRect = canvasRect.Intersection(outputRect);

            // Cache check
            if (_lastClippedRect == clippedRect)
            {
                return; // No change, skip update
            }
            _lastClippedRect = clippedRect;
            
            if (clippedRect.Size.X <= 0 || clippedRect.Size.Y <= 0)
            {
                // No valid region to display
                _outputRect.Texture = null;
                DisplayServer.WindowSetSize(new Vector2I(0, 0), GetWindowId());
                GD.Print($"VideoOutputDevice:UpdateOutputRegion - No valid region for output '{OutputName}' within canvas bounds.");
                return;
            }

            // Create an Image from the canvas texture
            var image = canvasTexture.GetImage();

            // Crop the clipped region
            var croppedImage = new Image();
            croppedImage = image.GetRegion(new Rect2I((Vector2I)clippedRect.Position, (Vector2I)clippedRect.Size));

            // Set to TextureRect
            _outputRect.Texture = ImageTexture.CreateFromImage(croppedImage);
            
            // Position and size window based on clipped region
            if (Mode == ModeEnum.ExclusiveFullscreen)
            {
                Borderless = false;
                this.SetSize(new Vector2I(Size.X - 1, Size.Y));
                SetMode(ModeEnum.Windowed);
                this.SetSize(new Vector2I(Size.X - 1, Size.Y));
            }
            Transparent = OutputTransparent;
            var monitorPos = DisplayServer.ScreenGetPosition(TargetMonitor);
            var windowPos = monitorPos + (clippedRect.Position - CanvasPosition);

            if (clippedRect.Size.X > 0 && clippedRect.Size.Y > 0)
            {
                Borderless = true;
                DisplayServer.WindowSetPosition(new Vector2I((int)windowPos.X, (int)windowPos.Y), GetWindowId());
                DisplayServer.WindowSetSize((Vector2I)clippedRect.Size, GetWindowId());
            }
            else
            {
                DisplayServer.WindowSetSize(new Vector2I(0, 0), GetWindowId());
            }

            
            GD.Print($"Mode after update: {Mode}, Borderless: {Borderless.ToString()}, Transparent: {Transparent}");

            if (TestPatternStatus())
            {
                ToggleTestPattern(false);
                ToggleTestPattern(true);
            }

            GD.Print($"VideoOutputDevice:UpdateOutputRegion - Updated '{OutputName}' to clipped region {clippedRect.Position}-{clippedRect.Size} from canvas {CanvasPosition}-{OutputSize}.");
        }
        catch (Exception ex)
        {
            GD.Print($"VideoOutputDevice:UpdateOutputRegion - Error: {ex.Message}. Stack trace: {ex.StackTrace}");
        }
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
    }
    
    public override void _ExitTree()
    {
        Dispose();
        base._ExitTree();
    }

    public void Dispose()
    {
        // Hide the window
        Hide();

        // Remove child TextureRect
        if (_outputRect != null)
        {
            RemoveChild(_outputRect);
            _outputRect.QueueFree();
            _outputRect = null;
        }

        // Remove test patterns
        if (_testPattern != null)
        {
            RemoveChild(_testPattern);
            _testPattern.QueueFree();
            _testPattern = null;
        }
        foreach (var kvp in _layerTestPatterns)
        {
            RemoveChild(kvp.Value);
            kvp.Value.QueueFree();
        }
        _layerTestPatterns.Clear();

        // Clear canvas reference
        _canvas = null;
        
        QueueFree();

        GD.Print($"VideoOutputDevice:Dispose - Disposed output device '{OutputName}'.");
    }

}