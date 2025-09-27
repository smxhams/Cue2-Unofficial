using Godot;
using System;
using System.Collections.Generic;
using Cue2.Base.Classes;
using Cue2.Shared;
using SDL3;

namespace Cue2.UI.Scenes.Settings;

public partial class SettingsCanvasEditor : ScrollContainer
{
    private GlobalData _globalData;
    private GlobalSignals _globalSignals;
    private Canvas _canvas;

    private PackedScene _videoOutputDeviceCardScene;
    
    // UI
    private LineEdit _canvasSizeXLineEdit;
    private LineEdit _canvasSizeYLineEdit;
    
    private VBoxContainer _targetLayersContainer;
    private VBoxContainer _outputDeviceContainer;
    private PanelContainer _canvasContainer;

    private MeshInstance3D _testMesh;
    

    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _canvas = _globalData.VideoCanvas;
        
        _videoOutputDeviceCardScene = SceneLoader.LoadPackedScene("uid://cafctoouo75sh", out string _);
        
        _canvasSizeXLineEdit = GetNode<LineEdit>("%CanvasSizeX");
        _canvasSizeYLineEdit = GetNode<LineEdit>("%CanvasSizeY");
        
        _targetLayersContainer = GetNode<VBoxContainer>("%TargetLayersContainer");
        _outputDeviceContainer = GetNode<VBoxContainer>("%OutputDevicesContainer");
        _canvasContainer = GetNode<PanelContainer>("%CanvasContainer");
        
        // Load current canvas size into line edits
        _canvasSizeXLineEdit.Text = _canvas.CanvasSize.X.ToString();
        _canvasSizeYLineEdit.Text = _canvas.CanvasSize.Y.ToString();
        
        // Connect text submitted signals
        _canvasSizeXLineEdit.TextSubmitted += OnCanvasSizeSubmitted;
        _canvasSizeYLineEdit.TextSubmitted += OnCanvasSizeSubmitted;
        
        // Create preview
        
        
        PopulateOutputDevices();

        _testMesh = GetNode<MeshInstance3D>("%MeshInstance3D");
    }

    public override void _Process(double delta)
    {
        _testMesh.RotateZ(0.001f);
    }


    /// <summary>
    /// Populates the output devices container with cards for each detected display.
    /// </summary>
    private void PopulateOutputDevices()
    {
        try
        {
            // To get display names - must use SDL. To match SDL video output to a Godot Display we compare display position.
            
            // Calculate display position offset
            var gPrimI = DisplayServer.GetPrimaryScreen();
            var gPrimPos = DisplayServer.ScreenGetPosition(gPrimI);

            var sPrimI = SDL.GetPrimaryDisplay();
            SDL.GetDisplayBounds(sPrimI, out SDL.Rect sPrimRect);
            
            var offsetX = gPrimPos.X - sPrimRect.X;
            var offsetY = gPrimPos.Y - sPrimRect.Y;

            
            int screenCount = DisplayServer.GetScreenCount();
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"Detected {screenCount} output devices (monitors).", 0);

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

                // Load UI
                PanelContainer instance = _videoOutputDeviceCardScene.Instantiate<PanelContainer>();
                _outputDeviceContainer.AddChild(instance);
                
                // Name label
                var nameLabel = instance.GetNode<Label>("%DisplayName");
                nameLabel.Text = displayName;
                nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
                
                // Resolution label
                var resLabel = instance.GetNode<Label>("%DisplayResolution");
                resLabel.Text = $"Resolution: {gSize.X} x {gSize.Y}";
                resLabel.HorizontalAlignment = HorizontalAlignment.Center;
            }
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"Error populating output devices: {ex.Message}", 2);
        }
    }
    
    /// <summary>
    /// Handles submission of new canvas size from line edits.
    /// </summary>
    /// <param name="newText">The submitted text (ignored).</param>
    private void OnCanvasSizeSubmitted(string newText)
    {
        try
        {
            int x = int.Parse(_canvasSizeXLineEdit.Text);
            int y = int.Parse(_canvasSizeYLineEdit.Text);
            
            _canvas.SetCanvasSize(new Vector2I(x, y));
            
            // Update line edits in case validation changed values
            _canvasSizeXLineEdit.Text = _canvas.CanvasSize.X.ToString();
            _canvasSizeYLineEdit.Text = _canvas.CanvasSize.Y.ToString();
            
            // Preview updates automatically via texture reference
            
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"Canvas size submitted and updated to {x}x{y}.", 0);
        }
        catch (FormatException)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                "Invalid canvas size input: Must be integers.", 2);
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"Error updating canvas size: {ex.Message}", 2);
        }
    }
}
