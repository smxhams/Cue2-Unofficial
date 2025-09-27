using Godot;
using System;
using System.Collections.Generic;
using Cue2.Shared;
using SDL3;

namespace Cue2.Base.Classes;

public partial class Canvas : SubViewport
{
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;

    /// <summary>
    /// The dimensions of the canvas in pixels (width, height).
    /// </summary>
    public Vector2I CanvasSize { get; private set; } = new Vector2I(1920, 1080); // Default size

    /// <summary>
    /// List of layers on the canvas, sorted by Z-index.
    /// </summary>
    private List<VideoTargetLayer> _layers = new List<VideoTargetLayer>();

    /// <summary>
    /// List of video output devices placed on the canvas.
    /// </summary>
    private List<VideoOutputDevice> _outputs = new List<VideoOutputDevice>();

    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        
        if (SDL.Init(SDL.InitFlags.Video) == false)
        {
            var errorMsg = $"SDL Init failed: {SDL.GetError}";
            GD.Print("Canvas:_Ready - " + errorMsg);
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), errorMsg, 3);
            return;
        }

        // Set up the viewport for 2D rendering (can be extended to 3D later)
        RenderTargetUpdateMode = UpdateMode.Always;
        Size = CanvasSize;
        TransparentBg = false; // Opaque background for compositing

        // Log initialization
        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
            $"Canvas initialized with size {CanvasSize.X}x{CanvasSize.Y}", 0);

        // TODO: Load from settings or saved data
    }

    /// <summary>
    /// Sets the canvas size and resizes the viewport accordingly.
    /// </summary>
    /// <param name="newSize">New canvas dimensions.</param>
    public void SetCanvasSize(Vector2I newSize)
    {
        if (newSize.X <= 0 || newSize.Y <= 0)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                "Invalid canvas size provided; must be positive integers.", 1);
            return;
        }

        CanvasSize = newSize;
        Size = newSize;

        // Notify outputs to update their rendering if needed
        foreach (var output in _outputs)
        {
            output.UpdateOutputRegion();
        }

        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
            $"Canvas size updated to {newSize.X}x{newSize.Y}", 0);
    }

    /// <summary>
    /// Adds a new layer to the canvas.
    /// </summary>
    /// <param name="layer">The layer to add.</param>
    public void AddLayer(VideoTargetLayer layer)
    {
        if (_layers.Contains(layer))
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"Layer '{layer.LayerName}' already exists on canvas.", 1);
            return;
        }

        _layers.Add(layer);
        AddChild(layer.LayerNode); // Add the layer's node to the viewport
        SortLayersByZIndex();

        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
            $"Added layer '{layer.LayerName}' to canvas at Z-index {layer.ZIndex}.", 0);
    }

    /// <summary>
    /// Removes a layer from the canvas.
    /// </summary>
    /// <param name="layerId">The ID of the layer to remove.</param>
    public void RemoveLayer(int layerId)
    {
        var layer = _layers.Find(l => l.LayerId == layerId);
        if (layer == null)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"Layer with ID {layerId} not found on canvas.", 1);
            return;
        }

        _layers.Remove(layer);
        RemoveChild(layer.LayerNode);
        layer.LayerNode.QueueFree();

        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
            $"Removed layer '{layer.LayerName}' from canvas.", 0);
    }

    /// <summary>
    /// Adds a video output device to the canvas.
    /// </summary>
    /// <param name="output">The output device to add.</param>
    public void AddOutput(VideoOutputDevice output)
    {
        if (_outputs.Contains(output))
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"Output '{output.OutputName}' already exists on canvas.", 1);
            return;
        }

        _outputs.Add(output);
        output.SetCanvasReference(this); // Link back to canvas

        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
            $"Added output '{output.OutputName}' to canvas at position {output.Position}.", 0);
    }

    /// <summary>
    /// Removes a video output device from the canvas.
    /// </summary>
    /// <param name="outputId">The ID of the output to remove.</param>
    public void RemoveOutput(int outputId)
    {
        var output = _outputs.Find(o => o.OutputId == outputId);
        if (output == null)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"Output with ID {outputId} not found on canvas.", 1);
            return;
        }

        _outputs.Remove(output);
        output.QueueFree(); // Assuming output is a Node-based class

        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
            $"Removed output '{output.OutputName}' from canvas.", 0);
    }

    /// <summary>
    /// Sorts layers by Z-index to ensure correct rendering order.
    /// </summary>
    private void SortLayersByZIndex()
    {
        _layers.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex));
        for (int i = 0; i < _layers.Count; i++)
        {
            var window = _layers[i] as VideoTargetLayer;
            _layers[i].ZIndex = i; // Assign rendering order
        }
    }

    /// <summary>
    /// Gets the canvas texture for rendering portions to outputs.
    /// </summary>
    /// <returns>The rendered texture of the canvas.</returns>
    public Texture2D GetCanvasTexture()
    {
        return GetTexture();
    }

    // TODO: Serialization methods for saving/loading canvas state (e.g., GetData(), FromData())
    // TODO: Extension points for 3D (e.g., switch to Viewport with 3D scene)
}