using Godot;
using System;
using System.Collections.Generic;
using Cue2.Base.Classes;
using Cue2.Base.Classes.Devices;
using Cue2.Shared;

namespace Cue2.UI.Scenes.Settings;

/// <summary>
/// Canvas editor UI for arranging screens and target layers on the video canvas.
/// Left: Screens + Target Layers trees. Center: interactive stage (move/resize). Right: properties.
/// </summary>
public partial class SettingsCanvasEditor : Control
{
    private enum SelectionKind
    {
        None,
        Canvas,
        Screen,
        Layer
    }

    private enum DragMode
    {
        None,
        Move,
        ResizeNW,
        ResizeN,
        ResizeNE,
        ResizeE,
        ResizeSE,
        ResizeS,
        ResizeSW,
        ResizeW
    }

    private GlobalData _globalData;
    private GlobalSignals _globalSignals;
    private Canvas _canvas;
    private DisplaysManager _displaysManager;

    // Hierarchy – two trees
    private Godot.Tree _screensTree;
    private Godot.Tree _layersTree;
    private Button _canvasSelectButton;
    private Button _addScreenButton;
    private Button _newTargetLayerButton;

    // Canvas view
    private Panel _canvasOutlinePanel;
    private SubViewportContainer _subViewportContainer;
    private SubViewport _viewport;
    private Control _control;
    private ScrollContainer _scrollContainer;
    private CanvasLayer _canvasLayer;
    private ColorRect _backgroundRect;
    private Button _zoomInButton;
    private Button _zoomOutButton;
    private Button _fitButton;
    private LineEdit _zoomPercentLineEdit;

    // Properties – empty / canvas
    private Label _emptyPropsLabel;
    private Control _canvasProps;
    private LineEdit _canvasSizeXLineEdit;
    private LineEdit _canvasSizeYLineEdit;

    // Properties – screen
    private Control _outputProps;
    private Label _outputPropsTitle;
    private Label _outputResolutionLabel;
    private LineEdit _screenNameLineEdit;
    private OptionButton _screenOutputOption;
    private LineEdit _outputSizeXLineEdit;
    private LineEdit _outputSizeYLineEdit;
    private LineEdit _outputPosXLineEdit;
    private LineEdit _outputPosYLineEdit;
    private LineEdit _displayOffsetXLineEdit;
    private LineEdit _displayOffsetYLineEdit;
    private CheckBox _screenKeepAspectCheckBox;
    private CheckBox _outputTransparentCheckBox;
    private CheckBox _outputTestPatternCheckBox;
    private Button _deleteScreenButton;
    private Button _screenOutputResetButton;
    private Button _screenSizeResetButton;
    private Button _screenKeepAspectResetButton;
    private Button _screenPosResetButton;
    private Button _screenDisplayOffsetResetButton;
    private Button _screenTransparentResetButton;
    private Button _screenTestPatternResetButton;

    // Properties – layer
    private Control _layerProps;
    private LineEdit _layerNameLineEdit;
    private LineEdit _layerSizeXLineEdit;
    private LineEdit _layerSizeYLineEdit;
    private LineEdit _layerPosXLineEdit;
    private LineEdit _layerPosYLineEdit;
    private CheckBox _layerKeepAspectCheckBox;
    private CheckBox _layerTransparentCheckBox;
    private CheckBox _layerTestPatternCheckBox;
    private Button _deleteLayerButton;
    private Button _layerSizeResetButton;
    private Button _layerKeepAspectResetButton;
    private Button _layerPosResetButton;
    private Button _layerTransparentResetButton;
    private Button _layerTestPatternResetButton;

    private float _zoom = 0.2f;
    private const float MinZoom = 0.05f;
    private const float MaxZoom = 3.0f;
    private const float HandleSizePx = 10f;
    private const float MinItemSize = 16f;
    private const float FitPadding = 48f;

    private bool _isPanning;
    private bool _isUpdatingProps;
    private bool _isRebuildingTree;
    private bool _isDraggingCanvas;
    /// <summary>Heavy stage setup is deferred until the panel is actually shown.</summary>
    private bool _stageInitialized;

    private SelectionKind _selectionKind = SelectionKind.None;
    private int _selectedScreenId = -1;
    private int _selectedLayerId = -1;

    private DragMode _dragMode = DragMode.None;
    private Vector2 _dragStartCanvasMouse;
    private Vector2I _dragStartPos;
    private Vector2I _dragStartSize;

    /// <summary>
    /// Maps OptionButton item index → monitor index (VirtualMonitorIndex for Virtual Output).
    /// </summary>
    private readonly List<int> _outputOptionMonitorMap = new();

    private readonly List<CanvasItemGizmo> _gizmos = new();

    /// <summary>
    /// Interactive visual for a screen or layer rectangle on the stage.
    /// </summary>
    private partial class CanvasItemGizmo : Control
    {
        public Color BorderColor = Colors.Red;
        public Color FillColor = new Color(1, 0, 0, 0.08f);
        public float DashLength = 10f;
        public bool OffsetDash;
        public bool Selected;
        public string LabelText = string.Empty;
        public bool IsScreen;
        public int ItemId;

        public override void _Draw()
        {
            Vector2 size = Size;
            if (size.X < 1 || size.Y < 1)
                return;

            // Soft fill
            Color fill = Selected ? FillColor.Lightened(0.15f) : FillColor;
            fill.A = Selected ? 0.18f : 0.08f;
            DrawRect(new Rect2(Vector2.Zero, size), fill, true);

            float offset = OffsetDash ? DashLength / 2 : 0;
            float width = Selected ? 2.5f : 1.5f;
            Color border = Selected ? BorderColor.Lightened(0.2f) : BorderColor;
            DrawDashedLine(new Vector2(0, 0), new Vector2(size.X, 0), border, width, DashLength, offset);
            DrawDashedLine(new Vector2(size.X, 0), new Vector2(size.X, size.Y), border, width, DashLength, offset);
            DrawDashedLine(new Vector2(size.X, size.Y), new Vector2(0, size.Y), border, width, DashLength, offset);
            DrawDashedLine(new Vector2(0, size.Y), new Vector2(0, 0), border, width, DashLength, offset);

            // Label
            if (!string.IsNullOrEmpty(LabelText))
            {
                var font = ThemeDB.FallbackFont;
                int fontSize = 11;
                Vector2 textSize = font.GetStringSize(LabelText, HorizontalAlignment.Left, -1, fontSize);
                Vector2 labelPos = new Vector2(4, 2 + textSize.Y);
                DrawRect(new Rect2(2, 2, textSize.X + 6, textSize.Y + 2), new Color(0, 0, 0, 0.55f), true);
                DrawString(font, labelPos, LabelText, HorizontalAlignment.Left, -1, fontSize, Colors.White);
            }

            // Resize handles when selected
            if (Selected)
            {
                float hs = HandleSizePx;
                Color handleFill = Colors.White;
                Color handleBorder = border;
                foreach (var center in GetHandleCenters(size, hs))
                {
                    var r = new Rect2(center - new Vector2(hs, hs) * 0.5f, new Vector2(hs, hs));
                    DrawRect(r, handleFill, true);
                    DrawRect(r, handleBorder, false, 1.5f);
                }
            }
        }

        public override void _Notification(int what)
        {
            // Ensure custom draw runs after first size assignment / enter tree
            if (what == NotificationResized || what == NotificationVisibilityChanged || what == NotificationDraw)
            {
                if (what != NotificationDraw)
                    QueueRedraw();
            }
        }

        public static Vector2[] GetHandleCenters(Vector2 size, float hs)
        {
            float hx = size.X;
            float hy = size.Y;
            return new[]
            {
                new Vector2(0, 0),       // NW
                new Vector2(hx * 0.5f, 0), // N
                new Vector2(hx, 0),      // NE
                new Vector2(hx, hy * 0.5f), // E
                new Vector2(hx, hy),     // SE
                new Vector2(hx * 0.5f, hy), // S
                new Vector2(0, hy),      // SW
                new Vector2(0, hy * 0.5f), // W
            };
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
                current += dashLength * 2;
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

        GetWindow().SizeChanged += OnWindowSizeChanged;
        VisibilityChanged += OnEditorVisibilityChanged;
        TreeExiting += Cleanup;

        // Light setup only — heavy stage work waits until this panel is shown.
        // Settings embeds every panel at open; running SubViewport + SDL display scans here
        // stalls main-thread video presentation for every Settings open.
        BindNodes();
        ConnectSignals();

        _canvasSelectButton.Text = $"Canvas ({_canvas.CanvasSize.X}×{_canvas.CanvasSize.Y})";

        // Never Always — that keeps rendering while the panel is hidden and competes with playback.
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        _viewport.TransparentBg = true;
        _viewport.HandleInputLocally = false;
        _subViewportContainer.Stretch = false;
        _subViewportContainer.CustomMinimumSize = new Vector2(64, 64);
        _viewport.Size = new Vector2I(64, 64);

        _scrollContainer.MouseFilter = MouseFilterEnum.Stop;
        _subViewportContainer.MouseFilter = MouseFilterEnum.Stop;
        _scrollContainer.Resized += OnStageResized;

        if (IsVisibleInTree())
            CallDeferred(nameof(EnsureStageInitialized));

        GD.Print("SettingsCanvasEditor:_Ready - Light init (stage deferred until shown)");
    }

    /// <summary>
    /// One-time heavy stage setup (background shader, trees, gizmos). Deferred until visible.
    /// </summary>
    private void EnsureStageInitialized()
    {
        if (_stageInitialized || !IsInsideTree())
            return;

        _stageInitialized = true;

        _displaysManager.EnsureDefaultScreen();
        SetupBackground();

        _canvasOutlinePanel.CustomMinimumSize = new Vector2(_canvas.CanvasSize.X, _canvas.CanvasSize.Y);

        RebuildTrees(selectCanvas: true);
        UpdateCanvasGizmos();
        UpdateViewportRenderMode();
        CallDeferred(nameof(RefreshStageView));

        GD.Print("SettingsCanvasEditor:EnsureStageInitialized - Stage ready");
    }

