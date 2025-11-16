using System;
using Godot;
using System.Collections.Generic;
using Cue2.Base.Classes;
using Cue2.Base.Classes.Devices;
using SDL3;

namespace Cue2.Shared;

/// <summary>
/// Manages video output devices for displaying canvas regions on monitors.
/// This is Autoloaded by project.
/// </summary>
public partial class DisplaysManager : Node
{
    /// <summary>
    /// Information about an available display.
    /// </summary>
    public struct DisplayInfo
    {
        public int Index;
        public string Name;
        public Vector2I Position;
        public Vector2I Size;
        public int Dpi;
        public float RefreshRate;
    }

    private GlobalSignals _globalSignals;
    private Canvas Canvas => GetNode<GlobalData>("/root/GlobalData").VideoCanvas;
    private PackedScene _videoLayer;

    /// <summary>
    /// List of active video output devices.
    /// </summary>
    public static List<VideoOutputDevice> Outputs { get; } = new List<VideoOutputDevice>();

    /// <summary>
    /// List of video target layers.
    /// </summary>
    public static List<VideoTargetLayer> Layers { get; } = new List<VideoTargetLayer>();

    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

        // Add default layer
        AddLayer("Default", 0);

        _videoLayer = SceneLoader.LoadPackedScene("uid://bnijb6qe1sop3", out _);

        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "DisplaysManager initialized.", 0);
    }

    /// <summary>
    /// Adds a new video output device for the specified monitor.
    /// </summary>
    /// <param name="monitorIndex">The target monitor index.</param>
    /// <param name="canvasPosition">Position on the canvas.</param>
    /// <param name="size">Size of the output region.</param>
    /// <param name="name">Name of the output.</param>
    /// <returns>The created VideoOutputDevice.</returns>
    public VideoOutputDevice AddOutput(int monitorIndex, Vector2I canvasPosition, Vector2I size, string name = null)
    {
        var output = new VideoOutputDevice();
        output.OutputName = name ?? $"Output {monitorIndex}";
        output.CanvasPosition = canvasPosition;
        output.OutputSize = size;
        output.TargetMonitor = monitorIndex;
        AddChild(output);
        
        Outputs.Add(output);
        output.Show();
        output.SetCanvasReference(Canvas);
        UpdateAllLayerTestPatterns();
        

        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Added video output '{output.OutputName}' for monitor {monitorIndex}.", 0);
        _globalSignals.EmitSignal(nameof(GlobalSignals.DisplaysChanged));
        return output;
    }

    /// <summary>
    /// Removes a video output device by ID.
    /// </summary>
    /// <param name="outputId">The output ID to remove.</param>
    public void RemoveOutput(int outputId)
    {
        var output = Outputs.Find(o => o.OutputId == outputId);
        if (output != null)
        {
            // Remove layer test patterns from this output
            foreach (var layer in Layers)
            {
                if (layer.TestPatternEnabled)
                {
                    output.RemoveLayerTestPattern(layer.LayerId);
                }
            }
            Outputs.Remove(output);
            output.QueueFree();
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Removed video output '{output.OutputName}'.", 0);
            _globalSignals.EmitSignal(nameof(GlobalSignals.DisplaysChanged));
        }
    }

    /// <summary>
    /// Gets a video output device by ID.
    /// </summary>
    /// <param name="outputId">The output ID.</param>
    /// <returns>The VideoOutputDevice or null.</returns>
    public VideoOutputDevice GetOutputById(int outputId)
    {
        return Outputs.Find(o => o.OutputId == outputId);
    }

    /// <summary>
    /// Updates all output regions (called when canvas changes).
    /// </summary>
    public void UpdateAllOutputs()
    {
        foreach (var output in Outputs)
        {
            output.UpdateOutputRegion();
        }
    }

    /// <summary>
    /// Updates the canvas position of a video output device.
    /// </summary>
    /// <param name="outputId">The output ID.</param>
    /// <param name="newCanvasPosition">The new canvas position.</param>
    public void UpdateOutputCanvasPosition(int outputId, Vector2I newCanvasPosition)
    {
        var output = Outputs.Find(o => o.OutputId == outputId);
        if (output != null)
        {
            output.CanvasPosition = newCanvasPosition;
            output.UpdateOutputRegion();
            UpdateAllLayerTestPatterns();
            _globalSignals.EmitSignal(nameof(GlobalSignals.DisplaysChanged));
        }
    }

    /// <summary>
    /// Updates the size of a video output device.
    /// </summary>
    /// <param name="outputId">The output ID.</param>
    /// <param name="newSize">The new size.</param>
    public void UpdateOutputSize(int outputId, Vector2I newSize)
    {
        var output = Outputs.Find(o => o.OutputId == outputId);
        if (output != null)
        {
            output.OutputSize = newSize;
            output.UpdateOutputRegion();
            UpdateAllLayerTestPatterns();
            _globalSignals.EmitSignal(nameof(GlobalSignals.DisplaysChanged));
        }
    }

    /// <summary>
    /// Adds a new layer to the canvas.
    /// </summary>
    /// <param name="name">Name of the layer.</param>
    /// <param name="zIndex">Z-index for ordering.</param>
    /// <returns>The created VideoTargetLayer.</returns>
    public VideoTargetLayer AddLayer(string name, int zIndex)
    {
        var layer = new VideoTargetLayer(name, zIndex);
        layer.Size = Canvas.CanvasSize;
        layer.CanvasPosition = Vector2I.Zero;
        Layers.Add(layer);
        Canvas.AddChild(layer);
        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Added layer '{name}' to canvas.", 0);
        return layer;
    }

    /// <summary>
    /// Removes a layer from the canvas.
    /// </summary>
    /// <param name="layerId">The ID of the layer to remove.</param>
    public void RemoveLayer(int layerId)
    {
        var layer = Layers.Find(l => l.LayerId == layerId);
        if (layer != null)
        {
            // Remove test patterns from all outputs
            foreach (var output in Outputs)
            {
                output.RemoveLayerTestPattern(layer.LayerId);
            }
            Layers.Remove(layer);
            Canvas.RemoveChild(layer);
            layer.QueueFree();
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Removed layer '{layer.LayerName}'.", 0);
        }
    }

    /// <summary>
    /// Updates the name of a layer.
    /// </summary>
    /// <param name="layerId">The layer ID.</param>
    /// <param name="newName">The new name.</param>
    public void UpdateLayerName(int layerId, string newName)
    {
        var layer = Layers.Find(l => l.LayerId == layerId);
        if (layer != null)
        {
            layer.LayerName = newName;
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Updated layer name to '{newName}'.", 0);
        }
    }

    /// <summary>
    /// Updates the canvas position of a layer.
    /// </summary>
    /// <param name="layerId">The layer ID.</param>
    /// <param name="newPosition">The new canvas position.</param>
    public void UpdateLayerCanvasPosition(int layerId, Vector2I newPosition)
    {
        var layer = Layers.Find(l => l.LayerId == layerId);
        if (layer != null)
        {
            layer.CanvasPosition = newPosition;
            UpdateLayerTestPatterns(layerId);
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Updated layer position to {newPosition}.", 0);
        }
    }

    /// <summary>
    /// Updates the size of a layer.
    /// </summary>
    /// <param name="layerId">The layer ID.</param>
    /// <param name="newSize">The new size.</param>
    public void UpdateLayerSize(int layerId, Vector2I newSize)
    {
        var layer = Layers.Find(l => l.LayerId == layerId);
        if (layer != null)
        {
            layer.Size = newSize;
            UpdateLayerTestPatterns(layerId);
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Updated layer size to {newSize}.", 0);
        }
    }

    /// <summary>
    /// Updates the transparency of a layer.
    /// </summary>
    /// <param name="layerId">The layer ID.</param>
    /// <param name="transparent">Whether the layer is transparent.</param>
    public void UpdateLayerTransparent(int layerId, bool transparent)
    {
        var layer = Layers.Find(l => l.LayerId == layerId);
        if (layer != null)
        {
            layer.Transparent = transparent;
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Updated layer transparency to {transparent}.", 0);
        }
    }

    /// <summary>
    /// Toggles the test pattern for a layer on intersecting outputs.
    /// </summary>
    /// <param name="layerId">The layer ID.</param>
    /// <param name="toggle">Whether to enable or disable.</param>
    public void ToggleLayerTestPattern(int layerId, bool toggle)
    {
        var layer = Layers.Find(l => l.LayerId == layerId);
        if (layer == null) return;

        layer.TestPatternEnabled = toggle;
        Rect2 layerRect = new Rect2(layer.CanvasPosition, layer.Size);
        Rect2 canvasRect = new Rect2(0, 0, Canvas.CanvasSize.X, Canvas.CanvasSize.Y);
        foreach (var output in Outputs)
        {
            Rect2 outputRect = new Rect2(output.CanvasPosition, output.OutputSize);
            Rect2 intersection = layerRect.Intersection(outputRect);
            if (intersection.Size.X > 0 && intersection.Size.Y > 0)
            {
                Rect2 clippedRect = canvasRect.Intersection(outputRect);
                // Convert to output local coordinates
                Vector2 localPos = layer.CanvasPosition - clippedRect.Position;
                if (toggle)
                {
                    output.AddLayerTestPattern(layer.LayerId, layer.LayerName, new Rect2(localPos, layer.Size));
                }
                else
                {
                    output.RemoveLayerTestPattern(layer.LayerId);
                }
            }
        }
        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"{(toggle ? "Enabled" : "Disabled")} test pattern for layer '{layer.LayerName}'.", 0);
    }

    /// <summary>
    /// Updates the test patterns for a layer on intersecting outputs.
    /// </summary>
    /// <param name="layerId">The layer ID.</param>
    public void UpdateLayerTestPatterns(int layerId)
    {
        var layer = Layers.Find(l => l.LayerId == layerId);
        if (layer == null || !layer.TestPatternEnabled) return;

        Rect2 layerRect = new Rect2(layer.CanvasPosition, layer.Size);
        Rect2 canvasRect = new Rect2(0, 0, Canvas.CanvasSize.X, Canvas.CanvasSize.Y);
        foreach (var output in Outputs)
        {
            Rect2 outputRect = new Rect2(output.CanvasPosition, output.OutputSize);
            Rect2 intersection = layerRect.Intersection(outputRect);
            if (intersection.Size.X > 0 && intersection.Size.Y > 0)
            {
                Rect2 clippedRect = canvasRect.Intersection(outputRect);
                // Convert to output local coordinates
                Vector2 localPos = layer.CanvasPosition - clippedRect.Position;
                output.AddLayerTestPattern(layer.LayerId, layer.LayerName, new Rect2(localPos, layer.Size));
            }
        }
    }

    /// <summary>
    /// Updates the test patterns for all layers on all outputs.
    /// </summary>
    public void UpdateAllLayerTestPatterns()
    {
        foreach (var layer in Layers)
        {
            if (layer.TestPatternEnabled)
            {
                UpdateLayerTestPatterns(layer.LayerId);
            }
        }
    }

    /// <summary>
    /// Gets a list of available displays with their information.
    /// </summary>
    /// <returns>List of DisplayInfo for each detected display.</returns>
    public List<DisplayInfo> GetAvailableDisplays()
    {
        var displays = new List<DisplayInfo>();
        try
        {
            // Calculate display position offset
            var gPrimI = DisplayServer.GetPrimaryScreen();
            var gPrimPos = DisplayServer.ScreenGetPosition(gPrimI);
            var sPrimI = SDL.GetPrimaryDisplay();
            SDL.GetDisplayBounds(sPrimI, out SDL.Rect sPrimRect);

            var offsetX = gPrimPos.X - sPrimRect.X;
            var offsetY = gPrimPos.Y - sPrimRect.Y;

            int screenCount = DisplayServer.GetScreenCount();

            var displayIDs = SDL.GetDisplays(out var sdlCount);
            if (sdlCount != screenCount)
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                    $"Mismatch in display counts: Godot {screenCount}, SDL {sdlCount}. Using Godot count.", 1);
            }

            // Get SDL display data
            var sdlDisplays = new List<(uint ID, Vector2I Position, Vector2I Size)>();
            for (int j = 0; j < sdlCount; j++)
            {
                var id = displayIDs[j];
                if (SDL.GetDisplayBounds(id, out SDL.Rect bounds) != true)
                {
                    _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                        $"SDL_GetDisplayBounds failed for SDL index {j}: {SDL.GetError()}", 2);
                    continue;
                }

                Vector2I pos = new Vector2I(bounds.X, bounds.Y);
                Vector2I size = new Vector2I(bounds.W, bounds.H);

                sdlDisplays.Add((id, pos, size));
            }

            // Compare SDL displays to Godot and name
            for (int i = 0; i < screenCount; i++)
            {
                Vector2I gPos = DisplayServer.ScreenGetPosition(i);
                Vector2I gSize = DisplayServer.ScreenGetSize(i);
                int gDpi = DisplayServer.ScreenGetDpi(i);
                float gRefresh = DisplayServer.ScreenGetRefreshRate(i);

                // Find matching SDL display
                uint matchedID = 0;
                bool found = false;
                for (int k = 0; k < sdlDisplays.Count; k++)
                {
                    var sdl = sdlDisplays[k];
                    if (sdl.Position.X == (gPos.X - offsetX) && sdl.Position.Y == (gPos.Y - offsetY) && sdl.Size.X == gSize.X && sdl.Size.Y == gSize.Y)
                    {
                        matchedID = sdl.ID;
                        found = true;
                        // Remove to avoid duplicate matches
                        sdlDisplays.RemoveAt(k);
                        break;
                    }
                }

                // Try to get actual name via SDL
                string displayName = $"Display {i}";
                if (found)
                {
                    var namePtr = SDL.GetDisplayName(matchedID);
                    if (namePtr != null)
                    {
                        displayName = namePtr;
                    }
                    else
                    {
                        _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                            $"SDL_GetDisplayName failed for display {i}: {SDL.GetError()}", 1);
                    }
                }

                displays.Add(new DisplayInfo
                {
                    Index = i,
                    Name = displayName,
                    Position = gPos,
                    Size = gSize,
                    Dpi = gDpi,
                    RefreshRate = gRefresh
                });
            }
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Error getting available displays: {ex.Message}", 2);
        }
        return displays;
    }

    /// <summary>
    /// Serializes the displays manager data.
    /// </summary>
    /// <returns>Dictionary containing layers and outputs data.</returns>
    public Godot.Collections.Dictionary GetData()
    {
        var data = new Godot.Collections.Dictionary();
        GD.Print($"SAVING CANVAS");
        var canvasData = Canvas.GetData();
        data.Add("Canvas", canvasData);
        
        var layersData = new Godot.Collections.Array();
        foreach (var layer in Layers)
        {
            layersData.Add(layer.GetData());
        }
        data.Add("Layers", layersData);

        var outputsData = new Godot.Collections.Array();
        foreach (var output in Outputs)
        {
            outputsData.Add(output.GetData());
        }
        data.Add("Outputs", outputsData);


        return data;
    }

    /// <summary>
    /// Loads the displays manager data from a dictionary.
    /// </summary>
    /// <param name="data">Dictionary containing layers and outputs data.</param>
    public void LoadFromData(Godot.Collections.Dictionary data)
    {
        if (data.ContainsKey("Canvas"))
        {
            GD.Print($"LOADING CANVAS");
            var canvasData = (Godot.Collections.Dictionary) data["Canvas"];
            Canvas.LoadFromData(canvasData);
        }

        foreach (var layer in Layers)
        {
            Canvas.RemoveChild(layer);
            layer.QueueFree();
        }
        Layers.Clear();
        
        if (data.ContainsKey("Layers"))
        {
            var layersData = (Godot.Collections.Array) data["Layers"];
            foreach (Godot.Collections.Dictionary layerData in layersData)
            {
                var layer = new VideoTargetLayer();
                layer.LoadFromData(layerData);
                Layers.Add(layer);
            }
        }

        if (Layers.Count == 0)
        {
            AddLayer("Default", 0);
        }

        foreach (var output in Outputs)
        {
            RemoveChild(output);
            output.QueueFree();
        }
        Outputs.Clear();

        if (data.ContainsKey("Outputs"))
        {
            var outputsData = (Godot.Collections.Array) data["Outputs"];
            foreach (Godot.Collections.Dictionary outputData in outputsData)
            {
                var output = new VideoOutputDevice();
                output.LoadFromData(outputData);
                AddChild(output);
                Outputs.Add(output);
                output.SetCanvasReference(Canvas);
                output.Show();
                DisplayServer.WindowMoveToForeground(GetWindow().GetWindowId());
            }
        }

        UpdateAllLayerTestPatterns();
    }

    public override void _ExitTree()
    {
        // Layers are children of Canvas, which is freed by GlobalData, so no need to clean them here
        foreach (var output in Outputs)
        {
            RemoveChild(output);
            output.QueueFree();
        }
        Outputs.Clear();
    }
}