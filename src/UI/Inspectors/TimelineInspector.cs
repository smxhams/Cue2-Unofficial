// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

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
using Cue2.Media.Audio;
using Cue2.UI.Utilities;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cue2.UI.Inspectors;

/// <summary>
/// Inspector for displaying and editing cue timelines, including hierarchical children.
/// Features a fixed track sidebar, time ruler, cue-colored bars, optional waveforms,
/// playhead scrubbing, and play-from-playhead. Parent rows can collapse to hide descendants.
/// </summary>
public partial class TimelineInspector : Control
{
    private GlobalData _globalData;
    private GlobalSignals _globalSignals;
    private HistoryManager _historyManager;
    private MediaEngine _mediaEngine;

    /// <summary>Cancels in-flight timeline waveform batch when the view reloads.</summary>
    private CancellationTokenSource _waveformCts;

    private Cue _focusedCue;

    private Label _infoLabel;
    private MarginContainer _timeLineContainer;

    // Toolbar
    private Button _goToStartButton;
    private Button _playFromPlayheadButton;
    private Label _playheadTimeLabel;
    private Label _durationSummaryLabel;
    private Button _fitButton;
    private Button _zoomOutButton;
    private HSlider _zoomSlider;
    private Button _zoomInButton;

    // Body layout
    private Control _trackSidebar;
    private Control _sidebarContent;
    private ColorRect _sidebarSeparator;
    private Control _rulerHost;
    private ScrollContainer _scrollContainer;
    private Control _timelineArea;
    private TimeGrid _timeGrid;
    private Ruler _ruler;
    private ColorRect _playheadLine;

    private float _scale = 10.0f; // Pixels per second
    private const float RowHeight = 42.0f;
    private const float MinScale = 1.0f;
    private const float MaxScale = 200.0f;
    private const float MinBarWidth = 4.0f;
    private const float InstantBarMinWidth = 8.0f;
    private const float LabelStartOffsetX = 6.0f;
    private const float ZoomStepFactor = 1.4f;
    private const float SidebarWidth = 156.0f;
    private const float CollapseBtnSize = 16.0f;
    private const float SwatchSize = 8.0f;
    private const float SidebarPadX = 4.0f;

    private readonly Dictionary<Cue, ColorRect> _cueToBar = new();
    private readonly Dictionary<Cue, ColorRect> _cueToPreWaitGhost = new();
    private readonly Dictionary<Cue, int> _cueToRow = new();
    private readonly Dictionary<Cue, Label> _cueToTimeLabel = new();
    private readonly Dictionary<Cue, Label> _cueToDurationLabel = new();
    private readonly Dictionary<Cue, Label> _cueToLoopBadge = new();
    private readonly Dictionary<Cue, Button> _cueToCollapseButton = new();
    private readonly Dictionary<Cue, Control> _cueToSidebarRow = new();
    private readonly List<ColorRect> _rowBackgrounds = new();
    private readonly List<TimelineItem> _visibleItems = new();
    /// <summary>Fallback single-cycle length when a looping cue has no measurable segment duration.</summary>
    private const float InfiniteLoopDisplaySeconds = 8.0f;
    /// <summary>Extra content size so bars/labels sit clear of ScrollContainer scrollbars.</summary>
    private const float ScrollbarPadRight = 20.0f;
    private const float ScrollbarPadBottom = 20.0f;

    /// <summary>Cue IDs whose children are hidden in the timeline (local UI state).</summary>
    private readonly HashSet<int> _collapsedCueIds = new();

    /// <summary>Bumped on each <see cref="LoadTimeline"/> so async waveform work abandons stale runs.</summary>
    private int _timelineLoadGeneration;

    /// <summary>Playhead time in display/body-aligned seconds (see <see cref="DisplayTimeToBodyTime"/>).</summary>
    private double _playheadSeconds;
    private bool _scrubbingPlayhead;
    private bool _followLivePlayhead;
    private bool _clickingEmptyTimeline;
    private double _contentMaxTime;

    private float _prevOffset;
    private float _prevVOffset;
    private float _prevScale;
    private Vector2 _prevSize = Vector2.Zero;

    private bool _dragging;
    private Vector2 _initialBarPos;
    private Vector2 _initialMousePos;
    private Cue _draggedCue;
    /// <summary>True after the first real pre-wait change in the current drag (history recorded).</summary>
    private bool _preWaitDragHistoryRecorded;
    private double _lastClickTime;
    private int _lastClickCueId = -1;

    /// <summary>
    /// Called when the node enters the scene tree for the first time.
    /// Initializes references and connects signals.
    /// </summary>
    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _mediaEngine = GetNodeOrNull<MediaEngine>("/root/MediaEngine");
        _historyManager = _globalData?.HistoryManager;

        FocusMode = FocusModeEnum.Click;

        _globalSignals.ShellFocused += ShellSelected;
        _globalSignals.ShowTimelineWaveformsChanged += OnShowTimelineWaveformsChanged;
        _globalSignals.NewSession += OnNewSession;
        if (_historyManager != null)
            _historyManager.HistoryRestored += OnHistoryRestored;

        _infoLabel = GetNode<Label>("%InfoLabel");
        _timeLineContainer = GetNode<MarginContainer>("%TimelineContainer");

        VisibilityChanged += LoadTimeline;

        WireToolbar();
        WireBody();

