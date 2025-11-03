using Godot;
using System;

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
    /// Reference to the parent canvas.
    /// </summary>
    private Canvas _canvas;

    /// <summary>
    /// TextureRect to display the cropped canvas region.
    /// </summary>
    private TextureRect _outputRect;

    private static int _nextOutputId = 0;

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
        AddChild(_outputRect);
        Borderless = true;
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

            // Position and size window based on clipped region, clamped to monitor bounds
            var monitorPos = DisplayServer.ScreenGetPosition(TargetMonitor);
            var monitorSize = DisplayServer.ScreenGetSize(TargetMonitor);
            var monitorRect = new Rect2(monitorPos, monitorSize);
            var windowPos = monitorPos + (clippedRect.Position - CanvasPosition);
            var windowRect = new Rect2(windowPos, clippedRect.Size);
            var clampedRect = monitorRect.Intersection(windowRect);
            
            if (clampedRect.Size.X > 0 && clampedRect.Size.Y > 0)
            {
                DisplayServer.WindowSetPosition(new Vector2I((int)clampedRect.Position.X, (int)clampedRect.Position.Y), GetWindow().GetWindowId());
                DisplayServer.WindowSetSize(new Vector2I((int)clampedRect.Size.X, (int)clampedRect.Size.Y), GetWindow().GetWindowId());
            }
            else
            {
                DisplayServer.WindowSetSize(new Vector2I(0, 0), GetWindow().GetWindowId());
            }

            GD.Print($"VideoOutputDevice:UpdateOutputRegion - Updated '{OutputName}' to clipped region {clippedRect.Position}-{clippedRect.Size} from canvas {CanvasPosition}-{OutputSize}.");
        }
        catch (Exception ex)
        {
            GD.Print($"VideoOutputDevice:UpdateOutputRegion - Error: {ex.Message}. Stack trace: {ex.StackTrace}");
        }
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

        // Clear canvas reference
        _canvas = null;

        GD.Print($"VideoOutputDevice:Dispose - Disposed output device '{OutputName}'.");
    }

}