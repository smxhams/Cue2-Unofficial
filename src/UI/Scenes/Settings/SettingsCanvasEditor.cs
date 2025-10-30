using Godot;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
    private Panel _canvasOutlinePanel;
    private SubViewportContainer _subViewportContainer;
    private SubViewport _viewport;
    private Control _control;
    private ScrollContainer _scrollContainer;
    private CanvasLayer _canvasLayer;
    private ColorRect _backgroundRect;
    private Button _zoomInButton;
    private Button _zoomOutButton;
    private LineEdit _zoomPercentLineEdit;

    private float _zoom = 0.2f;
    private const float MIN_ZOOM = 0.05f;
    private const float MAX_ZOOM = 3.0f;
    private const int VIEWPORT_WIDTH = 10000;
    private const int VIEWPORT_HEIGHT = 10000;

    private bool _isPanning = false;


    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _canvas = _globalData.VideoCanvas;

        GetWindow().SizeChanged += UpdateZoom;
        TreeExiting += Cleanup;
        
        _videoOutputDeviceCardScene = SceneLoader.LoadPackedScene("uid://cafctoouo75sh", out string _);
        
        _canvasSizeXLineEdit = GetNode<LineEdit>("%CanvasSizeX");
        _canvasSizeYLineEdit = GetNode<LineEdit>("%CanvasSizeY");
        
        _targetLayersContainer = GetNode<VBoxContainer>("%TargetLayersContainer");
        _outputDeviceContainer = GetNode<VBoxContainer>("%OutputDevicesContainer");
        _canvasOutlinePanel = GetNode<Panel>("%CanvasOutlinePanel");
        _subViewportContainer = GetNode<SubViewportContainer>("%SubViewportContainer");
        _viewport = GetNode<SubViewport>("%Viewport");
        _control = GetNode<Control>("%CanvasControl");
        _scrollContainer = GetNode<ScrollContainer>("%ScrollContainer");
        _canvasLayer = GetNode<CanvasLayer>("%CanvasLayer");

        // Add background with diagonal lines
        var backgroundRect = new ColorRect();
        backgroundRect.Size = new Vector2(VIEWPORT_WIDTH, VIEWPORT_HEIGHT);
        backgroundRect.Position = new Vector2(-500, -500);
        backgroundRect.ZIndex = -1; // Behind other elements

        var shader = new Shader();
        shader.Code = @"
            shader_type canvas_item;

            void fragment() {
                vec2 uv = UV * 600.0; // Scale for line density
                float diagonal1 = mod(uv.x + uv.y, 2.0);
                float diagonal2 = mod(uv.x - uv.y, 2.0);
                if (diagonal1 < 0.07 || diagonal2 < 0.07) {
                    COLOR = vec4(0.05, 0.05, 0.05, 1.0); // Grey diagonal lines both ways
                } else {
                    COLOR = vec4(0.0, 0.0, 0.0, 0.0); // Transparent
                }
            }
            ";
        var material = new ShaderMaterial();
        material.Shader = shader;
        backgroundRect.Material = material;

        _backgroundRect = backgroundRect;
        _canvasLayer.AddChild(_backgroundRect);
        _canvasLayer.MoveChild(_backgroundRect, 0);
        _zoomInButton = GetNode<Button>("%ZoomInButton");
        _zoomOutButton = GetNode<Button>("%ZoomOutButton");
        _zoomPercentLineEdit = GetNode<LineEdit>("%ZoomPercentLabel");

        // Set canvas container size to represent the canvas
        _canvasOutlinePanel.CustomMinimumSize = new Vector2(_canvas.CanvasSize.X, _canvas.CanvasSize.Y);
        _subViewportContainer.CustomMinimumSize = new Vector2(_canvas.CanvasSize.X, _canvas.CanvasSize.Y);
        _viewport.Size = new Vector2I(_canvas.CanvasSize.X, _canvas.CanvasSize.Y);

        // Set initial zoom
        UpdateZoom();

        // Load current canvas size into line edits
        _canvasSizeXLineEdit.Text = _canvas.CanvasSize.X.ToString();
        _canvasSizeYLineEdit.Text = _canvas.CanvasSize.Y.ToString();
        
        // Connect text submitted signals
        _canvasSizeXLineEdit.TextSubmitted += OnCanvasSizeSubmitted;
        _canvasSizeYLineEdit.TextSubmitted += OnCanvasSizeSubmitted;

        // Connect zoom signals
        _zoomInButton.Pressed += ZoomIn;
        _zoomOutButton.Pressed += ZoomOut;
        _zoomPercentLineEdit.TextSubmitted += OnZoomPercentSubmitted;
        
        // Create preview
        
        
        PopulateOutputDevices();


    }


    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseEvent)
        {
            if (mouseEvent.ButtonIndex == MouseButton.Middle)
            {
                if (mouseEvent.Pressed)
                {
                    var mousePos = GetViewport().GetMousePosition();
                    var rect = _scrollContainer.GetGlobalRect();
                    if (rect.HasPoint(mousePos))
                    {
                        _isPanning = true;
                    }
                }
                else
                {
                    _isPanning = false;
                }
            }
            else
            {
                var mousePos = GetViewport().GetMousePosition();
                var rect = _scrollContainer.GetGlobalRect();
                if (rect.HasPoint(mousePos))
                {
                    if (mouseEvent.ButtonIndex == MouseButton.WheelUp && Input.IsKeyPressed(Key.Ctrl))
                    {
                        ZoomIn();
                        GetViewport().SetInputAsHandled();
                    }
                    else if (mouseEvent.ButtonIndex == MouseButton.WheelDown && Input.IsKeyPressed(Key.Ctrl))
                    {
                        ZoomOut();
                        GetViewport().SetInputAsHandled();
                    }
                }
            }
        }
        else if (@event is InputEventMouseMotion motionEvent && _isPanning)
        {
            _canvasLayer.Offset += motionEvent.Relative;
            // Clamp to keep outline in view
            Vector2 zoomedSize = new Vector2(_canvas.CanvasSize.X * _zoom, _canvas.CanvasSize.Y * _zoom);
            _canvasLayer.Offset = new Vector2(
                Mathf.Clamp(_canvasLayer.Offset.X, 0, zoomedSize.X * 2 - 50),
                Mathf.Clamp(_canvasLayer.Offset.Y, 0, zoomedSize.Y * 2 - 50)
            );
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        // _testMesh.RotateZ(0.001f); // Commented out for 2D canvas focus
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

            // Update canvas container size
            _canvasOutlinePanel.CustomMinimumSize = new Vector2(_canvas.CanvasSize.X, _canvas.CanvasSize.Y);
            _subViewportContainer.CustomMinimumSize = new Vector2(_canvas.CanvasSize.X, _canvas.CanvasSize.Y);
            _viewport.Size = new Vector2I(_canvas.CanvasSize.X, _canvas.CanvasSize.Y);

            // Update zoom display
            UpdateZoom();

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

    private void ZoomIn()
    {
        float increment = _zoom * 0.1f; // Relative increment
        _zoom = Mathf.Clamp(_zoom + increment, MIN_ZOOM, MAX_ZOOM);
        UpdateZoom();
    }

    private void ZoomOut()
    {
        float increment = _zoom * 0.1f; // Relative increment
        _zoom = Mathf.Clamp(_zoom - increment, MIN_ZOOM, MAX_ZOOM);
        UpdateZoom();
    }

    private void UpdateZoom()
    {
        Vector2 zoomedSize = new Vector2(_canvas.CanvasSize.X * _zoom, _canvas.CanvasSize.Y * _zoom);
        Vector2 margin = new Vector2(100, 100);
        Vector2 panArea = zoomedSize * 2 + margin;
        Vector2 minSize = new Vector2(Mathf.Max(panArea.X, _scrollContainer.Size.X), Mathf.Max(panArea.Y, _scrollContainer.Size.Y));
        _control.Size = zoomedSize;
        _control.Position = Vector2.Zero;
        _subViewportContainer.CustomMinimumSize = minSize;
        _viewport.Size = new Vector2I((int)minSize.X, (int)minSize.Y);
        //_backgroundRect.Size = minSize;
        _canvasOutlinePanel.CustomMinimumSize = zoomedSize;
        UpdateZoomLabel();
    }

    private void UpdateZoomLabel()
    {
        _zoomPercentLineEdit.Text = $"{_zoom * 100:F0}";
    }

    private void OnZoomPercentSubmitted(string newText)
    {
        try
        {
            float percent = float.Parse(newText);
            _zoom = Mathf.Clamp(percent / 100f, MIN_ZOOM, MAX_ZOOM);
            UpdateZoom();
        }
        catch
        {
            UpdateZoomLabel(); // Reset to current value
        }
        _zoomPercentLineEdit.ReleaseFocus();
    }
    
    private void Cleanup()
    {
        GetWindow().SizeChanged -= UpdateZoom;
    }

}
