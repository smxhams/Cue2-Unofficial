using Godot;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cue2.Base.Classes;
using Cue2.Base.Classes.Devices;
using Cue2.Shared;
using Cue2.UI.Utilities;

namespace Cue2.UI.Scenes.Settings;

public partial class SettingsCanvasEditor : ScrollContainer
{
    private GlobalData _globalData;
    private GlobalSignals _globalSignals;
    private Canvas _canvas;
    private DisplaysManager _displaysManager;

    private PackedScene _videoOutputDeviceCardScene;
    private PackedScene _videoTargetLayerCardScene;
    
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
    private Button _newTargetLayerButton;

    private float _zoom = 0.2f;
    private const float MIN_ZOOM = 0.05f;
    private const float MAX_ZOOM = 3.0f;


    private bool _isPanning = false;
    private Dictionary<int, VideoOutputDevice> _activeOutputs = new();
    private List<Control> _outputOutlines = new();

    private partial class DashedOutline : Control
    {
        public Color BorderColor = Colors.Red;
        public float DashLength = 10f;
        public bool OffsetDash = false;

        public override void _Draw()
        {
            Vector2 size = Size;
            float offset = OffsetDash ? DashLength / 2 : 0;
            // Top
            DrawDashedLine(new Vector2(0, 0), new Vector2(size.X, 0), BorderColor, 2, DashLength, offset);
            // Right
            DrawDashedLine(new Vector2(size.X, 0), new Vector2(size.X, size.Y), BorderColor, 2, DashLength, offset);
            // Bottom
            DrawDashedLine(new Vector2(size.X, size.Y), new Vector2(0, size.Y), BorderColor, 2, DashLength, offset);
            // Left
            DrawDashedLine(new Vector2(0, size.Y), new Vector2(0, 0), BorderColor, 2, DashLength, offset);
        }

        private void DrawDashedLine(Vector2 from, Vector2 to, Color color, float width, float dashLength, float startOffset)
        {
            Vector2 dir = (to - from).Normalized();
            float length = (to - from).Length();
            float current = startOffset;
            while (current < length)
            {
                Vector2 start = from + dir * current;
                float endDist = Mathf.Min(current + dashLength, length);
                Vector2 end = from + dir * endDist;
                DrawLine(start, end, color, width);
                current += dashLength * 2; // skip gap
            }
        }
    }

    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _canvas = DisplaysManager.Canvas;
        _displaysManager = GetNode<DisplaysManager>("/root/DisplaysManager");

        _globalSignals.Connect(nameof(GlobalSignals.DisplaysChanged), Callable.From(OnDisplaysChanged));
        _globalSignals.Connect(nameof(GlobalSignals.CanvasSizeChanged), Callable.From<Vector2I>(OnCanvasSizeChanged));

        GetWindow().SizeChanged += UpdateZoom;
        TreeExiting += Cleanup;
        
        _videoOutputDeviceCardScene = SceneLoader.LoadPackedScene("uid://cafctoouo75sh", out string _);
        _videoTargetLayerCardScene = SceneLoader.LoadPackedScene("uid://duk02eyqpwjxe", out _);
        
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
        _newTargetLayerButton = GetNode<Button>("%AddTargetLayerButton");
        

        // Add background with diagonal lines
        var backgroundRect = new ColorRect();
        backgroundRect.ZIndex = -1; // Behind other elements

        var shader = new Shader();
        shader.Code = @"
            shader_type canvas_item;

            uniform vec2 rect_size;

            void fragment() {
                vec2 uv = UV;
                float aspect = max(rect_size.x, rect_size.y) / min(rect_size.x, rect_size.y);
                if (rect_size.x > rect_size.y) {
                    uv.x *= aspect;
                } else {
                    uv.y *= aspect;
                }
                vec2 scaled_uv = uv * 20.0;
                float diagonal1 = mod(scaled_uv.x + scaled_uv.y, 2.0);
                float diagonal2 = mod(scaled_uv.x - scaled_uv.y, 2.0);
                if (diagonal1 < 0.07 || diagonal2 < 0.07) {
                    COLOR = vec4(0.2, 0.2, 0.2, 1.0); // Grey diagonal lines both ways
                } else {
                    COLOR = vec4(0.0, 0.0, 0.0, 0.0); // Transparent
                }
            }
            ";
        var material = new ShaderMaterial();
        material.Shader = shader;
        material.SetShaderParameter("rect_size", _scrollContainer.Size);
        backgroundRect.Material = material;