    /// <summary>
    /// Enables SubViewport rendering only while this panel is visible in the settings stack.
    /// </summary>
    private void UpdateViewportRenderMode()
    {
        if (_viewport == null || !IsInstanceValid(_viewport))
            return;

        _viewport.RenderTargetUpdateMode = IsVisibleInTree()
            ? SubViewport.UpdateMode.WhenVisible
            : SubViewport.UpdateMode.Disabled;
    }

    /// <summary>
    /// Recomputes zoom/fit and redraws the stage. Safe to call when size was previously zero.
    /// </summary>
    private void RefreshStageView()
    {
        if (!IsInsideTree() || !_stageInitialized || !IsVisibleInTree())
            return;

        // Still not laid out — try again next frame only while visible (avoid infinite defer while hidden)
        if (_scrollContainer.Size.X < 8f || _scrollContainer.Size.Y < 8f)
        {
            CallDeferred(nameof(RefreshStageView));
            return;
        }

        FitToView();
        UpdateCanvasGizmos();
        ForceStageRedraw();
    }

    private void OnEditorVisibilityChanged()
    {
        UpdateViewportRenderMode();

        if (IsVisibleInTree())
        {
            // Heavy init only when user actually opens Canvas Editor (not every Settings open).
            CallDeferred(nameof(EnsureStageInitialized));
            CallDeferred(nameof(RefreshStageView));
        }
    }

    private void OnWindowSizeChanged()
    {
        if (IsVisibleInTree() && _stageInitialized)
            UpdateZoom();
    }

    private void OnStageResized()
    {
        if (!IsVisibleInTree() || !_stageInitialized)
            return;

        // First meaningful size after being blank/hidden: fit rather than leave zoom broken
        if (_viewport.Size.X <= 8 || _viewport.Size.Y <= 8)
            CallDeferred(nameof(RefreshStageView));
        else
            UpdateZoom();
    }

    /// <summary>
    /// Forces SubViewport + gizmo redraw so the stage is not blank after show/layout.
    /// Uses WhenVisible (not Always) so hidden settings do not steal GPU from playback.
    /// </summary>
    private void ForceStageRedraw()
    {
        UpdateViewportRenderMode();

        if (_subViewportContainer != null && IsInstanceValid(_subViewportContainer))
            _subViewportContainer.QueueRedraw();

        if (_canvasOutlinePanel != null && IsInstanceValid(_canvasOutlinePanel))
            _canvasOutlinePanel.QueueRedraw();

        if (_control != null && IsInstanceValid(_control))
            _control.QueueRedraw();

        foreach (var g in _gizmos)
        {
            if (IsInstanceValid(g))
                g.QueueRedraw();
        }
    }

    private void BindNodes()
    {
        _screensTree = GetNode<Godot.Tree>("%ScreensTree");
        _layersTree = GetNode<Godot.Tree>("%LayersTree");
        _canvasSelectButton = GetNode<Button>("%CanvasSelectButton");
        _addScreenButton = GetNode<Button>("%AddScreenButton");
        _newTargetLayerButton = GetNode<Button>("%AddTargetLayerButton");

        _canvasSizeXLineEdit = GetNode<LineEdit>("%CanvasSizeX");
        _canvasSizeYLineEdit = GetNode<LineEdit>("%CanvasSizeY");

        _canvasOutlinePanel = GetNode<Panel>("%CanvasOutlinePanel");
        _subViewportContainer = GetNode<SubViewportContainer>("%SubViewportContainer");
        _viewport = GetNode<SubViewport>("%Viewport");
        _control = GetNode<Control>("%CanvasControl");
        _scrollContainer = GetNode<ScrollContainer>("%ScrollContainer");
        _canvasLayer = GetNode<CanvasLayer>("%CanvasLayer");
        _zoomInButton = GetNode<Button>("%ZoomInButton");
        _zoomOutButton = GetNode<Button>("%ZoomOutButton");
        _fitButton = GetNode<Button>("%FitButton");
        _zoomPercentLineEdit = GetNode<LineEdit>("%ZoomPercentLabel");

        _emptyPropsLabel = GetNode<Label>("%EmptyPropsLabel");
        _canvasProps = GetNode<Control>("%CanvasProps");
        _outputProps = GetNode<Control>("%OutputProps");
        _outputPropsTitle = GetNode<Label>("%OutputPropsTitle");
        _outputResolutionLabel = GetNode<Label>("%OutputResolutionLabel");
        _screenNameLineEdit = GetNode<LineEdit>("%ScreenNameLineEdit");
        _screenOutputOption = GetNode<OptionButton>("%ScreenOutputOption");
        _outputSizeXLineEdit = GetNode<LineEdit>("%SizeXLineEdit");
        _outputSizeYLineEdit = GetNode<LineEdit>("%SizeYLineEdit");
        _outputPosXLineEdit = GetNode<LineEdit>("%PosXLineEdit");
        _outputPosYLineEdit = GetNode<LineEdit>("%PosYLineEdit");
        _displayOffsetXLineEdit = GetNode<LineEdit>("%DisplayOffsetXLineEdit");
        _displayOffsetYLineEdit = GetNode<LineEdit>("%DisplayOffsetYLineEdit");
        _screenKeepAspectCheckBox = GetNode<CheckBox>("%ScreenKeepAspectCheckBox");
        _outputTransparentCheckBox = GetNode<CheckBox>("%OutputTransparentCheckBox");
        _outputTestPatternCheckBox = GetNode<CheckBox>("%OutputTestPatternCheckBox");
        _deleteScreenButton = GetNode<Button>("%DeleteScreenButton");
        _screenOutputResetButton = GetNode<Button>("%ScreenOutputResetButton");
        _screenSizeResetButton = GetNode<Button>("%ScreenSizeResetButton");
        _screenKeepAspectResetButton = GetNode<Button>("%ScreenKeepAspectResetButton");
        _screenPosResetButton = GetNode<Button>("%ScreenPosResetButton");
        _screenDisplayOffsetResetButton = GetNode<Button>("%ScreenDisplayOffsetResetButton");
        _screenTransparentResetButton = GetNode<Button>("%ScreenTransparentResetButton");
        _screenTestPatternResetButton = GetNode<Button>("%ScreenTestPatternResetButton");

        _layerProps = GetNode<Control>("%LayerProps");
        _layerNameLineEdit = GetNode<LineEdit>("%LayerNameLineEdit");
        _layerSizeXLineEdit = GetNode<LineEdit>("%LayerSizeXLineEdit");
        _layerSizeYLineEdit = GetNode<LineEdit>("%LayerSizeYLineEdit");
        _layerPosXLineEdit = GetNode<LineEdit>("%LayerPosXLineEdit");
        _layerPosYLineEdit = GetNode<LineEdit>("%LayerPosYLineEdit");
        _layerKeepAspectCheckBox = GetNode<CheckBox>("%LayerKeepAspectCheckBox");
        _layerTransparentCheckBox = GetNode<CheckBox>("%LayerTransparentCheckBox");
        _layerTestPatternCheckBox = GetNode<CheckBox>("%LayerTestPatternCheckBox");
        _deleteLayerButton = GetNode<Button>("%DeleteLayerButton");
        _layerSizeResetButton = GetNode<Button>("%LayerSizeResetButton");
        _layerKeepAspectResetButton = GetNode<Button>("%LayerKeepAspectResetButton");
        _layerPosResetButton = GetNode<Button>("%LayerPosResetButton");
        _layerTransparentResetButton = GetNode<Button>("%LayerTransparentResetButton");
        _layerTestPatternResetButton = GetNode<Button>("%LayerTestPatternResetButton");

        SetupResetButtonIcons();
    }

    private void SetupResetButtonIcons()
    {
        var icon = GetThemeIcon("Refresh", "AtlasIcons");
        foreach (var btn in new[]
                 {
                     _screenOutputResetButton, _screenSizeResetButton, _screenKeepAspectResetButton,
                     _screenPosResetButton, _screenDisplayOffsetResetButton, _screenTransparentResetButton,
                     _screenTestPatternResetButton, _layerSizeResetButton, _layerKeepAspectResetButton,
                     _layerPosResetButton, _layerTransparentResetButton, _layerTestPatternResetButton
                 })
        {
            if (btn != null)
                btn.Icon = icon;
        }
    }

    private void SetupBackground()
    {
        var backgroundRect = new ColorRect();
        backgroundRect.ZIndex = -1;

        var shader = new Shader();
        shader.Code = @"
            shader_type canvas_item;
            uniform vec2 rect_size;
            void fragment() {
                vec2 uv = UV;
                float aspect = max(rect_size.x, rect_size.y) / min(rect_size.x, rect_size.y);
                if (rect_size.x > rect_size.y) { uv.x *= aspect; } else { uv.y *= aspect; }
                vec2 scaled_uv = uv * 20.0;
                float diagonal1 = mod(scaled_uv.x + scaled_uv.y, 2.0);
                float diagonal2 = mod(scaled_uv.x - scaled_uv.y, 2.0);
                if (diagonal1 < 0.07 || diagonal2 < 0.07) {
                    COLOR = vec4(0.2, 0.2, 0.2, 1.0);
                } else {
                    COLOR = vec4(0.0, 0.0, 0.0, 0.0);
                }
            }
            ";
        var material = new ShaderMaterial();
        material.Shader = shader;
        material.SetShaderParameter("rect_size", _scrollContainer.Size);
        backgroundRect.Material = material;

        _backgroundRect = backgroundRect;
        var backgroundLayer = new CanvasLayer();
        backgroundLayer.Layer = -1;
        _viewport.AddChild(backgroundLayer);
        backgroundLayer.AddChild(_backgroundRect);
    }

