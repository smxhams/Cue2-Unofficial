// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Cue2.Domain.Cuelist;
using Cue2.Domain.Playback;
using Cue2.Domain.Devices;
using Cue2.Domain.ShowSettings;
using Cue2.Domain.Metadata;
using Cue2.Domain.Cues;
using Cue2.Domain.Connections;
using Cue2.Domain.Library;
using Cue2.Domain.Commands;
using Cue2.Services;
using Cue2.UI.Popups;

namespace Cue2.UI.Settings;

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
    private Callable _layerGeometryChangedCallable;
    private HistoryManager _historyManager;
    private Canvas _canvas;
    private DisplaysManager _displaysManager;
    private ResourceInUseDeleteDialog _activeLayerDeleteDialog;

    /// <summary>
    /// Coalesce key for the active stage drag (move/resize). Sealed when the drag ends.
    /// </summary>
    private string _activeDragCoalesceKey;

    // Hierarchy – two trees
    private Godot.Tree _screensTree;
    private Button _refreshScreensButton;
    private Godot.Tree _layersTree;
    private Button _canvasSelectButton;
    private Button _addScreenButton;
    private Button _newTargetLayerButton;
    private Button _moveLayerUpButton;
    private Button _moveLayerDownButton;

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

    // Layout: structure | stage | properties — stage collapses first when space is tight
    private HSplitContainer _bodyHSplit;
    private HSplitContainer _centerRightSplit;
    private Control _leftPanel;
    private Control _rightPanel;
    private bool _isApplyingPanelLayout;

    /// <summary>Preferred width for the structure (screens/layers) panel while the stage still has room.</summary>
    private const float LeftPanelPreferredWidth = 180f;

    /// <summary>Preferred width for the properties panel while the stage still has room.</summary>
    private const float RightPanelPreferredWidth = 220f;

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
    private CheckBox _layerLockCheckBox;
    private Button _deleteLayerButton;
    private Button _layerSizeResetButton;
    private Button _layerKeepAspectResetButton;
    private Button _layerPosResetButton;
    private Button _layerTransparentResetButton;
    private Button _layerTestPatternResetButton;
    private Button _layerLockResetButton;

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

    /// <summary>
    /// True when Displays history restored while this editor was hidden — refresh on next show.
    /// </summary>
    private bool _needsHistoryRefresh;

    private SelectionKind _selectionKind = SelectionKind.None;
    private int _selectedScreenId = -1;
    private int _selectedLayerId = -1;

    private DragMode _dragMode = DragMode.None;
    private Vector2 _dragStartCanvasMouse;
    private Vector2I _dragStartPos;
    private Vector2I _dragStartSize;

    /// <summary>
    /// Maps OptionButton item index → destination index
    /// (VirtualMonitorIndex, WindowMonitorIndex, or physical monitor index).
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
        _historyManager = _globalData?.HistoryManager;
        _canvas = DisplaysManager.Canvas;
        _displaysManager = GetNode<DisplaysManager>("/root/DisplaysManager");

        _globalSignals.Connect(nameof(GlobalSignals.DisplaysChanged), Callable.From(OnDisplaysChanged));
        _globalSignals.Connect(nameof(GlobalSignals.CanvasSizeChanged), Callable.From<Vector2I>(OnCanvasSizeChanged));
        _layerGeometryChangedCallable = Callable.From<int>(OnLayerGeometryChanged);
        _globalSignals.Connect(nameof(GlobalSignals.LayerGeometryChanged), _layerGeometryChangedCallable);

        if (_historyManager != null)
            _historyManager.HistoryRestored += OnHistoryRestored;

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
        // Zero min so the center stage can fully collapse before side panels shrink.
        _subViewportContainer.CustomMinimumSize = Vector2.Zero;
        _viewport.Size = new Vector2I(1, 1);

        _scrollContainer.MouseFilter = MouseFilterEnum.Stop;
        _subViewportContainer.MouseFilter = MouseFilterEnum.Stop;
        _scrollContainer.Resized += OnStageResized;

        if (_bodyHSplit != null)
            _bodyHSplit.Resized += OnBodyHSplitResized;
        CallDeferred(nameof(ApplyResponsivePanelLayout));

        // Start input-off; enabled only while this panel is the visible settings page.
        SetProcessInput(IsVisibleInTree());

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

        // Stage collapsed or not laid out yet — wait for Resized; do not spin forever at zero width.
        if (_scrollContainer.Size.X < 8f || _scrollContainer.Size.Y < 8f)
            return;

        FitToView();
        UpdateCanvasGizmos();
        ForceStageRedraw();
    }

    private void OnEditorVisibilityChanged()
    {
        UpdateViewportRenderMode();

        if (IsVisibleInTree())
        {
            // Only process stage mouse/keyboard while this panel is the active settings page.
            // Hidden panels stay in the tree and share the right-side layout rect — without
            // this, _Input keeps hit-testing and mutates canvas while other menus are open.
            SetProcessInput(true);

            // Heavy init only when user actually opens Canvas Editor (not every Settings open).
            CallDeferred(nameof(EnsureStageInitialized));
            CallDeferred(nameof(ApplyResponsivePanelLayout));
            CallDeferred(nameof(RefreshStageView));
            if (_needsHistoryRefresh && _stageInitialized)
            {
                _needsHistoryRefresh = false;
                CallDeferred(nameof(RefreshAfterHistoryRestore));
            }
        }
        else
        {
            SetProcessInput(false);
            CancelStageInteractionOnHide();
        }
    }

    /// <summary>
    /// Ends pan/drag and resets cursor when leaving the canvas editor so no further model
    /// updates or DisplaysManager logs fire while another settings panel is visible.
    /// </summary>
    private void CancelStageInteractionOnHide()
    {
        if (_isDraggingCanvas)
        {
            // Commit the drag cleanly (history already recorded at StartDrag) rather than
            // leaving half-applied geometry without a DisplaysManager update.
            EndCanvasInteraction();
        }

        _isPanning = false;
        _isDraggingCanvas = false;
        _dragMode = DragMode.None;
        if (!string.IsNullOrEmpty(_activeDragCoalesceKey))
        {
            _historyManager?.EndCoalesceSession(_activeDragCoalesceKey);
            _activeDragCoalesceKey = null;
        }

        MouseDefaultCursorShape = CursorShape.Arrow;
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

        // Collapsed stage (narrow window): skip zoom work until space returns.
        if (_scrollContainer.Size.X < 8f || _scrollContainer.Size.Y < 8f)
            return;

        // First meaningful size after being blank/hidden: fit rather than leave zoom broken
        if (_viewport.Size.X <= 8 || _viewport.Size.Y <= 8)
            CallDeferred(nameof(RefreshStageView));
        else
            UpdateZoom();
    }

    private void OnBodyHSplitResized()
    {
        ApplyResponsivePanelLayout();
    }

    /// <summary>
    /// Shrink order: center stage collapses first; only when both side panels are already at their
    /// preferred widths with no stage left do left/right release their minimums and compress.
    /// </summary>
    private void ApplyResponsivePanelLayout()
    {
        if (_isApplyingPanelLayout || !IsInsideTree())
            return;
        if (_bodyHSplit == null || _centerRightSplit == null || _leftPanel == null || _rightPanel == null)
            return;

        float width = _bodyHSplit.Size.X;
        if (width < 1f)
            return;

        float separation = _bodyHSplit.GetThemeConstant("separation");
        if (separation < 0f)
            separation = 4f;

        // Body has one separator between left and center-right; center-right has one more.
        float neededForSides = LeftPanelPreferredWidth + RightPanelPreferredWidth + separation * 2f;

        _isApplyingPanelLayout = true;
        try
        {
            if (width >= neededForSides)
            {
                // Room for stage: pin side panels so all further shrink eats the center first.
                _leftPanel.CustomMinimumSize = new Vector2(LeftPanelPreferredWidth, 0f);
                _rightPanel.CustomMinimumSize = new Vector2(RightPanelPreferredWidth, 0f);
            }
            else
            {
                // Stage must be gone; free side mins and collapse center so only left/right share space.
                _leftPanel.CustomMinimumSize = Vector2.Zero;
                _rightPanel.CustomMinimumSize = Vector2.Zero;

                SetFirstSplitOffset(_centerRightSplit, 0);

                float forSides = Mathf.Max(0f, width - separation);
                float leftShare = forSides * (LeftPanelPreferredWidth / (LeftPanelPreferredWidth + RightPanelPreferredWidth));
                int leftOffset = Mathf.RoundToInt(leftShare);
                SetFirstSplitOffset(_bodyHSplit, leftOffset);
            }
        }
        finally
        {
            _isApplyingPanelLayout = false;
        }
    }

    /// <summary>
    /// Sets the first split offset on a Godot 4.6+ <see cref="SplitContainer"/> via <see cref="SplitContainer.SplitOffsets"/>.
    /// </summary>
    private static void SetFirstSplitOffset(SplitContainer split, int offset)
    {
        if (split == null || !IsInstanceValid(split))
            return;

        int[] current = split.SplitOffsets;
        if (current != null && current.Length > 0 && current[0] == offset)
            return;

        split.SplitOffsets = new int[] { offset };
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
        _refreshScreensButton = GetNode<Button>("%RefreshScreensButton");
        _layersTree = GetNode<Godot.Tree>("%LayersTree");
        _canvasSelectButton = GetNode<Button>("%CanvasSelectButton");
        _addScreenButton = GetNode<Button>("%AddScreenButton");
        _newTargetLayerButton = GetNode<Button>("%AddTargetLayerButton");
        _moveLayerUpButton = GetNode<Button>("%MoveLayerUpButton");
        _moveLayerDownButton = GetNode<Button>("%MoveLayerDownButton");

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

        _bodyHSplit = GetNodeOrNull<HSplitContainer>("MarginContainer/MainVBox/BodyHSplit");
        _centerRightSplit = GetNodeOrNull<HSplitContainer>("MarginContainer/MainVBox/BodyHSplit/CenterRightSplit");
        _leftPanel = GetNodeOrNull<Control>("MarginContainer/MainVBox/BodyHSplit/LeftPanel");
        _rightPanel = GetNodeOrNull<Control>("MarginContainer/MainVBox/BodyHSplit/CenterRightSplit/RightPanel");

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
        _layerLockCheckBox = GetNode<CheckBox>("%LayerLockCheckBox");
        _deleteLayerButton = GetNode<Button>("%DeleteLayerButton");
        _layerSizeResetButton = GetNode<Button>("%LayerSizeResetButton");
        _layerKeepAspectResetButton = GetNode<Button>("%LayerKeepAspectResetButton");
        _layerPosResetButton = GetNode<Button>("%LayerPosResetButton");
        _layerTransparentResetButton = GetNode<Button>("%LayerTransparentResetButton");
        _layerTestPatternResetButton = GetNode<Button>("%LayerTestPatternResetButton");
        _layerLockResetButton = GetNode<Button>("%LayerLockResetButton");

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
                     _layerPosResetButton, _layerTransparentResetButton, _layerTestPatternResetButton,
                     _layerLockResetButton
                 })
        {
            if (btn != null)
                btn.Icon = icon;
        }

        if (_refreshScreensButton != null)
        {
            _refreshScreensButton.Icon = icon;
            _refreshScreensButton.ExpandIcon = true;
            _refreshScreensButton.AddThemeConstantOverride("icon_max_width", 14);
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
        if (_refreshScreensButton != null)
            _refreshScreensButton.Pressed += OnRefreshScreensPressed;
        _newTargetLayerButton.Pressed += OnNewTargetLayerPressed;
        _moveLayerUpButton.Pressed += OnMoveLayerUpPressed;
        _moveLayerDownButton.Pressed += OnMoveLayerDownPressed;

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
        _layerLockCheckBox.Toggled += OnLayerLockToggled;
        _deleteLayerButton.Pressed += OnDeleteLayerPressed;
        _layerSizeResetButton.Pressed += OnLayerSizeResetPressed;
        _layerKeepAspectResetButton.Pressed += OnLayerKeepAspectResetPressed;
        _layerPosResetButton.Pressed += OnLayerPosResetPressed;
        _layerTransparentResetButton.Pressed += OnLayerTransparentResetPressed;
        _layerTestPatternResetButton.Pressed += OnLayerTestPatternResetPressed;
        _layerLockResetButton.Pressed += OnLayerLockResetPressed;
    }

}
