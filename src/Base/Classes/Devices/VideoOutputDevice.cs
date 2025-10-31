using Godot;
using System;

namespace Cue2.Base.Classes;

public partial class VideoOutputDevice : Window
{

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
    public Vector2 CanvasPosition { get; set; } = Vector2.Zero;

    /// <summary>
    /// Size of the output region on the canvas.
    /// </summary>
    public Vector2 Size { get; set; } = new Vector2(1920, 1080);

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

    public VideoOutputDevice()
    {
        OutputId = _nextOutputId++;
        Mode = ModeEnum.Windowed; // Windowed for proper sizing and positioning
        _outputRect = new TextureRect();
        _outputRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _outputRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        AddChild(_outputRect);
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
            Rect2 outputRect = new Rect2(CanvasPosition, Size);
            Rect2 clippedRect = canvasRect.Intersection(outputRect);

            if (clippedRect.Size.X <= 0 || clippedRect.Size.Y <= 0)
            {
                // No valid region to display
                _outputRect.Texture = null;
                DisplayServer.WindowSetSize(new Vector2I(0, 0), this.GetWindow().GetWindowId());
                GD.Print($"VideoOutputDevice: No valid region for output '{OutputName}' within canvas bounds.");
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
                DisplayServer.WindowSetPosition(new Vector2I((int)clampedRect.Position.X, (int)clampedRect.Position.Y), this.GetWindow().GetWindowId());
                DisplayServer.WindowSetSize(new Vector2I((int)clampedRect.Size.X, (int)clampedRect.Size.Y), this.GetWindow().GetWindowId());
            }
            else
            {
                DisplayServer.WindowSetSize(new Vector2I(0, 0), this.GetWindow().GetWindowId());
            }
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true, this.GetWindow().GetWindowId());

            GD.Print($"VideoOutputDevice: Updated output '{OutputName}' to clipped region {clippedRect.Position}-{clippedRect.Size} from canvas {CanvasPosition}-{Size}.");
        }
        catch (Exception ex)
        {
            GD.Print($"VideoOutputDevice: Error updating output region: {ex.Message}");
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
        data.Add("CanvasPosition", CanvasPosition);
        data.Add("Size", Size);
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
        // Backward compatibility: try "CanvasPosition" first, then "Position"
        if (data.ContainsKey("CanvasPosition"))
        {
            CanvasPosition = (Vector2)data["CanvasPosition"];
        }
        else if (data.ContainsKey("Position"))
        {
            CanvasPosition = (Vector2)data["Position"];
        }
        Size = (Vector2)data["Size"];
        TargetMonitor = (int)data["TargetMonitor"];
    }

    // TODO: Handle window resizing/moving for non-fullscreen modes
    // TODO: Integration with LibVLCSharp for direct rendering if needed
}