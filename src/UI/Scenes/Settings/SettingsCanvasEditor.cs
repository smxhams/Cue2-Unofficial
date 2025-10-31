using Godot;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cue2.Base.Classes;
using Cue2.Shared;

namespace Cue2.UI.Scenes.Settings;

public partial class SettingsCanvasEditor : ScrollContainer
{
    private GlobalData _globalData;
    private GlobalSignals _globalSignals;
    private Canvas _canvas;
    private DisplaysManager _displaysManager;

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
    private Dictionary<int, VideoOutputDevice> _activeOutputs = new();
    private List<ColorRect> _outputOutlines = new();


    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _canvas = _globalData.VideoCanvas;
        _displaysManager = GetNode<DisplaysManager>("/root/DisplaysManager");

        _globalSignals.Connect(nameof(GlobalSignals.DisplaysChanged), Callable.From(OnDisplaysChanged));
        _globalSignals.Connect(nameof(GlobalSignals.CanvasSizeChanged), Callable.From<Vector2I>(OnCanvasSizeChanged));

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
        PopulateTargetLayers();
        UpdateCanvasOutlines();


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
            var displays = _displaysManager.GetAvailableDisplays();
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Detected {displays.Count} output devices (monitors).", 0);

            foreach (var display in displays)
            {
                // Load UI
                PanelContainer instance = _videoOutputDeviceCardScene.Instantiate<PanelContainer>();
                _outputDeviceContainer.AddChild(instance);

                // Name label
                var nameLabel = instance.GetNode<Label>("%DisplayName");
                nameLabel.Text = display.Name;
                nameLabel.HorizontalAlignment = HorizontalAlignment.Center;

                // Resolution label
                var resLabel = instance.GetNode<Label>("%DisplayResolution");
                resLabel.Text = $"{display.Size.X} x {display.Size.Y}";
                resLabel.HorizontalAlignment = HorizontalAlignment.Center;

                // Get input fields
                var posXLineEdit = instance.GetNode<LineEdit>("%PosXLineEdit");
                var posYLineEdit = instance.GetNode<LineEdit>("%PosYLineEdit");
                var sizeXLineEdit = instance.GetNode<LineEdit>("%SizeXLineEdit");
                var sizeYLineEdit = instance.GetNode<LineEdit>("%SizeYLineEdit");

                // Set defaults
                posXLineEdit.Text = "0";
                posYLineEdit.Text = "0";
                sizeXLineEdit.Text = display.Size.X.ToString();
                sizeYLineEdit.Text = display.Size.Y.ToString();

                // Check for existing output
                var useOutputCheckButton = instance.GetNode<CheckButton>("%UseOutputCheckButton");
                var existing = _displaysManager.Outputs.Find(o => o.TargetMonitor == display.Index);
                if (existing != null)
                {
                    posXLineEdit.Text = existing.CanvasPosition.X.ToString();
                    posYLineEdit.Text = existing.CanvasPosition.Y.ToString();
                    sizeXLineEdit.Text = existing.Size.X.ToString();
                    sizeYLineEdit.Text = existing.Size.Y.ToString();
                    useOutputCheckButton.ButtonPressed = true;
                    _activeOutputs[display.Index] = existing;
                    UpdateUIForUseOutput(instance, true);
                }

                // Accordion
                var accordianCollapseButton = instance.GetNode<Button>("%AccordianCollapseButton");
                accordianCollapseButton.Icon = GetThemeIcon("Right", "AtlasIcons");
                var displaySettingsAccordianContainer = instance.GetNode<VBoxContainer>("%DisplaySettingsAccordianContainer");
                displaySettingsAccordianContainer.Visible = false;
                accordianCollapseButton.Pressed += () => ToggleAccordian(displaySettingsAccordianContainer, accordianCollapseButton);

                // UseOutputCheckButton
                useOutputCheckButton.Toggled += (bool toggled) => {
                    UpdateUIForUseOutput(instance, toggled);
                    // Update border color
                    var style = (StyleBoxFlat) instance.GetThemeStylebox("panel").Duplicate();
                    style.BorderColor = toggled ? Colors.Red : new Color(0.349484f, 0.349484f, 0.349484f, 1);
                    instance.AddThemeStyleboxOverride("panel", style);
                    if (toggled) {
                        try {
                            float px = float.Parse(posXLineEdit.Text);
                            float py = float.Parse(posYLineEdit.Text);
                            float sx = float.Parse(sizeXLineEdit.Text);
                            float sy = float.Parse(sizeYLineEdit.Text);
                            var canvasPos = new Vector2(px, py);
                            var size = new Vector2(sx, sy);
                            var output = _displaysManager.AddOutput(display.Index, canvasPos, size, display.Name);
                            _activeOutputs[display.Index] = output;
                        } catch (FormatException) {
                            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                                $"Invalid input for display {display.Index}: Position and size must be numbers.", 2);
                        }
                    } else {
                        if (_activeOutputs.TryGetValue(display.Index, out var output)) {
                            _displaysManager.RemoveOutput(output.OutputId);
                            _activeOutputs.Remove(display.Index);
                        }
                    }
                };
                // Connect LineEdit submissions
                posXLineEdit.TextSubmitted += (text) => {
                    if (_activeOutputs.TryGetValue(display.Index, out var outp)) {
                        try {
                            float val = float.Parse(text);
                            _displaysManager.UpdateOutputCanvasPosition(outp.OutputId, new Vector2(val, outp.CanvasPosition.Y));
                        } catch (FormatException) {
                            posXLineEdit.Text = outp.CanvasPosition.X.ToString();
                        }
                    }
                    posXLineEdit.ReleaseFocus();
                };
                posYLineEdit.TextSubmitted += (text) => {
                    if (_activeOutputs.TryGetValue(display.Index, out var outp)) {
                        try {
                            float val = float.Parse(text);
                            _displaysManager.UpdateOutputCanvasPosition(outp.OutputId, new Vector2(outp.CanvasPosition.X, val));
                        } catch (FormatException) {
                            posYLineEdit.Text = outp.CanvasPosition.Y.ToString();
                        }
                    }
                    posYLineEdit.ReleaseFocus();
                };
                sizeXLineEdit.TextSubmitted += (text) => {
                    if (_activeOutputs.TryGetValue(display.Index, out var outp)) {
                        try {
                            float val = float.Parse(text);
                            _displaysManager.UpdateOutputSize(outp.OutputId, new Vector2(val, outp.Size.Y));
                        } catch (FormatException) {
                            sizeXLineEdit.Text = outp.Size.X.ToString();
                        }
                    }
                    sizeXLineEdit.ReleaseFocus();
                };
                sizeYLineEdit.TextSubmitted += (text) => {
                    if (_activeOutputs.TryGetValue(display.Index, out var outp)) {
                        try {
                            float val = float.Parse(text);
                            _displaysManager.UpdateOutputSize(outp.OutputId, new Vector2(outp.Size.X, val));
                        } catch (FormatException) {
                            sizeYLineEdit.Text = outp.Size.Y.ToString();
                        }
                    }
                    sizeYLineEdit.ReleaseFocus();
                };

