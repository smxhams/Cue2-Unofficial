using Godot;
using System;

namespace Cue2.Base.Classes;

public partial class VideoTargetLayer : GodotObject
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
    /// The Godot node representing this layer (e.g., CanvasLayer or Control for 2D).
    /// </summary>
    public Node LayerNode { get; private set; }

    public VideoTargetLayer()
    {
        LayerId = _nextLayerId++;
        LayerNode = new CanvasLayer(); // Default to 2D CanvasLayer
        // For 3D extension: Could be a Spatial or MeshInstance
    }

    public VideoTargetLayer(string name, int zIndex) : this()
    {
        LayerName = name;
        ZIndex = zIndex;
    }

    /// <summary>
    /// Adds a child node to this layer (e.g., a TextureRect for video).
    /// </summary>
    /// <param name="child">The node to add (e.g., TextureRect).</param>
    public void AddContent(Node child)
    {
        if (child == null)
        {
            GD.PrintErr("VideoTargetLayer:AddContent - Cannot add null child.");
            return;
        }

        LayerNode.AddChild(child);
        GD.Print($"Added content to layer '{LayerName}'.");
    }

    /// <summary>
    /// Removes a child node from this layer.
    /// </summary>
    /// <param name="child">The node to remove.</param>
    public void RemoveContent(Node child)
    {
        if (child == null || !LayerNode.IsAncestorOf(child))
        {
            GD.PrintErr("VideoTargetLayer:RemoveContent - Child not found in layer.");
            return;
        }

        LayerNode.RemoveChild(child);
        GD.Print($"Removed content from layer '{LayerName}'.");
    }

    // TODO: Methods for positioning/scaling content within the layer
    // TODO: Extension for 3D (e.g., replace LayerNode with a 3D node)
}