        _backgroundRect = backgroundRect;
        var backgroundLayer = new CanvasLayer();
        backgroundLayer.Layer = -1; // Render behind canvas layer
        _viewport.AddChild(backgroundLayer);
        backgroundLayer.AddChild(_backgroundRect);
        _zoomInButton = GetNode<Button>("%ZoomInButton");
        _zoomOutButton = GetNode<Button>("%ZoomOutButton");
        _zoomPercentLineEdit = GetNode<LineEdit>("%ZoomPercentLabel");

        // Set canvas container size to represent the canvas
        _canvasOutlinePanel.CustomMinimumSize = new Vector2(_canvas.CanvasSize.X, _canvas.CanvasSize.Y);
        _subViewportContainer.CustomMinimumSize = new Vector2(_canvas.CanvasSize.X, _canvas.CanvasSize.Y);
        _viewport.Size = new Vector2I(_canvas.CanvasSize.X, _canvas.CanvasSize.Y);

        

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
        
        // Connect 
        _newTargetLayerButton.Pressed += OnNewTargetLayerPressed;


        PopulateOutputDevices();
        PopulateTargetLayers();
        UpdateCanvasOutlines();
        
        // Set initial zoom
        CallDeferred(nameof(UpdateZoom));

        GD.Print($"SettingsCanvasEditor:_ready - Initialised");
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
            // Allow free panning
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Populates the output devices container with cards for each detected display and saved outputs.
    /// </summary>
    private void PopulateOutputDevices()
    {
        GD.Print($"SettingsCanvasEditor:PopulateOutputDevices - Populating output devices");
        try
        {
            // Clear existing cards
            foreach (var child in _outputDeviceContainer.GetChildren())
            {
                child.QueueFree();
            }
            _activeOutputs.Clear();

            var displays = _displaysManager.GetAvailableDisplays();
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Detected {displays.Count} output devices (monitors).", 0);

            // Create cards for current displays
            foreach (var display in displays)
            {
                CreateOutputDeviceCard(display.Index, display.Name, display.Size, true);
            }

            // Create cards for saved outputs not matching current displays
            foreach (var output in DisplaysManager.Outputs)
            {
                if (!_activeOutputs.ContainsKey(output.TargetMonitor))
                {
                    CreateOutputDeviceCard(output.TargetMonitor, output.OutputName, output.OutputSize, false, output);
                }
            }

            CallDeferred(nameof(UpdateCanvasOutlines));
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Error populating output devices: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Creates a VideoOutputDevicesCard for the given display or saved output.
    /// </summary>
    /// <param name="monitorIndex">The monitor index.</param>
    /// <param name="name">The display/output name.</param>
    /// <param name="size">The display size.</param>
    /// <param name="isCurrent">Whether this is a current display.</param>
    /// <param name="savedOutput">The saved VideoOutputDevice if not current.</param>
    private void CreateOutputDeviceCard(int monitorIndex, string name, Vector2I size, bool isCurrent, VideoOutputDevice savedOutput = null)
    {
        GD.Print($"SettingsCanvasEditor:CreateOutputDeviceCard - Loading card: MonitorIndex: {monitorIndex}, Name: {name}, Size: {size}");
        
        // Load UI
        PanelContainer instance = _videoOutputDeviceCardScene.Instantiate<PanelContainer>();
        _outputDeviceContainer.AddChild(instance);

        // Name label
        var nameLabel = instance.GetNode<Label>("%DisplayName");
        if (!isCurrent)
        {
            nameLabel.Text = $"Not Found - {name}";
            nameLabel.AddThemeFontSizeOverride("font_size", 12); // Smaller font
            nameLabel.AddThemeColorOverride("font_color", Colors.Gray);
        }
        else
        {
            nameLabel.Text = name;
        }
        nameLabel.HorizontalAlignment = HorizontalAlignment.Center;

        // Resolution label
        var resLabel = instance.GetNode<Label>("%DisplayResolution");
        resLabel.Text = $"{size.X} x {size.Y}";
        resLabel.HorizontalAlignment = HorizontalAlignment.Center;

        // Get input fields
        var posXLineEdit = instance.GetNode<LineEdit>("%PosXLineEdit");
        var posYLineEdit = instance.GetNode<LineEdit>("%PosYLineEdit");
        var sizeXLineEdit = instance.GetNode<LineEdit>("%SizeXLineEdit");
        var sizeYLineEdit = instance.GetNode<LineEdit>("%SizeYLineEdit");

        // Set defaults or from saved
        VideoOutputDevice output = savedOutput ?? DisplaysManager.Outputs.Find(o => o.TargetMonitor == monitorIndex);
        if (output != null)
        {
            posXLineEdit.Text = output.CanvasPosition.X.ToString();
            posYLineEdit.Text = output.CanvasPosition.Y.ToString();
            sizeXLineEdit.Text = output.OutputSize.X.ToString();
            sizeYLineEdit.Text = output.OutputSize.Y.ToString();
            _activeOutputs[monitorIndex] = output;
        }
        else
        {
            posXLineEdit.Text = "0";
            posYLineEdit.Text = "0";
            sizeXLineEdit.Text = size.X.ToString();
            sizeYLineEdit.Text = size.Y.ToString();
        }

        // Accordion
        var accordianCollapseButton = instance.GetNode<Button>("%AccordianCollapseButton");
        accordianCollapseButton.Icon = GetThemeIcon("Right", "AtlasIcons");
        var displaySettingsAccordianContainer = instance.GetNode<VBoxContainer>("%DisplaySettingsAccordianContainer");
        displaySettingsAccordianContainer.Visible = false;
        accordianCollapseButton.Pressed += () => ToggleAccordian(displaySettingsAccordianContainer, accordianCollapseButton);

        // UseOutputCheckButton
        var useOutputCheckButton = instance.GetNode<CheckButton>("%UseOutputCheckButton");
        useOutputCheckButton.ButtonPressed = output != null;
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
                    var canvasPos = new Vector2I((int)px, (int)py);
                    var sizeVec = new Vector2I((int)sx, (int)sy);
                    var newOutput = _displaysManager.AddOutput(monitorIndex, canvasPos, sizeVec, name);
                    _activeOutputs[monitorIndex] = newOutput;
                } catch (FormatException) {
                    _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                        $"Invalid input for display {monitorIndex}: Position and size must be numbers.", 2);
                }
            } else {
                if (_activeOutputs.TryGetValue(monitorIndex, out var outp)) {
                    _displaysManager.RemoveOutput(outp.OutputId);
                    _activeOutputs.Remove(monitorIndex);
                }
            }
        };

        // TransparentCheckButton
        var transparentCheckButton = instance.GetNode<CheckBox>("%TransparentCheckBox");
        transparentCheckButton.ButtonPressed = output?.Transparent ?? false;
        transparentCheckButton.Toggled += (bool toggled) => {
            if (_activeOutputs.TryGetValue(monitorIndex, out var outp)) {
                outp.SetTransparent(toggled);
            }
        };
        
        // Connect LineEdit submissions
        posXLineEdit.TextSubmitted += (text) => {
            if (_activeOutputs.TryGetValue(monitorIndex, out var outp)) {
                try {
                    int val = int.Parse(text);
                    _displaysManager.UpdateOutputCanvasPosition(outp.OutputId, new Vector2I(val, outp.CanvasPosition.Y));
                } catch (FormatException) {
                    posXLineEdit.Text = outp.CanvasPosition.X.ToString();
                }
            }
            posXLineEdit.ReleaseFocus();
        };
        posYLineEdit.TextSubmitted += (text) => {
            if (_activeOutputs.TryGetValue(monitorIndex, out var outp)) {
                try {
                    int val = int.Parse(text);
                    _displaysManager.UpdateOutputCanvasPosition(outp.OutputId, new Vector2I(outp.CanvasPosition.X, val));
                } catch (FormatException) {
                    posYLineEdit.Text = outp.CanvasPosition.Y.ToString();
                }
            }
            posYLineEdit.ReleaseFocus();
        };
        sizeXLineEdit.TextSubmitted += (text) => {
            if (_activeOutputs.TryGetValue(monitorIndex, out var outp)) {
                try {
                    int val = int.Parse(text);
                    _displaysManager.UpdateOutputSize(outp.OutputId, new Vector2I(val, outp.OutputSize.Y));
                } catch (FormatException) {
                    sizeXLineEdit.Text = outp.OutputSize.X.ToString();
                }
            }
            sizeXLineEdit.ReleaseFocus();
        };
        sizeYLineEdit.TextSubmitted += (text) => {
            if (_activeOutputs.TryGetValue(monitorIndex, out var outp)) {
                try {
                    int val = int.Parse(text);
                    _displaysManager.UpdateOutputSize(outp.OutputId, new Vector2I(outp.OutputSize.X, val));
                } catch (FormatException) {
                    sizeYLineEdit.Text = outp.OutputSize.Y.ToString();
                }
            }
            sizeYLineEdit.ReleaseFocus();
        };
        
        // Test Pattern
        var testButton = instance.GetNode<Button>("%TestPatternCheckBox");
        if (output != null) testButton.ButtonPressed = output.TestPatternStatus();
        testButton.Toggled += (toggled) =>
        {
            if (_activeOutputs.TryGetValue(monitorIndex, out var outp))
            {
                outp.ToggleTestPattern(toggled);
            }
        };

        // Initial UI update
        UpdateUIForUseOutput(instance, useOutputCheckButton.ButtonPressed);
        // Set initial border color
        var initialStyle = (StyleBoxFlat) instance.GetThemeStylebox("panel").Duplicate();
        initialStyle.BorderColor = useOutputCheckButton.ButtonPressed ? Colors.Red : new Color(0.349484f, 0.349484f, 0.349484f, 1);
        instance.AddThemeStyleboxOverride("panel", initialStyle);
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
            
            _canvasSizeXLineEdit.ReleaseFocus();
            _canvasSizeYLineEdit.ReleaseFocus();
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
        _control.Size = zoomedSize;
        _control.Position = Vector2.Zero;
        Vector2 viewportSize = _scrollContainer.Size;
        _subViewportContainer.CustomMinimumSize = viewportSize;
        _viewport.Size = new Vector2I((int)viewportSize.X, (int)viewportSize.Y);
        _backgroundRect.Size = viewportSize;
        (_backgroundRect.Material as ShaderMaterial).SetShaderParameter("rect_size", viewportSize);
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
        //UpdateChildrenRecursively(accordianContainer, enabled);
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
            else if (child is CheckButton cb)
            {
                cb.Disabled = !enabled;
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
    
    private void OnNewTargetLayerPressed()
    {
        string name = $"Layer {DisplaysManager.Layers.Count + 1}";
        int zIndex = DisplaysManager.Layers.Count;
        _displaysManager.AddLayer(name, zIndex);
        PopulateTargetLayers();
        UpdateCanvasOutlines();
        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Added new target layer '{name}'.", 0);
    }

    /// <summary>
    /// Populates the target layers container with cards for each layer.
    /// </summary>
    private void PopulateTargetLayers()
    {
        // Clear existing
        foreach (var child in _targetLayersContainer.GetChildren())
        {
            child.QueueFree();
        }
        
        foreach (var layer in DisplaysManager.Layers)
        {
            CreateTargetLayerCard(layer);
        }
    }

    /// <summary>
    /// Creates a card for the given target layer.
    /// </summary>
    /// <param name="layer">The VideoTargetLayer to create a card for.</param>
    private void CreateTargetLayerCard(VideoTargetLayer layer)
    {
        var instance = _videoTargetLayerCardScene.Instantiate<PanelContainer>();
        _targetLayersContainer.AddChild(instance);

        // Name LineEdit
        var nameLineEdit = instance.GetNode<LineEdit>("%DisplayNameLineEdit");
        nameLineEdit.Text = layer.LayerName;
        nameLineEdit.TextSubmitted += (text) => {
            _displaysManager.UpdateLayerName(layer.LayerId, text);
            nameLineEdit.ReleaseFocus();
        };

        // Delete Button
        var deleteButton = instance.GetNode<Button>("%DeleteLayerButton");
        deleteButton.Icon = GetThemeIcon("DeleteBin", "AtlasIcons");
        deleteButton.Pressed += () => {
            _displaysManager.RemoveLayer(layer.LayerId);
            PopulateTargetLayers();
            UpdateCanvasOutlines();
        };

        // Accordion
        var accordianCollapseButton = instance.GetNode<Button>("%AccordianCollapseButton");
        accordianCollapseButton.Icon = GetThemeIcon("Right", "AtlasIcons");
        var displaySettingsAccordianContainer = instance.GetNode<VBoxContainer>("%DisplaySettingsAccordianContainer");
        displaySettingsAccordianContainer.Visible = false;
        accordianCollapseButton.Pressed += () => ToggleAccordian(displaySettingsAccordianContainer, accordianCollapseButton);

        // Canvas Position
        var posXLineEdit = instance.GetNode<LineEdit>("%PosXLineEdit");
        var posYLineEdit = instance.GetNode<LineEdit>("%PosYLineEdit");
        posXLineEdit.Text = layer.CanvasPosition.X.ToString();
        posYLineEdit.Text = layer.CanvasPosition.Y.ToString();
        posXLineEdit.TextSubmitted += (text) => {
            if (DisplaysManager.Layers.Find(l => l.LayerId == layer.LayerId) != null)
            {
                try
                {
                    int val = int.Parse(text);
                    _displaysManager.UpdateLayerCanvasPosition(layer.LayerId, new Vector2I(val, layer.CanvasPosition.Y));
                    UpdateCanvasOutlines();
                }
                catch (FormatException)
                {
                    posXLineEdit.Text = layer.CanvasPosition.X.ToString();
                }
            }
            posXLineEdit.ReleaseFocus();
        };
        posYLineEdit.TextSubmitted += (text) => {
            if (DisplaysManager.Layers.Find(l => l.LayerId == layer.LayerId) != null)
            {
                try
                {
                    int val = int.Parse(text);
                    _displaysManager.UpdateLayerCanvasPosition(layer.LayerId, new Vector2I(layer.CanvasPosition.X, val));
                    UpdateCanvasOutlines();
                }
                catch (FormatException)
                {
                    posYLineEdit.Text = layer.CanvasPosition.Y.ToString();
                }
            }
            posYLineEdit.ReleaseFocus();
        };

        // Size
        var sizeXLineEdit = instance.GetNode<LineEdit>("%SizeXLineEdit");
        var sizeYLineEdit = instance.GetNode<LineEdit>("%SizeYLineEdit");
        sizeXLineEdit.Text = layer.Size.X.ToString();
        sizeYLineEdit.Text = layer.Size.Y.ToString();
        sizeXLineEdit.TextSubmitted += (text) => {
            if (DisplaysManager.Layers.Find(l => l.LayerId == layer.LayerId) != null)
            {
                try
                {
                    int val = int.Parse(text);
                    _displaysManager.UpdateLayerSize(layer.LayerId, new Vector2I(val, layer.Size.Y));
                    UpdateCanvasOutlines();
                }
                catch (FormatException)
                {
                    sizeXLineEdit.Text = layer.Size.X.ToString();
                }
            }
            sizeXLineEdit.ReleaseFocus();
        };
        sizeYLineEdit.TextSubmitted += (text) => {
            if (DisplaysManager.Layers.Find(l => l.LayerId == layer.LayerId) != null)
            {
                try
                {
                    int val = int.Parse(text);
                    _displaysManager.UpdateLayerSize(layer.LayerId, new Vector2I(layer.Size.X, val));
                    UpdateCanvasOutlines();
                }
                catch (FormatException)
                {
                    sizeYLineEdit.Text = layer.Size.Y.ToString();
                }
            }
            sizeYLineEdit.ReleaseFocus();
        };

        // Transparent CheckBox
        var transparentCheckBox = instance.GetNode<CheckBox>("%TransparentCheckBox");
        transparentCheckBox.ButtonPressed = layer.Transparent;
        transparentCheckBox.Toggled += (bool toggled) => {
            _displaysManager.UpdateLayerTransparent(layer.LayerId, toggled);
        };

        // Test Pattern CheckBox
        var testPatternCheckBox = instance.GetNode<CheckBox>("%TestPatternCheckBox");
        testPatternCheckBox.ButtonPressed = layer.TestPatternEnabled;
        testPatternCheckBox.Toggled += (bool toggled) => {
            _displaysManager.ToggleLayerTestPattern(layer.LayerId, toggled);
        };
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
        foreach (var output in DisplaysManager.Outputs)
        {
            var outline = new DashedOutline();
            var posX = output.CanvasPosition.X * _zoom;
            var posY = output.CanvasPosition.Y * _zoom;
            outline.Position = new Vector2I((int)posX, (int)posY);
            var sizex = output.OutputSize.X * _zoom;
            var sizey = output.OutputSize.Y * _zoom;
            outline.Size = new Vector2I((int)sizex, (int)sizey);
            outline.BorderColor = new Color(1, 0, 0, 0.8f); // red
            outline.OffsetDash = false;
            _canvasLayer.AddChild(outline);
            _outputOutlines.Add(outline);
        }
        foreach (var layer in DisplaysManager.Layers)
        {
            var outline = new DashedOutline();
            var posX = layer.CanvasPosition.X * _zoom;
            var posY = layer.CanvasPosition.Y * _zoom;
            outline.Position = new Vector2I((int)posX, (int)posY);
            var sizex = layer.Size.X * _zoom;
            var sizey = layer.Size.Y * _zoom;
            outline.Size = new Vector2I((int)sizex, (int)sizey);
            outline.BorderColor = new Color(0, 0, 1, 0.8f); // blue
            outline.OffsetDash = true;
            _canvasLayer.AddChild(outline);
            _outputOutlines.Add(outline);
        }
    }

    

}