        LoadTimeline();
    }

    /// <summary>
    /// Disconnects signals and cleans up when leaving the tree.
    /// </summary>
    public override void _ExitTree()
    {
        try { _waveformCts?.Cancel(); } catch { /* ignore */ }
        try { _waveformCts?.Dispose(); } catch { /* ignore */ }
        _waveformCts = null;

        if (_globalSignals != null)
        {
            _globalSignals.ShellFocused -= ShellSelected;
            _globalSignals.ShowTimelineWaveformsChanged -= OnShowTimelineWaveformsChanged;
            _globalSignals.NewSession -= OnNewSession;
        }
        if (_historyManager != null)
            _historyManager.HistoryRestored -= OnHistoryRestored;

        if (_zoomSlider != null)
            _zoomSlider.ValueChanged -= OnZoomChanged;
        if (_goToStartButton != null)
            _goToStartButton.Pressed -= OnGoToStartPressed;
        if (_playFromPlayheadButton != null)
            _playFromPlayheadButton.Pressed -= OnPlayFromPlayheadPressed;
        if (_fitButton != null)
            _fitButton.Pressed -= OnFitPressed;
        if (_zoomInButton != null)
            _zoomInButton.Pressed -= OnZoomInPressed;
        if (_zoomOutButton != null)
            _zoomOutButton.Pressed -= OnZoomOutPressed;
        if (_ruler != null)
            _ruler.GuiInput -= OnRulerGuiInput;
        if (_timelineArea != null)
            _timelineArea.GuiInput -= OnTimelineAreaGuiInput;
        if (_scrollContainer != null)
            _scrollContainer.GuiInput -= OnScrollContainerGuiInput;

        VisibilityChanged -= LoadTimeline;

        // Explicitly free dynamic timeline nodes so CanvasItem RIDs are not leaked on exit
        // (playhead / bars may have been detached from the tree by clear/rebuild paths).
        ClearTimelineVisuals();
        FreeNodeIfValid(ref _playheadLine);
        FreeNodeIfValid(ref _timeGrid);
        FreeNodeIfValid(ref _ruler);
        FreeNodeIfValid(ref _sidebarSeparator);
        FreeNodeIfValid(ref _sidebarContent);

        base._ExitTree();
    }

    /// <summary>
    /// Detaches and frees a dynamically created node, clearing the field reference.
    /// </summary>
    private static void FreeNodeIfValid<T>(ref T node) where T : Node
    {
        if (node == null) return;
        if (IsInstanceValid(node))
        {
            var parent = node.GetParent();
            if (parent != null)
                parent.RemoveChild(node);
            node.QueueFree();
        }
        node = null;
    }

    private void WireToolbar()
    {
        _zoomSlider = GetNode<HSlider>("%ZoomSlider");
        _zoomSlider.MinValue = MinScale;
        _zoomSlider.MaxValue = MaxScale;
        _zoomSlider.Value = _scale;
        _zoomSlider.ValueChanged += OnZoomChanged;

        _goToStartButton = GetNodeOrNull<Button>("%GoToStartButton");
        if (_goToStartButton != null)
        {
            TrySetAtlasIcon(_goToStartButton, "Left", "⏮");
            _goToStartButton.Pressed += OnGoToStartPressed;
        }

        _playFromPlayheadButton = GetNodeOrNull<Button>("%PlayFromPlayheadButton");
        if (_playFromPlayheadButton != null)
        {
            TrySetAtlasIcon(_playFromPlayheadButton, "Play", "▶");
            _playFromPlayheadButton.Pressed += OnPlayFromPlayheadPressed;
        }

        _playheadTimeLabel = GetNodeOrNull<Label>("%PlayheadTimeLabel");
        _durationSummaryLabel = GetNodeOrNull<Label>("%DurationSummaryLabel");
        UpdatePlayheadTimeLabel();

        _fitButton = GetNodeOrNull<Button>("%FitButton");
        if (_fitButton != null)
            _fitButton.Pressed += OnFitPressed;

        _zoomOutButton = GetNodeOrNull<Button>("%ZoomOutButton");
        if (_zoomOutButton != null)
            _zoomOutButton.Pressed += OnZoomOutPressed;

        _zoomInButton = GetNodeOrNull<Button>("%ZoomInButton");
        if (_zoomInButton != null)
            _zoomInButton.Pressed += OnZoomInPressed;
    }

    private void WireBody()
    {
        // Corner cell above the track list — same height as RulerHost so rows align with bars.
        var sidebarCorner = GetNodeOrNull<Control>("%SidebarRulerCorner");
        if (sidebarCorner != null)
        {
            var cornerBg = new ColorRect
            {
                Name = "CornerBg",
                Color = new Color(0.08f, 0.09f, 0.1f, 0.95f),
                MouseFilter = MouseFilterEnum.Ignore
            };
            sidebarCorner.AddChild(cornerBg);
            cornerBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }

        _trackSidebar = GetNodeOrNull<Control>("%TrackSidebar");
        if (_trackSidebar != null)
        {
            _trackSidebar.ClipContents = true;
            _trackSidebar.MouseFilter = MouseFilterEnum.Stop;

            // Dark professional sidebar background
            var sidebarBg = new ColorRect
            {
                Name = "SidebarBg",
                Color = new Color(0.07f, 0.075f, 0.08f, 1f),
                MouseFilter = MouseFilterEnum.Ignore,
                ZIndex = -2
            };
            _trackSidebar.AddChild(sidebarBg);
            sidebarBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

            _sidebarContent = new Control
            {
                Name = "SidebarContent",
                MouseFilter = MouseFilterEnum.Pass,
                ZIndex = 0
            };
            _trackSidebar.AddChild(_sidebarContent);
            _sidebarContent.Position = Vector2.Zero;
            _sidebarContent.Size = new Vector2(SidebarWidth, 100);
        }

        // Full-height separator between sidebar and tracks (body-level, not content-height)
        _sidebarSeparator = new ColorRect
        {
            Name = "SidebarSeparator",
            Color = new Color(0.32f, 0.34f, 0.36f, 0.9f),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 25
        };
        AddChild(_sidebarSeparator);

        _scrollContainer = GetNode<ScrollContainer>("%TimelineScrollContainer");
        _scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.ShowAlways;
        _scrollContainer.VerticalScrollMode = ScrollContainer.ScrollMode.ShowAlways;
        _scrollContainer.GuiInput += OnScrollContainerGuiInput;

        _timelineArea = GetNode<Control>("%TimelineArea");
        _timelineArea.MouseFilter = MouseFilterEnum.Stop;
        _timelineArea.GuiInput += OnTimelineAreaGuiInput;
        _timelineArea.FocusMode = FocusModeEnum.Click;

        // Time grid (background of timeline content)
        _timeGrid = new TimeGrid
        {
            Name = "TimeGrid",
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = -2,
            ZoomScale = _scale
        };
        _timelineArea.AddChild(_timeGrid);

        _rulerHost = GetNodeOrNull<Control>("%RulerHost");
        if (_rulerHost == null)
        {
            _rulerHost = new Control { CustomMinimumSize = new Vector2(0, 26) };
        }

        // Keep corner spacer height locked to the ruler host height.
        SyncSidebarRulerCornerHeight();

        _ruler = new Ruler
        {
            Name = "Ruler",
            MouseFilter = MouseFilterEnum.Stop,
            ContentOriginX = 0f,
            ZoomScale = _scale
        };
        _rulerHost.AddChild(_ruler);
        _ruler.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _ruler.GuiInput += OnRulerGuiInput;

        // Create and parent playhead immediately so it is never an orphaned ObjectDB instance.
        // (Previously created unparented here and only added in EnsurePlayheadLine.)
        _playheadLine = new ColorRect
        {
            Name = "PlayheadLine",
            Color = new Color(0.95f, 0.35f, 0.15f, 0.95f),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 50,
            Size = new Vector2(2, RowHeight),
            Visible = false
        };
        if (_timelineArea != null)
            _timelineArea.AddChild(_playheadLine);
    }

    /// <summary>
    /// Matches the left corner cell height to <see cref="_rulerHost"/> so track rows
    /// align with timeline bars (ruler sits above the scroll area on the right).
    /// </summary>
    private void SyncSidebarRulerCornerHeight()
    {
        var corner = GetNodeOrNull<Control>("%SidebarRulerCorner");
        if (corner == null || _rulerHost == null) return;

        float h = _rulerHost.CustomMinimumSize.Y;
        if (_rulerHost.Size.Y > 1f)
            h = _rulerHost.Size.Y;
        if (h < 1f) h = 26f;

        corner.CustomMinimumSize = new Vector2(SidebarWidth, h);
    }

    private void TrySetAtlasIcon(Button button, string iconName, string fallbackText)
    {
        if (button == null) return;
        try
        {
            button.Icon = GetThemeIcon(iconName, "AtlasIcons");
            button.ExpandIcon = true;
            button.Text = string.Empty;
        }
        catch
        {
            button.Text = fallbackText;
        }
    }

    private void OnShowTimelineWaveformsChanged(bool _)
    {
        if (IsInstanceValid(this) && Visible)
            LoadTimeline();
    }

    private void OnNewSession()
    {
        _collapsedCueIds.Clear();
        _playheadSeconds = 0;
        _followLivePlayhead = false;
        UpdatePlayheadTimeLabel();
        if (IsInstanceValid(this) && Visible)
            LoadTimeline();
    }

    private void OnHistoryRestored(int scope)
    {
        if (!IsInstanceValid(this) || !Visible) return;
        if (scope == (int)HistoryManager.HistoryScope.Settings
            || scope == (int)HistoryManager.HistoryScope.Cue
            || scope == (int)HistoryManager.HistoryScope.Cuelist)
        {
            LoadTimeline();
        }
    }

    /// <summary>
    /// Called every frame. Syncs ruler, sidebar scroll, playhead, and live follow during playback.
    /// </summary>
    /// <param name="delta">The time elapsed since the previous frame.</param>
    public override void _Process(double delta)
    {
        SyncSidebarRulerCornerHeight();
        LayoutSidebarSeparator();
        SyncSidebarScroll();

        if (_followLivePlayhead && !_scrubbingPlayhead)
            TryFollowLivePlayhead();

        if (_ruler == null || _scrollContainer == null) return;

        float currentOffset = _scrollContainer.GetHScroll();
        float currentScale = _scale;
        Vector2 currentSize = _rulerHost != null
            ? new Vector2(_rulerHost.Size.X, _rulerHost.Size.Y)
            : new Vector2(_scrollContainer.Size.X, 26);

        bool needsRedraw = false;

        if (Mathf.Abs(currentOffset - _prevOffset) > 0.001f)
        {
            _ruler.Offset = currentOffset;
            _prevOffset = currentOffset;
            needsRedraw = true;
        }

        if (Mathf.Abs(currentScale - _prevScale) > 0.001f)
        {
            _ruler.ZoomScale = currentScale;
            if (_timeGrid != null)
                _timeGrid.ZoomScale = currentScale;
            _prevScale = currentScale;
            needsRedraw = true;
        }

        if (currentSize != _prevSize)
        {
            _ruler.Size = currentSize;
            _prevSize = currentSize;
            needsRedraw = true;
        }

        _ruler.PlayheadSeconds = _playheadSeconds;
        if (needsRedraw)
            _ruler.QueueRedraw();
        else if (_followLivePlayhead || _scrubbingPlayhead)
            _ruler.QueueRedraw();

        if (_timeGrid != null && needsRedraw)
            _timeGrid.QueueRedraw();

        UpdatePlayheadLineGeometry();
    }

    /// <summary>
    /// Handles keyboard shortcuts when the timeline is visible and no LineEdit is focused.
    /// Left/Right nudge playhead; Home resets. Play is via the toolbar button only.
    /// </summary>
    /// <param name="event">The input event.</param>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible || _focusedCue == null || _timeLineContainer == null || !_timeLineContainer.Visible)
            return;

        if (IsTextInputFocused())
            return;

        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
            return;

        bool handled = false;
        switch (key.Keycode)
        {
            case Key.Left:
                NudgePlayhead(key.ShiftPressed ? -1.0 : -0.1);
                handled = true;
                break;
            case Key.Right:
                NudgePlayhead(key.ShiftPressed ? 1.0 : 0.1);
                handled = true;
                break;
            case Key.Home:
                SetPlayheadSeconds(0);
                _followLivePlayhead = false;
                handled = true;
                break;
        }

        if (handled)
            GetViewport()?.SetInputAsHandled();
    }

    private static bool IsTextInputFocused()
    {
        var focus = GodotObject.IsInstanceValid(Engine.GetMainLoop())
            ? ((SceneTree)Engine.GetMainLoop()).Root?.GuiGetFocusOwner()
            : null;
        return focus is LineEdit or TextEdit or CodeEdit;
    }

    private void NudgePlayhead(double deltaSeconds)
    {
        _followLivePlayhead = false;
        SetPlayheadSeconds(_playheadSeconds + deltaSeconds);
        EnsurePlayheadVisible();
    }

    private void LayoutSidebarSeparator()
    {
        if (_sidebarSeparator == null || !IsInstanceValid(_sidebarSeparator))
            return;

        bool show = _timeLineContainer != null && _timeLineContainer.Visible && _focusedCue != null;
        _sidebarSeparator.Visible = show;
        if (!show) return;

        // Align with right edge of the left column (corner + track sidebar).
        Control leftEdge = _trackSidebar;
        var leftCol = GetNodeOrNull<Control>("TimelineContainer/VBoxContainer/Body/LeftColumn");
        if (leftCol != null && IsInstanceValid(leftCol))
            leftEdge = leftCol;
        if (leftEdge == null) return;

        var sideOrigin = leftEdge.GlobalPosition - GlobalPosition;
        float x = sideOrigin.X + leftEdge.Size.X - 1f;
        _sidebarSeparator.Position = new Vector2(x, 0);
        _sidebarSeparator.Size = new Vector2(1f, Size.Y);
    }

    private void SyncSidebarScroll()
    {
        if (_sidebarContent == null || _scrollContainer == null || !IsInstanceValid(_sidebarContent))
            return;

        float vScroll = _scrollContainer.GetVScroll();
        if (Mathf.Abs(vScroll - _prevVOffset) > 0.01f || _sidebarContent.Position.Y != -vScroll)
        {
            _sidebarContent.Position = new Vector2(0, -vScroll);
            _prevVOffset = vScroll;
        }

        // Keep sidebar content width matched to sidebar
        if (_trackSidebar != null)
        {
            float h = Math.Max(_timelineArea?.CustomMinimumSize.Y ?? 0, _trackSidebar.Size.Y);
            _sidebarContent.Size = new Vector2(_trackSidebar.Size.X, Math.Max(h, RowHeight));
        }
    }

    /// <summary>
    /// Handles changes to the zoom slider value.
    /// </summary>
    /// <param name="value">The new zoom scale value.</param>
    private void OnZoomChanged(double value)
    {
        _scale = (float)value;
        ApplyScaleToVisuals();
    }

    private void OnZoomInPressed()
    {
        SetScaleAnchored(_scale * ZoomStepFactor, _scrollContainer?.Size.X * 0.5f ?? 0f);
    }

    private void OnZoomOutPressed()
    {
        SetScaleAnchored(_scale / ZoomStepFactor, _scrollContainer?.Size.X * 0.5f ?? 0f);
    }

    private void OnGoToStartPressed()
    {
        _followLivePlayhead = false;
        SetPlayheadSeconds(0);
        if (_scrollContainer != null)
            _scrollContainer.SetHScroll(0);
    }

    private void OnFitPressed()
    {
        if (_scrollContainer == null || _contentMaxTime <= 1e-6)
            return;

        float viewW = Math.Max(40f, _scrollContainer.Size.X - 8f);
        // Leave a little padding on the right
        float targetScale = (float)(viewW / (_contentMaxTime + 0.5));
        targetScale = Mathf.Clamp(targetScale, MinScale, MaxScale);

        _scale = targetScale;
        if (_zoomSlider != null && Math.Abs(_zoomSlider.Value - _scale) > 0.001)
            _zoomSlider.SetValueNoSignal(_scale);
        ApplyScaleToVisuals();
        _scrollContainer.SetHScroll(0);
    }

    /// <summary>
    /// Sets zoom scale while keeping the time under <paramref name="anchorViewportX"/> stable.
    /// </summary>
    private void SetScaleAnchored(float newScale, float anchorViewportX)
    {
        newScale = Mathf.Clamp(newScale, MinScale, MaxScale);
        if (Mathf.Abs(newScale - _scale) < 0.001f) return;

        float hScroll = _scrollContainer?.GetHScroll() ?? 0f;
        double timeUnderCursor = (hScroll + anchorViewportX) / Math.Max(0.001f, _scale);

        _scale = newScale;
        if (_zoomSlider != null && Math.Abs(_zoomSlider.Value - _scale) > 0.001)
            _zoomSlider.SetValueNoSignal(_scale);

        ApplyScaleToVisuals();

        if (_scrollContainer != null)
        {
            float newScroll = (float)(timeUnderCursor * _scale) - anchorViewportX;
            _scrollContainer.SetHScroll(Mathf.Max(0, (int)newScroll));
        }
    }

    private void ApplyScaleToVisuals()
    {
        UpdateAllPositionsAndSizes();
        UpdatePlayheadLineGeometry();
        if (_ruler != null)
        {
            _ruler.ZoomScale = _scale;
            _ruler.QueueRedraw();
        }
        if (_timeGrid != null)
        {
            _timeGrid.ZoomScale = _scale;
            _timeGrid.QueueRedraw();
        }
    }

    private void OnRulerGuiInput(InputEvent @event)
    {
        if (_focusedCue == null || _scrollContainer == null) return;

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                _scrubbingPlayhead = true;
                _followLivePlayhead = false;
                SetPlayheadFromRulerLocalX(mb.Position.X);
                GrabFocusSafe();
            }
            else
            {
                _scrubbingPlayhead = false;
            }
            _ruler.GetViewport()?.SetInputAsHandled();
        }
        else if (@event is InputEventMouseMotion mm && _scrubbingPlayhead)
        {
            SetPlayheadFromRulerLocalX(mm.Position.X);
            _ruler.GetViewport()?.SetInputAsHandled();
        }
        else if (@event is InputEventMouseButton wheel
                 && (wheel.ButtonIndex == MouseButton.WheelUp || wheel.ButtonIndex == MouseButton.WheelDown)
                 && wheel.Pressed
                 && (wheel.CtrlPressed || wheel.MetaPressed))
        {
            ZoomAtViewportX(wheel.Position.X, wheel.ButtonIndex == MouseButton.WheelUp);
            _ruler.GetViewport()?.SetInputAsHandled();
        }
    }

    private void OnTimelineAreaGuiInput(InputEvent @event)
    {
        if (_focusedCue == null) return;

        if (@event is InputEventMouseButton mb)
        {
            if ((mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown)
                && mb.Pressed
                && (mb.CtrlPressed || mb.MetaPressed))
            {
                // Position is local to timeline area; convert to viewport-relative for scroll container
                float viewportX = mb.Position.X - (_scrollContainer?.GetHScroll() ?? 0);
                // Actually timeline area is inside scroll: local X is content X
                float contentX = mb.Position.X;
                float viewX = contentX - (_scrollContainer?.GetHScroll() ?? 0);
                ZoomAtViewportX(viewX, mb.ButtonIndex == MouseButton.WheelUp);
                _timelineArea.GetViewport()?.SetInputAsHandled();
                return;
            }

            if (mb.ButtonIndex == MouseButton.Left && mb.Pressed)
            {
                // Empty area click (not on a bar — bars consume the event themselves)
                _clickingEmptyTimeline = true;
                _followLivePlayhead = false;
                SetPlayheadFromContentX(mb.Position.X);
                GrabFocusSafe();
                _timelineArea.GetViewport()?.SetInputAsHandled();
            }
            else if (mb.ButtonIndex == MouseButton.Left && !mb.Pressed)
            {
                _clickingEmptyTimeline = false;
            }
        }
        else if (@event is InputEventMouseMotion mm && _clickingEmptyTimeline)
        {
            SetPlayheadFromContentX(mm.Position.X);
            _timelineArea.GetViewport()?.SetInputAsHandled();
        }
    }

    private void OnScrollContainerGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton wheel
            && (wheel.ButtonIndex == MouseButton.WheelUp || wheel.ButtonIndex == MouseButton.WheelDown)
            && wheel.Pressed
            && (wheel.CtrlPressed || wheel.MetaPressed))
        {
            ZoomAtViewportX(wheel.Position.X, wheel.ButtonIndex == MouseButton.WheelUp);
            _scrollContainer.GetViewport()?.SetInputAsHandled();
        }
    }

    private void ZoomAtViewportX(float viewportX, bool zoomIn)
    {
        float factor = zoomIn ? ZoomStepFactor : 1f / ZoomStepFactor;
        SetScaleAnchored(_scale * factor, viewportX);
    }

    private void SetPlayheadFromRulerLocalX(float localX)
    {
        float contentX = localX + (_scrollContainer?.GetHScroll() ?? 0);
        SetPlayheadFromContentX(contentX);
    }

    private void SetPlayheadFromContentX(float contentX)
    {
        double time = contentX / Math.Max(0.001f, _scale);
        SetPlayheadSeconds(time);
    }

    /// <summary>
    /// Sets the playhead to the given display-timeline seconds (clamped ≥ 0).
    /// </summary>
    /// <param name="seconds">Display time in seconds.</param>
    private void SetPlayheadSeconds(double seconds)
    {
        if (seconds < 0) seconds = 0;
        _playheadSeconds = seconds;
        UpdatePlayheadTimeLabel();
        UpdatePlayheadLineGeometry();
        _ruler?.QueueRedraw();
    }

    private void UpdatePlayheadTimeLabel()
    {
        if (_playheadTimeLabel == null || !IsInstanceValid(_playheadTimeLabel)) return;
        _playheadTimeLabel.Text = UiUtilities.FormatTime(_playheadSeconds);
    }

    private void UpdateDurationSummary()
    {
        if (_durationSummaryLabel == null || !IsInstanceValid(_durationSummaryLabel)) return;
        int tracks = _visibleItems.Count;
        string total = FormatCompactDuration(_contentMaxTime);
        _durationSummaryLabel.Text = tracks > 0
            ? $"Total: {total} · {tracks} track{(tracks == 1 ? "" : "s")}"
            : string.Empty;
    }

    private static string FormatCompactDuration(double seconds)
    {
        if (seconds < 0) return "∞";
        int totalSec = (int)Math.Floor(Math.Max(0, seconds));
        int min = totalSec / 60;
        int sec = totalSec % 60;
        if (min >= 60)
        {
            int hour = min / 60;
            min %= 60;
            return $"{hour}h{min:D2}m:{sec:D2}";
        }
        return $"{min}m:{sec:D2}";
    }

    private void EnsurePlayheadLine()
    {
        if (_timelineArea == null) return;

        // Reuse existing valid line when already under the timeline area.
        if (_playheadLine != null && IsInstanceValid(_playheadLine))
        {
            if (_playheadLine.GetParent() == _timelineArea)
                return;

            // Orphaned / wrong parent from older code paths — free before recreating.
            if (_playheadLine.GetParent() != null)
                _playheadLine.GetParent().RemoveChild(_playheadLine);
            _playheadLine.QueueFree();
            _playheadLine = null;
        }

        _playheadLine = new ColorRect
        {
            Name = "PlayheadLine",
            Color = new Color(0.95f, 0.35f, 0.15f, 0.95f),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 50,
            Size = new Vector2(2, RowHeight)
        };
        _timelineArea.AddChild(_playheadLine);
    }

    private void UpdatePlayheadLineGeometry()
    {
        if (_playheadLine == null || !IsInstanceValid(_playheadLine) || _timelineArea == null)
            return;

        float x = (float)(_playheadSeconds * _scale);
        // Stay within drawable content (exclude scrollbar pad) so the line doesn't sit under the bar.
        float h = Math.Max(RowHeight, _timelineArea.CustomMinimumSize.Y - ScrollbarPadBottom);
        if (_cueToRow.Count > 0)
            h = Math.Max(h, _cueToRow.Values.Max() * RowHeight + RowHeight);
        _playheadLine.Position = new Vector2(x, 0);
        _playheadLine.Size = new Vector2(2, h);
        _playheadLine.Visible = _focusedCue != null && _timeLineContainer != null && _timeLineContainer.Visible;
    }

    /// <summary>
    /// Auto-scrolls horizontally so the playhead stays within the visible viewport.
    /// </summary>
    private void EnsurePlayheadVisible()
    {
        if (_scrollContainer == null) return;
        float px = (float)(_playheadSeconds * _scale);
        float viewW = _scrollContainer.Size.X;
        float hScroll = _scrollContainer.GetHScroll();
        const float margin = 40f;

        if (px < hScroll + margin)
            _scrollContainer.SetHScroll(Mathf.Max(0, (int)(px - margin)));
        else if (px > hScroll + viewW - margin)
            _scrollContainer.SetHScroll(Mathf.Max(0, (int)(px - viewW + margin)));
    }

    private void GrabFocusSafe()
    {
        try { GrabFocus(); } catch { /* ignore */ }
    }

    /// <summary>
    /// Display timeline origin for the focused cue's body (pre-wait start).
    /// For a root cue this is 0; for nested focus it is the parent's action start.
    /// </summary>
    private double GetFocusedBodyOriginDisplayTime()
    {
        if (_focusedCue == null) return 0;
        double actionStart = ComputeActionStart(_focusedCue);
        return Math.Max(0.0, actionStart - Math.Max(0.0, _focusedCue.PreWait));
    }

    /// <summary>Maps inspector display time → ActiveCue body timeline seconds.</summary>
    private double DisplayTimeToBodyTime(double displaySeconds)
    {
        return Math.Max(0.0, displaySeconds - GetFocusedBodyOriginDisplayTime());
    }

    /// <summary>Maps ActiveCue body timeline seconds → inspector display time.</summary>
    private double BodyTimeToDisplayTime(double bodySeconds)
    {
        return Math.Max(0.0, GetFocusedBodyOriginDisplayTime() + bodySeconds);
    }

    private void TryFollowLivePlayhead()
    {
        var active = FindPlayingFocusedActiveCue();
        if (active == null)
        {
            _followLivePlayhead = false;
            return;
        }

        try
        {
            double bodyT = active.GetCueTimelineSeconds();
            SetPlayheadSeconds(BodyTimeToDisplayTime(bodyT));
            EnsurePlayheadVisible();
        }
        catch
        {
            _followLivePlayhead = false;
        }
    }

    private ActiveCue FindPlayingFocusedActiveCue()
    {
        if (_focusedCue == null || _globalData?.CueCommandExectutor == null)
            return null;

        foreach (var root in _globalData.CueCommandExectutor.ActiveCues)
        {
            if (root == null || !IsInstanceValid(root)) continue;
            try
            {
                foreach (var active in root.EnumerateSelfAndDescendants())
                {
                    if (active == null || !IsInstanceValid(active)) continue;
                    if (active.Cue != null && active.Cue.Id == _focusedCue.Id)
                        return active;
                }
            }
            catch
            {
                // Active may be mid-teardown
            }
        }
        return null;
    }

    /// <summary>
    /// Starts (or seeks) the focused cue from the current playhead position.
    /// </summary>
    private void OnPlayFromPlayheadPressed()
    {
        if (_focusedCue == null)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "Timeline: No cue selected to play", 1);
            return;
        }

        if (!_focusedCue.Armed)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"Timeline: Cue \"{_focusedCue.Name}\" is disarmed", 1);
            return;
        }

        var executor = _globalData?.CueCommandExectutor;
        if (executor == null)
        {
            GD.PrintErr("TimelineInspector:OnPlayFromPlayheadPressed - CueCommandExecutor missing");
            return;
        }

        try { _focusedCue.CalculateTotalDuration(); } catch { /* best-effort */ }

        double bodyTime = DisplayTimeToBodyTime(_playheadSeconds);
        double pre = Math.Max(0.0, _focusedCue.PreWait);
        double playable = _focusedCue.Duration < 0
            ? -1.0
            : pre + Math.Max(0.0, _focusedCue.Duration);

        if (playable >= 0 && bodyTime >= playable - 1e-4)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"Timeline: Playhead is past the end of \"{_focusedCue.Name}\" — nothing to play", 1);
            GD.Print(
                $"TimelineInspector:OnPlayFromPlayheadPressed - past end body={bodyTime:F3}s playable={playable:F3}s");
            return;
        }

        GD.Print(
            $"TimelineInspector:OnPlayFromPlayheadPressed - {_focusedCue.Name} @ display={_playheadSeconds:F3}s " +
            $"body={bodyTime:F3}s (pre={pre:F3}s content={Math.Max(0, bodyTime - pre):F3}s playable={playable:F3}s)");

        var existing = FindPlayingFocusedActiveCue();
        if (existing != null)
        {
            existing.RequestSeek(bodyTime, relative: false);
            _followLivePlayhead = true;
            return;
        }

        executor.ActivateSequenceFrom(_focusedCue, controlGoFadeIn: null, startAtTimelineSeconds: bodyTime);
        _followLivePlayhead = true;
        _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
            $"Timeline: Playing \"{_focusedCue.Name}\" from {UiUtilities.FormatTime(_playheadSeconds)}", 0);
    }

    /// <summary>
    /// Loads and renders the timeline for the focused cue.
    /// Clears existing UI elements and rebuilds based on the cue hierarchy.
    /// </summary>
    private void LoadTimeline()
    {
        if (!Visible || _timeLineContainer == null || !_timeLineContainer.Visible) return;

        GD.Print("TimelineInspector:LoadTimeline - Loading timeline");
        int gen = ++_timelineLoadGeneration;

        ClearTimelineVisuals();

        if (_focusedCue == null) return;

        _visibleItems.Clear();
        int row = 0;
        CollectCues(_focusedCue, _visibleItems, ref row);

        // Zebra row backgrounds in timeline content
        int maxRow = row;
        for (int i = 0; i < maxRow; i++)
        {
            var bg = new ColorRect
            {
                Color = (i % 2 == 0) ? GlobalStyles.ZebraOdd : GlobalStyles.ZebraEven,
                Position = new Vector2(0, i * RowHeight),
                Size = new Vector2(100, RowHeight),
                ZIndex = -1,
                MouseFilter = MouseFilterEnum.Ignore
            };
            _timelineArea.AddChild(bg);
            _rowBackgrounds.Add(bg);
        }

        // Ensure time grid is behind everything
        if (_timeGrid != null && IsInstanceValid(_timeGrid))
        {
            if (_timeGrid.GetParent() != _timelineArea)
                _timelineArea.AddChild(_timeGrid);
            _timelineArea.MoveChild(_timeGrid, 0);
            _timeGrid.ZoomScale = _scale;
            _timeGrid.Position = Vector2.Zero;
        }

        bool showWaveforms = _globalData?.Settings?.ShowTimelineWaveforms ?? true;

        foreach (var item in _visibleItems)
        {
            CreateSidebarRow(item);
            CreatePreWaitGhost(item);
            CreateCueBar(item, showWaveforms);
        }

        EnsurePlayheadLine();
        // Keep playhead on top
        if (_playheadLine != null && IsInstanceValid(_playheadLine) && _playheadLine.GetParent() == _timelineArea)
            _timelineArea.MoveChild(_playheadLine, _timelineArea.GetChildCount() - 1);

        UpdateAllPositionsAndSizes();
        UpdatePlayheadLineGeometry();
        UpdateDurationSummary();

        if (showWaveforms && _mediaEngine != null)
            _ = EnsureWaveformsForItemsAsync(_visibleItems.ToList(), gen);
    }

    private void ClearTimelineVisuals()
    {
        foreach (var bg in _rowBackgrounds)
        {
            if (bg != null && IsInstanceValid(bg))
                bg.QueueFree();
        }
        _rowBackgrounds.Clear();

        foreach (var bar in _cueToBar.Values)
        {
            if (bar != null && IsInstanceValid(bar))
                bar.QueueFree();
        }
        _cueToBar.Clear();
        _cueToRow.Clear();

        foreach (var ghost in _cueToPreWaitGhost.Values)
        {
            if (ghost != null && IsInstanceValid(ghost))
                ghost.QueueFree();
        }
        _cueToPreWaitGhost.Clear();

        foreach (var label in _cueToTimeLabel.Values)
        {
            if (label != null && IsInstanceValid(label))
                label.QueueFree();
        }
        _cueToTimeLabel.Clear();

        foreach (var label in _cueToDurationLabel.Values)
        {
            if (label != null && IsInstanceValid(label))
                label.QueueFree();
        }
        _cueToDurationLabel.Clear();

        foreach (var badge in _cueToLoopBadge.Values)
        {
            if (badge != null && IsInstanceValid(badge))
                badge.QueueFree();
        }
        _cueToLoopBadge.Clear();

        foreach (var btn in _cueToCollapseButton.Values)
        {
            if (btn != null && IsInstanceValid(btn))
                btn.QueueFree();
        }
        _cueToCollapseButton.Clear();

        foreach (var row in _cueToSidebarRow.Values)
        {
            if (row != null && IsInstanceValid(row))
                row.QueueFree();
        }
        _cueToSidebarRow.Clear();

        // Keep the playhead node parented under _timelineArea (do NOT RemoveChild without free —
        // that orphaned ColorRect "PlayheadLine" and leaked a CanvasItem RID on exit).
        // Hide until the next rebuild repositions it.
        if (_playheadLine != null && IsInstanceValid(_playheadLine))
            _playheadLine.Visible = false;

        // Keep time grid; just detach references that will be recreated
        _visibleItems.Clear();
    }

    private void CreateSidebarRow(TimelineItem item)
    {
        if (_sidebarContent == null) return;

        var cue = item.Cue;
        var row = new Control
        {
            Name = $"SidebarRow_{cue.Id}",
            MouseFilter = MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(SidebarWidth, RowHeight),
            Size = new Vector2(SidebarWidth, RowHeight),
            Position = new Vector2(0, item.Row * RowHeight)
        };
        row.GuiInput += e => OnSidebarRowGuiInput(e, cue);

        var bg = new ColorRect
        {
            Name = "Bg",
            Color = (item.Row % 2 == 0) ? GlobalStyles.ZebraOdd : GlobalStyles.ZebraEven,
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = -1
        };
        row.AddChild(bg);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        float x = SidebarPadX + item.Depth * 10f;

        if (item.HasChildren)
        {
            bool collapsed = _collapsedCueIds.Contains(cue.Id);
            var btn = new Button
            {
                Name = $"Collapse_{cue.Id}",
                FocusMode = FocusModeEnum.None,
                Flat = true,
                CustomMinimumSize = new Vector2(CollapseBtnSize, CollapseBtnSize),
                Size = new Vector2(CollapseBtnSize, CollapseBtnSize),
                Position = new Vector2(x, (RowHeight - CollapseBtnSize) * 0.5f),
                TooltipText = collapsed ? "Expand children" : "Collapse children",
                MouseDefaultCursorShape = CursorShape.PointingHand,
                ZIndex = 2
            };
            try
            {
                btn.ThemeTypeVariation = "AtlasIcons";
                btn.Icon = GetThemeIcon(collapsed ? "Right" : "Down", "AtlasIcons");
                btn.ExpandIcon = true;
                btn.AddThemeConstantOverride("icon_max_width", 12);
            }
            catch
            {
                btn.Text = collapsed ? "▶" : "▼";
            }

            int cueId = cue.Id;
            btn.Pressed += () => OnCollapseToggled(cueId);
            row.AddChild(btn);
            _cueToCollapseButton[cue] = btn;
            x += CollapseBtnSize + 2f;
        }
        else
        {
            x += 4f; // slight indent alignment with chevron-less rows
        }

        // Color swatch
        Color swatchColor = cue.Color;
        if (IsNearBlack(swatchColor))
            swatchColor = GlobalStyles.LowColor2;
        var swatch = new ColorRect
        {
            Name = "Swatch",
            Color = swatchColor,
            Size = new Vector2(SwatchSize, SwatchSize),
            Position = new Vector2(x, (RowHeight - SwatchSize) * 0.5f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        row.AddChild(swatch);
        x += SwatchSize + 4f;

        // Cue number
        var numLabel = new Label
        {
            Name = "CueNum",
            Text = cue.CueNum ?? string.Empty,
            Position = new Vector2(x, 4f),
            MouseFilter = MouseFilterEnum.Ignore,
            ClipText = true
        };
        numLabel.AddThemeFontSizeOverride("font_size", 11);
        numLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.75f, 0.78f, 1f));
        row.AddChild(numLabel);

        // Name (truncated)
        var nameLabel = new Label
        {
            Name = "CueName",
            Text = cue.Name ?? string.Empty,
            Position = new Vector2(x, 20f),
            MouseFilter = MouseFilterEnum.Ignore,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        nameLabel.AddThemeFontSizeOverride("font_size", 11);
        nameLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.92f, 0.94f, 1f));
        row.AddChild(nameLabel);

        // Size labels to remaining width
        float remaining = Math.Max(20f, SidebarWidth - x - SidebarPadX);
        numLabel.Size = new Vector2(remaining, 16f);
        nameLabel.Size = new Vector2(remaining, 16f);

        row.TooltipText = $"{cue.CueNum} — {cue.Name}";

        _sidebarContent.AddChild(row);
        _cueToSidebarRow[cue] = row;
        _cueToRow[cue] = item.Row;
    }

    private void OnSidebarRowGuiInput(InputEvent @event, Cue cue)
    {
        if (@event is InputEventMouseButton mb
            && mb.ButtonIndex == MouseButton.Left
            && mb.Pressed
            && !mb.DoubleClick)
        {
            // Ignore if click is on collapse button region — button handles that itself.
            _followLivePlayhead = false;
            SetPlayheadSeconds(ComputeActionStart(cue));
            EnsurePlayheadVisible();
            GrabFocusSafe();
            GetViewport()?.SetInputAsHandled();
        }
    }

    private void OnCollapseToggled(int cueId)
    {
        if (!_collapsedCueIds.Add(cueId))
            _collapsedCueIds.Remove(cueId);
        LoadTimeline();
    }

    private void CreatePreWaitGhost(TimelineItem item)
    {
        var cue = item.Cue;
        if (cue.PreWait <= 1e-4) return;

        var ghost = new ColorRect
        {
            Name = $"PreWaitGhost_{cue.Id}",
            Color = new Color(0.5f, 0.55f, 0.6f, 0.12f),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 0
        };
        _timelineArea.AddChild(ghost);
        _cueToPreWaitGhost[cue] = ghost;
    }

    private void CreateCueBar(TimelineItem item, bool showWaveforms)
    {
        var cue = item.Cue;
        var barColor = ResolveBarColor(cue);
        var accentColor = ResolveAccentColor(cue, barColor);

        var bar = new ColorRect
        {
            Color = barColor,
            MouseFilter = MouseFilterEnum.Stop,
            ClipContents = true,
            MouseDefaultCursorShape = CursorShape.Move,
            ZIndex = 2
        };
        bar.GuiInput += e => HandleBarInput(e, cue, bar);
        _timelineArea.AddChild(bar);

        if (showWaveforms && TryGetCueWaveformSource(cue, out var peaks, out float startNorm, out float endNorm, out int playCount))
            AttachWaveformLayer(bar, peaks, startNorm, endNorm, playCount);

        var startLine = new ColorRect
        {
            Name = "StartLine",
            Color = accentColor,
            Size = new Vector2(2, RowHeight - 6),
            Position = new Vector2(0, 0),
            MouseFilter = MouseFilterEnum.Ignore
        };
        bar.AddChild(startLine);

        var endLine = new ColorRect
        {
            Name = "EndLine",
            Color = accentColor.Darkened(0.15f),
            Size = new Vector2(2, RowHeight - 6),
            Position = new Vector2(0, 0),
            MouseFilter = MouseFilterEnum.Ignore
        };
        bar.AddChild(endLine);

        var flag = new ColorRect
        {
            Name = "Flag",
            Color = accentColor,
            Size = new Vector2(8, 8),
            Position = new Vector2(0, RowHeight - 16),
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.Move
        };
        flag.GuiInput += e => HandleBarInput(e, cue, bar);
        bar.AddChild(flag);

        // Free-floating timing: line 1 = start + pre-wait, line 2 = length (below).
        double actionStart = ComputeActionStart(cue);
        var timeLabel = new Label
        {
            Name = $"TimeLabel_{cue.Id}",
            Text = FormatBarStartPreLabel(cue, actionStart),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 5
        };
        StyleBarTextLabel(timeLabel, new Color(0.92f, 0.94f, 0.96f, 0.95f));
        _timelineArea.AddChild(timeLabel);

        var durationLabel = new Label
        {
            Name = $"DurationLabel_{cue.Id}",
            Text = FormatBarLengthLabel(cue),
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 5
        };
        StyleBarTextLabel(durationLabel, new Color(0.78f, 0.82f, 0.86f, 0.95f));
        _timelineArea.AddChild(durationLabel);

        // Infinite: own media loop vs child-driven infinite (different badge wording).
        if (IsInfiniteLoopCue(cue))
        {
            bool childLoop = IsChildDrivenInfinite(cue);
            var loopBadge = new Label
            {
                Name = $"LoopBadge_{cue.Id}",
                Text = FormatLoopBadgeText(cue),
                MouseFilter = MouseFilterEnum.Ignore,
                ZIndex = 6,
                TooltipText = childLoop
                    ? "A nested child cue loops indefinitely"
                    : "This cue's media loops indefinitely"
            };
            StyleBarTextLabel(loopBadge, GlobalStyles.HighColor1.Lightened(0.15f));
            _timelineArea.AddChild(loopBadge);
            _cueToLoopBadge[cue] = loopBadge;
        }

        _cueToBar[cue] = bar;
        _cueToRow[cue] = item.Row;
        _cueToTimeLabel[cue] = timeLabel;
        _cueToDurationLabel[cue] = durationLabel;
    }

    private static void StyleBarTextLabel(Label label, Color fontColor)
    {
        if (label == null) return;
        label.AddThemeFontSizeOverride("font_size", 10);
        label.AddThemeColorOverride("font_color", fontColor);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
    }

    private static Color ResolveBarColor(Cue cue)
    {
        if (cue == null) return GlobalStyles.LowColor5;
        Color c = cue.Color;
        if (IsNearBlack(c))
            return GlobalStyles.LowColor5;

        // Blend cue color with LowColor palette for a professional muted bar
        return GlobalStyles.LowColor4.Lerp(c, 0.55f).Darkened(0.1f) with { A = 0.92f };
    }

    private static Color ResolveAccentColor(Cue cue, Color barColor)
    {
        if (cue != null && !IsNearBlack(cue.Color))
            return cue.Color.Lightened(0.15f);
        return GlobalStyles.HighColor3;
    }

    private static bool IsNearBlack(Color c)
    {
        return c.R < 0.04f && c.G < 0.04f && c.B < 0.04f;
    }

    /// <summary>Line 1: absolute start and pre-wait.</summary>
    private static string FormatBarStartPreLabel(Cue cue, double actionStart)
    {
        if (cue == null) return string.Empty;
        string startStr = UiUtilities.FormatTime(actionStart);
        string preStr = UiUtilities.FormatTime(Math.Max(0, cue.PreWait));
        return $"{startStr}  (pre {preStr})";
    }

    /// <summary>Line 2: content length (or loop / child-loop notation).</summary>
    private static string FormatBarLengthLabel(Cue cue)
    {
        if (cue == null) return string.Empty;
        if (IsChildDrivenInfinite(cue))
            return "∞  Child Looping";

        if (HasSelfInfiniteContent(cue))
        {
            double cycle = GetSingleCycleDurationSeconds(cue);
            return cycle > 1e-4 ? $"len {UiUtilities.FormatTime(cycle)}  ↻" : "len ∞";
        }

        // Duration < 0 but we couldn't classify — still show infinite.
        if (IsInfiniteLoopCue(cue))
            return "len ∞";

        double len = Math.Max(0, cue.Duration);
        if (len < 1e-4) return "len 0s";
        if (len < 60) return $"len {len:0.##}s";
        return $"len {UiUtilities.FormatTime(len)}";
    }

    /// <summary>Badge text after the bar for infinite cues.</summary>
    private static string FormatLoopBadgeText(Cue cue)
    {
        if (IsChildDrivenInfinite(cue))
            return "∞ Child Looping";
        return "↻ LOOP";
    }

    /// <summary>True when cue content duration is infinite (own loop or child loop).</summary>
    private static bool IsInfiniteLoopCue(Cue cue) => cue != null && cue.Duration < 0;

    /// <summary>
    /// True when this cue itself has looping / infinite media (not only via a child).
    /// </summary>
    private static bool HasSelfInfiniteContent(Cue cue)
    {
        if (cue == null) return false;

        var audio = cue.GetAudioComponent();
        if (audio != null && audio.Loop)
            return true;

        var video = cue.GetVideoComponent();
        if (video != null)
        {
            if (video.Loop)
                return true;
            // Still image held until stopped is infinite on this cue.
            if (video.IsImage && video.Duration <= 0)
                return true;
        }

        var text = cue.GetTextComponent();
        if (text != null)
        {
            // Duration 0 / TotalDuration < 0 = hold until stopped.
            if (text.TotalDuration < 0 || text.Duration <= 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Infinite shell duration driven by a nested child loop (this cue has no self-infinite media).
    /// </summary>
    private static bool IsChildDrivenInfinite(Cue cue) =>
        IsInfiniteLoopCue(cue) && !HasSelfInfiniteContent(cue);

    /// <summary>
    /// One playback segment length for display (not × playcount / not infinite span).
    /// For looping cues this is a single cycle; for finite cues the full content duration.
    /// </summary>
    private static double GetBarDisplayDurationSeconds(Cue cue)
    {
        if (cue == null) return 0;
        if (!IsInfiniteLoopCue(cue))
            return Math.Max(0, cue.Duration);
        return GetSingleCycleDurationSeconds(cue);
    }

    /// <summary>
    /// Best-effort single media/content cycle length for looping cues.
    /// </summary>
    private static double GetSingleCycleDurationSeconds(Cue cue)
    {
        if (cue == null) return InfiniteLoopDisplaySeconds;

        double cycle = 0;
        var audio = cue.GetAudioComponent();
        if (audio != null && audio.Duration > 0)
            cycle = Math.Max(cycle, audio.Duration);

        var video = cue.GetVideoComponent();
        if (video != null && video.Duration > 0)
            cycle = Math.Max(cycle, video.Duration);

        var text = cue.GetTextComponent();
        if (text != null && text.Duration > 0)
            cycle = Math.Max(cycle, text.Duration);

        // Nested groups: longest finite child cycle as a stand-in
        if (cycle <= 1e-9 && cue.ChildCues != null)
        {
            foreach (var childId in cue.ChildCues)
            {
                var child = CueList.FetchCueFromId(childId);
                if (child == null) continue;
                double childCycle = child.Duration >= 0
                    ? child.Duration
                    : GetSingleCycleDurationSeconds(child);
                cycle = Math.Max(cycle, childCycle);
            }
        }

        return cycle > 1e-9 ? cycle : InfiniteLoopDisplaySeconds;
    }

    private static void AttachWaveformLayer(
        ColorRect bar,
        WaveformPeaks peaks,
        float startNorm,
        float endNorm,
        int playCount)
    {
        if (bar == null || peaks == null) return;
        var existing = bar.GetNodeOrNull<CueBarWaveform>("Waveform");
        if (existing != null)
        {
            existing.Peaks = peaks;
            existing.StartNorm = startNorm;
            existing.EndNorm = endNorm;
            existing.PlayCount = Math.Max(1, playCount);
            existing.Size = bar.Size;
            existing.QueueRedraw();
            return;
        }

        var wave = new CueBarWaveform
        {
            Name = "Waveform",
            MouseFilter = MouseFilterEnum.Ignore,
            Peaks = peaks,
            StartNorm = startNorm,
            EndNorm = endNorm,
            PlayCount = Math.Max(1, playCount),
            WaveColor = GlobalStyles.LowColor1.Lightened(0.25f),
            DividerColor = new Color(1f, 1f, 1f, 0.45f)
        };
        bar.AddChild(wave);
        bar.MoveChild(wave, 0);
        wave.Position = Vector2.Zero;
        wave.Size = bar.Size;
    }

    /// <summary>
    /// For each visible cue, load waveform peaks the same way inspectors do:
    /// component payload → session disk cache → generate, then store on the component.
    /// </summary>
    private async Task EnsureWaveformsForItemsAsync(List<TimelineItem> items, int gen)
    {
        if (_mediaEngine == null || items == null) return;

        // Cancel prior batch (rapid rebuild / toggle waveforms / focus) — single-flight engine
        // still shares in-flight path jobs; this abandons UI wait and stops starting more cues.
        try { _waveformCts?.Cancel(); } catch { /* ignore */ }
        try { _waveformCts?.Dispose(); } catch { /* ignore */ }
        _waveformCts = new CancellationTokenSource();
        var ct = _waveformCts.Token;

        foreach (var item in items)
        {
            if (gen != _timelineLoadGeneration || !IsInstanceValid(this) || ct.IsCancellationRequested)
                return;

            var cue = item.Cue;
            if (cue == null) continue;

            try
            {
                await EnsureCueWaveformDataAsync(cue, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"TimelineInspector:EnsureWaveformsForItemsAsync - Cue {cue.Id}: {ex.Message}");
                continue;
            }

            if (gen != _timelineLoadGeneration || !IsInstanceValid(this) || ct.IsCancellationRequested)
                return;

            if (!_cueToBar.TryGetValue(cue, out var bar) || bar == null || !IsInstanceValid(bar))
                continue;

            if (TryGetCueWaveformSource(cue, out var peaks, out float startNorm, out float endNorm, out int playCount))
            {
                AttachWaveformLayer(bar, peaks, startNorm, endNorm, playCount);
                if (_cueToRow.ContainsKey(cue))
                {
                    double start = ComputeActionStart(cue);
                    ApplyBarGeometry(bar, cue, start, out _, out _);
                }
            }
        }
    }

    /// <summary>
    /// Ensures <see cref="AudioComponent.WaveformData"/> / video waveform is populated via
    /// <see cref="MediaEngine.GenerateWaveformAsync"/> (cache hit or generate).
    /// </summary>
    private async Task EnsureCueWaveformDataAsync(Cue cue, CancellationToken ct = default)
    {
        if (cue == null || _mediaEngine == null) return;

        var audio = cue.GetAudioComponent();
        if (audio != null && !string.IsNullOrEmpty(audio.AudioFile))
        {
            if (audio.WaveformData == null || audio.WaveformData.Length == 0)
            {
                byte[] data = await _mediaEngine.GenerateWaveformAsync(audio.AudioFile, ct);
                if (data != null && data.Length > 0)
                    audio.WaveformData = data;
            }
            return;
        }

        var video = cue.GetVideoComponent();
        if (video != null && video.UseAudio && !video.IsImage && !string.IsNullOrEmpty(video.VideoFile))
        {
            if (video.WaveformData == null || video.WaveformData.Length == 0)
            {
                byte[] data = await _mediaEngine.GenerateWaveformAsync(video.VideoFile, ct);
                if (data != null && data.Length > 0)
                    video.WaveformData = data;
            }
        }
    }

    /// <summary>
    /// Resolves waveform peak data for a cue from dedicated audio or video-embedded audio.
    /// </summary>
    /// <returns>True when peaks are available to draw.</returns>
    private static bool TryGetCueWaveformSource(
        Cue cue,
        out WaveformPeaks peaks,
        out float startNorm,
        out float endNorm,
        out int playCount)
    {
        peaks = null;
        startNorm = 0f;
        endNorm = 1f;
        playCount = 1;
        if (cue == null) return false;

        var audio = cue.GetAudioComponent();
        if (audio != null && audio.WaveformData != null && audio.WaveformData.Length > 0)
        {
            peaks = WaveformPeaks.FromBytes(audio.WaveformData);
            if (peaks == null || peaks.BinCount < 1) return false;

            double fileDur = audio.Metadata?.Duration ?? 0;
            if (fileDur <= 1e-9 && audio.Duration > 0)
                fileDur = audio.StartTime + audio.Duration;
            if (fileDur > 1e-9)
            {
                startNorm = (float)Math.Clamp(audio.StartTime / fileDur, 0.0, 1.0);
                endNorm = audio.EndTime < 0
                    ? 1f
                    : (float)Math.Clamp(audio.EndTime / fileDur, startNorm + 1e-6, 1.0);
            }
            playCount = audio.Loop ? 1 : Math.Max(1, audio.PlayCount);
            return true;
        }

        var video = cue.GetVideoComponent();
        if (video != null && video.UseAudio && video.WaveformData != null && video.WaveformData.Length > 0)
        {
            peaks = WaveformPeaks.FromBytes(video.WaveformData);
            if (peaks == null || peaks.BinCount < 1) return false;

            double fileDur = video.Metadata?.Duration ?? 0;
            if (fileDur <= 1e-9 && video.Duration > 0)
                fileDur = video.StartTime + video.Duration;
            if (fileDur > 1e-9 && !video.IsImage)
            {
                startNorm = (float)Math.Clamp(video.StartTime / fileDur, 0.0, 1.0);
                endNorm = video.EndTime < 0
                    ? 1f
                    : (float)Math.Clamp(video.EndTime / fileDur, startNorm + 1e-6, 1.0);
            }
            playCount = video.Loop ? 1 : Math.Max(1, video.PlayCount);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Recursively collects cues and their children into a list for timeline rendering.
    /// Skips descendants of collapsed parents.
    /// </summary>
    /// <param name="cue">The current cue to add.</param>
    /// <param name="items">The list to populate with timeline items.</param>
    /// <param name="row">The current row index, incremented for each cue.</param>
    /// <param name="depth">Hierarchy depth (0 = focused root).</param>
    private void CollectCues(Cue cue, List<TimelineItem> items, ref int row, int depth = 0)
    {
        if (cue == null) return;

        bool hasChildren = cue.ChildCues != null && cue.ChildCues.Count > 0;
        items.Add(new TimelineItem
        {
            Cue = cue,
            Row = row++,
            Depth = depth,
            HasChildren = hasChildren
        });

        if (hasChildren && _collapsedCueIds.Contains(cue.Id))
            return;

        if (cue.ChildCues == null) return;
        foreach (var childId in cue.ChildCues)
        {
            var child = CueList.FetchCueFromId(childId);
            if (child != null)
                CollectCues(child, items, ref row, depth + 1);
        }
    }

    /// <summary>
    /// Computes the absolute start time of a cue, including accumulated pre-waits from parents.
    /// </summary>
    /// <param name="cue">The cue to compute the start time for.</param>
    /// <returns>The absolute start time in seconds.</returns>
    private double ComputeActionStart(Cue cue)
    {
        if (cue.ParentId == -1)
            return cue.PreWait;

        var parent = CueList.FetchCueFromId(cue.ParentId);
        if (parent == null)
        {
            GD.PrintErr($"TimelineInspector:ComputeActionStart - Parent not found for cue {cue.Id}");
            return 0;
        }
        return ComputeActionStart(parent) + cue.PreWait;
    }

    /// <summary>
    /// Computes the absolute start time of the parent cue.
    /// </summary>
    /// <param name="cue">The cue whose parent start time is needed.</param>
    /// <returns>The parent's absolute start time, or 0 if no parent.</returns>
    private double ComputeParentActionStart(Cue cue)
    {
        if (cue.ParentId == -1)
            return 0;

        var parent = CueList.FetchCueFromId(cue.ParentId);
        return parent != null ? ComputeActionStart(parent) : 0;
    }

    /// <summary>
    /// Updates positions and sizes for all cue bars in the timeline.
    /// </summary>
    private void UpdateAllPositionsAndSizes()
    {
        double maxTime = 0;

        foreach (var kvp in _cueToBar)
        {
            var cue = kvp.Key;
            var bar = kvp.Value;
            if (bar == null || !IsInstanceValid(bar)) continue;

            var start = ComputeActionStart(cue);
            ApplyBarGeometry(bar, cue, start, out _, out double contentDur);
            double end = start + contentDur;
            if (IsInfiniteLoopCue(cue))
                end += IsChildDrivenInfinite(cue) ? 5.0 : 2.5; // room for loop / "Child Looping" badge
            maxTime = Math.Max(maxTime, end);
        }

        // Pre-wait ghosts can extend before action start
        foreach (var kvp in _cueToPreWaitGhost)
        {
            var cue = kvp.Key;
            var ghost = kvp.Value;
            if (ghost == null || !IsInstanceValid(ghost)) continue;
            double parentStart = ComputeParentActionStart(cue);
            double actionStart = ComputeActionStart(cue);
            maxTime = Math.Max(maxTime, actionStart);
            maxTime = Math.Max(maxTime, parentStart + Math.Max(0, cue.PreWait));
        }

        _contentMaxTime = maxTime;

        if (_cueToRow.Count == 0)
        {
            ApplyTimelineContentSize(100, RowHeight);
            UpdateDurationSummary();
            return;
        }

        float contentWidth = (float)(maxTime * _scale + 100);
        float contentHeight = _cueToRow.Values.Max() * RowHeight + RowHeight;
        ApplyTimelineContentSize(contentWidth, contentHeight);

        foreach (var bg in _rowBackgrounds)
            bg.Size = new Vector2(contentWidth, RowHeight);

        if (_timeGrid != null && IsInstanceValid(_timeGrid))
        {
            _timeGrid.Position = Vector2.Zero;
            _timeGrid.Size = new Vector2(contentWidth, contentHeight);
            _timeGrid.ZoomScale = _scale;
            _timeGrid.ContentHeight = contentHeight;
            _timeGrid.QueueRedraw();
        }

        if (_sidebarContent != null && IsInstanceValid(_sidebarContent))
        {
            float sideW = _trackSidebar?.Size.X > 1 ? _trackSidebar.Size.X : SidebarWidth;
            _sidebarContent.CustomMinimumSize = new Vector2(sideW, contentHeight);
            _sidebarContent.Size = new Vector2(sideW, contentHeight);
        }

        UpdatePlayheadLineGeometry();
        UpdateDurationSummary();
    }

    /// <summary>
    /// Applies position/size/labels/waveform layout for a single cue bar.
    /// Content origin is 0 (sidebar is separate from timeline content).
    /// Infinite/looping cues show a single cycle block (not an arbitrary long span).
    /// </summary>
    private void ApplyBarGeometry(ColorRect bar, Cue cue, double start, out float calculatedWidth, out double contentDur)
    {
        bool infinite = IsInfiniteLoopCue(cue);
        contentDur = GetBarDisplayDurationSeconds(cue);
        bool instant = !infinite && contentDur < 1e-4;

        calculatedWidth = (float)(contentDur * _scale);

        float minW = instant ? InstantBarMinWidth : MinBarWidth;
        float displayWidth = Mathf.Max(calculatedWidth, minW);

        int row = _cueToRow.GetValueOrDefault(cue, 0);
        float barH = RowHeight - 6f;
        float barY = row * RowHeight + 3f;
        bar.Size = new Vector2(displayWidth, barH);
        bar.Position = new Vector2((float)(start * _scale), barY);

        // Instant cues get a brighter accent
        if (instant)
        {
            var accent = ResolveAccentColor(cue, bar.Color).Lightened(0.25f);
            var startLineInst = bar.GetNodeOrNull<ColorRect>("StartLine");
            if (startLineInst != null)
                startLineInst.Color = accent;
            var flagInst = bar.GetNodeOrNull<ColorRect>("Flag");
            if (flagInst != null)
                flagInst.Color = accent;
        }

        var wave = bar.GetNodeOrNull<CueBarWaveform>("Waveform");
        if (wave != null && IsInstanceValid(wave))
        {
            // Looping cues: draw one cycle only (playCount forced to 1 for display).
            if (infinite)
                wave.PlayCount = 1;
            wave.Position = Vector2.Zero;
            wave.Size = bar.Size;
            wave.QueueRedraw();
        }

        var endLine = bar.GetNodeOrNull<ColorRect>("EndLine");
        if (endLine != null)
        {
            endLine.Position = new Vector2(Mathf.Max(0, displayWidth - 2), 0);
            endLine.Size = new Vector2(2, bar.Size.Y);
            // Hide end accent for very short/instant markers
            endLine.Visible = !instant || displayWidth > 10f;
        }

        var startLine = bar.GetNodeOrNull<ColorRect>("StartLine");
        if (startLine != null)
            startLine.Size = new Vector2(2, bar.Size.Y);

        var flag = bar.GetNodeOrNull<ColorRect>("Flag");
        if (flag != null)
            flag.Position = new Vector2(0, Math.Max(0, bar.Size.Y - 8));

        // Pre-wait ghost: parentStart → actionStart
        if (_cueToPreWaitGhost.TryGetValue(cue, out var ghost) && ghost != null && IsInstanceValid(ghost))
        {
            double parentStart = ComputeParentActionStart(cue);
            float ghostX = (float)(parentStart * _scale);
            float ghostW = (float)(Math.Max(0, start - parentStart) * _scale);
            ghost.Position = new Vector2(ghostX, barY);
            ghost.Size = new Vector2(Mathf.Max(0, ghostW), barH);
            ghost.Visible = ghostW > 0.5f;
        }

        PositionCueLabels(cue, bar.Position, displayWidth, start);
    }

    /// <summary>
    /// Places start/pre on the first line, length on the second line below, and loop badge after the bar.
    /// </summary>
    private void PositionCueLabels(Cue cue, Vector2 barPosition, float barDisplayWidth, double startTimeSeconds)
    {
        float labelX = barPosition.X + LabelStartOffsetX;
        float topY = barPosition.Y + 1f;

        if (_cueToTimeLabel.TryGetValue(cue, out var timeLabel) && timeLabel != null && IsInstanceValid(timeLabel))
        {
            timeLabel.Text = FormatBarStartPreLabel(cue, startTimeSeconds);
            timeLabel.Position = new Vector2(labelX, topY);
            timeLabel.ResetSize();
        }

        if (_cueToDurationLabel.TryGetValue(cue, out var durationLabel)
            && durationLabel != null && IsInstanceValid(durationLabel))
        {
            durationLabel.Text = FormatBarLengthLabel(cue);
            // Second line: length sits below pre-wait / start line
            durationLabel.Position = new Vector2(labelX, topY + 13f);
            durationLabel.ResetSize();
        }

        if (_cueToLoopBadge.TryGetValue(cue, out var loopBadge) && loopBadge != null && IsInstanceValid(loopBadge))
        {
            bool childLoop = IsChildDrivenInfinite(cue);
            loopBadge.Text = FormatLoopBadgeText(cue);
            loopBadge.TooltipText = childLoop
                ? "A nested child cue loops indefinitely"
                : "This cue's media loops indefinitely";
            // Child-loop badge is longer — keep a bit more room after the bar.
            loopBadge.Position = new Vector2(barPosition.X + barDisplayWidth + 6f, topY + 4f);
            loopBadge.ResetSize();
            loopBadge.Visible = true;
        }
    }

    /// <summary>
    /// Sets TimelineArea minimum size with right/bottom padding so content stays clear of scrollbars.
    /// Drawing (rows, grid, bars) uses the unpadded content size; padding is empty space.
    /// </summary>
    private void ApplyTimelineContentSize(float contentWidth, float contentHeight)
    {
        if (_timelineArea == null || !IsInstanceValid(_timelineArea)) return;
        contentWidth = Math.Max(1f, contentWidth);
        contentHeight = Math.Max(1f, contentHeight);
        _timelineArea.CustomMinimumSize = new Vector2(
            contentWidth + ScrollbarPadRight,
            contentHeight + ScrollbarPadBottom);
    }

    /// <summary>
    /// Handles input events for cue bars, including dragging to adjust pre-wait times
    /// and double-click to set playhead at action start.
    /// </summary>
    /// <param name="event">The input event.</param>
    /// <param name="cue">The associated cue.</param>
    /// <param name="bar">The visual bar representation.</param>
    /// <remarks>
    /// History is recorded on the first real pre-wait change during a drag, not on mouse-down,
    /// so a click without drag does not push an empty undo step (P1-20).
    /// </remarks>
    private void HandleBarInput(InputEvent @event, Cue cue, ColorRect bar)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (mouseButton.Pressed)
                {
                    // Double-click: set playhead to action start
                    double now = Time.GetTicksMsec() / 1000.0;
                    if (cue.Id == _lastClickCueId && now - _lastClickTime < 0.35)
                    {
                        _followLivePlayhead = false;
                        SetPlayheadSeconds(ComputeActionStart(cue));
                        EnsurePlayheadVisible();
                        _lastClickCueId = -1;
                        _dragging = false;
                        _preWaitDragHistoryRecorded = false;
                        GrabFocusSafe();
                        GetViewport()?.SetInputAsHandled();
                        return;
                    }
                    _lastClickTime = now;
                    _lastClickCueId = cue.Id;

                    _dragging = true;
                    _initialBarPos = bar.Position;
                    _initialMousePos = GetViewport().GetMousePosition();
                    _draggedCue = cue;
                    // Do not RecordCueChange here — click without drag would create a no-op undo step.
                    _preWaitDragHistoryRecorded = false;
                    GrabFocusSafe();
                }
                else
                {
                    if (_draggedCue != null && _preWaitDragHistoryRecorded)
                        _globalData?.HistoryManager?.EndCoalesceSession($"cue:{_draggedCue.Id}:timeline-prewait");
                    _dragging = false;
                    _draggedCue = null;
                    _preWaitDragHistoryRecorded = false;
                    _globalSignals.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
                }
            }
        }
        else if (@event is InputEventMouseMotion && _dragging && _draggedCue == cue)
        {
            var currentMousePos = GetViewport().GetMousePosition();
            var delta = currentMousePos - _initialMousePos;

            // Earliest legal action start = parent action start (pre-wait 0). Never snap back
            // to drag-start position — that felt mouse-speed dependent and wrong for children.
            double parentStart = ComputeParentActionStart(cue);
            float minX = (float)(Math.Max(0.0, parentStart) * _scale);

            float newX = _initialBarPos.X + delta.X;
            newX = Mathf.Max(minX, newX);

            double newStart = newX / Math.Max(0.001f, _scale);
            double newPreWait = Math.Max(0.0, newStart - parentStart);

            // Skip no-op updates (still re-apply geometry so the bar stays clamped at minX).
            if (Math.Abs(cue.PreWait - newPreWait) < 1e-9)
            {
                if (_cueToBar.TryGetValue(cue, out var liveBar) && liveBar != null && IsInstanceValid(liveBar))
                    ApplyBarGeometry(liveBar, cue, ComputeActionStart(cue), out _, out _);
                return;
            }

            // First real change in this drag: capture pre-change memento (coalesced for the drag).
            if (!_preWaitDragHistoryRecorded
                && _globalData?.HistoryManager?.IsRestoring != true)
            {
                _globalData?.HistoryManager?.RecordCueChange(
                    cue.Id, "Edit pre-wait (timeline)", $"cue:{cue.Id}:timeline-prewait");
                _preWaitDragHistoryRecorded = true;
            }

            cue.PreWait = newPreWait;
            RecalcDurationsUp(cue);
            UpdateSubtreePositions(cue);

            if (cue.ParentId != -1)
            {
                var parent = CueList.FetchCueFromId(cue.ParentId);
                UpdateAncestorSizes(parent);
            }

            UpdateTimelineSize();
        }
    }

    /// <summary>
    /// Recalculates total durations for a cue and its ancestors.
    /// </summary>
    /// <param name="cue">The cue to start recalculating from.</param>
    private void RecalcDurationsUp(Cue cue)
    {
        cue.CalculateTotalDuration();
        if (cue.ParentId != -1)
        {
            var parent = CueList.FetchCueFromId(cue.ParentId);
            if (parent != null)
                RecalcDurationsUp(parent);
        }
    }

    /// <summary>
    /// Updates positions and sizes for a cue and its child subtree.
    /// </summary>
    /// <param name="cue">The root cue of the subtree.</param>
    private void UpdateSubtreePositions(Cue cue)
    {
        if (!_cueToBar.TryGetValue(cue, out var bar) || bar == null || !IsInstanceValid(bar)) return;

        var start = ComputeActionStart(cue);
        ApplyBarGeometry(bar, cue, start, out _, out _);

        if (cue.ChildCues == null) return;
        foreach (var childId in cue.ChildCues)
        {
            var child = CueList.FetchCueFromId(childId);
            if (child != null)
                UpdateSubtreePositions(child);
        }
    }

    /// <summary>
    /// Updates sizes for a cue and its ancestors without repositioning children only.
    /// </summary>
    /// <param name="cue">The cue to update.</param>
    private void UpdateAncestorSizes(Cue cue)
    {
        if (cue == null) return;

        if (_cueToBar.TryGetValue(cue, out var bar) && bar != null && IsInstanceValid(bar))
        {
            var start = ComputeActionStart(cue);
            ApplyBarGeometry(bar, cue, start, out _, out _);
        }

        if (cue.ParentId != -1)
        {
            var parent = CueList.FetchCueFromId(cue.ParentId);
            UpdateAncestorSizes(parent);
        }
    }

    /// <summary>
    /// Recalculates and sets the minimum size of the timeline area based on content.
    /// </summary>
    private void UpdateTimelineSize()
    {
        double maxTime = 0;
        foreach (var kvp in _cueToBar)
        {
            var cue = kvp.Key;
            var start = ComputeActionStart(cue);
            // Looping cues only show one cycle (+ badge padding).
            double end = start + GetBarDisplayDurationSeconds(cue);
            if (IsInfiniteLoopCue(cue))
                end += IsChildDrivenInfinite(cue) ? 5.0 : 2.5;
            maxTime = Math.Max(maxTime, end);
        }
        _contentMaxTime = maxTime;
        float contentWidth = (float)(maxTime * _scale + 100);
        float contentHeight = Math.Max(RowHeight, _timelineArea.CustomMinimumSize.Y - ScrollbarPadBottom);
        ApplyTimelineContentSize(contentWidth, contentHeight);

        foreach (var bg in _rowBackgrounds)
            bg.Size = new Vector2(contentWidth, RowHeight);

        if (_timeGrid != null && IsInstanceValid(_timeGrid))
        {
            _timeGrid.Size = new Vector2(contentWidth, contentHeight);
            _timeGrid.QueueRedraw();
        }

        UpdatePlayheadLineGeometry();
        UpdateDurationSummary();
    }

    /// <summary>
    /// Handles selection of a new cue shell.
    /// </summary>
    /// <param name="cueId">The ID of the selected cue.</param>
    private void ShellSelected(int cueId)
    {
        _focusedCue = CueList.FetchCueFromId(cueId);

        if (_focusedCue == null)
        {
            GD.Print("TimelineInspector:ShellSelected - No cue selected");
            _infoLabel.Visible = true;
            _timeLineContainer.Visible = false;
            if (_sidebarSeparator != null)
                _sidebarSeparator.Visible = false;
            return;
        }

        _infoLabel.Visible = false;
        _timeLineContainer.Visible = true;

        PruneCollapseStateToFocusedTree();
        LoadTimeline();
    }

    /// <summary>
    /// Removes collapse entries for cues not under the current focused root.
    /// </summary>
    private void PruneCollapseStateToFocusedTree()
    {
        if (_focusedCue == null || _collapsedCueIds.Count == 0)
            return;

        var live = new HashSet<int>();
        CollectAllDescendantIds(_focusedCue, live);
        _collapsedCueIds.RemoveWhere(id => !live.Contains(id));
    }

    private static void CollectAllDescendantIds(Cue cue, HashSet<int> ids)
    {
        if (cue == null || !ids.Add(cue.Id)) return;
        if (cue.ChildCues == null) return;
        foreach (var childId in cue.ChildCues)
        {
            var child = CueList.FetchCueFromId(childId);
            if (child != null)
                CollectAllDescendantIds(child, ids);
        }
    }

    /// <summary>
    /// Struct representing a cue and its row in the timeline.
    /// </summary>
    private struct TimelineItem
    {
        public Cue Cue;
        public int Row;
        public int Depth;
        public bool HasChildren;
    }

    /// <summary>
    /// Compact peak waveform drawn inside a timeline cue bar.
    /// Maps the bar width to the play region (start–end of file) tiled by <see cref="PlayCount"/>.
    /// All plays use a consistent colour; vertical dividers mark playcount boundaries.
    /// </summary>
    private partial class CueBarWaveform : Control
    {
        public WaveformPeaks Peaks { get; set; }
        public float StartNorm { get; set; }
        public float EndNorm { get; set; } = 1f;
        public int PlayCount { get; set; } = 1;
        public Color WaveColor { get; set; } = GlobalStyles.LowColor1;
        public Color DividerColor { get; set; } = new Color(1f, 1f, 1f, 0.45f);

        public override void _Draw()
        {
            if (Peaks == null || Peaks.BinCount < 1) return;
            float width = Size.X;
            float height = Size.Y;
            if (width < 2f || height < 4f) return;

            float midY = height * 0.5f;
            float startN = Mathf.Clamp(StartNorm, 0f, 1f);
            float endN = Mathf.Clamp(EndNorm, startN + 1e-5f, 1f);
            int plays = Math.Max(1, PlayCount);

            int binCount = Peaks.BinCount;
            float peakScale = 0.001f;
            int binStart = (int)(startN * binCount);
            int binEnd = (int)Math.Ceiling(endN * binCount);
            binStart = Math.Clamp(binStart, 0, binCount - 1);
            binEnd = Math.Clamp(binEnd, binStart + 1, binCount);
            for (int i = binStart; i < binEnd; i++)
            {
                peakScale = Math.Max(peakScale, Math.Abs(Peaks.GetMin(i)));
                peakScale = Math.Max(peakScale, Math.Abs(Peaks.GetMax(i)));
            }
            peakScale = Math.Max(peakScale, 0.05f);

            float segmentWidth = width / plays;
            var color = WaveColor;

            for (int play = 0; play < plays; play++)
            {
                float playX0 = play * segmentWidth;
                float playW = segmentWidth;

                int playCols = Math.Max(1, (int)Math.Ceiling(playW));
                for (int c = 0; c < playCols; c++)
                {
                    float t = (c + 0.5f) / playCols;
                    float fileNorm = startN + t * (endN - startN);
                    int bin = (int)(fileNorm * binCount);
                    bin = Math.Clamp(bin, 0, binCount - 1);

                    float minVal = Mathf.Clamp(Peaks.GetMin(bin) / peakScale, -1f, 1f);
                    float maxVal = Mathf.Clamp(Peaks.GetMax(bin) / peakScale, -1f, 1f);

                    float yMax = midY - maxVal * (height * 0.45f);
                    float yMin = midY - minVal * (height * 0.45f);
                    if (yMin < yMax)
                        (yMin, yMax) = (yMax, yMin);
                    if (yMin - yMax < 1f)
                    {
                        yMax = midY - 0.5f;
                        yMin = midY + 0.5f;
                    }

                    float x = playX0 + (c + 0.5f) / playCols * playW;
                    if (x < -1 || x > width + 1) continue;
                    DrawLine(new Vector2(x, yMax), new Vector2(x, yMin), color, 1.2f);
                }

                // Divider at the start of each subsequent play
                if (play > 0)
                {
                    DrawLine(new Vector2(playX0, 1f), new Vector2(playX0, height - 1f), DividerColor, 1.5f);
                }
            }

            DrawLine(new Vector2(0, midY), new Vector2(width, midY), new Color(1, 1, 1, 0.1f), 1f);
        }
    }

    /// <summary>
    /// Background grid for the timeline content area (major/minor vertical lines).
    /// </summary>
    private partial class TimeGrid : Control
    {
        public float ZoomScale { get; set; } = 10f;
        public float ContentHeight { get; set; }

        public override void _Draw()
        {
            if (ZoomScale <= 0.001f) return;

            float h = Math.Max(Size.Y, ContentHeight);
            float w = Size.X;
            if (w < 2f || h < 2f) return;

            float targetPixelSpacing = 100.0f;
            float interval = (float)Mathf.Pow(10, Mathf.Round(Math.Log10(targetPixelSpacing / ZoomScale)));
            if (interval * ZoomScale < 50) interval *= 2;
            else if (interval * ZoomScale > 200) interval /= 2;

            float minor = interval / 4f;
            if (minor * ZoomScale < 8f)
                minor = interval / 2f;

            var majorColor = new Color(1f, 1f, 1f, 0.07f);
            var minorColor = new Color(1f, 1f, 1f, 0.03f);

            float tEnd = w / ZoomScale + interval;
            for (float t = 0; t <= tEnd; t += minor)
            {
                float x = t * ZoomScale;
                if (x < -1 || x > w + 1) continue;
                bool isMajor = Math.Abs(t / interval - Math.Round(t / interval)) < 1e-4;
                DrawLine(new Vector2(x, 0), new Vector2(x, h), isMajor ? majorColor : minorColor, 1f);
            }
        }
    }

    /// <summary>
    /// Custom control for rendering the timeline ruler with major/minor ticks, time labels, and playhead triangle.
    /// </summary>
    private partial class Ruler : Control
    {
        public float ZoomScale { get; set; }
        public float Offset { get; set; }
        /// <summary>Pixel offset of content origin (0 when sidebar is separate).</summary>
        public float ContentOriginX { get; set; }
        /// <summary>Playhead time in seconds (display timeline).</summary>
        public double PlayheadSeconds { get; set; }

        public override void _Draw()
        {
            float h = Size.Y;
            float w = Size.X;

            // Taller professional background
            DrawRect(new Rect2(0, 0, w, h), new Color(0.07f, 0.08f, 0.09f, 0.98f), true);
            DrawLine(new Vector2(0, h - 1), new Vector2(w, h - 1), new Color(0.3f, 0.32f, 0.34f, 0.8f), 1f);

            if (ZoomScale <= 0.001f) return;

            float targetPixelSpacing = 90.0f;
            float interval = (float)Mathf.Pow(10, Mathf.Round(Math.Log10(targetPixelSpacing / ZoomScale)));
            if (interval * ZoomScale < 45) interval *= 2;
            else if (interval * ZoomScale > 180) interval /= 2;

            float minor = interval / 4f;
            if (minor * ZoomScale < 6f)
                minor = interval / 2f;

            float tStart = (Offset - ContentOriginX) / ZoomScale;
            float tEnd = (Offset + w - ContentOriginX) / ZoomScale;
            if (tStart < 0) tStart = 0;

            float firstMinor = Mathf.Floor(tStart / minor) * minor;
            var font = ThemeDB.FallbackFont;

            for (float t = firstMinor; t <= tEnd + minor * 0.01f; t += minor)
            {
                if (t < -1e-4f) continue;
                float x = ContentOriginX + t * ZoomScale - Offset;
                if (x < -20 || x > w + 20) continue;

                bool isMajor = Math.Abs(t / interval - Math.Round(t / interval)) < 1e-3;
                float tickTop = isMajor ? h * 0.28f : h * 0.55f;
                var tickColor = isMajor
                    ? new Color(0.88f, 0.9f, 0.92f, 0.95f)
                    : new Color(0.55f, 0.58f, 0.6f, 0.7f);
                DrawLine(new Vector2(x, tickTop), new Vector2(x, h - 1), tickColor, isMajor ? 1.2f : 1f);

                if (isMajor)
                {
                    string labelText = FormatRulerTime(t);
                    DrawString(font, new Vector2(x + 3, h * 0.42f), labelText, HorizontalAlignment.Left, -1, 10,
                        new Color(0.82f, 0.85f, 0.88f, 0.95f));
                }
            }

            // Playhead triangle + line
            float px = ContentOriginX + (float)(PlayheadSeconds * ZoomScale) - Offset;
            if (px >= -6 && px <= w + 6)
            {
                var phColor = new Color(0.95f, 0.35f, 0.15f, 1f);
                DrawLine(new Vector2(px, 0), new Vector2(px, h), phColor, 2f);
                DrawColoredPolygon(new[]
                {
                    new Vector2(px - 6, 0),
                    new Vector2(px + 6, 0),
                    new Vector2(px, 8)
                }, phColor);
            }
        }

        private static string FormatRulerTime(float seconds)
        {
            if (seconds < 0) seconds = 0;
            int total = (int)Math.Floor(seconds);
            int min = total / 60;
            int sec = total % 60;
            float frac = seconds - total;
            if (min > 0)
            {
                if (frac > 0.05f)
                    return $"{min}:{sec:D2}.{ (int)(frac * 10) }";
                return $"{min}:{sec:D2}";
            }
            if (seconds < 10 && frac > 0.01f)
                return $"{seconds:0.#}s";
            return $"{sec}s";
        }
    }
}