    private void ConnectSignals()
    {
        _screensTree.ItemSelected += OnScreensTreeItemSelected;
        _layersTree.ItemSelected += OnLayersTreeItemSelected;
        _canvasSelectButton.Pressed += OnCanvasSelectPressed;
        _addScreenButton.Pressed += OnNewScreenPressed;
        _newTargetLayerButton.Pressed += OnNewTargetLayerPressed;

        _canvasSizeXLineEdit.TextSubmitted += OnCanvasSizeSubmitted;
        _canvasSizeYLineEdit.TextSubmitted += OnCanvasSizeSubmitted;

        _zoomInButton.Pressed += ZoomIn;
        _zoomOutButton.Pressed += ZoomOut;
        _fitButton.Pressed += FitToView;
        _zoomPercentLineEdit.TextSubmitted += OnZoomPercentSubmitted;

        _screenNameLineEdit.TextSubmitted += OnScreenNameSubmitted;
        _screenOutputOption.ItemSelected += OnScreenOutputSelected;
        _outputSizeXLineEdit.TextSubmitted += OnScreenSizeXSubmitted;
        _outputSizeYLineEdit.TextSubmitted += OnScreenSizeYSubmitted;
        _outputPosXLineEdit.TextSubmitted += OnScreenPosXSubmitted;
        _outputPosYLineEdit.TextSubmitted += OnScreenPosYSubmitted;
        _displayOffsetXLineEdit.TextSubmitted += OnDisplayOffsetXSubmitted;
        _displayOffsetYLineEdit.TextSubmitted += OnDisplayOffsetYSubmitted;
        _screenKeepAspectCheckBox.Toggled += OnScreenKeepAspectToggled;
        _outputTransparentCheckBox.Toggled += OnScreenTransparentToggled;
        _outputTestPatternCheckBox.Toggled += OnScreenTestPatternToggled;
        _deleteScreenButton.Pressed += OnDeleteScreenPressed;
        _screenOutputResetButton.Pressed += OnScreenOutputResetPressed;
        _screenSizeResetButton.Pressed += OnScreenSizeResetPressed;
        _screenKeepAspectResetButton.Pressed += OnScreenKeepAspectResetPressed;
        _screenPosResetButton.Pressed += OnScreenPosResetPressed;
        _screenDisplayOffsetResetButton.Pressed += OnScreenDisplayOffsetResetPressed;
        _screenTransparentResetButton.Pressed += OnScreenTransparentResetPressed;
        _screenTestPatternResetButton.Pressed += OnScreenTestPatternResetPressed;

        _layerNameLineEdit.TextSubmitted += OnLayerNameSubmitted;
        _layerSizeXLineEdit.TextSubmitted += OnLayerSizeXSubmitted;
        _layerSizeYLineEdit.TextSubmitted += OnLayerSizeYSubmitted;
        _layerPosXLineEdit.TextSubmitted += OnLayerPosXSubmitted;
        _layerPosYLineEdit.TextSubmitted += OnLayerPosYSubmitted;
        _layerKeepAspectCheckBox.Toggled += OnLayerKeepAspectToggled;
        _layerTransparentCheckBox.Toggled += OnLayerTransparentToggled;
        _layerTestPatternCheckBox.Toggled += OnLayerTestPatternToggled;
        _deleteLayerButton.Pressed += OnDeleteLayerPressed;
        _layerSizeResetButton.Pressed += OnLayerSizeResetPressed;
        _layerKeepAspectResetButton.Pressed += OnLayerKeepAspectResetPressed;
        _layerPosResetButton.Pressed += OnLayerPosResetPressed;
        _layerTransparentResetButton.Pressed += OnLayerTransparentResetPressed;
        _layerTestPatternResetButton.Pressed += OnLayerTestPatternResetPressed;
    }

    #region Stage input (move / resize / select)