                // Initial check
                UpdateUIForUseOutput(instance, useOutputCheckButton.ButtonPressed);
                // Set initial border color
                var initialStyle = (StyleBoxFlat) instance.GetThemeStylebox("panel").Duplicate();
                initialStyle.BorderColor = useOutputCheckButton.ButtonPressed ? Colors.Red : new Color(0.349484f, 0.349484f, 0.349484f, 1);
                instance.AddThemeStyleboxOverride("panel", initialStyle);
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
        UpdateCanvasOutlines();
    }

    private void UpdateZoomLabel()
    {
        _zoomPercentLineEdit.Text = $"{_zoom * 100:F0}";
    }

    /// <summary>
    /// Toggles visibility of an accordion container and updates button icon.
    /// </summary>
    /// <param name="accordian">The VBoxContainer to toggle.</param>
    /// <param name="button">The Button controlling the toggle.</param>
    private void ToggleAccordian(VBoxContainer accordian, Button button)
    {
        accordian.Visible = !accordian.Visible;
        button.Icon = GetThemeIcon(accordian.Visible ? "Down" : "Right", "AtlasIcons");
    }

    /// <summary>
    /// Updates UI elements in the display settings accordion based on UseOutputCheckButton state.
    /// </summary>
    /// <param name="card">The card PanelContainer.</param>
    /// <param name="enabled">Whether output is enabled.</param>
    private void UpdateUIForUseOutput(PanelContainer card, bool enabled)
    {
        var accordianContainer = card.GetNode<VBoxContainer>("%DisplaySettingsAccordianContainer");
        UpdateChildrenRecursively(accordianContainer, enabled);
    }

    /// <summary>
    /// Recursively updates LineEdits and Labels in the node tree.
    /// </summary>
    /// <param name="node">The root node to traverse.</param>
    /// <param name="enabled">Whether to enable or disable.</param>
    private void UpdateChildrenRecursively(Node node, bool enabled)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is LineEdit le)
            {
                le.Editable = enabled;
                le.AddThemeColorOverride("font_color", enabled ? Colors.White : Colors.Black);
                le.AddThemeColorOverride("font_placeholder_color", enabled ? Colors.Gray : Colors.Black);
            }
            else if (child is Label label)
            {
                label.AddThemeColorOverride("font_color", enabled ? Colors.White : Colors.DarkGray);
            }
            else
            {
                UpdateChildrenRecursively(child, enabled);
            }
        }
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
    
    /// <summary>
    /// Populates the target layers container with labels for each layer.
    /// </summary>
    private void PopulateTargetLayers()
    {
        // Clear existing
        foreach (var child in _targetLayersContainer.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var layer in _displaysManager.Layers)
        {
            var label = new Label();
            label.Text = layer.LayerName;
            _targetLayersContainer.AddChild(label);
        }
    }

    private void Cleanup()
    {
        GetWindow().SizeChanged -= UpdateZoom;
    }

    private void OnDisplaysChanged()
    {
        UpdateCanvasOutlines();
    }

    private void OnCanvasSizeChanged(Vector2I newSize)
    {
        _canvasSizeXLineEdit.Text = newSize.X.ToString();
        _canvasSizeYLineEdit.Text = newSize.Y.ToString();
    }

    private void UpdateCanvasOutlines()
    {
        foreach (var rect in _outputOutlines) { _canvasLayer.RemoveChild(rect); rect.QueueFree(); }
        _outputOutlines.Clear();
        foreach (var output in _displaysManager.Outputs)
        {
            var outline = new ColorRect();
            outline.Position = output.CanvasPosition * _zoom;
            outline.Size = output.Size * _zoom;
            outline.Color = new Color(1, 0, 0, 0.5f); // semi-transparent red
            _canvasLayer.AddChild(outline);
            _outputOutlines.Add(outline);
        }
    }

}
