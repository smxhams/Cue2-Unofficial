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

    #region Stage input (move / resize / select)

    public override void _Input(InputEvent @event)
    {
        // Hidden settings pages remain in the scene tree; never handle stage input off-page.
        if (!IsVisibleInTree() || !_stageInitialized)
            return;

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
        // GetGlobalRect still reports the last layout box while Hidden — other settings
        // panels occupy the same right-side area, so visibility must be checked first.
        if (!IsVisibleInTree())
            return false;
        if (_scrollContainer == null || !IsInstanceValid(_scrollContainer))
            return false;
        if (!_scrollContainer.IsVisibleInTree())
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
        // Prefer topmost layer (list order) then screens when picking on stage.
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

        // Layers first, top-of-stack first (list order) so the top layer wins overlaps.
        foreach (var layer in DisplaysManager.Layers)
        {
            var r = new Rect2(layer.CanvasPosition, layer.Size);
            if (!r.HasPoint(canvasMouse))
                continue;
            kind = SelectionKind.Layer;
            id = layer.LayerId;
            return true;
        }

        foreach (var screen in DisplaysManager.Screens)
        {
            var r = new Rect2(screen.CanvasPosition, screen.OutputSize);
            if (!r.HasPoint(canvasMouse))
                continue;
            kind = SelectionKind.Screen;
            id = screen.OutputId;
            return true;
        }

        return false;
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

        // Snapshot once at drag start; continuous move/resize coalesces into one undo step.
        string kind = _selectionKind == SelectionKind.Screen ? "screen" : "layer";
        int id = _selectionKind == SelectionKind.Screen ? _selectedScreenId : _selectedLayerId;
        _activeDragCoalesceKey = $"settings:displays:{kind}:{id}:geom";
        string desc = mode == DragMode.Move ? "Move canvas item" : "Resize canvas item";
        RecordDisplaysHistory(desc, _activeDragCoalesceKey);
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
            // Defer OS window resize until drag ends, but keep live video + test patterns in sync.
            screen.UpdateAllLayerDisplayRects();
            _displaysManager.RefreshTestPatternsLive(outputId: _selectedScreenId);
        }
        else if (_selectionKind == SelectionKind.Layer)
        {
            var layer = DisplaysManager.GetLayerById(_selectedLayerId);
            if (layer == null)
                return;
            layer.CanvasPosition = pos;
            layer.Size = size;
            // Push geometry to playing video TextureRects and layer test patterns on every output.
            foreach (var output in DisplaysManager.Outputs)
                output.UpdateLayerDisplayRect(_selectedLayerId);
            _displaysManager.RefreshTestPatternsLive(layerId: _selectedLayerId);
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

        // Seal the drag session so the next move/resize is a new undo step.
        if (!string.IsNullOrEmpty(_activeDragCoalesceKey))
        {
            _historyManager?.EndCoalesceSession(_activeDragCoalesceKey);
            _activeDragCoalesceKey = null;
        }

        if (mode == DragMode.None)
            return;

        if (!TryGetSelectedRect(out Vector2I pos, out Vector2I size))
            return;

        // Commit through DisplaysManager so outputs / test patterns update
        // (geometry was already applied live; history was captured at StartDrag).
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
            string dest = GetScreenDestinationShortLabel(screen);
            item.SetText(0, $"{screen.OutputName}  [{dest}]");
            item.SetMetadata(0, screen.OutputId);
            item.SetTooltipText(0,
                $"{screen.OutputName}\n{screen.OutputSize.X}×{screen.OutputSize.Y} @ {screen.CanvasPosition}\nOutput: {dest}");
            item.SetCustomColor(0, GetScreenTreeColor(screen));
        }
    }

    private void RebuildLayersTree()
    {
        _layersTree.Clear();
        var root = _layersTree.CreateItem();
        root.SetText(0, "Layers");

        // List is top-first: first entry is drawn on top of later ones.
        int count = DisplaysManager.Layers.Count;
        for (int i = 0; i < count; i++)
        {
            var layer = DisplaysManager.Layers[i];
            var item = _layersTree.CreateItem(root);
            string stackLabel = i == 0 ? "top" : (i == count - 1 ? "bottom" : $"#{i + 1}");
            item.SetText(0, $"{layer.LayerName}  [{stackLabel}]");
            item.SetMetadata(0, layer.LayerId);
            item.SetTooltipText(0,
                $"{layer.LayerName}\nStack: {stackLabel} (first = on top)\n{layer.Size.X}×{layer.Size.Y} @ {layer.CanvasPosition}");
            item.SetCustomColor(0, new Color(0.55f, 0.75f, 1f));
        }

        UpdateLayerOrderButtons();
    }

    /// <summary>
    /// Enables ↑/↓ based on the selected layer's place in the top-first stack.
    /// </summary>
    private void UpdateLayerOrderButtons()
    {
        if (_moveLayerUpButton == null || _moveLayerDownButton == null)
            return;

        if (_selectionKind != SelectionKind.Layer || _selectedLayerId < 0)
        {
            _moveLayerUpButton.Disabled = true;
            _moveLayerDownButton.Disabled = true;
            return;
        }

        int index = _displaysManager.GetLayerStackIndex(_selectedLayerId);
        int count = DisplaysManager.Layers.Count;
        _moveLayerUpButton.Disabled = index <= 0;
        _moveLayerDownButton.Disabled = index < 0 || index >= count - 1;
    }

    private void OnMoveLayerUpPressed()
    {
        if (_selectionKind != SelectionKind.Layer)
            return;
        if (_historyManager != null && _historyManager.IsRestoring)
            return;
        // Only snapshot when the layer can actually move (avoid no-op undo steps).
        if (_displaysManager.GetLayerStackIndex(_selectedLayerId) <= 0)
            return;
        RecordDisplaysHistory("Move layer up");
        if (_displaysManager.MoveLayerUp(_selectedLayerId))
        {
            RebuildTrees();
            UpdateCanvasGizmos();
        }
    }

    private void OnMoveLayerDownPressed()
    {
        if (_selectionKind != SelectionKind.Layer)
            return;
        if (_historyManager != null && _historyManager.IsRestoring)
            return;
        int index = _displaysManager.GetLayerStackIndex(_selectedLayerId);
        int count = DisplaysManager.Layers.Count;
        if (index < 0 || index >= count - 1)
            return;
        RecordDisplaysHistory("Move layer down");
        if (_displaysManager.MoveLayerDown(_selectedLayerId))
        {
            RebuildTrees();
            UpdateCanvasGizmos();
        }
    }

    private string GetMonitorLabel(int monitorIndex)
    {
        if (monitorIndex == VideoOutputDevice.VirtualMonitorIndex)
            return "Virtual";
        if (monitorIndex == VideoOutputDevice.WindowMonitorIndex)
            return "Window";

        var displays = _displaysManager.GetAvailableDisplays();
        foreach (var d in displays)
        {
            if (d.Index == monitorIndex)
                return d.Name;
        }

        return $"Monitor {monitorIndex} (missing)";
    }

    /// <summary>
    /// Short destination label for screen tree rows and tooltips.
    /// </summary>
    private string GetScreenDestinationShortLabel(VideoOutputDevice screen)
    {
        if (screen == null)
            return "Unknown";
        if (screen.IsVirtual)
            return "Virtual";
        if (screen.IsWindow)
            return "Window";
        return GetMonitorLabel(screen.TargetMonitor);
    }

    private static Color GetScreenTreeColor(VideoOutputDevice screen)
    {
        if (screen == null)
            return new Color(1f, 0.55f, 0.45f);
        if (screen.IsVirtual)
            return new Color(0.75f, 0.55f, 0.45f);
        if (screen.IsWindow)
            return new Color(0.55f, 0.8f, 0.55f);
        return new Color(1f, 0.55f, 0.45f);
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
        UpdateLayerOrderButtons();
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
            UpdateDisplayOffsetLabel(screen);

            if (screen.IsVirtual)
            {
                _outputResolutionLabel.Text = "Virtual Output — not shown on a physical display";
            }
            else if (screen.IsWindow)
            {
                if (screen.IsWindowDismissed)
                {
                    _outputResolutionLabel.Text =
                        "Window closed — change size/position or reselect Window to show again";
                }
                else
                {
                    _outputResolutionLabel.Text =
                        $"Portable Window  ·  {screen.OutputSize.X}×{screen.OutputSize.Y}  (OS title bar + controls)";
                }
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

        // Destination options: Virtual, Window, then physical displays.
        _screenOutputOption.AddItem("Virtual Output");
        _outputOptionMonitorMap.Add(VideoOutputDevice.VirtualMonitorIndex);

        _screenOutputOption.AddItem("Window");
        _outputOptionMonitorMap.Add(VideoOutputDevice.WindowMonitorIndex);

        var displays = _displaysManager.GetAvailableDisplays();
        int selectIndex = 0;
        if (selectedMonitor == VideoOutputDevice.WindowMonitorIndex)
            selectIndex = 1;

        // Physical displays start after Virtual (0) and Window (1).
        const int physicalStartIndex = 2;
        for (int i = 0; i < displays.Count; i++)
        {
            var d = displays[i];
            _screenOutputOption.AddItem($"{d.Name}  ({d.Size.X}×{d.Size.Y})");
            _outputOptionMonitorMap.Add(d.Index);
            if (d.Index == selectedMonitor)
                selectIndex = physicalStartIndex + i;
        }

        if (selectedMonitor >= 0 && selectIndex == 0)
        {
            _screenOutputOption.AddItem($"Monitor {selectedMonitor} (missing)");
            _outputOptionMonitorMap.Add(selectedMonitor);
            selectIndex = _outputOptionMonitorMap.Count - 1;
        }

        _screenOutputOption.Select(selectIndex);
    }

    /// <summary>
    /// Display Offset means monitor-relative offset for physical screens, absolute desktop position for Window.
    /// </summary>
    private void UpdateDisplayOffsetLabel(VideoOutputDevice screen)
    {
        var offsetLabel = GetNodeOrNull<Label>("%DisplayOffsetLabel");
        if (offsetLabel == null)
            return;

        if (screen != null && screen.IsWindow)
        {
            offsetLabel.Text = "Window Position";
            if (_displayOffsetXLineEdit != null)
                _displayOffsetXLineEdit.TooltipText = "Desktop X position of the portable window";
            if (_displayOffsetYLineEdit != null)
                _displayOffsetYLineEdit.TooltipText = "Desktop Y position of the portable window";
        }
        else
        {
            offsetLabel.Text = "Display Offset";
            if (_displayOffsetXLineEdit != null)
                _displayOffsetXLineEdit.TooltipText = "Offset from the target display origin (X)";
            if (_displayOffsetYLineEdit != null)
                _displayOffsetYLineEdit.TooltipText = "Offset from the target display origin (Y)";
        }
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
            _layerLockCheckBox.ButtonPressed = layer.Locked;
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
    private const bool DefaultLocked = false;
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
        SetResetVisible(_layerLockResetButton, layer.Locked != DefaultLocked,
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
        RecordDisplaysHistory("Reset screen output");
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
        RecordDisplaysHistory("Reset screen size");
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
        RecordDisplaysHistory("Reset screen keep-aspect");
        _displaysManager.UpdateScreenKeepAspect(screen.OutputId, DefaultKeepAspect);
        LoadScreenProps();
    }

    private void OnScreenPosResetPressed()
    {
        var screen = GetSelectedScreen();
        if (screen == null)
            return;
        RecordDisplaysHistory("Reset screen position");
        _displaysManager.UpdateOutputCanvasPosition(screen.OutputId, DefaultCanvasPosition);
        LoadScreenProps();
        UpdateCanvasGizmos();
    }

    private void OnScreenDisplayOffsetResetPressed()
    {
        var screen = GetSelectedScreen();
        if (screen == null)
            return;
        RecordDisplaysHistory("Reset screen display offset");
        _displaysManager.UpdateScreenDisplayOffset(screen.OutputId, DefaultDisplayOffset);
        LoadScreenProps();
    }

    private void OnScreenTransparentResetPressed()
    {
        var screen = GetSelectedScreen();
        if (screen == null)
            return;
        RecordDisplaysHistory("Reset screen transparency");
        screen.SetTransparent(DefaultTransparent);
        LoadScreenProps();
    }

    private void OnScreenTestPatternResetPressed()
    {
        var screen = GetSelectedScreen();
        if (screen == null)
            return;
        RecordDisplaysHistory("Reset screen test pattern");
        screen.ToggleTestPattern(DefaultTestPattern);
        LoadScreenProps();
    }

    private void OnLayerSizeResetPressed()
    {
        if (_selectionKind != SelectionKind.Layer)
            return;
        RecordDisplaysHistory("Reset layer size");
        var def = _displaysManager.GetDefaultLayerSize();
        _displaysManager.UpdateLayerSize(_selectedLayerId, def);
        LoadLayerProps();
        UpdateCanvasGizmos();
    }

    private void OnLayerKeepAspectResetPressed()
    {
        if (_selectionKind != SelectionKind.Layer)
            return;
        RecordDisplaysHistory("Reset layer keep-aspect");
        _displaysManager.UpdateLayerKeepAspect(_selectedLayerId, DefaultKeepAspect);
        LoadLayerProps();
    }

    private void OnLayerPosResetPressed()
    {
        if (_selectionKind != SelectionKind.Layer)
            return;
        RecordDisplaysHistory("Reset layer position");
        _displaysManager.UpdateLayerCanvasPosition(_selectedLayerId, DefaultCanvasPosition);
        LoadLayerProps();
        UpdateCanvasGizmos();
    }

    private void OnLayerTransparentResetPressed()
    {
        if (_selectionKind != SelectionKind.Layer)
            return;
        RecordDisplaysHistory("Reset layer transparency");
        _displaysManager.UpdateLayerTransparent(_selectedLayerId, DefaultTransparent);
        LoadLayerProps();
    }

    private void OnLayerTestPatternResetPressed()
    {
        if (_selectionKind != SelectionKind.Layer)
            return;
        RecordDisplaysHistory("Reset layer test pattern");
        _displaysManager.ToggleLayerTestPattern(_selectedLayerId, DefaultTestPattern);
        LoadLayerProps();
    }

    private void OnLayerLockResetPressed()
    {
        if (_selectionKind != SelectionKind.Layer)
            return;
        RecordDisplaysHistory("Reset layer lock");
        _displaysManager.UpdateLayerLocked(_selectedLayerId, DefaultLocked);
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

        RecordDisplaysHistory("Rename screen");
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
        if (screen.TargetMonitor == monitor)
            return;

        RecordDisplaysHistory("Change screen output");
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
            if (size == screen.OutputSize)
            {
                _outputSizeXLineEdit.ReleaseFocus();
                return;
            }
            RecordDisplaysHistory("Change screen size");
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
            if (size == screen.OutputSize)
            {
                _outputSizeYLineEdit.ReleaseFocus();
                return;
            }
            RecordDisplaysHistory("Change screen size");
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
            if (val == screen.CanvasPosition.X)
            {
                _outputPosXLineEdit.ReleaseFocus();
                return;
            }
            RecordDisplaysHistory("Change screen position");
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
            if (val == screen.CanvasPosition.Y)
            {
                _outputPosYLineEdit.ReleaseFocus();
                return;
            }
            RecordDisplaysHistory("Change screen position");
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
            if (val == screen.DisplayOffset.X)
            {
                _displayOffsetXLineEdit.ReleaseFocus();
                return;
            }
            RecordDisplaysHistory("Change screen display offset");
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
            if (val == screen.DisplayOffset.Y)
            {
                _displayOffsetYLineEdit.ReleaseFocus();
                return;
            }
            RecordDisplaysHistory("Change screen display offset");
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

        RecordDisplaysHistory(toggled ? "Enable screen keep-aspect" : "Disable screen keep-aspect");
        _displaysManager.UpdateScreenKeepAspect(screen.OutputId, toggled);
        UpdateScreenResetButtons(screen);
    }

    private void OnScreenTransparentToggled(bool toggled)
    {
        if (_isUpdatingProps)
            return;

        var screen = GetSelectedScreen();
        if (screen == null)
            return;
        RecordDisplaysHistory(toggled ? "Enable screen transparency" : "Disable screen transparency");
        screen.SetTransparent(toggled);
        UpdateScreenResetButtons(screen);
    }

    private void OnScreenTestPatternToggled(bool toggled)
    {
        if (_isUpdatingProps)
            return;

        var screen = GetSelectedScreen();
        if (screen == null)
            return;
        RecordDisplaysHistory(toggled ? "Enable screen test pattern" : "Disable screen test pattern");
        screen.ToggleTestPattern(toggled);
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

        RecordDisplaysHistory("Delete screen");
        _displaysManager.RemoveOutput(_selectedScreenId);
        RebuildTrees(selectCanvas: true);
        UpdateCanvasGizmos();
    }

    private void OnNewScreenPressed()
    {
        RecordDisplaysHistory("Create screen");
        string name = $"Screen {DisplaysManager.Screens.Count + 1}";
        var screen = _displaysManager.AddScreen(name, VideoOutputDevice.VirtualMonitorIndex);
        RebuildTrees();
        SelectScreenInTree(screen.OutputId);
        ApplySelection(SelectionKind.Screen, screen.OutputId, -1);
        UpdateCanvasGizmos();
    }

    /// <summary>
    /// Re-checks all screens/outputs: restores closed portable windows, re-places physical
    /// outputs, and refreshes the available-display list.
    /// </summary>
    private void OnRefreshScreensPressed()
    {
        _displaysManager.RefreshAllScreens();
        RebuildTrees();
        if (_selectionKind == SelectionKind.Screen && _selectedScreenId >= 0)
        {
            SelectScreenInTree(_selectedScreenId);
            LoadScreenProps();
        }
        else if (_selectionKind == SelectionKind.Layer && _selectedLayerId >= 0)
        {
            SelectLayerInTree(_selectedLayerId);
            LoadLayerProps();
        }

        UpdateCanvasGizmos();
        _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
            "Canvas Editor: Screens refreshed.", 0);
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

        RecordDisplaysHistory("Rename layer");
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
            if (size == layer.Size)
            {
                _layerSizeXLineEdit.ReleaseFocus();
                return;
            }
            RecordDisplaysHistory("Change layer size");
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
            if (size == layer.Size)
            {
                _layerSizeYLineEdit.ReleaseFocus();
                return;
            }
            RecordDisplaysHistory("Change layer size");
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
            if (val == layer.CanvasPosition.X)
            {
                _layerPosXLineEdit.ReleaseFocus();
                return;
            }
            RecordDisplaysHistory("Change layer position");
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
            if (val == layer.CanvasPosition.Y)
            {
                _layerPosYLineEdit.ReleaseFocus();
                return;
            }
            RecordDisplaysHistory("Change layer position");
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

        RecordDisplaysHistory(toggled ? "Enable layer keep-aspect" : "Disable layer keep-aspect");
        _displaysManager.UpdateLayerKeepAspect(_selectedLayerId, toggled);
        var layer = DisplaysManager.GetLayerById(_selectedLayerId);
        if (layer != null)
            UpdateLayerResetButtons(layer);
    }

    private void OnLayerTransparentToggled(bool toggled)
    {
        if (_isUpdatingProps || _selectionKind != SelectionKind.Layer)
            return;

        RecordDisplaysHistory(toggled ? "Enable layer transparency" : "Disable layer transparency");
        _displaysManager.UpdateLayerTransparent(_selectedLayerId, toggled);
        var layer = DisplaysManager.GetLayerById(_selectedLayerId);
        if (layer != null)
            UpdateLayerResetButtons(layer);
    }

    private void OnLayerTestPatternToggled(bool toggled)
    {
        if (_isUpdatingProps || _selectionKind != SelectionKind.Layer)
            return;

        RecordDisplaysHistory(toggled ? "Enable layer test pattern" : "Disable layer test pattern");
        _displaysManager.ToggleLayerTestPattern(_selectedLayerId, toggled);
        var layer = DisplaysManager.GetLayerById(_selectedLayerId);
        if (layer != null)
            UpdateLayerResetButtons(layer);
    }

    private void OnLayerLockToggled(bool toggled)
    {
        if (_isUpdatingProps || _selectionKind != SelectionKind.Layer)
            return;

        RecordDisplaysHistory(toggled ? "Lock layer" : "Unlock layer");
        _displaysManager.UpdateLayerLocked(_selectedLayerId, toggled);
        var layer = DisplaysManager.GetLayerById(_selectedLayerId);
        if (layer != null)
            UpdateLayerResetButtons(layer);
    }

    private void OnDeleteLayerPressed()
    {
        if (_selectionKind != SelectionKind.Layer)
            return;
        if (_historyManager?.IsRestoring == true)
            return;
        if (_activeLayerDeleteDialog != null && GodotObject.IsInstanceValid(_activeLayerDeleteDialog))
            return;

        int layerId = _selectedLayerId;
        var layer = DisplaysManager.GetLayerById(layerId);
        if (layer == null)
            return;

        string layerName = layer.LayerName ?? $"Layer {layerId}";
        var usage = CueResourceUsage.FindCuesUsingTargetLayer(layerId);

        if (usage.Count == 0)
        {
            PerformLayerDelete(layerId, reassign: null);
            return;
        }

        var alternatives = DisplaysManager.Layers
            .Where(l => l != null && l.LayerId != layerId)
            .OrderBy(l => l.LayerName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(l => (l.LayerId, l.LayerName ?? $"Layer {l.LayerId}"))
            .ToList();

        // Same flow as FileDropPopup: Create → Configure → AddChild → ShowConfigured
        var dialog = ResourceInUseDeleteDialog.Create(out string loadErr);
        if (dialog == null)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"Failed to open delete dialog: {loadErr}", 2);
            return;
        }

        _activeLayerDeleteDialog = dialog;
        dialog.Configure("target layer", layerName, usage.Cues, alternatives);
        dialog.Confirmed += result => OnLayerDeleteDialogConfirmed(layerId, result);
        dialog.Cancelled += () =>
        {
            if (_activeLayerDeleteDialog == dialog) _activeLayerDeleteDialog = null;
        };
        dialog.TreeExiting += () =>
        {
            if (_activeLayerDeleteDialog == dialog) _activeLayerDeleteDialog = null;
        };

        GetTree()?.Root?.AddChild(dialog);
        dialog.ShowConfigured();
    }

    private void OnLayerDeleteDialogConfirmed(int layerId, ResourceInUseDeleteResult result)
    {
        if (_activeLayerDeleteDialog != null)
            _activeLayerDeleteDialog = null;

        if (result == null || result.Action == ResourceInUseDeleteAction.Cancel)
            return;

        var usingCues = CueResourceUsage.FindCuesUsingTargetLayer(layerId).Cues;
        Action reassign = null;

        if (result.Action == ResourceInUseDeleteAction.Unassign)
        {
            reassign = () => CueResourceUsage.UnassignTargetLayer(usingCues, layerId);
        }
        else if (result.Action == ResourceInUseDeleteAction.Replace)
        {
            if (DisplaysManager.GetLayerById(result.ReplaceWithId) == null)
            {
                _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                    $"Cannot replace layer: target id {result.ReplaceWithId} not found.", 2);
                return;
            }
            reassign = () => CueResourceUsage.ReplaceTargetLayer(usingCues, layerId, result.ReplaceWithId);
        }

        PerformLayerDelete(layerId, reassign);
    }

    /// <summary>
    /// Records history, optionally reassigns cues, removes the layer, and refreshes the canvas UI.
    /// </summary>
    private void PerformLayerDelete(int layerId, Action reassign)
    {
        RecordDisplaysHistory("Delete layer");
        if (reassign != null)
        {
            _historyManager?.RecordCuelistChange("Reassign cues after layer delete");
            reassign.Invoke();
        }

        _displaysManager.RemoveLayer(layerId);
        GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.RecheckAllQuiet();
        _globalSignals?.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
        RebuildTrees(selectCanvas: true);
        UpdateCanvasGizmos();
    }

    private void OnNewTargetLayerPressed()
    {
        RecordDisplaysHistory("Create target layer");
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

            if (x == _canvas.CanvasSize.X && y == _canvas.CanvasSize.Y)
            {
                _canvasSizeXLineEdit.ReleaseFocus();
                _canvasSizeYLineEdit.ReleaseFocus();
                return;
            }

            RecordDisplaysHistory("Change canvas size");
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

            _globalSignals.EmitSignal(nameof(GlobalSignals.CanvasSizeChanged), _canvas.CanvasSize);
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
        if (_scrollContainer == null || _subViewportContainer == null || _viewport == null)
            return;

        Vector2 viewportSize = _scrollContainer.Size;
        // Collapsed stage: keep content min at zero so layout can hide the center panel.
        if (viewportSize.X < 8f || viewportSize.Y < 8f)
        {
            _subViewportContainer.CustomMinimumSize = Vector2.Zero;
            _viewport.Size = new Vector2I(1, 1);
            return;
        }

        Vector2 zoomedSize = new Vector2(_canvas.CanvasSize.X * _zoom, _canvas.CanvasSize.Y * _zoom);
        _control.Size = zoomedSize;
        _control.Position = Vector2.Zero;
        _subViewportContainer.CustomMinimumSize = viewportSize;
        _viewport.Size = new Vector2I(Mathf.Max(1, (int)viewportSize.X), Mathf.Max(1, (int)viewportSize.Y));
        if (_backgroundRect != null)
        {
            _backgroundRect.Size = viewportSize;
            (_backgroundRect.Material as ShaderMaterial)?.SetShaderParameter("rect_size", viewportSize);
        }
        if (_canvasOutlinePanel != null)
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
            Color border;
            Color fill;
            if (screen.IsVirtual)
            {
                border = new Color(1f, 0.5f, 0.2f, 0.85f);
                fill = new Color(1f, 0.45f, 0.15f, 0.1f);
            }
            else if (screen.IsWindow)
            {
                border = new Color(0.35f, 0.85f, 0.45f, 0.9f);
                fill = new Color(0.25f, 0.75f, 0.35f, 0.1f);
            }
            else
            {
                border = new Color(1f, 0.2f, 0.15f, 0.9f);
                fill = new Color(1f, 0.15f, 0.1f, 0.12f);
            }

            var gizmo = new CanvasItemGizmo
            {
                IsScreen = true,
                ItemId = screen.OutputId,
                LabelText = screen.OutputName,
                BorderColor = border,
                FillColor = fill,
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

        // Draw bottom-of-stack first so top layer gizmos appear above.
        for (int i = DisplaysManager.Layers.Count - 1; i >= 0; i--)
        {
            var layer = DisplaysManager.Layers[i];
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
                MouseFilter = MouseFilterEnum.Ignore,
                ZIndex = layer.ZIndex
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
        if (_bodyHSplit != null && IsInstanceValid(_bodyHSplit))
            _bodyHSplit.Resized -= OnBodyHSplitResized;
        if (_historyManager != null)
            _historyManager.HistoryRestored -= OnHistoryRestored;
        if (_globalSignals != null && GodotObject.IsInstanceValid(_globalSignals) &&
            _layerGeometryChangedCallable.Target != null &&
            _globalSignals.IsConnected(nameof(GlobalSignals.LayerGeometryChanged), _layerGeometryChangedCallable))
        {
            _globalSignals.Disconnect(nameof(GlobalSignals.LayerGeometryChanged), _layerGeometryChangedCallable);
        }
    }

    /// <summary>
    /// Records a full Displays snapshot (canvas + screens + layers) before a user mutation.
    /// </summary>
    private void RecordDisplaysHistory(string description, string coalesceKey = null)
    {
        if (_historyManager == null || _historyManager.IsRestoring)
            return;
        _historyManager.RecordSettingsChange(description, coalesceKey, "Displays");
    }

    /// <summary>
    /// After settings undo/redo that reloads Displays, rebuild trees/gizmos/props from the new model.
    /// Output windows are recreated by <see cref="DisplaysManager.LoadFromData"/> — never keep
    /// stale screen references in selection beyond IDs.
    /// </summary>
    private void OnHistoryRestored(int scope)
    {
        if (scope != (int)HistoryManager.HistoryScope.Settings)
            return;
        if (!IsInstanceValid(this))
            return;

        // Drop any in-progress drag against a model that was just replaced.
        _isDraggingCanvas = false;
        _dragMode = DragMode.None;
        if (!string.IsNullOrEmpty(_activeDragCoalesceKey))
        {
            _historyManager?.EndCoalesceSession(_activeDragCoalesceKey);
            _activeDragCoalesceKey = null;
        }

        // Canvas instance is stable; size may have changed.
        _canvas = DisplaysManager.Canvas;

        if (!_stageInitialized || !IsVisibleInTree())
        {
            // Stage not ready / not shown — refresh when the user opens this panel again.
            _needsHistoryRefresh = true;
            return;
        }

        _needsHistoryRefresh = false;
        RefreshAfterHistoryRestore();
    }

    /// <summary>
    /// Full UI sync after Displays history restore (or when stage becomes visible after a restore).
    /// </summary>
    private void RefreshAfterHistoryRestore()
    {
        if (!IsInstanceValid(this) || _canvas == null)
            return;

        // Drop selection if the screen/layer no longer exists after restore.
        if (_selectionKind == SelectionKind.Screen
            && _displaysManager.GetOutputById(_selectedScreenId) == null)
        {
            _selectionKind = SelectionKind.Canvas;
            _selectedScreenId = -1;
            _selectedLayerId = -1;
        }
        else if (_selectionKind == SelectionKind.Layer
                 && DisplaysManager.GetLayerById(_selectedLayerId) == null)
        {
            _selectionKind = SelectionKind.Canvas;
            _selectedScreenId = -1;
            _selectedLayerId = -1;
        }

        Vector2I size = _canvas.CanvasSize;
        if (_canvasOutlinePanel != null && IsInstanceValid(_canvasOutlinePanel))
            _canvasOutlinePanel.CustomMinimumSize = new Vector2(size.X, size.Y);
        if (_canvasSelectButton != null && IsInstanceValid(_canvasSelectButton))
            _canvasSelectButton.Text = $"Canvas ({size.X}×{size.Y})";

        _isUpdatingProps = true;
        try
        {
            if (_canvasSizeXLineEdit != null)
                _canvasSizeXLineEdit.Text = size.X.ToString();
            if (_canvasSizeYLineEdit != null)
                _canvasSizeYLineEdit.Text = size.Y.ToString();
        }
        finally
        {
            _isUpdatingProps = false;
        }

        RebuildTrees();
        UpdateCanvasGizmos();
        ShowPropertiesForSelection();
        CallDeferred(nameof(RefreshStageView));
    }

    private void OnDisplaysChanged()
    {
        // Skip mid-history restore — OnHistoryRestored performs a coordinated full refresh.
        if (_historyManager != null && _historyManager.IsRestoring)
            return;

        // Canvas instance is stable; rebind size after ResetToDefaults / load.
        _canvas = DisplaysManager.Canvas;

        // Skip UI work while canvas editor is not shown — mark dirty for next open.
        if (!_stageInitialized || !IsVisibleInTree())
        {
            _needsHistoryRefresh = true;
            return;
        }

        if (_isDraggingCanvas)
        {
            UpdateCanvasGizmos();
            return;
        }

        if (!_isRebuildingTree)
            RebuildTrees();
        UpdateCanvasGizmos();
        // Keep canvas size labels in sync (New Session / load)
        if (_canvas != null)
            OnCanvasSizeChanged(_canvas.CanvasSize);
    }

    /// <summary>
    /// Lightweight follow of live layer geometry (e.g. Translate Layer control while this editor is open).
    /// Avoids a full tree rebuild every animation frame.
    /// </summary>
    /// <param name="layerId">Layer that changed size and/or canvas position.</param>
    private void OnLayerGeometryChanged(int layerId)
    {
        if (_historyManager != null && _historyManager.IsRestoring)
            return;

        // Not shown — no stage work; next open rebuilds from model via DisplaysChanged / dirty flag.
        if (!_stageInitialized || !IsVisibleInTree())
            return;

        // User is dragging on stage — don't fight the gizmo with external updates mid-gesture.
        if (_isDraggingCanvas)
            return;

        UpdateCanvasGizmos();

        // Keep the right-hand property fields live when this layer is selected.
        if (_selectionKind == SelectionKind.Layer && _selectedLayerId == layerId)
            LoadLayerProps();
    }

    private void OnCanvasSizeChanged(Vector2I newSize)
    {
        if (_historyManager != null && _historyManager.IsRestoring)
            return;

        if (_canvasSizeXLineEdit != null)
            _canvasSizeXLineEdit.Text = newSize.X.ToString();
        if (_canvasSizeYLineEdit != null)
            _canvasSizeYLineEdit.Text = newSize.Y.ToString();
        if (_canvasSelectButton != null)
            _canvasSelectButton.Text = $"Canvas ({newSize.X}×{newSize.Y})";
    }

    #endregion
}