    public override void _Input(InputEvent @event)
    {
        bool overStage = IsMouseOverStage();

        if (@event is InputEventMouseButton mouseEvent)
        {
            if (mouseEvent.ButtonIndex == MouseButton.Middle)
            {
                if (mouseEvent.Pressed && overStage)
                    _isPanning = true;
                else if (!mouseEvent.Pressed)
                    _isPanning = false;
            }
            else if (mouseEvent.ButtonIndex == MouseButton.Left)
            {
                if (mouseEvent.Pressed && overStage)
                {
                    if (BeginCanvasInteraction())
                        GetViewport().SetInputAsHandled();
                }
                else if (!mouseEvent.Pressed && _isDraggingCanvas)
                {
                    EndCanvasInteraction();
                    GetViewport().SetInputAsHandled();
                }
            }
            else if (overStage)
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
        else if (@event is InputEventMouseMotion motionEvent)
        {
            if (_isPanning)
            {
                _canvasLayer.Offset += motionEvent.Relative;
                GetViewport().SetInputAsHandled();
            }
            else if (_isDraggingCanvas && _dragMode != DragMode.None)
            {
                UpdateCanvasDrag();
                GetViewport().SetInputAsHandled();
            }
            else if (overStage)
            {
                UpdateStageCursor();
            }
        }
    }

    private bool IsMouseOverStage()
    {
        if (_scrollContainer == null || !IsInstanceValid(_scrollContainer))
            return false;
        return _scrollContainer.GetGlobalRect().HasPoint(GetViewport().GetMousePosition());
    }

    /// <summary>
    /// Mouse position in canvas units (not zoomed).
    /// </summary>
    private Vector2 GetCanvasMousePosition()
    {
        Vector2 local = _subViewportContainer.GetLocalMousePosition();
        Vector2 inLayer = local - _canvasLayer.Offset;
        if (_zoom <= 0.0001f)
            return Vector2.Zero;
        return inLayer / _zoom;
    }

    /// <summary>
    /// Mouse position in zoomed layer pixels (matches gizmo coordinates).
    /// </summary>
    private Vector2 GetLayerMousePosition()
    {
        Vector2 local = _subViewportContainer.GetLocalMousePosition();
        return local - _canvasLayer.Offset;
    }

    private bool BeginCanvasInteraction()
    {
        Vector2 layerMouse = GetLayerMousePosition();
        Vector2 canvasMouse = GetCanvasMousePosition();

        // 1) Prefer handles / body of current selection
        if (_selectionKind == SelectionKind.Screen || _selectionKind == SelectionKind.Layer)
        {
            if (TryGetSelectedRect(out Vector2I pos, out Vector2I size))
            {
                Rect2 zoomed = new Rect2(pos.X * _zoom, pos.Y * _zoom, size.X * _zoom, size.Y * _zoom);
                var handle = HitTestHandle(zoomed, layerMouse);
                if (handle != DragMode.None)
                {
                    StartDrag(handle, canvasMouse, pos, size);
                    return true;
                }

                if (zoomed.Grow(HandleSizePx * 0.5f).HasPoint(layerMouse))
                {
                    StartDrag(DragMode.Move, canvasMouse, pos, size);
                    return true;
                }
            }
        }

        // 2) Hit-test items (layers first — typically on top conceptually — then screens)
        // Prefer smallest area under cursor so nested items are selectable.
        if (TryHitTestItem(canvasMouse, out SelectionKind kind, out int id))
        {
            if (kind == SelectionKind.Screen)
            {
                SelectScreenInTree(id);
                ApplySelection(SelectionKind.Screen, id, -1);
            }
            else if (kind == SelectionKind.Layer)
            {
                SelectLayerInTree(id);
                ApplySelection(SelectionKind.Layer, -1, id);
            }

            if (TryGetSelectedRect(out Vector2I pos, out Vector2I size))
            {
                StartDrag(DragMode.Move, canvasMouse, pos, size);
                return true;
            }
        }

        return false;
    }

    private bool TryHitTestItem(Vector2 canvasMouse, out SelectionKind kind, out int id)
    {
        kind = SelectionKind.None;
        id = -1;
        float bestArea = float.MaxValue;

        // Layers — prefer smaller hit area
        foreach (var layer in DisplaysManager.Layers)
        {
            var r = new Rect2(layer.CanvasPosition, layer.Size);
            if (!r.HasPoint(canvasMouse))
                continue;
            float area = layer.Size.X * (float)layer.Size.Y;
            if (area < bestArea)
            {
                bestArea = area;
                kind = SelectionKind.Layer;
                id = layer.LayerId;
            }
        }

        foreach (var screen in DisplaysManager.Screens)
        {
            var r = new Rect2(screen.CanvasPosition, screen.OutputSize);
            if (!r.HasPoint(canvasMouse))
                continue;
            float area = screen.OutputSize.X * (float)screen.OutputSize.Y;
            if (area < bestArea)
            {
                bestArea = area;
                kind = SelectionKind.Screen;
                id = screen.OutputId;
            }
        }

        return kind != SelectionKind.None;
    }

    private bool TryGetSelectedRect(out Vector2I pos, out Vector2I size)
    {
        pos = Vector2I.Zero;
        size = Vector2I.Zero;

        if (_selectionKind == SelectionKind.Screen)
        {
            var screen = _displaysManager.GetOutputById(_selectedScreenId);
            if (screen == null)
                return false;
            pos = screen.CanvasPosition;
            size = screen.OutputSize;
            return true;
        }

        if (_selectionKind == SelectionKind.Layer)
        {
            var layer = DisplaysManager.GetLayerById(_selectedLayerId);
            if (layer == null)
                return false;
            pos = layer.CanvasPosition;
            size = layer.Size;
            return true;
        }

        return false;
    }

    private static DragMode HitTestHandle(Rect2 zoomedRect, Vector2 layerMouse)
    {
        float hs = HandleSizePx;
        var centers = CanvasItemGizmo.GetHandleCenters(zoomedRect.Size, hs);
        var modes = new[]
        {
            DragMode.ResizeNW, DragMode.ResizeN, DragMode.ResizeNE, DragMode.ResizeE,
            DragMode.ResizeSE, DragMode.ResizeS, DragMode.ResizeSW, DragMode.ResizeW
        };

        for (int i = 0; i < centers.Length; i++)
        {
            Vector2 world = zoomedRect.Position + centers[i];
            var handleRect = new Rect2(world - new Vector2(hs, hs) * 0.5f, new Vector2(hs, hs));
            // Slightly larger hit target
            if (handleRect.Grow(2f).HasPoint(layerMouse))
                return modes[i];
        }

        return DragMode.None;
    }

    private void StartDrag(DragMode mode, Vector2 canvasMouse, Vector2I pos, Vector2I size)
    {
        _isDraggingCanvas = true;
        _dragMode = mode;
        _dragStartCanvasMouse = canvasMouse;
        _dragStartPos = pos;
        _dragStartSize = size;
    }

    private void UpdateCanvasDrag()
    {
        if (!_isDraggingCanvas || _dragMode == DragMode.None)
            return;

        Vector2 canvasMouse = GetCanvasMousePosition();
        Vector2 delta = canvasMouse - _dragStartCanvasMouse;
        Vector2I d = new Vector2I(Mathf.RoundToInt(delta.X), Mathf.RoundToInt(delta.Y));

        Vector2I newPos = _dragStartPos;
        Vector2I newSize = _dragStartSize;

        switch (_dragMode)
        {
            case DragMode.Move:
                newPos = _dragStartPos + d;
                break;
            case DragMode.ResizeE:
                newSize = new Vector2I(Mathf.Max((int)MinItemSize, _dragStartSize.X + d.X), _dragStartSize.Y);
                break;
            case DragMode.ResizeS:
                newSize = new Vector2I(_dragStartSize.X, Mathf.Max((int)MinItemSize, _dragStartSize.Y + d.Y));
                break;
            case DragMode.ResizeSE:
                newSize = new Vector2I(
                    Mathf.Max((int)MinItemSize, _dragStartSize.X + d.X),
                    Mathf.Max((int)MinItemSize, _dragStartSize.Y + d.Y));
                break;
            case DragMode.ResizeW:
            {
                int right = _dragStartPos.X + _dragStartSize.X;
                newPos = new Vector2I(Mathf.Min(right - (int)MinItemSize, _dragStartPos.X + d.X), _dragStartPos.Y);
                newSize = new Vector2I(right - newPos.X, _dragStartSize.Y);
                break;
            }
            case DragMode.ResizeN:
            {
                int bottom = _dragStartPos.Y + _dragStartSize.Y;
                newPos = new Vector2I(_dragStartPos.X, Mathf.Min(bottom - (int)MinItemSize, _dragStartPos.Y + d.Y));
                newSize = new Vector2I(_dragStartSize.X, bottom - newPos.Y);
                break;
            }
            case DragMode.ResizeNW:
            {
                int right = _dragStartPos.X + _dragStartSize.X;
                int bottom = _dragStartPos.Y + _dragStartSize.Y;
                newPos = new Vector2I(
                    Mathf.Min(right - (int)MinItemSize, _dragStartPos.X + d.X),
                    Mathf.Min(bottom - (int)MinItemSize, _dragStartPos.Y + d.Y));
                newSize = new Vector2I(right - newPos.X, bottom - newPos.Y);
                break;
            }
            case DragMode.ResizeNE:
            {
                int bottom = _dragStartPos.Y + _dragStartSize.Y;
                newPos = new Vector2I(_dragStartPos.X, Mathf.Min(bottom - (int)MinItemSize, _dragStartPos.Y + d.Y));
                newSize = new Vector2I(
                    Mathf.Max((int)MinItemSize, _dragStartSize.X + d.X),
                    bottom - newPos.Y);
                break;
            }
            case DragMode.ResizeSW:
            {
                int right = _dragStartPos.X + _dragStartSize.X;
                newPos = new Vector2I(Mathf.Min(right - (int)MinItemSize, _dragStartPos.X + d.X), _dragStartPos.Y);
                newSize = new Vector2I(right - newPos.X, Mathf.Max((int)MinItemSize, _dragStartSize.Y + d.Y));
                break;
            }
        }

        if (_dragMode != DragMode.Move && IsSelectedKeepAspect())
            ApplyKeepAspectToDrag(ref newPos, ref newSize);

        ApplyLiveRect(newPos, newSize);
    }

    private bool IsSelectedKeepAspect()
    {
        if (_selectionKind == SelectionKind.Screen)
            return GetSelectedScreen()?.KeepAspect ?? false;
        if (_selectionKind == SelectionKind.Layer)
            return DisplaysManager.GetLayerById(_selectedLayerId)?.KeepAspect ?? false;
        return false;
    }

    /// <summary>
    /// Constrains drag resize to the aspect ratio at drag start.
    /// </summary>
    private void ApplyKeepAspectToDrag(ref Vector2I newPos, ref Vector2I newSize)
    {
        float aspect = _dragStartSize.X / (float)Mathf.Max(1, _dragStartSize.Y);

        switch (_dragMode)
        {
            case DragMode.ResizeE:
            case DragMode.ResizeW:
                newSize = new Vector2I(newSize.X, Mathf.Max((int)MinItemSize, Mathf.RoundToInt(newSize.X / aspect)));
                break;
            case DragMode.ResizeN:
            case DragMode.ResizeS:
                newSize = new Vector2I(Mathf.Max((int)MinItemSize, Mathf.RoundToInt(newSize.Y * aspect)), newSize.Y);
                break;
            case DragMode.ResizeSE:
                newSize = new Vector2I(newSize.X, Mathf.Max((int)MinItemSize, Mathf.RoundToInt(newSize.X / aspect)));
                break;
            case DragMode.ResizeNE:
            {
                int bottom = _dragStartPos.Y + _dragStartSize.Y;
                newSize = new Vector2I(newSize.X, Mathf.Max((int)MinItemSize, Mathf.RoundToInt(newSize.X / aspect)));
                newPos = new Vector2I(newPos.X, bottom - newSize.Y);
                break;
            }
            case DragMode.ResizeSW:
            {
                int right = _dragStartPos.X + _dragStartSize.X;
                newSize = new Vector2I(newSize.X, Mathf.Max((int)MinItemSize, Mathf.RoundToInt(newSize.X / aspect)));
                newPos = new Vector2I(right - newSize.X, newPos.Y);
                break;
            }
            case DragMode.ResizeNW:
            {
                int right = _dragStartPos.X + _dragStartSize.X;
                int bottom = _dragStartPos.Y + _dragStartSize.Y;
                newSize = new Vector2I(newSize.X, Mathf.Max((int)MinItemSize, Mathf.RoundToInt(newSize.X / aspect)));
                newPos = new Vector2I(right - newSize.X, bottom - newSize.Y);
                break;
            }
        }
    }

    /// <summary>
    /// Returns a new size keeping aspect of <paramref name="reference"/> when changing width or height.
    /// </summary>
    private static Vector2I SizeWithKeepAspect(Vector2I reference, int? newWidth, int? newHeight)
    {
        if (reference.X <= 0 || reference.Y <= 0)
        {
            return new Vector2I(
                newWidth ?? Mathf.Max(1, reference.X),
                newHeight ?? Mathf.Max(1, reference.Y));
        }

        float aspect = reference.X / (float)reference.Y;
        if (newWidth.HasValue)
        {
            int w = Mathf.Max(1, newWidth.Value);
            return new Vector2I(w, Mathf.Max(1, Mathf.RoundToInt(w / aspect)));
        }

        if (newHeight.HasValue)
        {
            int h = Mathf.Max(1, newHeight.Value);
            return new Vector2I(Mathf.Max(1, Mathf.RoundToInt(h * aspect)), h);
        }

        return reference;
    }

    private void ApplyLiveRect(Vector2I pos, Vector2I size)
    {
        if (_selectionKind == SelectionKind.Screen)
        {
            var screen = _displaysManager.GetOutputById(_selectedScreenId);
            if (screen == null)
                return;
            screen.CanvasPosition = pos;
            screen.OutputSize = size;
            // Defer expensive window updates until drag ends
        }
        else if (_selectionKind == SelectionKind.Layer)
        {
            var layer = DisplaysManager.GetLayerById(_selectedLayerId);
            if (layer == null)
                return;
            layer.CanvasPosition = pos;
            layer.Size = size;
        }
        else
        {
            return;
        }

        SyncPropsFromSelectionLive(pos, size);
        UpdateCanvasGizmos();
    }

    private void SyncPropsFromSelectionLive(Vector2I pos, Vector2I size)
    {
        _isUpdatingProps = true;
        try
        {
            if (_selectionKind == SelectionKind.Screen && _outputProps.Visible)
            {
                _outputPosXLineEdit.Text = pos.X.ToString();
                _outputPosYLineEdit.Text = pos.Y.ToString();
                _outputSizeXLineEdit.Text = size.X.ToString();
                _outputSizeYLineEdit.Text = size.Y.ToString();
            }
            else if (_selectionKind == SelectionKind.Layer && _layerProps.Visible)
            {
                _layerPosXLineEdit.Text = pos.X.ToString();
                _layerPosYLineEdit.Text = pos.Y.ToString();
                _layerSizeXLineEdit.Text = size.X.ToString();
                _layerSizeYLineEdit.Text = size.Y.ToString();
            }
        }
        finally
        {
            _isUpdatingProps = false;
        }
    }

    private void EndCanvasInteraction()
    {
        if (!_isDraggingCanvas)
            return;

        _isDraggingCanvas = false;
        var mode = _dragMode;
        _dragMode = DragMode.None;

        if (mode == DragMode.None)
            return;

        if (!TryGetSelectedRect(out Vector2I pos, out Vector2I size))
            return;

        // Commit through DisplaysManager so outputs / test patterns update
        if (_selectionKind == SelectionKind.Screen)
        {
            _displaysManager.UpdateOutputCanvasPosition(_selectedScreenId, pos);
            _displaysManager.UpdateOutputSize(_selectedScreenId, size);
            LoadScreenProps();
        }
        else if (_selectionKind == SelectionKind.Layer)
        {
            _displaysManager.UpdateLayerCanvasPosition(_selectedLayerId, pos);
            _displaysManager.UpdateLayerSize(_selectedLayerId, size);
            LoadLayerProps();
        }

        UpdateCanvasGizmos();
    }

    private void UpdateStageCursor()
    {
        if (!TryGetSelectedRect(out Vector2I pos, out Vector2I size))
        {
            MouseDefaultCursorShape = CursorShape.Arrow;
            return;
        }

        Rect2 zoomed = new Rect2(pos.X * _zoom, pos.Y * _zoom, size.X * _zoom, size.Y * _zoom);
        var handle = HitTestHandle(zoomed, GetLayerMousePosition());
        MouseDefaultCursorShape = handle switch
        {
            DragMode.ResizeN or DragMode.ResizeS => CursorShape.Vsize,
            DragMode.ResizeE or DragMode.ResizeW => CursorShape.Hsize,
            DragMode.ResizeNE or DragMode.ResizeSW => CursorShape.Bdiagsize,
            DragMode.ResizeNW or DragMode.ResizeSE => CursorShape.Fdiagsize,
            DragMode.None when zoomed.HasPoint(GetLayerMousePosition()) => CursorShape.Move,
            _ => CursorShape.Arrow
        };
    }

    #endregion

    #region Trees

    private void RebuildTrees(bool selectCanvas = false)
    {
        if (_isDraggingCanvas)
            return;

        _isRebuildingTree = true;
        try
        {
            var prevKind = _selectionKind;
            var prevScreenId = _selectedScreenId;
            var prevLayerId = _selectedLayerId;

            RebuildScreensTree();
            RebuildLayersTree();

            if (selectCanvas)
            {
                DeselectTrees();
                ApplySelection(SelectionKind.Canvas, -1, -1);
            }
            else if (prevKind == SelectionKind.Screen && prevScreenId >= 0)
            {
                SelectScreenInTree(prevScreenId);
                ApplySelection(SelectionKind.Screen, prevScreenId, -1);
            }
            else if (prevKind == SelectionKind.Layer && prevLayerId >= 0)
            {
                SelectLayerInTree(prevLayerId);
                ApplySelection(SelectionKind.Layer, -1, prevLayerId);
            }
            else if (prevKind == SelectionKind.Canvas)
            {
                ApplySelection(SelectionKind.Canvas, -1, -1);
            }
            else
            {
                ApplySelection(SelectionKind.None, -1, -1);
            }
        }
        finally
        {
            _isRebuildingTree = false;
        }
    }

    private void RebuildScreensTree()
    {
        _screensTree.Clear();
        var root = _screensTree.CreateItem();
        root.SetText(0, "Screens");

        foreach (var screen in DisplaysManager.Screens)
        {
            var item = _screensTree.CreateItem(root);
            string dest = screen.IsVirtual ? "Virtual" : GetMonitorLabel(screen.TargetMonitor);
            item.SetText(0, $"{screen.OutputName}  [{dest}]");
            item.SetMetadata(0, screen.OutputId);
            item.SetTooltipText(0,
                $"{screen.OutputName}\n{screen.OutputSize.X}×{screen.OutputSize.Y} @ {screen.CanvasPosition}\nOutput: {dest}");
            item.SetCustomColor(0, screen.IsVirtual
                ? new Color(0.75f, 0.55f, 0.45f)
                : new Color(1f, 0.55f, 0.45f));
        }
    }

    private void RebuildLayersTree()
    {
        _layersTree.Clear();
        var root = _layersTree.CreateItem();
        root.SetText(0, "Layers");

        foreach (var layer in DisplaysManager.Layers)
        {
            var item = _layersTree.CreateItem(root);
            item.SetText(0, layer.LayerName);
            item.SetMetadata(0, layer.LayerId);
            item.SetTooltipText(0, $"{layer.LayerName}\n{layer.Size.X}×{layer.Size.Y} @ {layer.CanvasPosition}");
            item.SetCustomColor(0, new Color(0.55f, 0.75f, 1f));
        }
    }

    private string GetMonitorLabel(int monitorIndex)
    {
        if (monitorIndex < 0)
            return "Virtual";

        var displays = _displaysManager.GetAvailableDisplays();
        foreach (var d in displays)
        {
            if (d.Index == monitorIndex)
                return d.Name;
        }

        return $"Monitor {monitorIndex} (missing)";
    }

    private void DeselectTrees()
    {
        _screensTree.DeselectAll();
        _layersTree.DeselectAll();
    }

    private void SelectScreenInTree(int screenId)
    {
        _layersTree.DeselectAll();
        var root = _screensTree.GetRoot();
        if (root == null)
            return;

        var child = root.GetFirstChild();
        while (child != null)
        {
            if (child.GetMetadata(0).AsInt32() == screenId)
            {
                child.Select(0);
                return;
            }
            child = child.GetNext();
        }
    }

    private void SelectLayerInTree(int layerId)
    {
        _screensTree.DeselectAll();
        var root = _layersTree.GetRoot();
        if (root == null)
            return;

        var child = root.GetFirstChild();
        while (child != null)
        {
            if (child.GetMetadata(0).AsInt32() == layerId)
            {
                child.Select(0);
                return;
            }
            child = child.GetNext();
        }
    }

    private void OnCanvasSelectPressed()
    {
        DeselectTrees();
        ApplySelection(SelectionKind.Canvas, -1, -1);
    }

    private void OnScreensTreeItemSelected()
    {
        if (_isRebuildingTree)
            return;

        var item = _screensTree.GetSelected();
        if (item == null || item == _screensTree.GetRoot())
            return;

        _layersTree.DeselectAll();
        int screenId = item.GetMetadata(0).AsInt32();
        ApplySelection(SelectionKind.Screen, screenId, -1);
    }

    private void OnLayersTreeItemSelected()
    {
        if (_isRebuildingTree)
            return;

        var item = _layersTree.GetSelected();
        if (item == null || item == _layersTree.GetRoot())
            return;

        _screensTree.DeselectAll();
        int layerId = item.GetMetadata(0).AsInt32();
        ApplySelection(SelectionKind.Layer, -1, layerId);
    }

    private void ApplySelection(SelectionKind kind, int screenId, int layerId)
    {
        _selectionKind = kind;
        _selectedScreenId = screenId;
        _selectedLayerId = layerId;
        ShowPropertiesForSelection();
        UpdateCanvasGizmos();
    }

    #endregion

    #region Properties panel

    private void ShowPropertiesForSelection()
    {
        _emptyPropsLabel.Visible = false;
        _canvasProps.Visible = false;
        _outputProps.Visible = false;
        _layerProps.Visible = false;

        switch (_selectionKind)
        {
            case SelectionKind.Canvas:
                _canvasProps.Visible = true;
                LoadCanvasProps();
                break;
            case SelectionKind.Screen:
                _outputProps.Visible = true;
                LoadScreenProps();
                break;
            case SelectionKind.Layer:
                _layerProps.Visible = true;
                LoadLayerProps();
                break;
            default:
                _emptyPropsLabel.Visible = true;
                _emptyPropsLabel.Text = "Select Canvas, a Screen, or a Target Layer.";
                break;
        }
    }

    private void LoadCanvasProps()
    {
        _isUpdatingProps = true;
        _canvasSizeXLineEdit.Text = _canvas.CanvasSize.X.ToString();
        _canvasSizeYLineEdit.Text = _canvas.CanvasSize.Y.ToString();
        _isUpdatingProps = false;
    }

    private void LoadScreenProps()
    {
        _isUpdatingProps = true;
        try
        {
            var screen = _displaysManager.GetOutputById(_selectedScreenId);
            if (screen == null)
            {
                _outputProps.Visible = false;
                _emptyPropsLabel.Visible = true;
                _emptyPropsLabel.Text = "Screen not found.";
                return;
            }

            _outputPropsTitle.Text = "Screen";
            _screenNameLineEdit.Text = screen.OutputName;
            _outputPosXLineEdit.Text = screen.CanvasPosition.X.ToString();
            _outputPosYLineEdit.Text = screen.CanvasPosition.Y.ToString();
            _outputSizeXLineEdit.Text = screen.OutputSize.X.ToString();
            _outputSizeYLineEdit.Text = screen.OutputSize.Y.ToString();
            _displayOffsetXLineEdit.Text = screen.DisplayOffset.X.ToString();
            _displayOffsetYLineEdit.Text = screen.DisplayOffset.Y.ToString();
            _screenKeepAspectCheckBox.ButtonPressed = screen.KeepAspect;
            _outputTransparentCheckBox.ButtonPressed = screen.OutputTransparent;
            _outputTestPatternCheckBox.ButtonPressed = screen.TestPatternStatus();

            PopulateOutputOption(screen.TargetMonitor);
            UpdateScreenResetButtons(screen);

            if (screen.IsVirtual)
            {
                _outputResolutionLabel.Text = "Virtual Output — not shown on a physical display";
            }
            else
            {
                var displays = _displaysManager.GetAvailableDisplays();
                string res = "Physical output";
                foreach (var d in displays)
                {
                    if (d.Index == screen.TargetMonitor)
                    {
                        res = $"{d.Name}  ·  {d.Size.X}×{d.Size.Y}";
                        break;
                    }
                }

                if (screen.TargetMonitor >= DisplayServer.GetScreenCount())
                    res = $"Monitor {screen.TargetMonitor} (not connected)";

                _outputResolutionLabel.Text = res;
            }

            _deleteScreenButton.Disabled = DisplaysManager.Screens.Count <= 1;
        }
        finally
        {
            _isUpdatingProps = false;
        }
    }

    private void PopulateOutputOption(int selectedMonitor)
    {
        _screenOutputOption.Clear();
        _outputOptionMonitorMap.Clear();

        _screenOutputOption.AddItem("Virtual Output");
        _outputOptionMonitorMap.Add(VideoOutputDevice.VirtualMonitorIndex);

        var displays = _displaysManager.GetAvailableDisplays();
        int selectIndex = 0;

        for (int i = 0; i < displays.Count; i++)
        {
            var d = displays[i];
            _screenOutputOption.AddItem($"{d.Name}  ({d.Size.X}×{d.Size.Y})");
            _outputOptionMonitorMap.Add(d.Index);
            if (d.Index == selectedMonitor)
                selectIndex = i + 1;
        }

        if (selectedMonitor >= 0 && selectIndex == 0)
        {
            _screenOutputOption.AddItem($"Monitor {selectedMonitor} (missing)");
            _outputOptionMonitorMap.Add(selectedMonitor);
            selectIndex = _outputOptionMonitorMap.Count - 1;
        }

        _screenOutputOption.Select(selectIndex);
    }

    private void LoadLayerProps()
    {
        _isUpdatingProps = true;
        try
        {
            var layer = DisplaysManager.GetLayerById(_selectedLayerId);
            if (layer == null)
            {
                _layerProps.Visible = false;
                _emptyPropsLabel.Visible = true;
                _emptyPropsLabel.Text = "Layer not found.";
                return;
            }

            _layerNameLineEdit.Text = layer.LayerName;
            _layerPosXLineEdit.Text = layer.CanvasPosition.X.ToString();
            _layerPosYLineEdit.Text = layer.CanvasPosition.Y.ToString();
            _layerSizeXLineEdit.Text = layer.Size.X.ToString();
            _layerSizeYLineEdit.Text = layer.Size.Y.ToString();
            _layerKeepAspectCheckBox.ButtonPressed = layer.KeepAspect;
            _layerTransparentCheckBox.ButtonPressed = layer.Transparent;
            _layerTestPatternCheckBox.ButtonPressed = layer.TestPatternEnabled;
            UpdateLayerResetButtons(layer);
        }
        finally
        {
            _isUpdatingProps = false;
        }
    }

    #endregion

    #region Defaults / reset buttons

    private static readonly Vector2I DefaultCanvasPosition = Vector2I.Zero;
    private static readonly Vector2I DefaultDisplayOffset = Vector2I.Zero;
    private const bool DefaultKeepAspect = false;
    private const bool DefaultTransparent = false;
    private const bool DefaultTestPattern = false;
    private const int DefaultOutputMonitor = VideoOutputDevice.VirtualMonitorIndex;

    private void UpdateScreenResetButtons(VideoOutputDevice screen)
    {
        if (screen == null)
            return;

        Vector2I defaultSize = _displaysManager.GetDefaultScreenSize(screen);

        SetResetVisible(_screenOutputResetButton, screen.TargetMonitor != DefaultOutputMonitor,
            "Reset to default: Virtual Output");
        SetResetVisible(_screenSizeResetButton, screen.OutputSize != defaultSize,
            $"Reset to default: {defaultSize.X}×{defaultSize.Y}");
        SetResetVisible(_screenKeepAspectResetButton, screen.KeepAspect != DefaultKeepAspect,
            "Reset to default: Off");
        SetResetVisible(_screenPosResetButton, screen.CanvasPosition != DefaultCanvasPosition,
            "Reset to default: 0×0");
        SetResetVisible(_screenDisplayOffsetResetButton, screen.DisplayOffset != DefaultDisplayOffset,
            "Reset to default: 0×0");
        SetResetVisible(_screenTransparentResetButton, screen.OutputTransparent != DefaultTransparent,
            "Reset to default: Off");
        SetResetVisible(_screenTestPatternResetButton, screen.TestPatternStatus() != DefaultTestPattern,
            "Reset to default: Off");
    }

    private void UpdateLayerResetButtons(VideoTargetLayer layer)
    {
        if (layer == null)
            return;

        Vector2I defaultSize = _displaysManager.GetDefaultLayerSize();

        SetResetVisible(_layerSizeResetButton, layer.Size != defaultSize,
            $"Reset to default: {defaultSize.X}×{defaultSize.Y}");
        SetResetVisible(_layerKeepAspectResetButton, layer.KeepAspect != DefaultKeepAspect,
            "Reset to default: Off");
        SetResetVisible(_layerPosResetButton, layer.CanvasPosition != DefaultCanvasPosition,
            "Reset to default: 0×0");
        SetResetVisible(_layerTransparentResetButton, layer.Transparent != DefaultTransparent,
            "Reset to default: Off");
        SetResetVisible(_layerTestPatternResetButton, layer.TestPatternEnabled != DefaultTestPattern,
            "Reset to default: Off");
    }

    private static void SetResetVisible(Button button, bool show, string tooltip)
    {
        if (button == null)
            return;
        button.Visible = show;
        if (show)
            button.TooltipText = tooltip;
    }

    private void OnScreenOutputResetPressed()
    {
        var screen = GetSelectedScreen();
        if (screen == null)
            return;
        _displaysManager.UpdateScreenTargetMonitor(screen.OutputId, DefaultOutputMonitor);
        RebuildTrees();
        LoadScreenProps();
        UpdateCanvasGizmos();
    }

    private void OnScreenSizeResetPressed()
    {
        var screen = GetSelectedScreen();
        if (screen == null)
            return;
        var def = _displaysManager.GetDefaultScreenSize(screen);
        _displaysManager.UpdateOutputSize(screen.OutputId, def);
        LoadScreenProps();
        UpdateCanvasGizmos();
    }

    private void OnScreenKeepAspectResetPressed()
    {
        var screen = GetSelectedScreen();
        if (screen == null)
            return;
        _displaysManager.UpdateScreenKeepAspect(screen.OutputId, DefaultKeepAspect);
        LoadScreenProps();
    }

    private void OnScreenPosResetPressed()
    {
        var screen = GetSelectedScreen();
        if (screen == null)
            return;
        _displaysManager.UpdateOutputCanvasPosition(screen.OutputId, DefaultCanvasPosition);
        LoadScreenProps();
        UpdateCanvasGizmos();
    }

    private void OnScreenDisplayOffsetResetPressed()
    {
        var screen = GetSelectedScreen();
        if (screen == null)
            return;
        _displaysManager.UpdateScreenDisplayOffset(screen.OutputId, DefaultDisplayOffset);
        LoadScreenProps();
    }

    private void OnScreenTransparentResetPressed()
    {
        var screen = GetSelectedScreen();
        if (screen == null)
            return;
        screen.SetTransparent(DefaultTransparent);
        LoadScreenProps();
    }

    private void OnScreenTestPatternResetPressed()
    {
        var screen = GetSelectedScreen();
        if (screen == null)
            return;
        screen.ToggleTestPattern(DefaultTestPattern);
        LoadScreenProps();
    }

    private void OnLayerSizeResetPressed()
    {
        if (_selectionKind != SelectionKind.Layer)
            return;
        var def = _displaysManager.GetDefaultLayerSize();
        _displaysManager.UpdateLayerSize(_selectedLayerId, def);
        LoadLayerProps();
        UpdateCanvasGizmos();
    }

    private void OnLayerKeepAspectResetPressed()
    {
        if (_selectionKind != SelectionKind.Layer)
            return;
        _displaysManager.UpdateLayerKeepAspect(_selectedLayerId, DefaultKeepAspect);
        LoadLayerProps();
    }

    private void OnLayerPosResetPressed()
    {
        if (_selectionKind != SelectionKind.Layer)
            return;
        _displaysManager.UpdateLayerCanvasPosition(_selectedLayerId, DefaultCanvasPosition);
        LoadLayerProps();
        UpdateCanvasGizmos();
    }

    private void OnLayerTransparentResetPressed()
    {
        if (_selectionKind != SelectionKind.Layer)
            return;
        _displaysManager.UpdateLayerTransparent(_selectedLayerId, DefaultTransparent);
        LoadLayerProps();
    }

    private void OnLayerTestPatternResetPressed()
    {
        if (_selectionKind != SelectionKind.Layer)
            return;
        _displaysManager.ToggleLayerTestPattern(_selectedLayerId, DefaultTestPattern);
        LoadLayerProps();
    }

    #endregion

    #region Screen property handlers

    private VideoOutputDevice GetSelectedScreen()
    {
        if (_selectionKind != SelectionKind.Screen)
            return null;
        return _displaysManager.GetOutputById(_selectedScreenId);
    }

    private void OnScreenNameSubmitted(string text)
    {
        if (_isUpdatingProps)
        {
            _screenNameLineEdit.ReleaseFocus();
            return;
        }

        var screen = GetSelectedScreen();
        if (screen == null)
        {
            _screenNameLineEdit.ReleaseFocus();
            return;
        }

        _displaysManager.UpdateScreenName(screen.OutputId, text);
        RebuildTrees();
        UpdateCanvasGizmos();
        _screenNameLineEdit.ReleaseFocus();
    }

    private void OnScreenOutputSelected(long index)
    {
        if (_isUpdatingProps)
            return;

        var screen = GetSelectedScreen();
        if (screen == null)
            return;

        int i = (int)index;
        if (i < 0 || i >= _outputOptionMonitorMap.Count)
            return;

        int monitor = _outputOptionMonitorMap[i];
        _displaysManager.UpdateScreenTargetMonitor(screen.OutputId, monitor);
        RebuildTrees();
        if (_selectionKind == SelectionKind.Screen)
            LoadScreenProps();
        UpdateCanvasGizmos();
    }

    private void OnScreenSizeXSubmitted(string text)
    {
        var screen = GetSelectedScreen();
        if (_isUpdatingProps || screen == null)
        {
            _outputSizeXLineEdit.ReleaseFocus();
            return;
        }

        try
        {
            int val = int.Parse(text);
            Vector2I size = screen.KeepAspect
                ? SizeWithKeepAspect(screen.OutputSize, val, null)
                : new Vector2I(val, screen.OutputSize.Y);
            _displaysManager.UpdateOutputSize(screen.OutputId, size);
            LoadScreenProps();
            UpdateCanvasGizmos();
        }
        catch (FormatException)
        {
            _outputSizeXLineEdit.Text = screen.OutputSize.X.ToString();
        }

        _outputSizeXLineEdit.ReleaseFocus();
    }

    private void OnScreenSizeYSubmitted(string text)
    {
        var screen = GetSelectedScreen();
        if (_isUpdatingProps || screen == null)
        {
            _outputSizeYLineEdit.ReleaseFocus();
            return;
        }

        try
        {
            int val = int.Parse(text);
            Vector2I size = screen.KeepAspect
                ? SizeWithKeepAspect(screen.OutputSize, null, val)
                : new Vector2I(screen.OutputSize.X, val);
            _displaysManager.UpdateOutputSize(screen.OutputId, size);
            LoadScreenProps();
            UpdateCanvasGizmos();
        }
        catch (FormatException)
        {
            _outputSizeYLineEdit.Text = screen.OutputSize.Y.ToString();
        }

        _outputSizeYLineEdit.ReleaseFocus();
    }

    private void OnScreenPosXSubmitted(string text)
    {
        var screen = GetSelectedScreen();
        if (_isUpdatingProps || screen == null)
        {
            _outputPosXLineEdit.ReleaseFocus();
            return;
        }

        try
        {
            int val = int.Parse(text);
            _displaysManager.UpdateOutputCanvasPosition(screen.OutputId, new Vector2I(val, screen.CanvasPosition.Y));
            LoadScreenProps();
            UpdateCanvasGizmos();
        }
        catch (FormatException)
        {
            _outputPosXLineEdit.Text = screen.CanvasPosition.X.ToString();
        }

        _outputPosXLineEdit.ReleaseFocus();
    }

    private void OnScreenPosYSubmitted(string text)
    {
        var screen = GetSelectedScreen();
        if (_isUpdatingProps || screen == null)
        {
            _outputPosYLineEdit.ReleaseFocus();
            return;
        }

        try
        {
            int val = int.Parse(text);
            _displaysManager.UpdateOutputCanvasPosition(screen.OutputId, new Vector2I(screen.CanvasPosition.X, val));
            LoadScreenProps();
            UpdateCanvasGizmos();
        }
        catch (FormatException)
        {
            _outputPosYLineEdit.Text = screen.CanvasPosition.Y.ToString();
        }

        _outputPosYLineEdit.ReleaseFocus();
    }

    private void OnDisplayOffsetXSubmitted(string text)
    {
        var screen = GetSelectedScreen();
        if (_isUpdatingProps || screen == null)
        {
            _displayOffsetXLineEdit.ReleaseFocus();
            return;
        }

        try
        {
            int val = int.Parse(text);
            _displaysManager.UpdateScreenDisplayOffset(screen.OutputId, new Vector2I(val, screen.DisplayOffset.Y));
            UpdateScreenResetButtons(screen);
        }
        catch (FormatException)
        {
            _displayOffsetXLineEdit.Text = screen.DisplayOffset.X.ToString();
        }

        _displayOffsetXLineEdit.ReleaseFocus();
    }

    private void OnDisplayOffsetYSubmitted(string text)
    {
        var screen = GetSelectedScreen();
        if (_isUpdatingProps || screen == null)
        {
            _displayOffsetYLineEdit.ReleaseFocus();
            return;
        }

        try
        {
            int val = int.Parse(text);
            _displaysManager.UpdateScreenDisplayOffset(screen.OutputId, new Vector2I(screen.DisplayOffset.X, val));
            UpdateScreenResetButtons(screen);
        }
        catch (FormatException)
        {
            _displayOffsetYLineEdit.Text = screen.DisplayOffset.Y.ToString();
        }

        _displayOffsetYLineEdit.ReleaseFocus();
    }

    private void OnScreenKeepAspectToggled(bool toggled)
    {
        if (_isUpdatingProps)
            return;

        var screen = GetSelectedScreen();
        if (screen == null)
            return;

        _displaysManager.UpdateScreenKeepAspect(screen.OutputId, toggled);
        UpdateScreenResetButtons(screen);
    }

    private void OnScreenTransparentToggled(bool toggled)
    {
        if (_isUpdatingProps)
            return;

        var screen = GetSelectedScreen();
        screen?.SetTransparent(toggled);
        if (screen != null)
            UpdateScreenResetButtons(screen);
    }

    private void OnScreenTestPatternToggled(bool toggled)
    {
        if (_isUpdatingProps)
            return;

        var screen = GetSelectedScreen();
        screen?.ToggleTestPattern(toggled);
        if (screen != null)
            UpdateScreenResetButtons(screen);
    }

    private void OnDeleteScreenPressed()
    {
        if (_selectionKind != SelectionKind.Screen)
            return;

        if (DisplaysManager.Screens.Count <= 1)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                "Cannot delete the last screen.", 1);
            return;
        }

