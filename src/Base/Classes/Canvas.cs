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

        // Notify DisplaysManager to update all outputs
        var displaysManager = GetNode<DisplaysManager>("/root/DisplaysManager");
        displaysManager.UpdateAllOutputs();

        _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
            $"Canvas size updated to {newSize.X}x{newSize.Y}", 0);
        _globalSignals.EmitSignal(nameof(GlobalSignals.CanvasSizeChanged), newSize);
    }
    

    /// <summary>
    /// Gets the canvas texture for rendering portions to outputs.
    /// </summary>
    /// <returns>The rendered texture of the canvas.</returns>
    public Texture2D GetCanvasTexture()
    {
        return GetTexture();
    }

    /// <summary>
    /// Serializes the canvas state to a dictionary.
    /// </summary>
    /// <returns>Dictionary containing canvas data.</returns>
    public Godot.Collections.Dictionary GetData()
    {
        var data = new Godot.Collections.Dictionary();
        data.Add("CanvasSizeX", CanvasSize.X);
        data.Add("CanvasSizeY", CanvasSize.Y);
        return data;
    }

    /// <summary>
    /// Loads the canvas state from a dictionary.
    /// </summary>
    /// <param name="data">Dictionary containing canvas data.</param>
    public void LoadFromData(Godot.Collections.Dictionary data)
    {
        var size = new Vector2I(1920, 1080);
        size.X = data.ContainsKey("CanvasSizeX") ? (int)data["CanvasSizeX"] : 1920;
        size.Y = data.ContainsKey("CanvasSizeY") ? (int)data["CanvasSizeY"] : 1080;
        SetCanvasSize(size);
    }

    public override void _ExitTree()
    {
        // Clean up SDL if initialized
        if (SDL.WasInit(SDL.InitFlags.Video) != 0)
        {
            SDL.Quit();
        }
    }

    // TODO: Extension points for 3D (e.g., switch to Viewport with 3D scene)
}