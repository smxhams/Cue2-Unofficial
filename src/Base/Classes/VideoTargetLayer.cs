using Godot;
using System;

namespace Cue2.Base.Classes;

public class VideoTargetLayer
{
    private static int _nextLayerId = 0;

    /// <summary>
    /// Unique ID for the layer.
    /// </summary>
    public int LayerId { get; private set; }

    /// <summary>
    /// Name of the layer for identification.
    /// </summary>
    public string LayerName { get; set; } = "Unnamed Layer";

    /// <summary>
    /// Z-index for ordering layers (lower values render first).
    /// </summary>
    public int ZIndex { get; set; } = 0;

    /// <summary>
    /// Position on the canvas (top-left corner).
    /// </summary>
    public Vector2I CanvasPosition { get; set; } = Vector2I.Zero;

    /// <summary>
    /// Size of the layer on the canvas.
    /// </summary>
    public Vector2I Size { get; set; } = new Vector2I(1920, 1080);

    /// <summary>
    /// Whether the layer is transparent.
    /// </summary>
    public bool Transparent { get; set; } = false;

    /// <summary>
    /// Whether the test pattern is enabled for this layer.
    /// </summary>
    public bool TestPatternEnabled { get; set; } = false;
    

    public VideoTargetLayer()
    {
        LayerId = _nextLayerId++;
    }

    public VideoTargetLayer(string name, int zIndex) : this()
    {
        LayerName = name;
        ZIndex = zIndex;
    }
    

    /// <summary>
    /// Serializes the layer data.
    /// </summary>
    /// <returns>Dictionary containing layer data.</returns>
    public Godot.Collections.Dictionary GetData()
    {
        var data = new Godot.Collections.Dictionary();
        data.Add("LayerId", LayerId);
        data.Add("LayerName", LayerName);
        data.Add("ZIndex", ZIndex);
        data.Add("CanvasPositionX", CanvasPosition.X);
        data.Add("CanvasPositionY", CanvasPosition.Y);
        data.Add("SizeX", Size.X);
        data.Add("SizeY", Size.Y);
        data.Add("Transparent", Transparent);
        GD.Print($"SAVING LAYER DATA: NAME={LayerName}, SizeX={Size.X}, SizeY={Size.Y}, ZIndex={ZIndex}, CanvasPositionX={CanvasPosition.X}, CanvasPositionY={CanvasPosition.Y}");
        return data;
    }

    /// <summary>
    /// Loads the layer data from a dictionary.
    /// </summary>
    /// <param name="data">Dictionary containing layer data.</param>
    public void LoadFromData(Godot.Collections.Dictionary data)
    {
        LayerId = (int)data["LayerId"];
        if (LayerId >= _nextLayerId) _nextLayerId = LayerId++;
        LayerName = (string)data["LayerName"];
        ZIndex = (int)data["ZIndex"];
        
        var outSizeX = data.ContainsKey("SizeX") ? (int)data["SizeX"] : 1920;
        var outSizeY = data.ContainsKey("SizeY") ? (int)data["SizeY"] : 1080;
        Size = new Vector2I(outSizeX, outSizeY);
        
        var canvPosX = data.ContainsKey("CanvasPositionX") ? (int)data["CanvasPositionX"] : 0;
        var canvPosY = data.ContainsKey("CanvasPositionY") ? (int)data["CanvasPositionY"] : 0;
        CanvasPosition = new Vector2I(canvPosX, canvPosY);

        Transparent = data.ContainsKey("Transparent") ? (bool)data["Transparent"] : false;
        GD.Print($"LOADING LAYER DATA: NAME={LayerName}, SizeX={Size.X}, SizeY={Size.Y}, ZIndex={ZIndex}, CanvasPositionX={CanvasPosition.X}, CanvasPositionY={CanvasPosition.Y}");

    }
    
}