        _displaysManager.RemoveOutput(_selectedScreenId);
        RebuildTrees(selectCanvas: true);
        UpdateCanvasGizmos();
    }

    private void OnNewScreenPressed()
    {
        string name = $"Screen {DisplaysManager.Screens.Count + 1}";
        var screen = _displaysManager.AddScreen(name, VideoOutputDevice.VirtualMonitorIndex);
        RebuildTrees();
        SelectScreenInTree(screen.OutputId);
        ApplySelection(SelectionKind.Screen, screen.OutputId, -1);
        UpdateCanvasGizmos();
    }

    #endregion

    #region Layer property handlers

    private void OnLayerNameSubmitted(string text)
    {
        if (_isUpdatingProps || _selectionKind != SelectionKind.Layer)
        {
            _layerNameLineEdit.ReleaseFocus();
            return;
        }

        _displaysManager.UpdateLayerName(_selectedLayerId, text);
        RebuildTrees();
        UpdateCanvasGizmos();
        _layerNameLineEdit.ReleaseFocus();
    }

    private void OnLayerSizeXSubmitted(string text)
    {
        if (_isUpdatingProps)
        {
            _layerSizeXLineEdit.ReleaseFocus();
            return;
        }

        var layer = DisplaysManager.GetLayerById(_selectedLayerId);
        if (layer == null)
        {
            _layerSizeXLineEdit.ReleaseFocus();
            return;
        }

        try
        {
            int val = int.Parse(text);
            Vector2I size = layer.KeepAspect
                ? SizeWithKeepAspect(layer.Size, val, null)
                : new Vector2I(val, layer.Size.Y);
            _displaysManager.UpdateLayerSize(_selectedLayerId, size);
            LoadLayerProps();
            UpdateCanvasGizmos();
        }
        catch (FormatException)
        {
            _layerSizeXLineEdit.Text = layer.Size.X.ToString();
        }

        _layerSizeXLineEdit.ReleaseFocus();
    }

