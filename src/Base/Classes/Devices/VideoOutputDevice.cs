using Godot;
using System;
using Cue2.Shared;

namespace Cue2.Base.Classes;

public partial class VideoOutputDevice : Window
{
    private GlobalSignals _globalSignals;

    /// <summary>
    /// Unique ID for the output device.
    /// </summary>
    public int OutputId { get; private set; }

    /// <summary>
    /// Name of the output device.
    /// </summary>
    public string OutputName { get; set; } = "Unnamed Output";

    /// <summary>
    /// Position on the canvas (top-left corner).
    /// </summary>
    public Vector2 Position { get; set; } = Vector2.Zero;

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

    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

        OutputId = _nextOutputId++;
        Mode = ModeEnum.Fullscreen; // Default to fullscreen; can be configured

        _outputRect = new TextureRect();
        _outputRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _outputRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        AddChild(_outputRect);

        UpdateOutputRegion();

        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
            $"VideoOutputDevice '{OutputName}' initialized.", 0);
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
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                "VideoOutputDevice:UpdateOutputRegion - No canvas reference set.", 1);
            return;
        }

        try
        {
            var canvasTexture = _canvas.GetCanvasTexture();
            if (canvasTexture == null)
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                    "VideoOutputDevice:UpdateOutputRegion - Canvas texture not available.", 1);
                return;
            }

            // Create an Image from the canvas texture
            var image = canvasTexture.GetImage();

            // Crop the region
            var croppedImage = new Image();
            croppedImage = image.GetRegion(new Rect2I((Vector2I)Position, (Vector2I)Size));

            // Set to TextureRect
            _outputRect.Texture = ImageTexture.CreateFromImage(croppedImage);

            // Position window on target monitor
            DisplayServer.WindowSetCurrentScreen(GetWindowId(), TargetMonitor);

            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"Updated output '{OutputName}' to region {Position}-{Size}.", 0);
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"Error updating output region: {ex.Message}", 2);
        }
    }

    // TODO: Handle window resizing/moving for non-fullscreen modes
    // TODO: Integration with LibVLCSharp for direct rendering if needed
    // TODO: Serialization for saving output configuration
}