    private void OnLayerSizeYSubmitted(string text)
    {
        if (_isUpdatingProps)
        {
            _layerSizeYLineEdit.ReleaseFocus();
            return;
        }

        var layer = DisplaysManager.GetLayerById(_selectedLayerId);
        if (layer == null)
        {
            _layerSizeYLineEdit.ReleaseFocus();
            return;
        }

        try
        {
            int val = int.Parse(text);
            Vector2I size = layer.KeepAspect
                ? SizeWithKeepAspect(layer.Size, null, val)
                : new Vector2I(layer.Size.X, val);
            _displaysManager.UpdateLayerSize(_selectedLayerId, size);
            LoadLayerProps();
            UpdateCanvasGizmos();
        }
        catch (FormatException)
        {
            _layerSizeYLineEdit.Text = layer.Size.Y.ToString();
        }

        _layerSizeYLineEdit.ReleaseFocus();
    }

    private void OnLayerPosXSubmitted(string text)
    {
        if (_isUpdatingProps)
        {
            _layerPosXLineEdit.ReleaseFocus();
            return;
        }

        var layer = DisplaysManager.GetLayerById(_selectedLayerId);
        if (layer == null)
        {
            _layerPosXLineEdit.ReleaseFocus();
            return;
        }

        try
        {
            int val = int.Parse(text);
            _displaysManager.UpdateLayerCanvasPosition(_selectedLayerId, new Vector2I(val, layer.CanvasPosition.Y));
            LoadLayerProps();
            UpdateCanvasGizmos();
        }
        catch (FormatException)
        {
            _layerPosXLineEdit.Text = layer.CanvasPosition.X.ToString();
        }

        _layerPosXLineEdit.ReleaseFocus();
    }

    private void OnLayerPosYSubmitted(string text)
    {
        if (_isUpdatingProps)
        {
            _layerPosYLineEdit.ReleaseFocus();
            return;
        }

        var layer = DisplaysManager.GetLayerById(_selectedLayerId);
        if (layer == null)
        {
            _layerPosYLineEdit.ReleaseFocus();
            return;
        }

        try
        {
            int val = int.Parse(text);
            _displaysManager.UpdateLayerCanvasPosition(_selectedLayerId, new Vector2I(layer.CanvasPosition.X, val));
            LoadLayerProps();
            UpdateCanvasGizmos();
        }
        catch (FormatException)
        {
            _layerPosYLineEdit.Text = layer.CanvasPosition.Y.ToString();
        }

        _layerPosYLineEdit.ReleaseFocus();
    }

    private void OnLayerKeepAspectToggled(bool toggled)
    {
        if (_isUpdatingProps || _selectionKind != SelectionKind.Layer)
            return;

        _displaysManager.UpdateLayerKeepAspect(_selectedLayerId, toggled);
        var layer = DisplaysManager.GetLayerById(_selectedLayerId);
        if (layer != null)
            UpdateLayerResetButtons(layer);
    }

    private void OnLayerTransparentToggled(bool toggled)
    {
        if (_isUpdatingProps || _selectionKind != SelectionKind.Layer)
            return;

        _displaysManager.UpdateLayerTransparent(_selectedLayerId, toggled);
        var layer = DisplaysManager.GetLayerById(_selectedLayerId);
        if (layer != null)
            UpdateLayerResetButtons(layer);
    }

    private void OnLayerTestPatternToggled(bool toggled)
    {
        if (_isUpdatingProps || _selectionKind != SelectionKind.Layer)
            return;

        _displaysManager.ToggleLayerTestPattern(_selectedLayerId, toggled);
        var layer = DisplaysManager.GetLayerById(_selectedLayerId);
        if (layer != null)
            UpdateLayerResetButtons(layer);
    }

    private void OnDeleteLayerPressed()
    {
        if (_selectionKind != SelectionKind.Layer)
            return;

        _displaysManager.RemoveLayer(_selectedLayerId);
        RebuildTrees(selectCanvas: true);
        UpdateCanvasGizmos();
    }

    private void OnNewTargetLayerPressed()
    {
        string name = $"Layer {DisplaysManager.Layers.Count + 1}";
        int zIndex = DisplaysManager.Layers.Count;
        var layer = _displaysManager.AddLayer(name, zIndex);
        RebuildTrees();
        SelectLayerInTree(layer.LayerId);
        ApplySelection(SelectionKind.Layer, -1, layer.LayerId);
        UpdateCanvasGizmos();
        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Added new target layer '{name}'.", 0);
    }

    #endregion

    #region Canvas size / zoom / fit

    private void OnCanvasSizeSubmitted(string newText)
    {
        try
        {
            int x = int.Parse(_canvasSizeXLineEdit.Text);
            int y = int.Parse(_canvasSizeYLineEdit.Text);

            _canvas.SetCanvasSize(new Vector2I(x, y));

            _canvasOutlinePanel.CustomMinimumSize = new Vector2(_canvas.CanvasSize.X, _canvas.CanvasSize.Y);
            _subViewportContainer.CustomMinimumSize = new Vector2(_canvas.CanvasSize.X, _canvas.CanvasSize.Y);
            _viewport.Size = new Vector2I(_canvas.CanvasSize.X, _canvas.CanvasSize.Y);

            UpdateZoom();

            _canvasSizeXLineEdit.Text = _canvas.CanvasSize.X.ToString();
            _canvasSizeYLineEdit.Text = _canvas.CanvasSize.Y.ToString();

            _canvasSizeXLineEdit.ReleaseFocus();
            _canvasSizeYLineEdit.ReleaseFocus();

            _canvasSelectButton.Text = $"Canvas ({x}×{y})";
            UpdateCanvasGizmos();

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
        float increment = _zoom * 0.1f;
        _zoom = Mathf.Clamp(_zoom + increment, MinZoom, MaxZoom);
        UpdateZoom();
    }

    private void ZoomOut()
    {
        float increment = _zoom * 0.1f;
        _zoom = Mathf.Clamp(_zoom - increment, MinZoom, MaxZoom);
        UpdateZoom();
    }

    /// <summary>
    /// Fits the full canvas into the center stage view with padding and centers it.
    /// </summary>
    private void FitToView()
    {
        Vector2 viewSize = _scrollContainer.Size;
        if (viewSize.X < 8f || viewSize.Y < 8f || _canvas.CanvasSize.X <= 0 || _canvas.CanvasSize.Y <= 0)
            return;

        float availX = Mathf.Max(8f, viewSize.X - FitPadding);
        float availY = Mathf.Max(8f, viewSize.Y - FitPadding);
        float zoomX = availX / _canvas.CanvasSize.X;
        float zoomY = availY / _canvas.CanvasSize.Y;
        _zoom = Mathf.Clamp(Mathf.Min(zoomX, zoomY), MinZoom, MaxZoom);

        Vector2 zoomed = new Vector2(_canvas.CanvasSize.X * _zoom, _canvas.CanvasSize.Y * _zoom);
        _canvasLayer.Offset = (viewSize - zoomed) * 0.5f;
        UpdateZoom();
    }

    private void UpdateZoom()
    {
        Vector2 zoomedSize = new Vector2(_canvas.CanvasSize.X * _zoom, _canvas.CanvasSize.Y * _zoom);
        _control.Size = zoomedSize;
        _control.Position = Vector2.Zero;
        Vector2 viewportSize = _scrollContainer.Size;
        _subViewportContainer.CustomMinimumSize = viewportSize;
        _viewport.Size = new Vector2I(Mathf.Max(1, (int)viewportSize.X), Mathf.Max(1, (int)viewportSize.Y));
        _backgroundRect.Size = viewportSize;
        (_backgroundRect.Material as ShaderMaterial)?.SetShaderParameter("rect_size", viewportSize);
        _canvasOutlinePanel.CustomMinimumSize = zoomedSize;
        UpdateZoomLabel();
        UpdateCanvasGizmos();
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
            _zoom = Mathf.Clamp(percent / 100f, MinZoom, MaxZoom);
            UpdateZoom();
        }
        catch
        {
            UpdateZoomLabel();
        }

        _zoomPercentLineEdit.ReleaseFocus();
    }

    #endregion

    #region Gizmos / refresh

    /// <summary>
    /// Rebuilds stage gizmos for all screens and layers, highlighting the selection with handles.
    /// </summary>
    private void UpdateCanvasGizmos()
    {
        foreach (var g in _gizmos)
        {
            if (IsInstanceValid(g))
            {
                _canvasLayer.RemoveChild(g);
                g.QueueFree();
            }
        }

        _gizmos.Clear();

        // Screens under layers so layers draw on top
        foreach (var screen in DisplaysManager.Screens)
        {
            bool selected = _selectionKind == SelectionKind.Screen && screen.OutputId == _selectedScreenId;
            var gizmo = new CanvasItemGizmo
            {
                IsScreen = true,
                ItemId = screen.OutputId,
                LabelText = screen.OutputName,
                BorderColor = screen.IsVirtual
                    ? new Color(1f, 0.5f, 0.2f, 0.85f)
                    : new Color(1f, 0.2f, 0.15f, 0.9f),
                FillColor = screen.IsVirtual
                    ? new Color(1f, 0.45f, 0.15f, 0.1f)
                    : new Color(1f, 0.15f, 0.1f, 0.12f),
                OffsetDash = false,
                Selected = selected,
                MouseFilter = MouseFilterEnum.Ignore
            };
            gizmo.Position = new Vector2(screen.CanvasPosition.X * _zoom, screen.CanvasPosition.Y * _zoom);
            gizmo.Size = new Vector2(
                Mathf.Max(1f, screen.OutputSize.X * _zoom),
                Mathf.Max(1f, screen.OutputSize.Y * _zoom));
            _canvasLayer.AddChild(gizmo);
            gizmo.QueueRedraw();
            _gizmos.Add(gizmo);
        }

        foreach (var layer in DisplaysManager.Layers)
        {
            bool selected = _selectionKind == SelectionKind.Layer && layer.LayerId == _selectedLayerId;
            var gizmo = new CanvasItemGizmo
            {
                IsScreen = false,
                ItemId = layer.LayerId,
                LabelText = layer.LayerName,
                BorderColor = new Color(0.25f, 0.55f, 1f, 0.9f),
                FillColor = new Color(0.2f, 0.45f, 1f, 0.1f),
                OffsetDash = true,
                Selected = selected,
                MouseFilter = MouseFilterEnum.Ignore
            };
            gizmo.Position = new Vector2(layer.CanvasPosition.X * _zoom, layer.CanvasPosition.Y * _zoom);
            gizmo.Size = new Vector2(
                Mathf.Max(1f, layer.Size.X * _zoom),
                Mathf.Max(1f, layer.Size.Y * _zoom));
            _canvasLayer.AddChild(gizmo);
            gizmo.QueueRedraw();
            _gizmos.Add(gizmo);
        }

        ForceStageRedraw();
    }

    private void Cleanup()
    {
        GetWindow().SizeChanged -= OnWindowSizeChanged;
        VisibilityChanged -= OnEditorVisibilityChanged;
        if (_scrollContainer != null && IsInstanceValid(_scrollContainer))
            _scrollContainer.Resized -= OnStageResized;
    }

    private void OnDisplaysChanged()
    {
        // Skip UI work while canvas editor is not shown — keeps main thread free for video.
        if (!_stageInitialized || !IsVisibleInTree())
            return;

        if (_isDraggingCanvas)
        {
            UpdateCanvasGizmos();
            return;
        }

        if (!_isRebuildingTree)
            RebuildTrees();
        UpdateCanvasGizmos();
    }

    private void OnCanvasSizeChanged(Vector2I newSize)
    {
        if (_canvasSizeXLineEdit != null)
            _canvasSizeXLineEdit.Text = newSize.X.ToString();
        if (_canvasSizeYLineEdit != null)
            _canvasSizeYLineEdit.Text = newSize.Y.ToString();
        if (_canvasSelectButton != null)
            _canvasSelectButton.Text = $"Canvas ({newSize.X}×{newSize.Y})";
    }

    #endregion
}
