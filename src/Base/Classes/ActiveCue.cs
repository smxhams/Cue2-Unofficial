using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Cue2.UI.Utilities;

namespace Cue2.Base.Classes;


/// <summary>
/// Manages the active state of a playing cue, including timers for fades/delays, UI updates, and interaction with playback engines.
/// Encapsulates playback logic to keep CueCommandExecutor clean and allow easy pause/stop/fade control.
/// Supports minimal latency by preloading and triggering on demand.
/// </summary>
public partial class ActiveCue : GodotObject
{
    private readonly Cue _cue;
    private readonly VBoxContainer _activeCueList;
    private readonly GlobalSignals _globalSignals;
    private readonly MediaEngine _mediaEngine;
    private readonly AudioDevices _audioDevices;
    private readonly Settings _settings;

    /// <summary>
    /// Chain membership from GO (null for nested child activations under a parent group).
    /// </summary>
    private readonly CueChainMember _chainMember;

    private PanelContainer _activeCueBar;
    private Timer _fadeTimer; // For fade-in/out
    private Timer _updateTimer;
    private bool _isPlaying;
    private bool _inPreWait = false;
    
    private readonly object _lock = new object(); // For thread safety

    private Timer _preWaitTimer;
    private Timer _incomingWaitTimer;
    private bool _preWaitUpdateHooked;
    private bool _incomingWaitUpdateHooked;
    private bool _incomingWaitTimeoutHooked;

    /// <summary>True once content setup/trigger has been kicked off.</summary>
    private bool _contentStarted;

    /// <summary>True after UI has been built and added to the active list (may precede StartAsync).</summary>
    private bool _uiPrepared;

    /// <summary>True after chain member entry path has run (pending or head start).</summary>
    private bool _chainRunStarted;

    /// <summary>True after <see cref="ArmIncoming"/> has been called (or head started).</summary>
    private bool _incomingArmed;

    /// <summary>True while counting the post-wait lead-in after arm.</summary>
    private bool _inIncomingWait;

    /// <summary>Post-wait duration for the active incoming lead-in (from previous cue).</summary>
    private double _incomingWaitDuration;

    /// <summary>Mode of the active/pending incoming link.</summary>
    private FollowType _incomingMode;

    /// <summary>User skipped this cue's pre-wait (now or when it becomes active).</summary>
    private bool _skipPreWait;

    /// <summary>True while PreWaitComplete is subscribed to the pre-wait timer.</summary>
    private bool _preWaitTimeoutHooked;

    /// <summary>Guards against double entry into content after pre-wait.</summary>
    private bool _preWaitFinished;
    
    private readonly Dictionary<PanelContainer, ActiveAudioPlayback> _activeAudioComponents = new();
    private readonly Dictionary<PanelContainer, AudioComponent> _componentToAudio = new();
    private readonly Dictionary<PanelContainer, ActiveVideoPlayback> _activeVideoComponents = new();
    private readonly Dictionary<PanelContainer, VideoComponent> _componentToVideo = new();
    private readonly Dictionary<PanelContainer, ActiveTextPlayback> _activeTextComponents = new();
    private readonly Dictionary<PanelContainer, TextComponent> _componentToText = new();
    private readonly Dictionary<PanelContainer, CueLightComponent> _activeCueLightComponents = new();
    private readonly Dictionary<PanelContainer, OscComponent> _activeOscComponents = new();
    private readonly Dictionary<PanelContainer, MidiOutputComponent> _activeMidiOutputComponents = new();
    private readonly Dictionary<PanelContainer, ControlComponent> _activeControlComponents = new();

    /// <summary>Keeps handler refs so we can disconnect before freeing UI (avoids disposed-panel callbacks).</summary>
    private readonly List<(ActiveAudioPlayback Playback, ActiveAudioPlayback.CompletedEventHandler Handler)> _audioCompleteHandlers = new();
    private readonly List<(ActiveVideoPlayback Playback, ActiveVideoPlayback.CompletedEventHandler Handler)> _videoCompleteHandlers = new();
    private readonly List<(ActiveTextPlayback Playback, ActiveTextPlayback.CompletedEventHandler Handler)> _textCompleteHandlers = new();
    
    private int _activeComponentCount = 0;
    
    /// <summary>
    /// The cue this active instance is playing.
    /// </summary>
    public Cue Cue => _cue;

    /// <summary>
    /// Yields this active cue and all nested child active cues.
    /// </summary>
    /// <returns>Self and descendants currently tracked.</returns>
    public IEnumerable<ActiveCue> EnumerateSelfAndDescendants()
    {
        yield return this;
        foreach (var child in _childActiveCues.ToList())
        {
            if (child == null || !IsInstanceValid(child)) continue;
            foreach (var nested in child.EnumerateSelfAndDescendants())
                yield return nested;
        }
    }

    /// <summary>
    /// Optional fade-in override from a control GO (seconds). When set, media starts with this fade
    /// instead of (or as) the component's own fade-in for this activation.
    /// </summary>
    private double? _controlFadeInDuration;

    /// <summary>
    /// Sets a control-GO fade-in duration for this active instance (head of a control GO).
    /// </summary>
    /// <param name="seconds">Fade-in length; values ≤ 0 clear the override.</param>
    public void SetControlFadeInDuration(double seconds)
    {
        if (seconds <= 1e-9)
        {
            _controlFadeInDuration = null;
            return;
        }

        _controlFadeInDuration = seconds;
    }

    /// <summary>
    /// Seeks this cue on its body timeline and propagates to components and nested children.
    /// </summary>
    /// <param name="timeSeconds">
    /// Absolute time on this cue's playable timeline (pre-wait + content), or a relative offset
    /// when <paramref name="relative"/> is true.
    /// </param>
    /// <param name="relative">When true, offset from the current playhead.</param>
    /// <remarks>
    /// Timeline layout: <c>[0 .. PreWait)</c> pre-wait, then <c>[PreWait .. PreWait+Duration)</c> content.
    /// Nested children share the parent's content origin (child t=0 == parent content t=0).
    /// Seeking past a child's occupancy finishes that child.
    /// </remarks>
    public void RequestSeek(double timeSeconds, bool relative)
    {
        if (_isCleaned) return;

        double current = GetCueTimelineSeconds();
        double target = relative ? current + timeSeconds : timeSeconds;
        if (target < 0) target = 0;

        ApplyCueTimelineSeek(target);
    }

    /// <summary>
    /// Seeks this cue to an absolute body-timeline position (pre-wait + content).
    /// Used for head-bar scrub and parent→child propagation.
    /// </summary>
    /// <param name="timelineSeconds">Seconds from this cue's body start (start of pre-wait).</param>
    public void ApplyCueTimelineSeek(double timelineSeconds)
    {
        if (_isCleaned || _suppressContentCompleted) return;
        if (timelineSeconds < 0) timelineSeconds = 0;

        double pre = Math.Max(0.0, _cue?.PreWait ?? 0.0);
        double playable = GetPlayableTimelineDuration();

        // Past end of playable span → finish this cue (and nested children).
        if (playable >= 0 && timelineSeconds >= playable - 1e-4)
        {
            GD.Print(
                $"ActiveCue:ApplyCueTimelineSeek - {_cue?.Name}: {timelineSeconds:F3}s past end {playable:F3}s — finishing");
            SetTimelineSeconds(playable);
            if (_inPreWait || _contentPlaybackActive || _childActiveCues.Count > 0 || HasOwnMediaPlayback())
                StopAll(propagateToChildren: true, fadeDurationOverride: 0);
            else
                UpdateHeadProgressUi();
            return;
        }

        // Not yet in body (still on continue/follow, or setup): queue for when body runs.
        if (!_contentStarted && !_timelineStarted)
        {
            _pendingTimelineSeekSeconds = timelineSeconds;
            return;
        }

        if (timelineSeconds < pre - 1e-9)
        {
            SeekIntoPreWaitRegion(timelineSeconds);
            return;
        }

        // Content region on the proportional bar.
        SeekIntoContentRegion(timelineSeconds - pre, timelineSeconds);
    }

    /// <summary>
    /// Pauses transport for this active cue (media + children). Used by control components.
    /// </summary>
    public void RequestPause()
    {
        if (_isCleaned) return;

        if (_inIncomingWait && _incomingWaitTimer != null && IsInstanceValid(_incomingWaitTimer))
        {
            _incomingWaitTimer.SetPaused(true);
            if (_sequencePause != null && _activeCueBar != null)
                _sequencePause.Icon = _activeCueBar.GetThemeIcon("Play", "AtlasIcons");
        }

        if (_inPreWait)
            PreWaitPause();

        PauseAll(propagateToChildren: true);
    }

    /// <summary>
    /// Resumes transport for this active cue (media + children). Used by control components.
    /// </summary>
    public void RequestResume()
    {
        if (_isCleaned) return;

        if (_inIncomingWait && _incomingWaitTimer != null && IsInstanceValid(_incomingWaitTimer))
        {
            _incomingWaitTimer.SetPaused(false);
            if (_sequencePause != null && _activeCueBar != null)
                _sequencePause.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
        }

        if (_inPreWait)
            PreWaitResume();

        ResumeAll(propagateToChildren: true);
    }

    /// <summary>
    /// Starts this cue's content immediately, bypassing continue/follow lead-in and pre-wait.
    /// </summary>
    /// <remarks>
    /// Used by the <see cref="ControlAction.StartNow"/> control component.
    /// No-op if content is already running (past pre-wait). Does not start a cue that
    /// is not already present in the active list (use GO for that).
    /// <para>
    /// Safe to call while a prior control GO is still in setup: sets <c>_skipPreWait</c> so
    /// the in-flight <see cref="StartPlaybackCoreAsync"/> skips pre-wait when it reaches that step.
    /// </para>
    /// </remarks>
    public void RequestStartNow()
    {
        if (_isCleaned || _suppressContentCompleted) return;

        GD.Print($"ActiveCue:RequestStartNow - {_cue?.Name}: skipping waits, starting content");

        // Cancel any continue/follow countdown or pending lead-in UI.
        if (_inIncomingWait)
        {
            HookIncomingWaitUpdate(false);
            if (_incomingWaitTimer != null && IsInstanceValid(_incomingWaitTimer))
                _incomingWaitTimer.Stop();
            FreeIncomingWaitTimer();
            _inIncomingWait = false;
        }

        // Ignore later ArmIncoming from a predecessor; we are starting independently.
        _incomingArmed = true;
        _skipPreWait = true;
        HideSequencePanel();

        // Active pre-wait → jump straight into content.
        if (_inPreWait)
        {
            _ = FinishPreWaitAndStartContent();
            return;
        }

        // Not started yet: start core without pre-wait (or race-safe with an incoming StartAsync).
        if (!_contentStarted)
        {
            if (_chainMember != null && !_chainRunStarted)
                _chainRunStarted = true;
            _ = StartPlaybackCoreAsync(includePreWait: false);
            return;
        }

        // Setup already running (e.g. loading media before PreWait): _skipPreWait is set so
        // StartPlaybackCoreAsync will skip pre-wait when it reaches that decision.
        // Already playing content → nothing to skip.
        if (_isPlaying || _preWaitFinished)
        {
            GD.Print($"ActiveCue:RequestStartNow - {_cue?.Name}: already in content, ignoring");
        }
        else
        {
            GD.Print($"ActiveCue:RequestStartNow - {_cue?.Name}: setup in progress, pre-wait will be skipped");
        }
    }

    /// <summary>
    /// Re-applies expand/stretch/opacity on live video TextureRects for a video component.
    /// </summary>
    public void RefreshVideoVisuals(VideoComponent component)
    {
        if (component == null || _isCleaned)
            return;

        foreach (var playback in _activeVideoComponents.Values)
        {
            if (playback != null && playback.UsesVideoComponent(component))
                playback.RefreshVisualProperties();
        }
    }

    /// <summary>
    /// Re-applies text content and style on live RichTextLabels for a text component.
    /// </summary>
    /// <param name="component">Text component whose active playback should refresh.</param>
    public void RefreshTextVisuals(TextComponent component)
    {
        if (component == null || _isCleaned)
            return;

        foreach (var playback in _activeTextComponents.Values)
        {
            if (playback != null && playback.UsesTextComponent(component))
                playback.RefreshVisualProperties();
        }
    }

    /// <summary>
    /// Event raised when the cue playback is completed (cleanup finished).
    /// </summary>
    [Signal]
    public delegate void CompletedEventHandler();

    /// <summary>
    /// Raised once when cue content finishes naturally (components + child cues done).
    /// Not raised on stop/panic. Used for auto-follow arming (real completion, seek-aware).
    /// </summary>
    public event Action ContentCompleted;

    /// <summary>
    /// Raised when the content phase begins (after this cue's pre-wait / lead-in).
    /// Used for auto-continue arming.
    /// </summary>
    public event Action ContentPhaseStarted;

    /// <summary>
    /// Next cue in a pre-spawned continue/follow chain (cancelled if this cue is stopped early).
    /// </summary>
    public ActiveCue NextInChain { get; set; }

    /// <summary>
    /// True after natural content completion has been reported (or suppressed by stop).
    /// </summary>
    private bool _contentCompletedRaised;

    /// <summary>
    /// True after content-phase-started has been reported.
    /// </summary>
    private bool _contentPhaseStartedRaised;

    /// <summary>
    /// When true, stop paths must not report natural content completion or arm the chain.
    /// </summary>
    private bool _suppressContentCompleted;
    
    
    // UI
    private ProgressBar _headProgressBar;
    private Label _headLabelName;
    private Label _headLabelTimeLeft;
    private Label _headLabelTimeRight;

    private Button _headPause;
    private Button _headStop;
    
    private VBoxContainer _componentContainer;
        
    private Label _preWaitTimerLabel;
    private ProgressBar _preWaitProgress;
    private PanelContainer _preWaitPanel;
    private Button _preWaitPause;
    private Button _preWaitSkip;

    private PanelContainer _sequencePanel;
    private ProgressBar _sequenceProgress;
    private Label _sequenceLabel;
    private Label _sequenceNameLabel;
    private Label _sequenceTimerLabel;
    private Button _sequencePause;
    private Button _sequenceSkip;

    private PanelContainer _postWaitPanel;
    private ProgressBar _postWaitProgress;
    private Label _postWaitLabel;
    private Label _postWaitNameLabel;
    private Label _postWaitTimerLabel;
    private Button _postWaitPause;
    private Button _postWaitSkip;
    
    // Main cue progress scene
    private PackedScene _activeCueBarScene = SceneLoader.LoadPackedScene("uid://dt7rlfag7yr2c", out string error); 
    // Component progress scene
    private PackedScene _componentProgressBarScene = SceneLoader.LoadPackedScene("uid://cb7g4xgryo2dg", out string error);
    
    private bool _isPaused = false;
    private bool _isCleaned = false;
    private bool _isFinished = false;
    /// <summary>True after the first Stop while a stop-fade is in progress (second Stop = hard stop).</summary>
    private bool _isStopFading = false;

    private readonly List<ActiveCue> _childActiveCues = new List<ActiveCue>();

    // --- Body timeline (head progress / parent↔child seek) ---
    // Layout: [0, PreWait) pre-wait, [PreWait, PreWait+Duration) content (Duration includes nested children).

    /// <summary>Frozen body-timeline position (seconds) when the clock is not running.</summary>
    private double _timelineBase;

    /// <summary>Engine msec when the running body clock was started/resumed.</summary>
    private ulong _timelineClockStartMsec;

    /// <summary>True while the body wall clock advances.</summary>
    private bool _timelineClockRunning;

    /// <summary>True after pre-wait or content has started (body clock origin established).</summary>
    private bool _timelineStarted;

    /// <summary>How much pre-wait was honored when content began (0 if skipped).</summary>
    private double _preWaitSecondsHonored;

    /// <summary>True while content playback is active (media and/or children running).</summary>
    private bool _contentPlaybackActive;

    /// <summary>True after <see cref="TriggerComponents"/> has been called at least once.</summary>
    private bool _componentsTriggered;

    /// <summary>True while the user is scrubbing the head progress bar.</summary>
    private bool _headIsSeeking;

    /// <summary>Preview body-timeline time while scrubbing the head bar.</summary>
    private double _pendingHeadSeekSeconds;

    /// <summary>
    /// Absolute body-timeline seek to apply when body/content becomes ready.
    /// </summary>
    private double? _pendingTimelineSeekSeconds;

    /// <summary>
    /// True while rewinding content for a scrub into pre-wait (ignore child Completed → parent finish).
    /// </summary>
    private bool _isRewindingContent;

    /// <summary>
    /// Child cue ids that have already finished (or been finished by seek). Never resurrected on scrub-back.
    /// </summary>
    private readonly HashSet<int> _finishedChildCueIds = new();

    /// <summary>
    /// Initializes a new instance of the ActiveCue class for Godot serialization.
    /// </summary>
    public ActiveCue()
    {
        // Blank constructor for Godot
    }
    
    /// <summary>
    /// Initializes a new instance of the ActiveCue class with the specified cue and dependencies.
    /// </summary>
    /// <param name="cue">The cue to activate.</param>
    /// <param name="activeCueList">The UI container for active cues.</param>
    /// <param name="mediaEngine">The media engine for audio processing.</param>
    /// <param name="audioDevices">The audio devices manager.</param>
    /// <param name="globalSignals">The global signals for event communication.</param>
    /// <param name="chainMember">
    /// Optional chain membership from GO. Null for nested child cues under a parent group.
    /// </param>
    public ActiveCue(
        Cue cue,
        VBoxContainer activeCueList,
        MediaEngine mediaEngine,
        AudioDevices audioDevices,
        GlobalSignals globalSignals,
        CueChainMember chainMember = null)
    {
        _cue = cue ?? throw new ArgumentNullException(nameof(cue));
        _activeCueList = activeCueList;
        _mediaEngine = mediaEngine ?? throw new ArgumentNullException(nameof(mediaEngine));
        _audioDevices = audioDevices ?? throw new ArgumentNullException(nameof(audioDevices));
        _globalSignals = globalSignals ?? throw new ArgumentNullException(nameof(globalSignals));
        _settings = _activeCueList.GetNode<GlobalData>("/root/GlobalData").Settings;
        _chainMember = chainMember;
        _incomingMode = chainMember?.IncomingMode ?? FollowType.None;
        _incomingWaitDuration = chainMember?.IncomingPostWait ?? 0.0;
    }

    /// <summary>
    /// Builds the active-cue row and inserts it into the list immediately (synchronous).
    /// Call in chain order so the active list matches sequence occurrence order.
    /// </summary>
    public void PrepareUiInOrder()
    {
        if (_uiPrepared || _isCleaned) return;
        SetupUi();
        SetupSignals();
        SetupTimers();
        _uiPrepared = true;
    }

    private void SetupUi()
    {
        // Parent cue UI 
        _activeCueBar = _activeCueBarScene.Instantiate<PanelContainer>();
        _activeCueList.AddChild(_activeCueBar);
        
        _componentContainer = _activeCueBar.GetNode<VBoxContainer>("%ComponentList");
        _headProgressBar = _activeCueBar.GetNode<ProgressBar>("%ProgressBar"); 
        _headLabelName = _activeCueBar.GetNode<Label>("%LabelName"); 
        _headLabelTimeLeft = _activeCueBar.GetNode<Label>("%LabelTimeLeft"); 
        _headLabelTimeRight = _activeCueBar.GetNode<Label>("%LabelTimeRight");
        _headPause = _activeCueBar.GetNode<Button>("%HeadPause");
        _headStop = _activeCueBar.GetNode<Button>("%HeadStop");
        
        _preWaitPanel = _activeCueBar.GetNode<PanelContainer>("%PreWaitBar");
        _preWaitTimerLabel = _preWaitPanel.GetNode<Label>("%PreWaitTimer");
        _preWaitProgress = _preWaitPanel.GetNode<ProgressBar>("%PreWaitProgress");
        _preWaitPause = _preWaitPanel.GetNode<Button>("%PreWaitPause");
        _preWaitSkip = _preWaitPanel.GetNode<Button>("%PreWaitSkip");

        _sequencePanel = _activeCueBar.GetNodeOrNull<PanelContainer>("%SequenceBar");
        if (_sequencePanel != null)
        {
            _sequenceProgress = _sequencePanel.GetNodeOrNull<ProgressBar>("%SequenceProgress");
            _sequenceLabel = _sequencePanel.GetNodeOrNull<Label>("%SequenceLabel");
            _sequenceNameLabel = _sequencePanel.GetNodeOrNull<Label>("%SequenceNameLabel");
            _sequenceTimerLabel = _sequencePanel.GetNodeOrNull<Label>("%SequenceTimer");
            _sequencePause = _sequencePanel.GetNodeOrNull<Button>("%SequencePause");
            _sequenceSkip = _sequencePanel.GetNodeOrNull<Button>("%SequenceSkip");
        }

        _postWaitPanel = _activeCueBar.GetNodeOrNull<PanelContainer>("%PostWaitBar");
        if (_postWaitPanel != null)
        {
            _postWaitProgress = _postWaitPanel.GetNodeOrNull<ProgressBar>("%PostWaitProgress");
            _postWaitLabel = _postWaitPanel.GetNodeOrNull<Label>("%PostWaitLabel");
            _postWaitNameLabel = _postWaitPanel.GetNodeOrNull<Label>("%PostWaitNameLabel");
            _postWaitTimerLabel = _postWaitPanel.GetNodeOrNull<Label>("%PostWaitTimer");
            _postWaitPause = _postWaitPanel.GetNodeOrNull<Button>("%PostWaitPause");
            _postWaitSkip = _postWaitPanel.GetNodeOrNull<Button>("%PostWaitSkip");
        }

        // Continue/follow completes before pre-wait — keep that bar above pre-wait.
        EnsureSequenceBarAbovePreWait();

        //GD.Print($"ActiveCue:SetupUi - Setting everything the same colour why? {_cue.Name}");
        var cueColor = _cue.Color;
        var colorBar = _activeCueBar.GetNode<Panel>("%ColorBar");
        var style = colorBar.GetThemeStylebox("panel").Duplicate() as StyleBoxFlat;
        style.BgColor = cueColor;
        colorBar.AddThemeStyleboxOverride("panel", style);
        
        var borderStyle = _activeCueBar.GetThemeStylebox("panel").Duplicate() as StyleBoxFlat; //!!!
        borderStyle.BorderColor = cueColor;
        _activeCueBar.AddThemeStyleboxOverride("panel", borderStyle);
        
        _headPause.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
        _headStop.Icon = _activeCueBar.GetThemeIcon("Stop", "AtlasIcons");
        
        _headProgressBar.Value = 0;
        _headLabelName.Text = _cue.Name;

        // Ensure duration includes nested children before first paint.
        try { _cue.CalculateTotalDuration(); } catch { /* best-effort */ }

        double playable = GetPlayableTimelineDuration();
        _headLabelTimeLeft.Text = UiUtilities.FormatTime(0);
        _headLabelTimeRight.Text = playable < 0
            ? "∞"
            : $"-({UiUtilities.FormatTime(playable)})";

        // Head bar scrub seeks full body timeline (pre-wait + content + nested children).
        WireHeadProgressSeek();
    }

    /// <summary>
    /// Wires click/drag scrubbing on the cue head progress bar to the body timeline.
    /// </summary>
    private void WireHeadProgressSeek()
    {
        if (_headProgressBar == null) return;

        _headProgressBar.GuiInput += OnHeadProgressGuiInput;
    }

    private void OnHeadProgressGuiInput(InputEvent @event)
    {
        if (_isCleaned || _headProgressBar == null || !IsInstanceValid(_headProgressBar))
            return;

        double playable = GetPlayableTimelineDuration();
        // Infinite / unknown / empty: no meaningful scrub target.
        if (playable < 0 || playable <= 1e-9)
            return;

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                _headIsSeeking = true;
                double percent = Math.Clamp(mb.Position.X / Math.Max(1.0, _headProgressBar.Size.X), 0.0, 1.0);
                _pendingHeadSeekSeconds = percent * playable;
                _headProgressBar.Value = percent * 100.0;
                UpdateHeadTimeLabels(_pendingHeadSeekSeconds, playable);
            }
            else if (_headIsSeeking)
            {
                _headIsSeeking = false;
                ApplyCueTimelineSeek(_pendingHeadSeekSeconds);
                GD.Print($"ActiveCue:OnHeadProgressGuiInput - {_cue?.Name}: seek to {_pendingHeadSeekSeconds:F3}s / {playable:F3}s");
            }
        }
        else if (@event is InputEventMouseMotion mm && _headIsSeeking)
        {
            double percent = Math.Clamp(mm.Position.X / Math.Max(1.0, _headProgressBar.Size.X), 0.0, 1.0);
            _pendingHeadSeekSeconds = percent * playable;
            _headProgressBar.Value = percent * 100.0;
            UpdateHeadTimeLabels(_pendingHeadSeekSeconds, playable);
        }
    }

    /// <summary>
    /// Moves the continue/follow strip above pre-wait (sequence lead-in runs first).
    /// </summary>
    private void EnsureSequenceBarAbovePreWait()
    {
        if (_sequencePanel == null || _preWaitPanel == null) return;
        if (!IsInstanceValid(_sequencePanel) || !IsInstanceValid(_preWaitPanel)) return;

        var parent = _sequencePanel.GetParent();
        if (parent == null || _preWaitPanel.GetParent() != parent) return;

        int preIdx = _preWaitPanel.GetIndex();
        int seqIdx = _sequencePanel.GetIndex();
        if (seqIdx > preIdx)
            parent.MoveChild(_sequencePanel, preIdx);
    }

    private void SetupSignals()
    {
        _globalSignals.StopAll += GlobalStopAll;
        // Use named handlers so Cleanup can disconnect them (lambdas cannot be unsubscribed).
        _globalSignals.PauseAll += GlobalPauseAll;
        _globalSignals.ResumeAll += GlobalResumeAll;

        _headPause.Pressed += TogglePauseAll;
        _headStop.Pressed += OnHeadStopPressed;

        _preWaitPause.Pressed += TogglePreWaitPause;
        _preWaitSkip.Pressed += OnPreWaitSkipPressed;

        if (_sequencePause != null)
            _sequencePause.Pressed += ToggleSchedulePause;
        if (_sequenceSkip != null)
            _sequenceSkip.Pressed += OnSequenceSkipPressed;
        if (_postWaitPause != null)
            _postWaitPause.Pressed += ToggleSchedulePause;
        if (_postWaitSkip != null)
            _postWaitSkip.Pressed += OnPostWaitSkipPressed;
    }

    private void OnHeadStopPressed()
    {
        StopAll();
    }

    private void SetupTimers()
    {
        _updateTimer = new Timer { WaitTime = 0.1, OneShot = false }; // 10Hz UI update
        _activeCueBar.AddChild(_updateTimer);
        _updateTimer.Timeout += UpdateUi;
        _updateTimer.Start();
        
        _fadeTimer = new Timer { OneShot = true }; // For fades, set dynamically
        _activeCueBar.AddChild(_fadeTimer);
        
        if (_cue.PreWait > 0)
        {
            _preWaitTimer = new Timer { WaitTime = _cue.PreWait, OneShot = true, IgnoreTimeScale = true};
            _activeCueBar.AddChild(_preWaitTimer);
        }
    }
    
    /// <summary>
    /// Starts the cue playback asynchronously, setting up UI and triggering components.
    /// </summary>
    /// <returns>
    /// A task that completes when this cue has entered pre-wait or started content
    /// (or shown as pending for non-head chain members). Awaited by control GO so
    /// subsequent control actions (e.g. Start Now) see a live active instance.
    /// </returns>
    public async Task StartAsync()
    {
        if (_isPlaying || _contentStarted) return;
        GD.Print($"ActiveCue:StartAsync - Starting: {_cue.Name} (chain={_chainMember != null}, incoming={_incomingMode})");
        
        // UI may already be prepared (chain order); otherwise build now.
        if (!_uiPrepared)
            PrepareUiInOrder();

        // GO chain member: show pending continue/follow + inactive pre-wait; head starts now.
        if (_chainMember != null)
        {
            if (_chainRunStarted) return;
            await BeginChainMemberRunAsync();
            return;
        }

        // Nested child under a parent: classic path.
        await StartPlaybackCoreAsync(includePreWait: true);
    }

    /// <summary>
    /// Arms this cue from the previous chain member (continue at content-phase start, follow at complete).
    /// Starts post-wait lead-in, then this cue's pre-wait and content.
    /// </summary>
    /// <param name="mode">Continue or Follow (for UI).</param>
    /// <param name="postWait">Previous cue's post-wait duration.</param>
    public void ArmIncoming(FollowType mode, double postWait)
    {
        if (_isCleaned || _suppressContentCompleted || _incomingArmed || _contentStarted)
            return;

        _incomingMode = mode;
        _incomingWaitDuration = Math.Max(0.0, postWait);
        _incomingArmed = true;

        GD.Print(
            $"ActiveCue:ArmIncoming - {_cue.Name} mode={mode} postWait={_incomingWaitDuration:F3} skipPre={_skipPreWait}");

        // Zero post-wait: go straight into this cue's pre-wait / content.
        if (_incomingWaitDuration <= 1e-9)
        {
            HideSequencePanel();
            _ = StartPlaybackCoreAsync(includePreWait: true);
            return;
        }

        BeginIncomingPostWait();
    }

    /// <summary>
    /// Cancels this cue if it has not started content yet, and propagates to the rest of the chain.
    /// </summary>
    public void CancelPendingFromPredecessor()
    {
        if (_isCleaned) return;
        if (_contentStarted) return;

        _suppressContentCompleted = true;
        NextInChain?.CancelPendingFromPredecessor();
        Cleanup();
    }

    /// <summary>
    /// Optional pre-wait, then content phase (children + component trigger).
    /// Children start only when the content phase begins so parent pre-wait delays the group.
    /// </summary>
    private async Task StartPlaybackCoreAsync(bool includePreWait)
    {
        if (_isCleaned || _suppressContentCompleted) return;
        // Prevent double-entry (e.g. control Start Now racing StartAsync / GO).
        if (_contentStarted) return;
        _contentStarted = true;
        _incomingArmed = true;
        HideSequencePanel();

        // Disarmed: no content, children, or components. Still raise phase events so
        // continue/follow chains arm the next member without playing this cue.
        if (_cue != null && !_cue.Armed)
        {
            GD.Print($"ActiveCue:StartPlaybackCoreAsync - {_cue.Name}: disarmed, skipping playback");
            _isFinished = true;
            RaiseContentPhaseStarted();
            HandleNaturalContentFinished();
            return;
        }

        // Preload own media during pre-wait for lower trigger latency. Children wait for content phase.
        await SetupComponents();

        bool doPreWait = includePreWait && !_skipPreWait && _cue.PreWait > 0;
        if (doPreWait)
        {
            EnsureTimelineStarted();
            PreWait();
            ApplyPendingTimelineSeekIfAny();
            return;
        }

        // Zero or skipped pre-wait: content origin sits at PreWait on the proportional bar
        // (skipped wait still occupies the pre-wait segment so scrub proportions stay stable).
        EnsureTimelineStarted();
        _preWaitSecondsHonored = Math.Max(0.0, _cue.PreWait);
        SetTimelineSeconds(_preWaitSecondsHonored);

        HidePreWaitPanel();
        await BeginContentPhaseAsync();
    }

    /// <summary>
    /// Starts (or restarts) content playback: nested children + component trigger.
    /// Body timeline clock continues from pre-wait (not reset).
    /// </summary>
    private async Task BeginContentPhaseAsync()
    {
        if (_isCleaned || _suppressContentCompleted) return;

        RaiseContentPhaseStarted();
        EnsureTimelineStarted();

        // _preWaitSecondsHonored set by FinishPreWait / skip path before we get here.
        _contentPlaybackActive = true;
        _isPlaying = true;

        // Nested children share this content origin (child t=0 == parent content t=0).
        if (_childActiveCues.Count == 0)
            StartChildCues();

        bool hasChildren = _childActiveCues.Count > 0;
        if (_activeComponentCount == 0)
        {
            _isFinished = true;
            if (!hasChildren)
            {
                HandleNaturalContentFinished();
                return;
            }

            // Group with only children: play until children complete.
            ApplyPendingTimelineSeekIfAny();
            UpdateHeadProgressUi();
            return;
        }

        if (!_componentsTriggered)
        {
            await TriggerComponents();
            _componentsTriggered = true;
        }
        else
        {
            // Re-entered content after rewind into pre-wait: media still loaded — unpause transport.
            if (!_isPaused)
            {
                foreach (var playback in _activeAudioComponents.Values)
                    playback.Resume();
                foreach (var playback in _activeVideoComponents.Values)
                    playback.Resume();
                foreach (var playback in _activeTextComponents.Values)
                    playback.Resume();
            }
        }

        ApplyPendingTimelineSeekIfAny();
        EnsureAliveOrCleanup();
        UpdateHeadProgressUi();
    }

    /// <summary>
    /// Spawns nested active cues under this bar's child list and starts them.
    /// Skips children that have already finished this activation (no resurrect on scrub-back).
    /// </summary>
    private void StartChildCues()
    {
        if (_cue?.ChildCues == null || _cue.ChildCues.Count == 0) return;
        if (_activeCueBar == null || !IsInstanceValid(_activeCueBar)) return;

        var childCueList = _activeCueBar.GetNodeOrNull<VBoxContainer>("%ChildCuelist");
        if (childCueList == null)
        {
            GD.PrintErr($"ActiveCue:StartChildCues - ChildCuelist missing on {_cue.Name}");
            return;
        }

        foreach (var childId in _cue.ChildCues)
        {
            if (_finishedChildCueIds.Contains(childId))
                continue; // Already completed this run — do not resurrect.

            // Already live under this parent.
            if (_childActiveCues.Any(c => c != null && IsInstanceValid(c) && c.Cue?.Id == childId))
                continue;

            var child = CueList.FetchCueFromId(childId);
            if (child == null)
            {
                GD.PrintErr($"ActiveCue:StartChildCues - Child cue {childId} not found");
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                    $"Child cue {childId} not found for parent {_cue.Name}", 2);
                continue;
            }

            var activeCue = new ActiveCue(child, childCueList, _mediaEngine, _audioDevices, _globalSignals);
            _childActiveCues.Add(activeCue);
            activeCue.Completed += () => OnChildCompleted(activeCue);
            _ = activeCue.StartAsync();
        }
    }

    /// <summary>
    /// Applies a body-timeline seek deferred across pre-wait / content-phase start.
    /// </summary>
    private void ApplyPendingTimelineSeekIfAny()
    {
        if (!_pendingTimelineSeekSeconds.HasValue) return;
        double t = _pendingTimelineSeekSeconds.Value;
        _pendingTimelineSeekSeconds = null;
        ApplyCueTimelineSeek(t);
    }

    /// <summary>
    /// After trigger, if nothing is left running and no children remain, finish content (and sequence if needed).
    /// </summary>
    private void EnsureAliveOrCleanup()
    {
        if (_isCleaned) return;

        bool hasActive =
            _activeAudioComponents.Count > 0 ||
            _activeVideoComponents.Count > 0 ||
            _activeTextComponents.Count > 0 ||
            _activeOscComponents.Count > 0 ||
            _activeMidiOutputComponents.Count > 0 ||
            _activeCueLightComponents.Count > 0 ||
            _activeControlComponents.Count > 0;

        if (!hasActive)
        {
            _isFinished = true;
            if (_childActiveCues.Count == 0)
                HandleNaturalContentFinished();
        }
    }

    private async Task TriggerComponents()
    {
        // Media / OSC / cue-light kick off in parallel as we walk the list.
        // Control components await in list order so GO can finish before Start Now, etc.
        var parallel = new List<Task>();

        foreach (var comp in _cue.Components)
        {
            if (comp is AudioComponent audioComp)
            {
                parallel.Add(TriggerAudioComponent(audioComp));
            }
            else if (comp is VideoComponent videoComp)
            {
                parallel.Add(TriggerVideoComponent(videoComp));
            }
            else if (comp is TextComponent textComp)
            {
                parallel.Add(TriggerTextComponent(textComp));
            }
            else if (comp is CueLightComponent cueLightComp)
            {
                parallel.Add(TriggerCueLightComponent(cueLightComp));
            }
            else if (comp is OscComponent oscComp)
            {
                parallel.Add(TriggerOscComponent(oscComp));
            }
            else if (comp is MidiOutputComponent midiOutComp)
            {
                parallel.Add(TriggerMidiOutputComponent(midiOutComp));
            }
            else if (comp is ControlComponent controlComp)
            {
                await TriggerControlComponent(controlComp);
            }
        }

        if (parallel.Count > 0)
            await Task.WhenAll(parallel);
    }

    private async Task TriggerAudioComponent(AudioComponent audioComp)
    {
        PanelContainer panel = null;
        try
        {
            // Find specific playback for this audioComp
            panel = _componentToAudio.FirstOrDefault(kv => kv.Value == audioComp).Key;
            if (panel == null)
            {
                GD.PrintErr($"ActiveCue:TriggerAudioComponent - No playback found for {audioComp.AudioFile}");
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                    $"No playback for audio component in cue {_cue.Name}", 2);
                return;
            }
            var playback = _activeAudioComponents[panel];

            bool started = await _audioDevices.StartAudioPlayback(playback);
            if (!started || playback.DeviceStreams == null || playback.DeviceStreams.Count == 0)
            {
                GD.PrintErr($"ActiveCue:TriggerAudioComponent - Failed to start audio for {audioComp.AudioFile}");
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                    $"Failed to start audio in {_cue.Name}: no output streams (check patch/device assignment).", 2);
                // Clean() emits Completed → CompleteAudioByPanelId removes UI
                playback.Clean();
                return;
            }

            // Control GO fade-in override wins when set; otherwise component FadeInDuration.
            double fadeIn = _controlFadeInDuration ?? audioComp.FadeInDuration;
            playback.Play(fadeIn);
            // Honour cue-level pause (e.g. global pause while this component was still setting up).
            if (_isPaused)
                playback.Pause();
            SyncPauseTransportUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveCue:TriggerAudioComponent - Error triggering {audioComp.AudioFile}: {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Trigger failed for audio in {_cue.Name}: {ex.Message}", 2);
            if (panel != null && _activeAudioComponents.TryGetValue(panel, out var failedPlayback))
            {
                try { failedPlayback.Clean(); } catch { /* best-effort */ }
            }
        }
    }

    private async Task TriggerVideoComponent(VideoComponent videoComp)
    {
        PanelContainer panel = null;
        try
        {
            // Find specific playback for this videoComp
            panel = _componentToVideo.FirstOrDefault(kv => kv.Value == videoComp).Key;
            if (panel == null)
            {
                GD.PrintErr($"ActiveCue:TriggerVideoComponent - No playback found for {videoComp.VideoFile}");
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                    $"No playback for video component in cue {_cue.Name}", 2);
                return;
            }
            var playback = _activeVideoComponents[panel];

            if (videoComp.UseAudio && videoComp.HasAudio && videoComp.HasAudioOutputAssigned)
            {
                bool started = await _audioDevices.StartAudioPlayback(playback);
                if (!started || !playback.HasBoundAudioStreams)
                {
                    _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                        $"Video audio in {_cue.Name} failed to bind; playing video silently.", 1);
                    // Ensure presentation uses wall clock (no stuck audio master).
                    playback.DisableEmbeddedAudio();
                }
            }
            else if (videoComp.UseAudio && videoComp.HasAudio && !videoComp.HasAudioOutputAssigned)
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                    $"Video in {_cue.Name} has no audio output assigned; playing video silently.", 1);
            }

            // Control GO fade-in override wins when set; otherwise component FadeInDuration.
            // FadeInAsync starts playback then ramps volume/opacity; PlayAsync is the zero-fade path.
            double fadeIn = _controlFadeInDuration ?? videoComp.FadeInDuration;
            if (fadeIn > 1e-9)
                await playback.FadeInAsync(fadeIn);
            else
                await playback.PlayAsync();

            if (_isPaused)
                playback.Pause();
            SyncPauseTransportUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveCue:TriggerVideoComponent - Error triggering {videoComp.VideoFile}: {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Trigger failed for video in {_cue.Name}: {ex.Message}", 2);
            if (panel != null && _activeVideoComponents.TryGetValue(panel, out var failedPlayback))
            {
                try { _ = failedPlayback.Stop(0); } catch { /* best-effort */ }
            }
        }
    }

    private async Task TriggerCueLightComponent(CueLightComponent comp)
    {
        try
        {
            await comp.ExecuteAsync();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveCue:TriggerCueLightComponent - {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Cue light trigger failed in {_cue.Name}: {ex.Message}", 2);
        }
        finally
        {
            var panel = _activeCueLightComponents.FirstOrDefault(kv => kv.Value == comp).Key;
            if (panel != null)
            {
                HandleCueLightComponentCompleted(panel);
            }
        }
    }

    private async Task TriggerOscComponent(OscComponent comp)
    {
        try
        {
            await comp.Execute();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveCue:TriggerOscComponent - {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"OSC trigger failed in {_cue.Name}: {ex.Message}", 2);
        }
        finally
        {
            var panel = _activeOscComponents.FirstOrDefault(kv => kv.Value == comp).Key;
            if (panel != null)
            {
                HandleOscComponentCompleted(panel);
            }
        }
    }

    private async Task TriggerMidiOutputComponent(MidiOutputComponent comp)
    {
        try
        {
            await comp.Execute();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveCue:TriggerMidiOutputComponent - {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"MIDI output failed in {_cue.Name}: {ex.Message}", 2);
        }
        finally
        {
            var panel = _activeMidiOutputComponents.FirstOrDefault(kv => kv.Value == comp).Key;
            if (panel != null)
                HandleMidiOutputComponentCompleted(panel);
        }
    }

    private async Task TriggerControlComponent(ControlComponent comp)
    {
        try
        {
            GlobalData gd = null;
            if (Engine.GetMainLoop() is SceneTree st)
                gd = st.Root.GetNodeOrNull<GlobalData>("/root/GlobalData");

            float sessionStopFade = gd?.Settings?.StopFadeDuration ?? 0f;
            await comp.ExecuteAsync(gd?.CueCommandExectutor, _cue?.Id ?? -1, sessionStopFade);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveCue:TriggerControlComponent - {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Control trigger failed in {_cue.Name}: {ex.Message}", 2);
        }
        finally
        {
            var panel = _activeControlComponents.FirstOrDefault(kv => kv.Value == comp).Key;
            if (panel != null)
                HandleControlComponentCompleted(panel);
        }
    }


    private void PreWait()
    {
        GD.Print($"ActiveCue:PreWait - Pre-wait of {_cue.PreWait} detected");
        
        // Ensure timer exists (chain path may have skipped SetupTimers pre-wait if duration was 0 at setup).
        if (_preWaitTimer == null || !IsInstanceValid(_preWaitTimer))
        {
            _preWaitTimer = new Timer { WaitTime = _cue.PreWait, OneShot = true, IgnoreTimeScale = true };
            _activeCueBar.AddChild(_preWaitTimer);
        }
        else
        {
            _preWaitTimer.WaitTime = _cue.PreWait;
        }

        //Ui
        var preWaitNameLabel = _activeCueBar.GetNode<Label>("%PreWaitNameLabel");
        preWaitNameLabel.Text = _cue.Name;
        _preWaitTimerLabel.Text = UiUtilities.FormatTime(_cue.PreWait);
        if (_preWaitProgress != null)
            _preWaitProgress.Value = 100;

        _preWaitPause.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
        _preWaitSkip.Icon = _activeCueBar.GetThemeIcon("Skip", "AtlasIcons");
        
        _preWaitPanel.Modulate = Colors.White;
        _preWaitPanel.Visible = true;

        _inPreWait = true;
        _preWaitFinished = false;
        _contentPlaybackActive = false;
        EnsureTimelineStarted();
        HookPreWaitUpdate(true);
        
        // Pause logic
        if (!_preWaitTimeoutHooked)
        {
            _preWaitTimer.Timeout += OnPreWaitTimerTimeout;
            _preWaitTimeoutHooked = true;
        }
        _preWaitTimer.Start();
        if (_isPaused)
            _preWaitTimer.SetPaused(true);

        UpdateHeadProgressUi();
    }

    private void PreWaitUpdate()
    {
        if (_preWaitTimer == null || !IsInstanceValid(_preWaitTimer)) return;
        _preWaitTimerLabel.Text = UiUtilities.FormatTime(_preWaitTimer.TimeLeft);
        var preWaitPercentage = (_preWaitTimer.TimeLeft / (float)_cue.PreWait) * 100;
        _preWaitProgress.Value = preWaitPercentage;
        // Keep head bar in sync during pre-wait (proportional pre-wait segment).
        UpdateHeadProgressUi();
    }

    private void TogglePreWaitPause()
    {
        if (_preWaitTimer == null || !IsInstanceValid(_preWaitTimer)) return;
        if (_preWaitTimer.Paused)
            PreWaitResume();
        else
            PreWaitPause();
    }

    private void PreWaitPause()
    {
        if (_preWaitTimer == null || !IsInstanceValid(_preWaitTimer)) return;
        _preWaitTimer.SetPaused(true);
        PauseTimelineClock();
        _preWaitPause.Icon = _activeCueBar.GetThemeIcon("Play", "AtlasIcons");
        UpdateHeadProgressUi();
    }

    private void PreWaitResume()
    {
        if (_preWaitTimer == null || !IsInstanceValid(_preWaitTimer)) return;
        if (_isPaused) return; // cue-level pause owns transport
        _preWaitTimer.SetPaused(false);
        ResumeTimelineClock();
        _preWaitPause.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
        UpdateHeadProgressUi();
    }

    private void OnPreWaitTimerTimeout()
    {
        _ = FinishPreWaitAndStartContent();
    }

    /// <summary>
    /// Ends pre-wait (timer or skip) and starts content once. Safe against double-call / missing timer hooks.
    /// </summary>
    private async Task FinishPreWaitAndStartContent()
    {
        if (_preWaitFinished || _isCleaned || _suppressContentCompleted) return;
        if (!_inPreWait && _contentStarted) return;

        _preWaitFinished = true;
        _inPreWait = false;
        HookPreWaitUpdate(false);

        if (_preWaitTimer != null && IsInstanceValid(_preWaitTimer))
        {
            _preWaitTimer.Stop();
            if (_preWaitTimeoutHooked)
            {
                _preWaitTimer.Timeout -= OnPreWaitTimerTimeout;
                _preWaitTimeoutHooked = false;
            }
        }

        HidePreWaitPanel();

        // Content region starts after the pre-wait segment on the proportional head bar.
        _preWaitSecondsHonored = Math.Max(0.0, _cue?.PreWait ?? 0.0);
        // Keep playhead continuous: if wall clock drifted, snap to pre-wait boundary.
        if (GetCueTimelineSeconds() < _preWaitSecondsHonored - 1e-3)
            SetTimelineSeconds(_preWaitSecondsHonored);

        await BeginContentPhaseAsync();
    }

    private void HidePreWaitPanel()
    {
        if (_preWaitPanel != null && IsInstanceValid(_preWaitPanel))
        {
            _preWaitPanel.Visible = false;
            _preWaitPanel.Modulate = Colors.White;
        }
    }

    /// <summary>
    /// Pre-wait skip: only advances past pre-wait. Does not skip continue/follow.
    /// If pre-wait is not active yet, marks it skipped for when playback reaches that phase.
    /// </summary>
    private void OnPreWaitSkipPressed()
    {
        if (_isCleaned || _suppressContentCompleted) return;

        // Already past pre-wait / in content.
        if (_contentStarted && !_inPreWait)
            return;

        if (_inPreWait)
        {
            // Active pre-wait → end it now (content starts; continue/follow already finished).
            _ = FinishPreWaitAndStartContent();
            return;
        }

        // Not in pre-wait yet (still on continue/follow, or pending) → skip pre-wait when we get there.
        _skipPreWait = true;
        HidePreWaitPanel();
        GD.Print($"ActiveCue:OnPreWaitSkipPressed - {_cue.Name}: pre-wait will be skipped");
    }

    /// <summary>
    /// GO chain path: head starts immediately; other members wait to be armed (continue/follow rules).
    /// </summary>
    /// <returns>
    /// Completes when the head has entered pre-wait or started content, or when a non-head
    /// member has shown its pending UI.
    /// </returns>
    private async Task BeginChainMemberRunAsync()
    {
        if (_isCleaned || _chainRunStarted) return;
        _chainRunStarted = true;

        // Pre-wait strip: inactive until this cue is allowed to run its own pre-wait.
        ShowInactivePreWaitPreview();

        // Outgoing post-wait strip is not used — next cue shows continue/follow lead-in instead.
        if (_postWaitPanel != null && IsInstanceValid(_postWaitPanel))
            _postWaitPanel.Visible = false;

        bool isHead = _incomingMode == FollowType.None;
        if (isHead)
        {
            // Head of the GO: no incoming wait — go straight into pre-wait / content.
            _incomingArmed = true;
            HideSequencePanel();
            // Await so control GO can finish before a following Start Now / Pause / etc.
            await StartPlaybackCoreAsync(includePreWait: true);
            return;
        }

        // Pending: visible but not counting until predecessor arms us.
        ShowPendingIncomingUi();
    }

    private void ShowInactivePreWaitPreview()
    {
        if (_preWaitPanel == null || !IsInstanceValid(_preWaitPanel)) return;
        if (_cue.PreWait <= 1e-9)
        {
            _preWaitPanel.Visible = false;
            return;
        }

        var preWaitNameLabel = _activeCueBar.GetNodeOrNull<Label>("%PreWaitNameLabel");
        if (preWaitNameLabel != null)
            preWaitNameLabel.Text = _cue.Name ?? string.Empty;
        if (_preWaitTimerLabel != null)
            _preWaitTimerLabel.Text = UiUtilities.FormatTime(_cue.PreWait);
        if (_preWaitProgress != null)
            _preWaitProgress.Value = 100;
        _preWaitPanel.Modulate = new Color(1f, 1f, 1f, 0.4f);
        _preWaitPanel.Visible = true;
        if (_preWaitPause != null)
            _preWaitPause.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
        if (_preWaitSkip != null)
            _preWaitSkip.Icon = _activeCueBar.GetThemeIcon("Skip", "AtlasIcons");
    }

    private void ShowPendingIncomingUi()
    {
        if (_sequencePanel == null || !IsInstanceValid(_sequencePanel)) return;

        bool isFollow = _incomingMode == FollowType.Follow;
        if (_sequenceLabel != null)
            _sequenceLabel.Text = isFollow ? "Follow" : "Continue";
        if (_sequenceNameLabel != null)
        {
            _sequenceNameLabel.Text = isFollow
                ? "Waiting for previous to complete…"
                : "Waiting to continue…";
        }
        if (_sequenceTimerLabel != null)
        {
            _sequenceTimerLabel.Text = _incomingWaitDuration > 1e-9
                ? UiUtilities.FormatTime(_incomingWaitDuration)
                : "";
        }

        // Continue: no progress bar — label only. Follow: keep bar for post-wait once armed.
        if (_sequenceProgress != null)
        {
            _sequenceProgress.Value = 100;
            // Hide fill for continue (label-style); show for follow.
            _sequenceProgress.Modulate = isFollow ? Colors.White : new Color(1, 1, 1, 0.15f);
        }

        _sequencePanel.Modulate = new Color(1f, 1f, 1f, 0.45f);
        _sequencePanel.Visible = true;
        if (_sequencePause != null)
            _sequencePause.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
        if (_sequenceSkip != null)
            _sequenceSkip.Icon = _activeCueBar.GetThemeIcon("Skip", "AtlasIcons");
    }

    /// <summary>
    /// Starts the post-wait after arm (continue label countdown or follow progress bar).
    /// </summary>
    private void BeginIncomingPostWait()
    {
        if (_isCleaned || _suppressContentCompleted) return;

        _inIncomingWait = true;
        bool isFollow = _incomingMode == FollowType.Follow;

        if (_sequencePanel != null && IsInstanceValid(_sequencePanel))
        {
            if (_sequenceLabel != null)
                _sequenceLabel.Text = isFollow ? "Follow" : "Continue";
            if (_sequenceNameLabel != null)
            {
                // Continue uses a text status instead of a progress bar.
                _sequenceNameLabel.Text = isFollow
                    ? (_cue.Name ?? "")
                    : $"Continuing after {UiUtilities.FormatTime(_incomingWaitDuration)}";
            }
            if (_sequenceTimerLabel != null)
                _sequenceTimerLabel.Text = UiUtilities.FormatTime(_incomingWaitDuration);
            if (_sequenceProgress != null)
            {
                _sequenceProgress.Value = 100;
                _sequenceProgress.Modulate = isFollow ? Colors.White : new Color(1, 1, 1, 0.12f);
            }
            _sequencePanel.Modulate = Colors.White;
            _sequencePanel.Visible = true;
        }

        _incomingWaitTimer = new Timer
        {
            WaitTime = _incomingWaitDuration,
            OneShot = true,
            IgnoreTimeScale = true,
            Autostart = false
        };
        _activeCueBar.AddChild(_incomingWaitTimer);
        _incomingWaitTimer.Timeout += OnIncomingPostWaitComplete;
        _incomingWaitTimeoutHooked = true;
        _incomingWaitTimer.Start();
        HookIncomingWaitUpdate(true);
    }

    private void IncomingWaitUpdate()
    {
        if (!_inIncomingWait || _incomingWaitTimer == null || !IsInstanceValid(_incomingWaitTimer))
            return;

        double left = _incomingWaitTimer.TimeLeft;
        bool isFollow = _incomingMode == FollowType.Follow;

        if (_sequenceTimerLabel != null)
            _sequenceTimerLabel.Text = UiUtilities.FormatTime(left);

        if (!isFollow && _sequenceNameLabel != null)
            _sequenceNameLabel.Text = $"Continuing after {UiUtilities.FormatTime(left)}";

        if (isFollow && _sequenceProgress != null && _incomingWaitDuration > 1e-9)
            _sequenceProgress.Value = (left / _incomingWaitDuration) * 100.0;
    }

    private void OnIncomingPostWaitComplete()
    {
        if (!_inIncomingWait) return;
        HookIncomingWaitUpdate(false);
        FreeIncomingWaitTimer();
        _inIncomingWait = false;
        HideSequencePanel();

        if (_isCleaned || _suppressContentCompleted) return;
        // Proceed to this cue's pre-wait (unless pre-wait was already skipped).
        _ = StartPlaybackCoreAsync(includePreWait: true);
    }

    /// <summary>
    /// Continue/follow skip: abandon continue/follow entirely and play this cue now.
    /// Does not skip this cue's own pre-wait (use the pre-wait skip for that).
    /// </summary>
    private void OnSequenceSkipPressed()
    {
        if (_isCleaned || _suppressContentCompleted) return;
        if (_contentStarted) return;

        // Head has no continue/follow bar to skip, or we already moved past it into pre-wait alone.
        if (_incomingMode == FollowType.None) return;
        if (_incomingArmed && !_inIncomingWait) return;

        GD.Print($"ActiveCue:OnSequenceSkipPressed - {_cue.Name}: skipping continue/follow, playing cue now");

        // Cancel any active continue/follow countdown.
        if (_inIncomingWait)
        {
            HookIncomingWaitUpdate(false);
            if (_incomingWaitTimer != null && IsInstanceValid(_incomingWaitTimer))
                _incomingWaitTimer.Stop();
            FreeIncomingWaitTimer();
            _inIncomingWait = false;
        }

        // Independently started — ignore later ArmIncoming from the predecessor.
        _incomingArmed = true;
        HideSequencePanel();

        // Play this cue (own pre-wait still applies unless the user also skipped it).
        _ = StartPlaybackCoreAsync(includePreWait: true);
    }

    private void OnPostWaitSkipPressed()
    {
        // Outgoing post-wait bar unused in chain mode.
    }

    private void ToggleSchedulePause()
    {
        // Pause incoming post-wait and/or media.
        if (_inIncomingWait && _incomingWaitTimer != null && IsInstanceValid(_incomingWaitTimer))
        {
            if (_incomingWaitTimer.Paused)
            {
                _incomingWaitTimer.SetPaused(false);
                if (_sequencePause != null)
                    _sequencePause.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
            }
            else
            {
                _incomingWaitTimer.SetPaused(true);
                if (_sequencePause != null)
                    _sequencePause.Icon = _activeCueBar.GetThemeIcon("Play", "AtlasIcons");
            }
            return;
        }

        if (_inPreWait)
            TogglePreWaitPause();
        else if (_contentStarted)
            TogglePauseAll();
    }

    private void HideSequencePanel()
    {
        if (_sequencePanel != null && IsInstanceValid(_sequencePanel))
        {
            _sequencePanel.Visible = false;
            _sequencePanel.Modulate = Colors.White;
        }
    }

    private void FreeIncomingWaitTimer()
    {
        if (_incomingWaitTimer == null) return;
        var timer = _incomingWaitTimer;
        _incomingWaitTimer = null;
        bool wasHooked = _incomingWaitTimeoutHooked;
        _incomingWaitTimeoutHooked = false;
        if (!IsInstanceValid(timer))
            return;
        timer.Stop();
        // Only disconnect if we still believe we are hooked (avoids Godot disconnect errors).
        if (wasHooked)
        {
            try { timer.Timeout -= OnIncomingPostWaitComplete; }
            catch { /* already disconnected */ }
        }
        timer.QueueFree();
    }

    private void HookIncomingWaitUpdate(bool hook)
    {
        if (_updateTimer == null || !IsInstanceValid(_updateTimer))
        {
            _incomingWaitUpdateHooked = false;
            return;
        }
        if (hook && !_incomingWaitUpdateHooked)
        {
            _updateTimer.Timeout += IncomingWaitUpdate;
            _incomingWaitUpdateHooked = true;
        }
        else if (!hook && _incomingWaitUpdateHooked)
        {
            _updateTimer.Timeout -= IncomingWaitUpdate;
            _incomingWaitUpdateHooked = false;
        }
    }

    private void HookPreWaitUpdate(bool hook)
    {
        if (_updateTimer == null || !IsInstanceValid(_updateTimer))
        {
            _preWaitUpdateHooked = false;
            return;
        }

        if (hook && !_preWaitUpdateHooked)
        {
            _updateTimer.Timeout += PreWaitUpdate;
            _preWaitUpdateHooked = true;
        }
        else if (!hook && _preWaitUpdateHooked)
        {
            _updateTimer.Timeout -= PreWaitUpdate;
            _preWaitUpdateHooked = false;
        }
    }

    private void RaiseContentPhaseStarted()
    {
        if (_contentPhaseStartedRaised || _suppressContentCompleted || _isCleaned)
            return;
        _contentPhaseStartedRaised = true;
        try
        {
            ContentPhaseStarted?.Invoke();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveCue:RaiseContentPhaseStarted - {_cue?.Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Natural content end: raise ContentCompleted (follow arms next in real time) and cleanup.
    /// </summary>
    private void HandleNaturalContentFinished()
    {
        if (_isCleaned) return;

        RaiseContentCompleted();
        ScheduleCleanup();
    }


    private void UpdateUi()
    {
        if (_isCleaned) return;

        // Snapshot keys — dictionary may change if a component completes mid-tick.
        foreach (var panel in _activeAudioComponents.Keys.ToList())
        {
            if (!IsInstanceValid(panel)) continue;
            if (!_componentToAudio.TryGetValue(panel, out var audioComponent) || audioComponent == null)
                continue;
            UpdateComponentUiState(panel, audioComponent);
        }
        // Video components are updated via TimeUpdated event for real-time updates

        UpdateHeadProgressUi();
    }

    /// <summary>
    /// Playable head-bar span: PreWait + Duration. -1 when infinite/unknown.
    /// </summary>
    private double GetPlayableTimelineDuration()
    {
        if (_cue == null) return 0;
        if (_cue.Duration < 0) return -1;
        return Math.Max(0.0, _cue.PreWait) + Math.Max(0.0, _cue.Duration);
    }

    /// <summary>
    /// True when this active cue still holds audio/video/text playback instances.
    /// </summary>
    private bool HasOwnMediaPlayback()
    {
        return _activeAudioComponents.Count > 0
               || _activeVideoComponents.Count > 0
               || _activeTextComponents.Count > 0;
    }

    /// <summary>
    /// Seeks into the pre-wait region of the body timeline (0 .. PreWait).
    /// </summary>
    private void SeekIntoPreWaitRegion(double preWaitElapsed)
    {
        double pre = Math.Max(0.0, _cue?.PreWait ?? 0.0);
        preWaitElapsed = Math.Clamp(preWaitElapsed, 0.0, Math.Max(0.0, pre - 1e-4));

        // Rewind from content: tear down children / park media, re-enter pre-wait.
        if (_contentPlaybackActive || (_contentPhaseStartedRaised && !_inPreWait))
        {
            TearDownContentPlaybackForRewind();
            BeginPreWaitAtElapsed(preWaitElapsed);
            SetTimelineSeconds(preWaitElapsed);
            UpdateHeadProgressUi();
            return;
        }

        if (_inPreWait)
        {
            AdjustPreWaitElapsed(preWaitElapsed);
            SetTimelineSeconds(preWaitElapsed);
            UpdateHeadProgressUi();
            return;
        }

        // Body not in pre-wait yet — queue.
        _pendingTimelineSeekSeconds = preWaitElapsed;
    }

    /// <summary>
    /// Seeks into the content region. <paramref name="contentTimeSeconds"/> is time since content origin;
    /// <paramref name="absoluteTimelineSeconds"/> is the full body playhead (including pre-wait).
    /// </summary>
    private void SeekIntoContentRegion(double contentTimeSeconds, double absoluteTimelineSeconds)
    {
        if (contentTimeSeconds < 0) contentTimeSeconds = 0;

        // Still in pre-wait: finish it, then land at this absolute body time.
        if (_inPreWait)
        {
            _pendingTimelineSeekSeconds = absoluteTimelineSeconds;
            _ = FinishPreWaitAndStartContent();
            return;
        }

        // Content not started yet (setup) — apply once content begins.
        if (!_contentPhaseStartedRaised && !_contentPlaybackActive)
        {
            _pendingTimelineSeekSeconds = absoluteTimelineSeconds;
            return;
        }

        SetTimelineSeconds(absoluteTimelineSeconds);

        // Content was rewound away (e.g. scrubbed into pre-wait) — restart playback.
        // Finished children are not resurrected (see <see cref="_finishedChildCueIds"/>).
        if (!_contentPlaybackActive)
        {
            _pendingTimelineSeekSeconds = absoluteTimelineSeconds;
            _ = BeginContentPhaseAsync();
            return;
        }

        // Do not resurrect finished / inactive children on scrub-back — only seek still-live ones.
        SeekOwnMediaToContentTime(contentTimeSeconds);
        PropagateTimelineSeekToChildren(contentTimeSeconds);
        UpdateHeadProgressUi();
    }

    /// <summary>
    /// Pushes a content-origin timeline (child t=0) into every nested active child.
    /// </summary>
    private void PropagateTimelineSeekToChildren(double childTimelineSeconds)
    {
        foreach (var child in _childActiveCues.ToList())
        {
            if (child == null || !IsInstanceValid(child)) continue;
            try
            {
                // Child body timeline starts when parent content starts.
                child.ApplyCueTimelineSeek(childTimelineSeconds);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ActiveCue:PropagateTimelineSeekToChildren - {_cue?.Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Stops nested children and parks own media so pre-wait can be re-entered without freeing this cue.
    /// </summary>
    private void TearDownContentPlaybackForRewind()
    {
        _isRewindingContent = true;
        try
        {
            foreach (var child in _childActiveCues.ToList())
            {
                if (child == null || !IsInstanceValid(child)) continue;
                try
                {
                    // Immediate cleanup (no fade) — parent is rewinding.
                    child.Cleanup();
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"ActiveCue:TearDownContentPlaybackForRewind - child: {ex.Message}");
                }
            }
            _childActiveCues.Clear();
        }
        finally
        {
            _isRewindingContent = false;
        }

        // Park media at start without completing (Stop/Clean would free component rows).
        foreach (var kv in _activeAudioComponents.ToList())
        {
            var playback = kv.Value;
            if (playback == null) continue;
            _componentToAudio.TryGetValue(kv.Key, out var audioComp);
            try
            {
                playback.Pause();
                double start = audioComp?.StartTime ?? 0;
                playback.Seek((long)(start * 1_000_000.0));
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ActiveCue:TearDownContentPlaybackForRewind - audio: {ex.Message}");
            }
        }

        foreach (var kv in _activeVideoComponents.ToList())
        {
            var playback = kv.Value;
            if (playback == null) continue;
            _componentToVideo.TryGetValue(kv.Key, out var videoComp);
            try
            {
                playback.Pause();
                double start = videoComp != null && !videoComp.IsImage ? videoComp.StartTime : 0;
                playback.Seek(start);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ActiveCue:TearDownContentPlaybackForRewind - video: {ex.Message}");
            }
        }

        foreach (var playback in _activeTextComponents.Values.ToList())
        {
            try { playback?.Pause(); }
            catch { /* best-effort */ }
        }

        _contentPlaybackActive = false;
        _isFinished = false;
    }

    /// <summary>
    /// Re-enters pre-wait at a specific elapsed time (used when scrubbing backward into pre-wait).
    /// </summary>
    private void BeginPreWaitAtElapsed(double elapsedSeconds)
    {
        double pre = Math.Max(0.0, _cue?.PreWait ?? 0.0);
        if (pre <= 1e-9)
        {
            _ = BeginContentPhaseAsync();
            return;
        }

        elapsedSeconds = Math.Clamp(elapsedSeconds, 0.0, pre);
        double remaining = pre - elapsedSeconds;
        if (remaining <= 1e-4)
        {
            _preWaitSecondsHonored = pre;
            _ = BeginContentPhaseAsync();
            return;
        }

        // Reuse PreWait UI path then adjust remaining.
        if (!_inPreWait)
            PreWait();

        AdjustPreWaitElapsed(elapsedSeconds);
    }

    /// <summary>
    /// Seeks own audio/video components so content-local time maps onto media (StartTime + t).
    /// </summary>
    private void SeekOwnMediaToContentTime(double contentTimeSeconds)
    {
        foreach (var kv in _activeAudioComponents.ToList())
        {
            var playback = kv.Value;
            if (playback == null) continue;
            _componentToAudio.TryGetValue(kv.Key, out var audioComp);
            try
            {
                double start = audioComp?.StartTime ?? 0;
                double mediaTime = start + contentTimeSeconds;
                if (mediaTime < 0) mediaTime = 0;
                // Clamp to component end when known so scrubbing past a short file doesn't hang.
                if (audioComp != null && audioComp.Duration > 0)
                    mediaTime = Math.Min(mediaTime, start + audioComp.Duration);
                playback.Seek((long)(mediaTime * 1_000_000.0));
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ActiveCue:SeekOwnMediaToContentTime - Audio seek failed on {_cue?.Name}: {ex.Message}");
            }
        }

        foreach (var kv in _activeVideoComponents.ToList())
        {
            var playback = kv.Value;
            if (playback == null) continue;
            _componentToVideo.TryGetValue(kv.Key, out var videoComp);
            try
            {
                double start = videoComp != null && !videoComp.IsImage ? videoComp.StartTime : 0;
                double mediaTime = start + contentTimeSeconds;
                if (mediaTime < 0) mediaTime = 0;
                double span = playback.GetDuration();
                if (span > 0)
                    mediaTime = Math.Min(mediaTime, start + span);
                playback.Seek(mediaTime);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ActiveCue:SeekOwnMediaToContentTime - Video seek failed on {_cue?.Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Starts the body timeline clock at 0 if not already running.
    /// </summary>
    private void EnsureTimelineStarted()
    {
        if (_timelineStarted) return;
        _timelineStarted = true;
        _timelineBase = 0;
        _timelineClockStartMsec = Time.GetTicksMsec();
        _timelineClockRunning = !_isPaused;
    }

    /// <summary>
    /// Sets the body playhead, preserving running/paused clock state.
    /// </summary>
    private void SetTimelineSeconds(double seconds)
    {
        EnsureTimelineStarted();
        _timelineBase = Math.Max(0.0, seconds);
        _timelineClockStartMsec = Time.GetTicksMsec();
    }

    private void PauseTimelineClock()
    {
        if (!_timelineStarted || !_timelineClockRunning) return;
        _timelineBase = GetWallTimelineSeconds();
        _timelineClockRunning = false;
    }

    private void ResumeTimelineClock()
    {
        if (!_timelineStarted || _isCleaned || _timelineClockRunning) return;
        _timelineClockStartMsec = Time.GetTicksMsec();
        _timelineClockRunning = true;
    }

    /// <summary>
    /// Wall-clock body playhead (no media/child soft-sync).
    /// </summary>
    private double GetWallTimelineSeconds()
    {
        if (!_timelineStarted) return 0;
        double t = _timelineBase;
        if (_timelineClockRunning)
            t += (Time.GetTicksMsec() - _timelineClockStartMsec) / 1000.0;
        return Math.Max(0.0, t);
    }

    /// <summary>
    /// Current body playhead in seconds (0 = start of pre-wait).
    /// Prefers live pre-wait timer / media / nested children when available so group bars track real progress.
    /// </summary>
    public double GetCueTimelineSeconds()
    {
        if (!_timelineStarted && !_inPreWait && !_contentPlaybackActive)
            return 0;

        double pre = Math.Max(0.0, _cue?.PreWait ?? 0.0);

        // Pre-wait: derive from timer so head bar matches pre-wait strip.
        if (_inPreWait && _preWaitTimer != null && IsInstanceValid(_preWaitTimer))
        {
            double elapsed = pre - _preWaitTimer.TimeLeft;
            return Math.Clamp(elapsed, 0.0, pre);
        }

        if (!(_contentPlaybackActive || _contentPhaseStartedRaised))
            return GetWallTimelineSeconds();

        double media = TryGetMaxOwnMediaContentSeconds();

        // Leaf with own media: media playhead is authoritative (component scrub must move the head bar,
        // including seek-backward — wall clock alone would stick at the high-water mark).
        if (_childActiveCues.Count == 0 && media >= 0)
            return _preWaitSecondsHonored + media;

        // Groups: max of wall, own media, and still-active children.
        double best = GetWallTimelineSeconds();
        if (media >= 0)
            best = Math.Max(best, _preWaitSecondsHonored + media);

        foreach (var child in _childActiveCues)
        {
            if (child == null || !IsInstanceValid(child)) continue;
            try
            {
                double childT = child.GetCueTimelineSeconds();
                best = Math.Max(best, _preWaitSecondsHonored + childT);
            }
            catch
            {
                // ignore disposed child during teardown
            }
        }

        return Math.Max(0.0, best);
    }

    /// <summary>
    /// After a component-level scrub, snap the body playhead to own media so the head bar updates immediately.
    /// </summary>
    /// <param name="contentLocalSeconds">Content-local time (media time − StartTime).</param>
    private void SyncHeadTimelineFromComponentSeek(double contentLocalSeconds)
    {
        if (_isCleaned) return;
        if (contentLocalSeconds < 0) contentLocalSeconds = 0;

        double absolute = _preWaitSecondsHonored + contentLocalSeconds;
        SetTimelineSeconds(absolute);
        UpdateHeadProgressUi();
    }

    /// <summary>
    /// Max content-local time from active audio/video (media time − StartTime).
    /// </summary>
    /// <returns>Seconds, or -1 when no running media.</returns>
    private double TryGetMaxOwnMediaContentSeconds()
    {
        double max = -1;

        foreach (var kv in _activeAudioComponents)
        {
            var playback = kv.Value;
            if (playback == null || playback.IsStopped) continue;
            _componentToAudio.TryGetValue(kv.Key, out var audioComp);
            double start = audioComp?.StartTime ?? 0;
            double media = playback.GetPlaybackTimeMs() / 1000.0;
            double local = media - start;
            if (local > max) max = local;
        }

        foreach (var kv in _activeVideoComponents)
        {
            var playback = kv.Value;
            if (playback == null || playback.IsStopped) continue;
            _componentToVideo.TryGetValue(kv.Key, out var videoComp);
            double start = videoComp != null && !videoComp.IsImage ? videoComp.StartTime : 0;
            double media = playback.GetPlaybackTimeSeconds();
            double local = media - start;
            if (local > max) max = local;
        }

        return max;
    }

    /// <summary>
    /// Adjusts an active pre-wait timer so elapsed time matches <paramref name="elapsedSeconds"/>.
    /// </summary>
    private void AdjustPreWaitElapsed(double elapsedSeconds)
    {
        if (!_inPreWait || _preWaitTimer == null || !IsInstanceValid(_preWaitTimer))
            return;

        double preWait = Math.Max(0.0, _cue?.PreWait ?? 0.0);
        if (preWait <= 1e-9)
            return;

        elapsedSeconds = Math.Clamp(elapsedSeconds, 0.0, preWait);
        double remaining = preWait - elapsedSeconds;
        if (remaining <= 1e-4)
        {
            _ = FinishPreWaitAndStartContent();
            return;
        }

        bool wasPaused = _preWaitTimer.Paused || _isPaused;
        _preWaitTimer.Stop();
        _preWaitTimer.WaitTime = remaining;
        _preWaitTimer.Start();
        if (wasPaused)
            _preWaitTimer.SetPaused(true);

        if (_preWaitTimerLabel != null)
            _preWaitTimerLabel.Text = UiUtilities.FormatTime(remaining);
        if (_preWaitProgress != null)
            _preWaitProgress.Value = (remaining / preWait) * 100.0;
    }

    /// <summary>
    /// Refreshes head progress bar and time labels from the body timeline (pre-wait + content).
    /// </summary>
    private void UpdateHeadProgressUi()
    {
        if (_isCleaned || _headProgressBar == null || !IsInstanceValid(_headProgressBar))
            return;
        if (_headIsSeeking)
            return;

        double playable = GetPlayableTimelineDuration();
        double elapsed = 0;

        if (_timelineStarted || _inPreWait || _contentPlaybackActive)
            elapsed = GetCueTimelineSeconds();

        // Soft-sync wall clock to derived playhead so pause/resume stays consistent.
        if (!_headIsSeeking && (_timelineClockRunning || _timelineStarted))
        {
            _timelineBase = elapsed;
            _timelineClockStartMsec = Time.GetTicksMsec();
        }

        UpdateHeadTimeLabels(elapsed, playable);

        if (playable < 0)
        {
            // Infinite: show empty fill, elapsed only.
            _headProgressBar.Value = 0;
        }
        else if (playable <= 1e-9)
        {
            _headProgressBar.Value = _contentPlaybackActive || _inPreWait ? 100 : 0;
        }
        else
        {
            _headProgressBar.Value = Math.Clamp(elapsed / playable * 100.0, 0.0, 100.0);
        }
    }

    /// <summary>
    /// Writes head elapsed / remaining labels for the playable body span.
    /// </summary>
    private void UpdateHeadTimeLabels(double elapsedSeconds, double durationSeconds)
    {
        if (_headLabelTimeLeft != null && IsInstanceValid(_headLabelTimeLeft))
            _headLabelTimeLeft.Text = UiUtilities.FormatTime(Math.Max(0, elapsedSeconds));

        if (_headLabelTimeRight == null || !IsInstanceValid(_headLabelTimeRight))
            return;

        if (durationSeconds < 0)
        {
            _headLabelTimeRight.Text = "∞";
        }
        else
        {
            double remaining = Math.Max(0, durationSeconds - elapsedSeconds);
            _headLabelTimeRight.Text = $"-({UiUtilities.FormatTime(remaining)})";
        }
    }
    
    private async Task SetupComponents()
    {
        var tasks = new List<Task>();
        foreach (var component in _cue.Components)
        {
            if (component is AudioComponent audioComponent)
            {
                tasks.Add(SetupAudioComponent(audioComponent));
            }
            else if (component is VideoComponent videoComponent)
            {
                tasks.Add(SetupVideoComponent(videoComponent));
            }
            else if (component is TextComponent textComponent)
            {
                tasks.Add(SetupTextComponent(textComponent));
            }
            else if (component is CueLightComponent cueLightComponent)
            {
                tasks.Add(SetupCueLightComponent(cueLightComponent));
                //var cueLightComp = component as CueLightComponent;
                //tasks.Add(cueLightComp.ExecuteAsync(_cue.CueNum));
            }
            else if (component is OscComponent oscComponent)
            {
                tasks.Add(SetupOscComponent(oscComponent));
            }
            else if (component is MidiOutputComponent midiOutComponent)
            {
                tasks.Add(SetupMidiOutputComponent(midiOutComponent));
            }
            else if (component is ControlComponent controlComponent)
            {
                tasks.Add(SetupControlComponent(controlComponent));
            }
        }
        await Task.WhenAll(tasks);
        LinkVideoSubtitlesToText();
    }

    /// <summary>
    /// When the video component has closed captions enabled, wires the active text
    /// playback so timed subtitles drive its live text.
    /// </summary>
    private void LinkVideoSubtitlesToText()
    {
        try
        {
            var videoComp = _cue?.GetVideoComponent();
            if (videoComp == null || !videoComp.UseSubtitles || videoComp.IsImage)
                return;

            ActiveVideoPlayback videoPlayback = null;
            foreach (var kv in _componentToVideo)
            {
                if (kv.Value == videoComp && _activeVideoComponents.TryGetValue(kv.Key, out var pb))
                {
                    videoPlayback = pb;
                    break;
                }
            }

            ActiveTextPlayback textPlayback = null;
            foreach (var pb in _activeTextComponents.Values)
            {
                if (pb != null && IsInstanceValid(pb))
                {
                    textPlayback = pb;
                    break;
                }
            }

            if (videoPlayback == null)
                return;

            if (textPlayback == null)
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                    $"Video in {_cue.Name} has closed captions enabled but no text component is present.",
                    1);
                return;
            }

            // CC slave: hold until video ends (do not auto-complete on text duration alone).
            textPlayback.IsSubtitleSlave = true;
            videoPlayback.SetLinkedTextPlayback(textPlayback);
            // Start blank until the first cue.
            textPlayback.SetLiveTextOverride(string.Empty);
            GD.Print($"ActiveCue:LinkVideoSubtitlesToText - Linked CC to text on {_cue.Name}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveCue:LinkVideoSubtitlesToText - {ex.Message}");
        }
    }

    private async Task SetupAudioComponent(AudioComponent audioComponent)
    {
        try
        {
            // Fail fast: no output means playback can never produce sound or complete.
            if (!audioComponent.HasOutputAssigned)
            {
                GD.PrintErr($"ActiveCue:SetupAudioComponent - No output assigned for {audioComponent.AudioFile}");
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                    $"Cannot play audio in {_cue.Name}: no output assigned. Assign a patch or device in the inspector.", 2);
                return;
            }

            if (string.IsNullOrEmpty(audioComponent.AudioFile))
            {
                GD.PrintErr("ActiveCue:SetupAudioComponent - Audio file path is empty");
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                    $"Cannot play audio in {_cue.Name}: no audio file set.", 2);
                return;
            }

            if (!MediaFileAvailable(audioComponent.AudioFile))
            {
                ReportMissingMedia(audioComponent.AudioFile);
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                    $"Cannot play audio in {_cue.Name}: file missing ({audioComponent.AudioFile}).", 2);
                return;
            }

            // Preload metadata if not already (assuming done in inspector/load)
            if (audioComponent.Metadata == null)
            {
                audioComponent.Metadata = await _mediaEngine.GetAudioFileMetadataAsync(audioComponent.AudioFile);
            }

            var playback = new ActiveAudioPlayback(audioComponent, _audioDevices);
            await playback.InitAsync();
            
            // UI
            PanelContainer componentPanel = _componentProgressBarScene.Instantiate<PanelContainer>();
            _componentContainer.AddChild(componentPanel);
            componentPanel.GetNode<Label>("%ComponentLabel").Text = Path.GetFileName(audioComponent.AudioFile);
            var typeIcon = componentPanel.GetNode<Button>("%ComponentIcon");
            var pauseButton = componentPanel.GetNode<Button>("%ComponentPause");
            var stopButton = componentPanel.GetNode<Button>("%ComponentStop");
            var timeLabel = componentPanel.GetNode<Label>("%ComponentTime");
            timeLabel.Text = UiUtilities.FormatTime(audioComponent.TotalDuration);
            
            typeIcon.Icon = _activeCueBar.GetThemeIcon("Audio2", "AtlasIcons");
            // If cue is already paused (global pause during setup), show resume icon.
            pauseButton.Icon = _activeCueBar.GetThemeIcon(_isPaused ? "Play" : "Pause", "AtlasIcons");
            stopButton.Icon = _activeCueBar.GetThemeIcon("Stop", "AtlasIcons");
            
            
            // Component Logic
            _activeAudioComponents.Add(componentPanel, playback);
            _componentToAudio.Add(componentPanel, audioComponent);
            
            
            pauseButton.Pressed += () => 
            {
                if (_isCleaned || !IsInstanceValid(componentPanel)) return;
                if (!_activeAudioComponents.TryGetValue(componentPanel, out var pb) || pb == null)
                    return;

                if (!pb.IsPaused)
                    pb.Pause();
                else
                    pb.Resume();

                // Component-level toggle; refresh this button from playback state.
                if (IsInstanceValid(pauseButton) && IsInstanceValid(_activeCueBar))
                    pauseButton.Icon = _activeCueBar.GetThemeIcon(pb.IsPaused ? "Play" : "Pause", "AtlasIcons");
            };
            
            // Stop
            stopButton.Pressed += async () => await StopComponent(componentPanel);
            
            
            // Progress bar seeking
            var progressBar = componentPanel.GetNode<ProgressBar>("ComponentProgress");
            double pendingSeekTimeSec = 0;
            progressBar.GuiInput += (@event) =>
            {
                if (!_activeAudioComponents.ContainsKey(componentPanel)) return;
                if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
                {
                    if (mb.Pressed)
                    {
                        playback.IsSeeking = true;
                        // Calculate initial seek time
                        var localPos = progressBar.GetLocalMousePosition();
                        float percent = Mathf.Clamp(localPos.X / progressBar.Size.X, 0f, 1f);
                        pendingSeekTimeSec = audioComponent.StartTime + percent * audioComponent.Duration;
                        progressBar.Value = percent * 100; // Preview
                        timeLabel.Text = UiUtilities.FormatTime(pendingSeekTimeSec); // Preview time
                        // Live head preview while scrubbing component.
                        SyncHeadTimelineFromComponentSeek(pendingSeekTimeSec - audioComponent.StartTime);
                    }
                    else
                    {
                        // Release: perform the seek
                        if (playback.IsSeeking)
                        {
                            long timestampUs = (long)(pendingSeekTimeSec * 1_000_000);
                            playback.Seek(timestampUs);
                            SyncHeadTimelineFromComponentSeek(pendingSeekTimeSec - audioComponent.StartTime);
                            GD.Print($"ActiveCue:ProgressBar - Sought to {pendingSeekTimeSec} sec on release");
                        }
                        playback.IsSeeking = false;
                    }
                }
                else if (@event is InputEventMouseMotion && playback.IsSeeking)
                {
                    // Update preview during drag
                    var localPos = progressBar.GetLocalMousePosition();
                    float percent = Mathf.Clamp(localPos.X / progressBar.Size.X, 0f, 1f);
                    pendingSeekTimeSec = audioComponent.StartTime + percent * audioComponent.Duration;
                    progressBar.Value = percent * 100; // Update preview
                    timeLabel.Text = UiUtilities.FormatTime(pendingSeekTimeSec); // Update preview time
                    SyncHeadTimelineFromComponentSeek(pendingSeekTimeSec - audioComponent.StartTime);
                }
            };

            _activeComponentCount++;
            WireAudioCompleted(playback, componentPanel);
        }
        catch (Exception ex)
        {
            GD.Print($"ActiveCue:SetupAudioComponent - Exception: {ex.Message}");
            if (IsMissingFileException(ex, audioComponent.AudioFile))
                ReportMissingMedia(audioComponent.AudioFile);
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Error activating audio component for cue {_cue.Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Subscribes to audio completion without capturing a freeable PanelContainer into CallDeferred args.
    /// </summary>
    private void WireAudioCompleted(ActiveAudioPlayback playback, PanelContainer componentPanel)
    {
        ulong panelId = componentPanel.GetInstanceId();
        ActiveAudioPlayback.CompletedEventHandler handler = () =>
        {
            // Never touch the PanelContainer here — it may already be disposed during Cleanup.
            Callable.From(() => CompleteAudioByPanelId(panelId)).CallDeferred();
        };
        playback.Completed += handler;
        _audioCompleteHandlers.Add((playback, handler));
    }

    private void CompleteAudioByPanelId(ulong panelId)
    {
        if (_isCleaned || !IsInstanceValid(this)) return;

        PanelContainer panel = FindLivePanelKey(_activeAudioComponents.Keys, panelId);
        if (panel == null)
        {
            CheckForCueCompletion();
            return;
        }

        _activeAudioComponents.Remove(panel);
        _componentToAudio.Remove(panel);
        if (IsInstanceValid(panel))
            panel.QueueFree();
        CheckForCueCompletion();
    }

    /// <summary>
    /// Creates Ui for VideoComponent and handles input.
    /// </summary>
    /// <param name="videoComponent"></param>
    private async Task SetupVideoComponent(VideoComponent videoComponent)
    {
        try
        {
            if (string.IsNullOrEmpty(videoComponent.VideoFile))
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                    $"Cannot play video in {_cue.Name}: no video file set.", 2);
                return;
            }

            if (!MediaFileAvailable(videoComponent.VideoFile))
            {
                ReportMissingMedia(videoComponent.VideoFile);
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                    $"Cannot play video in {_cue.Name}: file missing ({videoComponent.VideoFile}).", 2);
                return;
            }

            videoComponent.RefreshIsImageFromPath();

            // Preload metadata if not already (assuming done in inspector/load)
            if (videoComponent.Metadata == null)
            {
                videoComponent.Metadata = await _mediaEngine.GetVideoFileMetadataAsync(videoComponent.VideoFile);
            }

            if (videoComponent.IsImage)
            {
                videoComponent.HasAudio = false;
                videoComponent.UseAudio = false;
                videoComponent.RecalculateDuration();
            }
            
            var playback = new ActiveVideoPlayback(videoComponent, _audioDevices);
            // Must be in the scene tree so _Process can present video frames.
            // ActiveCue is a GodotObject (not a Node); parent under the active cue bar.
            _activeCueBar.AddChild(playback);
            await playback.InitAsync();
            
            // UI
            PanelContainer componentPanel = _componentProgressBarScene.Instantiate<PanelContainer>();
            _componentContainer.AddChild(componentPanel);
            componentPanel.GetNode<Label>("%ComponentLabel").Text = Path.GetFileName(videoComponent.VideoFile);
            var typeIcon = componentPanel.GetNode<Button>("%ComponentIcon");
            var pauseButton = componentPanel.GetNode<Button>("%ComponentPause");
            var stopButton = componentPanel.GetNode<Button>("%ComponentStop");
            var timeLabel = componentPanel.GetNode<Label>("%ComponentTime");
            if (videoComponent.IsImage && videoComponent.Duration <= 0)
                timeLabel.Text = "∞";
            else
                timeLabel.Text = UiUtilities.FormatTime(videoComponent.TotalDuration);

            // Prefer Image icon when available; fall back to Video.
            try
            {
                typeIcon.Icon = _activeCueBar.GetThemeIcon(
                    videoComponent.IsImage ? "Image" : "Video", "AtlasIcons");
            }
            catch
            {
                typeIcon.Icon = _activeCueBar.GetThemeIcon("Video", "AtlasIcons");
            }
            pauseButton.Icon = _activeCueBar.GetThemeIcon(_isPaused ? "Play" : "Pause", "AtlasIcons");
            stopButton.Icon = _activeCueBar.GetThemeIcon("Stop", "AtlasIcons");

            // Component Logic
            _activeVideoComponents.Add(componentPanel, playback);
            _componentToVideo.Add(componentPanel, videoComponent);
            
            pauseButton.Pressed += () =>
            {
                if (_isCleaned || !IsInstanceValid(componentPanel)) return;
                if (!playback.IsPaused)
                    playback.Pause();
                else
                    playback.Resume();

                if (IsInstanceValid(pauseButton) && IsInstanceValid(_activeCueBar))
                    pauseButton.Icon = _activeCueBar.GetThemeIcon(playback.IsPaused ? "Play" : "Pause", "AtlasIcons");
            };

            // Stop
            stopButton.Pressed += async () => await StopVideoComponent(componentPanel);
            
            // Progress bar seeking
            var progressBar = componentPanel.GetNode<ProgressBar>("ComponentProgress");
            double pendingSeekTimeSec = 0;
            progressBar.GuiInput += (@event) =>
            {
                if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
                {
                    if (mb.Pressed)
                    {
                        playback.IsSeeking = true;
                        // Calculate initial seek time
                        var localPos = progressBar.GetLocalMousePosition();
                        // Still images held until stopped have no seekable timeline.
                        if (videoComponent.IsImage && playback.GetDuration() <= 0)
                        {
                            playback.IsSeeking = false;
                            return;
                        }
                        float percent = Mathf.Clamp(localPos.X / progressBar.Size.X, 0f, 1f);
                        double start = videoComponent.IsImage ? 0 : videoComponent.StartTime;
                        pendingSeekTimeSec = start + percent * playback.GetDuration();
                        progressBar.Value = percent * 100; // Preview
                        timeLabel.Text = UiUtilities.FormatTime(pendingSeekTimeSec); // Preview time
                        SyncHeadTimelineFromComponentSeek(pendingSeekTimeSec - start);
                    }
                    else
                    {
                        // Release: perform the seek
                        if (playback.IsSeeking)
                        {
                            double time = pendingSeekTimeSec;
                            double start = videoComponent.IsImage ? 0 : videoComponent.StartTime;
                            playback.Seek(time);
                            SyncHeadTimelineFromComponentSeek(time - start);
                            GD.Print($"ActiveCue:ProgressBar - Sought to {pendingSeekTimeSec} sec on release");
                        }
                        playback.IsSeeking = false;
                    }
                }
                else if (@event is InputEventMouseMotion && playback.IsSeeking)
                {
                    // Update preview during drag
                    var localPos = progressBar.GetLocalMousePosition();
                    float percent = Mathf.Clamp(localPos.X / progressBar.Size.X, 0f, 1f);
                    double start = videoComponent.IsImage ? 0 : videoComponent.StartTime;
                    pendingSeekTimeSec = start + percent * playback.GetDuration();
                    progressBar.Value = percent * 100; // Update preview
                    timeLabel.Text = UiUtilities.FormatTime(pendingSeekTimeSec); // Update preview time
                    SyncHeadTimelineFromComponentSeek(pendingSeekTimeSec - start);
                }
            };
            ulong videoPanelId = componentPanel.GetInstanceId();
            playback.TimeUpdated += time =>
            {
                // Capture id only — panel may be freed before deferred UI runs.
                double t = time;
                Callable.From(() => UpdateVideoUiByPanelId(t, videoPanelId)).CallDeferred();
            };
            
            _activeComponentCount++;
            WireVideoCompleted(playback, componentPanel);
        }
        catch (Exception ex)
        {
            GD.Print($"ActiveCue:SetupVideoComponent - Exception: {ex.Message}, Stack: {ex.StackTrace}, Target: {ex.TargetSite}, {ex.InnerException}, {ex.Source}");
            if (IsMissingFileException(ex, videoComponent.VideoFile))
                ReportMissingMedia(videoComponent.VideoFile);
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Error activating video component for cue {_cue.Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Subscribes to video completion without capturing a freeable PanelContainer into CallDeferred args.
    /// </summary>
    private void WireVideoCompleted(ActiveVideoPlayback playback, PanelContainer componentPanel)
    {
        ulong panelId = componentPanel.GetInstanceId();
        ActiveVideoPlayback.CompletedEventHandler handler = () =>
        {
            Callable.From(() => CompleteVideoByPanelId(panelId)).CallDeferred();
        };
        playback.Completed += handler;
        _videoCompleteHandlers.Add((playback, handler));
    }

    private void CompleteVideoByPanelId(ulong panelId)
    {
        if (_isCleaned || !IsInstanceValid(this)) return;

        PanelContainer panel = FindLivePanelKey(_activeVideoComponents.Keys, panelId);
        if (panel == null)
        {
            CheckForCueCompletion();
            return;
        }

        _activeVideoComponents.Remove(panel);
        _componentToVideo.Remove(panel);
        if (IsInstanceValid(panel))
            panel.QueueFree();

        // Closed-caption text holds until stopped; end it with the video so the cue can finish.
        StopSubtitleSlaveTextComponents();
        CheckForCueCompletion();
    }

    /// <summary>
    /// Stops text playbooks that are slaves of video closed captions.
    /// </summary>
    private void StopSubtitleSlaveTextComponents()
    {
        foreach (var kv in _activeTextComponents.ToList())
        {
            var playback = kv.Value;
            if (playback == null || !IsInstanceValid(playback) || !playback.IsSubtitleSlave)
                continue;
            try
            {
                _ = playback.Stop(0);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ActiveCue:StopSubtitleSlaveTextComponents - {ex.Message}");
            }
        }
    }

    private void UpdateVideoUiByPanelId(double time, ulong panelId)
    {
        if (_isCleaned || !IsInstanceValid(this)) return;
        PanelContainer panel = FindLivePanelKey(_activeVideoComponents.Keys, panelId);
        if (panel == null) return;
        UpdateVideoUi(time, panel);
    }

    /// <summary>
    /// Prepares a text overlay for presentation on the target layer.
    /// </summary>
    /// <param name="textComponent">Text component to set up.</param>
    private Task SetupTextComponent(TextComponent textComponent)
    {
        try
        {
            textComponent.RecalculateDuration();

            var playback = new ActiveTextPlayback(textComponent);
            // Must be in the scene tree so _Process can drive the hold timer.
            _activeCueBar.AddChild(playback);
            playback.Init();

            PanelContainer componentPanel = _componentProgressBarScene.Instantiate<PanelContainer>();
            _componentContainer.AddChild(componentPanel);
            componentPanel.GetNode<Label>("%ComponentLabel").Text = textComponent.GetDisplayLabel();
            var typeIcon = componentPanel.GetNode<Button>("%ComponentIcon");
            var pauseButton = componentPanel.GetNode<Button>("%ComponentPause");
            var stopButton = componentPanel.GetNode<Button>("%ComponentStop");
            var timeLabel = componentPanel.GetNode<Label>("%ComponentTime");

            if (textComponent.Duration <= 0)
                timeLabel.Text = "∞";
            else
                timeLabel.Text = UiUtilities.FormatTime(textComponent.TotalDuration);

            try
            {
                typeIcon.Icon = _activeCueBar.GetThemeIcon("Text", "AtlasIcons");
            }
            catch
            {
                try
                {
                    typeIcon.Icon = _activeCueBar.GetThemeIcon("Label", "AtlasIcons");
                }
                catch
                {
                    // Icon optional.
                }
            }

            pauseButton.Icon = _activeCueBar.GetThemeIcon(_isPaused ? "Play" : "Pause", "AtlasIcons");
            stopButton.Icon = _activeCueBar.GetThemeIcon("Stop", "AtlasIcons");

            _activeTextComponents.Add(componentPanel, playback);
            _componentToText.Add(componentPanel, textComponent);

            pauseButton.Pressed += () =>
            {
                if (_isCleaned || !IsInstanceValid(componentPanel)) return;
                if (!playback.IsPaused)
                    playback.Pause();
                else
                    playback.Resume();

                if (IsInstanceValid(pauseButton) && IsInstanceValid(_activeCueBar))
                    pauseButton.Icon = _activeCueBar.GetThemeIcon(playback.IsPaused ? "Play" : "Pause", "AtlasIcons");
            };

            stopButton.Pressed += async () => await StopTextComponent(componentPanel);

            ulong textPanelId = componentPanel.GetInstanceId();
            playback.TimeUpdated += time =>
            {
                double t = time;
                Callable.From(() => UpdateTextUiByPanelId(t, textPanelId)).CallDeferred();
            };

            _activeComponentCount++;
            WireTextCompleted(playback, componentPanel);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveCue:SetupTextComponent - Exception: {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Error activating text component for cue {_cue.Name}: {ex.Message}", 2);
        }

        return Task.CompletedTask;
    }

    private async Task TriggerTextComponent(TextComponent textComp)
    {
        PanelContainer panel = null;
        try
        {
            panel = _componentToText.FirstOrDefault(kv => kv.Value == textComp).Key;
            if (panel == null)
            {
                GD.PrintErr($"ActiveCue:TriggerTextComponent - No playback found for text on {_cue.Name}");
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                    $"No playback for text component in cue {_cue.Name}", 2);
                return;
            }

            if (!_activeTextComponents.TryGetValue(panel, out var playback) || playback == null)
                return;

            double fadeIn = _controlFadeInDuration ?? textComp.FadeInDuration;
            if (fadeIn > 1e-9)
                await playback.FadeInAsync(fadeIn);
            else
                await playback.PlayAsync();

            if (_isPaused)
                playback.Pause();
            SyncPauseTransportUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveCue:TriggerTextComponent - Error: {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Trigger failed for text in {_cue.Name}: {ex.Message}", 2);
            if (panel != null && _activeTextComponents.TryGetValue(panel, out var failedPlayback))
            {
                try { _ = failedPlayback.Stop(0); } catch { /* best-effort */ }
            }
        }
    }

    private async Task StopTextComponent(PanelContainer componentPanel)
    {
        if (_isCleaned || componentPanel == null || !IsInstanceValid(componentPanel))
            return;
        if (!_activeTextComponents.TryGetValue(componentPanel, out var playback) || playback == null)
            return;

        await playback.Stop(_settings.StopFadeDuration);
    }

    private void WireTextCompleted(ActiveTextPlayback playback, PanelContainer componentPanel)
    {
        ulong panelId = componentPanel.GetInstanceId();
        ActiveTextPlayback.CompletedEventHandler handler = () =>
        {
            Callable.From(() => CompleteTextByPanelId(panelId)).CallDeferred();
        };
        playback.Completed += handler;
        _textCompleteHandlers.Add((playback, handler));
    }

    private void CompleteTextByPanelId(ulong panelId)
    {
        if (_isCleaned || !IsInstanceValid(this)) return;

        PanelContainer panel = FindLivePanelKey(_activeTextComponents.Keys, panelId);
        if (panel == null)
        {
            CheckForCueCompletion();
            return;
        }

        _activeTextComponents.Remove(panel);
        _componentToText.Remove(panel);
        if (IsInstanceValid(panel))
            panel.QueueFree();
        CheckForCueCompletion();
    }

    private void UpdateTextUiByPanelId(double time, ulong panelId)
    {
        if (_isCleaned || !IsInstanceValid(this)) return;
        PanelContainer panel = FindLivePanelKey(_activeTextComponents.Keys, panelId);
        if (panel == null) return;
        UpdateTextUi(time, panel);
    }

    private void UpdateTextUi(double time, PanelContainer componentPanel)
    {
        if (_isCleaned || !IsInstanceValid(this)) return;
        if (!IsInstanceValid(componentPanel)) return;
        if (!_activeTextComponents.TryGetValue(componentPanel, out var textPlayback) || textPlayback == null)
            return;
        if (!_componentToText.TryGetValue(componentPanel, out var textComponent) || textComponent == null)
            return;

        var progressBar = componentPanel.GetNodeOrNull<ProgressBar>("ComponentProgress");
        if (progressBar == null) return;
        if (textPlayback.IsStopped) return;

        UpdateComponentFadeProgress(
            componentPanel,
            textPlayback.IsFadingIn,
            textPlayback.IsFadingOut,
            textPlayback.CurrentFadeLevel);

        if (textPlayback.IsPaused && !textPlayback.IsFadingOut)
            return;

        double span = textPlayback.GetDuration();
        float progressPercentage;
        if (span <= 0)
            progressPercentage = 0f;
        else
            progressPercentage = (float)(time / span * 100.0);

        var timeLabel = componentPanel.GetNodeOrNull<Label>("ComponentProgress/MarginContainer/HBoxContainer/ComponentTime");
        if (timeLabel != null)
        {
            if (span <= 0)
                timeLabel.Text = "∞";
            else
                timeLabel.Text = UiUtilities.FormatTime(time);
        }

        progressBar.Value = progressPercentage;
    }

    private static PanelContainer FindLivePanelKey(IEnumerable<PanelContainer> keys, ulong panelId)
    {
        foreach (var key in keys)
        {
            if (key != null && IsInstanceValid(key) && key.GetInstanceId() == panelId)
                return key;
        }
        return null;
    }


    private Task SetupCueLightComponent(CueLightComponent cueLightComponent)
    {
        try
        {
            PanelContainer componentPanel = _componentProgressBarScene.Instantiate<PanelContainer>();
            _componentContainer.AddChild(componentPanel);
            var labelText = $"{cueLightComponent.CueLight.Name} : {cueLightComponent.Action.ToString()}";
            componentPanel.GetNode<Label>("%ComponentLabel").Text = labelText;
            var typeIcon = componentPanel.GetNode<Button>("%ComponentIcon");
            componentPanel.GetNode<Button>("%ComponentPause").QueueFree(); // No pause implemented
            var stopButton = componentPanel.GetNode<Button>("%ComponentStop");
            var timeLabel = componentPanel.GetNode<Label>("%ComponentTime");
            timeLabel.Text = UiUtilities.FormatTime(cueLightComponent.CountInTime);
            
            typeIcon.Icon = _activeCueBar.GetThemeIcon("Connection", "AtlasIcons");
            stopButton.Icon = _activeCueBar.GetThemeIcon("Stop", "AtlasIcons");
            
            _activeCueLightComponents.Add(componentPanel, cueLightComponent);
            
            _activeComponentCount++;

            stopButton.Pressed += () => HandleCueLightComponentCompleted(componentPanel);
        }
        catch (Exception ex)
        {
            GD.Print($"ActiveCue:SetupCueLightComponent - Exception: {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Error setting up cuelight component for cue {_cue.Name}: {ex.Message}", 2);
        }

        return Task.CompletedTask;
    }
    
    private Task SetupOscComponent(OscComponent oscComponent)
    {
        try
        {
            PanelContainer componentPanel = _componentProgressBarScene.Instantiate<PanelContainer>();
            _componentContainer.AddChild(componentPanel);
            var labelText = $"{oscComponent.OscConnection.Name} : {oscComponent.OscMessage}";
            componentPanel.GetNode<Label>("%ComponentLabel").Text = labelText;
            var typeIcon = componentPanel.GetNode<Button>("%ComponentIcon");
            componentPanel.GetNode<Button>("%ComponentPause").QueueFree(); // No pause implemented
            var stopButton = componentPanel.GetNode<Button>("%ComponentStop");
            componentPanel.GetNode<Label>("%ComponentTime").QueueFree();
            typeIcon.Icon = _activeCueBar.GetThemeIcon("Connection", "AtlasIcons");
            stopButton.Icon = _activeCueBar.GetThemeIcon("Stop", "AtlasIcons");
            
            _activeOscComponents.Add(componentPanel, oscComponent);
            
            _activeComponentCount++;

            stopButton.Pressed += () => HandleOscComponentCompleted(componentPanel);
        }
        catch (Exception ex)
        {
            GD.Print($"ActiveCue:SetupOscComponent - Exception: {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Error setting up osc component for cue {_cue.Name}: {ex.Message}", 2);
        }

        return Task.CompletedTask;
    }

    private Task SetupMidiOutputComponent(MidiOutputComponent midiComponent)
    {
        try
        {
            PanelContainer componentPanel = _componentProgressBarScene.Instantiate<PanelContainer>();
            _componentContainer.AddChild(componentPanel);
            componentPanel.GetNode<Label>("%ComponentLabel").Text = midiComponent.GetDisplaySummary();
            var typeIcon = componentPanel.GetNode<Button>("%ComponentIcon");
            componentPanel.GetNode<Button>("%ComponentPause").QueueFree();
            var stopButton = componentPanel.GetNode<Button>("%ComponentStop");
            componentPanel.GetNode<Label>("%ComponentTime").QueueFree();
            try
            {
                typeIcon.Icon = _activeCueBar.GetThemeIcon("Connection", "AtlasIcons");
            }
            catch { /* optional */ }
            stopButton.Icon = _activeCueBar.GetThemeIcon("Stop", "AtlasIcons");

            _activeMidiOutputComponents.Add(componentPanel, midiComponent);
            _activeComponentCount++;
            stopButton.Pressed += () => HandleMidiOutputComponentCompleted(componentPanel);
        }
        catch (Exception ex)
        {
            GD.Print($"ActiveCue:SetupMidiOutputComponent - Exception: {ex.Message}");
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"Error setting up MIDI output for cue {_cue.Name}: {ex.Message}", 2);
        }

        return Task.CompletedTask;
    }

    private Task SetupControlComponent(ControlComponent controlComponent)
    {
        try
        {
            PanelContainer componentPanel = _componentProgressBarScene.Instantiate<PanelContainer>();
            _componentContainer.AddChild(componentPanel);

            controlComponent.ResolveTargetIfNeeded();
            string targetLabel = controlComponent.TargetCueId >= 0
                ? $"#{controlComponent.TargetCueNum} (id {controlComponent.TargetCueId})"
                : "(no target)";
            componentPanel.GetNode<Label>("%ComponentLabel").Text =
                $"{ControlComponent.GetActionDisplayName(controlComponent.Action)} → {targetLabel}";

            var typeIcon = componentPanel.GetNode<Button>("%ComponentIcon");
            componentPanel.GetNode<Button>("%ComponentPause").QueueFree();
            var stopButton = componentPanel.GetNode<Button>("%ComponentStop");
            componentPanel.GetNode<Label>("%ComponentTime").QueueFree();

            string iconName = controlComponent.Action switch
            {
                ControlAction.Go => "Play",
                ControlAction.Pause => "Pause",
                ControlAction.Stop => "Stop",
                ControlAction.Resume => "Play",
                ControlAction.StartNow => "Skip",
                _ => "Play"
            };
            typeIcon.Icon = _activeCueBar.GetThemeIcon(iconName, "AtlasIcons");
            stopButton.Icon = _activeCueBar.GetThemeIcon("Stop", "AtlasIcons");

            _activeControlComponents.Add(componentPanel, controlComponent);
            _activeComponentCount++;

            stopButton.Pressed += () => HandleControlComponentCompleted(componentPanel);
        }
        catch (Exception ex)
        {
            GD.Print($"ActiveCue:SetupControlComponent - Exception: {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Error setting up control component for cue {_cue.Name}: {ex.Message}", 2);
        }

        return Task.CompletedTask;
    }

    private async Task StopComponent(PanelContainer componentPanel)
    {
        if (!_activeAudioComponents.TryGetValue(componentPanel, out var playback))
            return;
        await playback.Stop(_settings.StopFadeDuration);
    }

    private async Task StopVideoComponent(PanelContainer componentPanel)
    {
        if (!_activeVideoComponents.TryGetValue(componentPanel, out var playback))
            return;
        StopSubtitleSlaveTextComponents();
        await playback.Stop(_settings.StopFadeDuration);
    }
    
    private void UpdateComponentUiState(PanelContainer componentPanel, AudioComponent audioComponent)
    {
        if (!IsInstanceValid(componentPanel) || audioComponent == null) return;
        if (!_activeAudioComponents.TryGetValue(componentPanel, out var audioPlayback) || audioPlayback == null)
            return;

        var progressBar = componentPanel.GetNodeOrNull<ProgressBar>("ComponentProgress");
        if (progressBar == null) return;
        if (audioPlayback.IsStopped) return;

        // Fade overlay updates even while paused (e.g. mid fade-in/out).
        UpdateComponentFadeProgress(componentPanel, audioPlayback.IsFadingIn, audioPlayback.IsFadingOut,
            audioPlayback.CurrentVolume);

        if (audioPlayback.IsPaused) return;

        //GD.Print($"ActiveCue:UpdateComponentUiState - Current time ms: {audioPlayback.Decoder.CurrentTime}");
        float trackTime = audioPlayback.GetPlaybackTimeMs() / 1000f; // ms to seconds
        float progressPercentage = ((trackTime - (float)audioComponent.StartTime) / (float)audioComponent.Duration) * 100f;
        var timeLabel = componentPanel.GetNode<Label>("ComponentProgress/MarginContainer/HBoxContainer/ComponentTime");
        if (!audioPlayback.IsSeeking)
        {
            timeLabel.Text = UiUtilities.FormatTime(trackTime);
            progressBar.Value = progressPercentage;
        }
    }



    private void UpdateVideoUi(double time, PanelContainer componentPanel)
    {
        if (_isCleaned || !IsInstanceValid(this)) return;
        if (!IsInstanceValid(componentPanel)) return;
        if (!_activeVideoComponents.TryGetValue(componentPanel, out var videoPlayback) || videoPlayback == null)
            return;
        if (!_componentToVideo.TryGetValue(componentPanel, out var videoComponent) || videoComponent == null)
            return;

        var progressBar = componentPanel.GetNodeOrNull<ProgressBar>("ComponentProgress");
        if (progressBar == null) return;
        if (videoPlayback.IsStopped) return;

        UpdateComponentFadeProgress(componentPanel, videoPlayback.IsFadingIn, videoPlayback.IsFadingOut,
            videoPlayback.CurrentVolume);

        if (videoPlayback.IsPaused) return;

        float trackTime = (float)time;
        double span = videoPlayback.GetDuration();
        float progressPercentage;
        if (videoComponent.IsImage && span <= 0)
        {
            // Image held until stopped — no finite progress span.
            progressPercentage = 0f;
        }
        else
        {
            double start = videoComponent.IsImage ? 0 : videoComponent.StartTime;
            progressPercentage = span > 0
                ? (float)((trackTime - start) / span * 100.0)
                : 0f;
        }
        var timeLabel = componentPanel.GetNode<Label>("ComponentProgress/MarginContainer/HBoxContainer/ComponentTime");
        if (!videoPlayback.IsSeeking)
        {
            timeLabel.Text = UiUtilities.FormatTime(trackTime);
            progressBar.Value = progressPercentage;
        }
    }

    /// <summary>
    /// Shows/hides the vertical component fade overlay and sets its fill from current volume.
    /// </summary>
    /// <param name="componentPanel">Component progress row.</param>
    /// <param name="fadingIn">True while fade-in is active.</param>
    /// <param name="fadingOut">True while fade-out is active.</param>
    /// <param name="currentVolume">Playback volume in [0, 1].</param>
    private static void UpdateComponentFadeProgress(
        PanelContainer componentPanel,
        bool fadingIn,
        bool fadingOut,
        float currentVolume)
    {
        if (componentPanel == null || !IsInstanceValid(componentPanel)) return;

        var fadeProgress = componentPanel.GetNodeOrNull<ProgressBar>("%ComponentFadeProgress");
        if (fadeProgress == null) return;

        if (fadingIn || fadingOut)
        {
            fadeProgress.Visible = true;
            // Remaining "unfaded" cover: full at silent, empty at full volume.
            fadeProgress.Value = (1f - Mathf.Clamp(currentVolume, 0f, 1f)) * 100f;
        }
        else
        {
            fadeProgress.Visible = false;
        }
    }
    
    
    private void TogglePauseAll()
    {
        if (_isPaused)
        {
            ResumeAll();
        }
        else
        {
            PauseAll();
        }
    }

    private void ResumeAll(bool propagateToChildren = true)
    {
        lock (_lock)
        {
            // _isPlaying may be false during edge cases; still allow resume when paused.
            if (!_isPaused) return;
            _isPaused = false;
            _isPlaying = true;
        }

        ResumeTimelineClock();
        if (_inPreWait)
            PreWaitResume();

        if (propagateToChildren)
        {
            foreach (var child in _childActiveCues.ToList())
                child.ResumeAll(true);
        }

        // Only resume media while content playback is active (not parked for pre-wait rewind).
        if (_contentPlaybackActive)
        {
            foreach (var playback in _activeAudioComponents.Values)
                playback.Resume();

            foreach (var playback in _activeVideoComponents.Values)
                playback.Resume();

            foreach (var playback in _activeTextComponents.Values)
                playback.Resume();
        }

        if (_updateTimer != null && IsInstanceValid(_updateTimer))
            _updateTimer.Start();

        // Keep head + component + nested transport icons in sync with playback state.
        SyncPauseTransportUi();
    }

    private void PauseAll(bool propagateToChildren = true)
    {
        _isPaused = true;
        PauseTimelineClock();
        if (_inPreWait)
            PreWaitPause();

        if (propagateToChildren)
        {
            foreach (var child in _childActiveCues.ToList())
                child.PauseAll(true);
        }

        foreach (var playback in _activeAudioComponents.Values)
            playback.Pause();

        foreach (var playback in _activeVideoComponents.Values)
            playback.Pause();

        foreach (var playback in _activeTextComponents.Values)
            playback.Pause();

        SyncPauseTransportUi();
    }

    /// <summary>
    /// Sets head and component pause/play icons from actual pause state.
    /// Call after any pause/resume path (per-cue, global, nested).
    /// </summary>
    private void SyncPauseTransportUi()
    {
        if (_isCleaned || _activeCueBar == null || !IsInstanceValid(_activeCueBar))
            return;

        // Head: Play icon means "currently paused / press to resume".
        if (_headPause != null && IsInstanceValid(_headPause))
        {
            _headPause.Icon = _activeCueBar.GetThemeIcon(
                _isPaused ? "Play" : "Pause", "AtlasIcons");
        }

        SyncComponentPauseIcons(_activeAudioComponents, p => p != null && p.IsPaused);
        SyncComponentPauseIcons(_activeVideoComponents, p => p != null && p.IsPaused);
        SyncComponentPauseIcons(_activeTextComponents, p => p != null && p.IsPaused);
    }

    private static void SyncComponentPauseIcons<T>(
        Dictionary<PanelContainer, T> map,
        Func<T, bool> isPaused)
    {
        foreach (var kv in map)
        {
            if (!IsInstanceValid(kv.Key) || kv.Value == null) continue;
            var btn = kv.Key.GetNodeOrNull<Button>("%ComponentPause");
            if (btn == null) continue;
            // Component may be paused on its own, or via cue-level pause (IsPaused true on playback).
            btn.Icon = kv.Key.GetThemeIcon(isPaused(kv.Value) ? "Play" : "Pause", "AtlasIcons");
        }
    }
    

    /// <summary>
    /// Stops all playback for this cue.
    /// First call: fade-out using <paramref name="fadeDurationOverride"/> or
    /// <see cref="Settings.StopFadeDuration"/> (0 = immediate).
    /// Second call while still fading: hard-stop immediately.
    /// </summary>
    /// <param name="propagateToChildren">Whether to stop child cues as well.</param>
    /// <param name="fadeDurationOverride">
    /// Optional fade seconds for this stop (e.g. from a control component).
    /// When null, uses the session stop-fade setting. When 0, stops immediately.
    /// </param>
    public async void StopAll(bool propagateToChildren = true, double? fadeDurationOverride = null)
    {
        bool hardStop;
        lock (_lock)
        {
            if (_isCleaned) return;

            // User/panic stop — do not arm continue/follow; cancel unstarted chain peers.
            _suppressContentCompleted = true;
            NextInChain?.CancelPendingFromPredecessor();

            // Waiting / paused / not yet playing content — tear down immediately.
            // Still stop nested children first so they do not outlive the parent bar.
            if (!_contentStarted || _inIncomingWait || _inPreWait || _isPaused)
            {
                if (propagateToChildren)
                    StopChildCuesImmediate(fadeDurationOverride ?? 0.0);
                Cleanup();
                return;
            }

            // Second Stop while a fade is in progress → hard stop.
            hardStop = _isStopFading;
            _isStopFading = true;
        }

        // Stop child cues if propagating (same fade override).
        // Nested bars live under this cue's UI — do not free parent until children finish.
        if (propagateToChildren)
        {
            foreach (var child in _childActiveCues.ToList())
            {
                child.StopAll(true, fadeDurationOverride);
            }
        }

        // Override or session fade; second press or zero duration forces immediate stop.
        double baseFade = fadeDurationOverride ?? _settings.StopFadeDuration;
        double fadeDuration = hardStop ? 0.0 : Math.Max(0.0, baseFade);

        var tasks = new List<Task>();
        foreach (var audioComp in _activeAudioComponents.Values.ToList())
        {
            tasks.Add(audioComp.Stop(fadeDuration));
        }
        foreach (var videoComp in _activeVideoComponents.Values.ToList())
        {
            tasks.Add(videoComp.Stop(fadeDuration));
        }
        foreach (var textComp in _activeTextComponents.Values.ToList())
        {
            tasks.Add(textComp.Stop(fadeDuration));
        }

        // Instant components (OSC / MIDI / cue light / control) have no async stop — clear them now.
        foreach (var panel in _activeOscComponents.Keys.ToList())
        {
            HandleOscComponentCompleted(panel);
        }
        foreach (var panel in _activeMidiOutputComponents.Keys.ToList())
        {
            HandleMidiOutputComponentCompleted(panel);
        }
        foreach (var panel in _activeCueLightComponents.Keys.ToList())
        {
            HandleCueLightComponentCompleted(panel);
        }
        foreach (var panel in _activeControlComponents.Keys.ToList())
        {
            HandleControlComponentCompleted(panel);
        }

        // Group with no own media (or only instant comps): keep parent bar until children finish fade.
        if (tasks.Count == 0)
        {
            TryCleanupAfterStop();
            return;
        }

        await Task.WhenAll(tasks);
        _isPlaying = false;

        // Own media finished stop-fade; still wait for nested children before freeing UI.
        if (!_isCleaned)
            TryCleanupAfterStop();
    }

    /// <summary>
    /// Hard-stops nested children without awaiting (used when parent tears down immediately).
    /// </summary>
    private void StopChildCuesImmediate(double fadeDuration)
    {
        foreach (var child in _childActiveCues.ToList())
        {
            if (child == null || !IsInstanceValid(child)) continue;
            try
            {
                child.StopAll(true, fadeDuration);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ActiveCue:StopChildCuesImmediate - {_cue?.Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// After a stop, free this cue only when own media is gone and nested children have finished.
    /// Parent bars own the child UI tree — freeing early aborts child stop-fades.
    /// </summary>
    private void TryCleanupAfterStop()
    {
        if (_isCleaned) return;

        bool hasOwnMedia =
            _activeAudioComponents.Count > 0 ||
            _activeVideoComponents.Count > 0 ||
            _activeTextComponents.Count > 0;

        if (hasOwnMedia)
            return;

        if (_childActiveCues.Count > 0)
        {
            // Children still fading/playing — OnChildCompleted will call back when the last one ends.
            _isFinished = true;
            _isPlaying = false;
            GD.Print(
                $"ActiveCue:TryCleanupAfterStop - {_cue?.Name}: waiting for {_childActiveCues.Count} child cue(s) to finish stop");
            return;
        }

        Cleanup();
    }

    private void GlobalStopAll()
    {
        StopAll(false); // Don't propagate, as all cues receive the global signal
    }
    
    private void GlobalPauseAll()
    {
        // Every ActiveCue (including nested group children) receives this signal itself —
        // do not rely on parent propagation for globals.
        if (_inIncomingWait && _incomingWaitTimer != null && IsInstanceValid(_incomingWaitTimer))
        {
            _incomingWaitTimer.SetPaused(true);
            if (_sequencePause != null && _activeCueBar != null)
                _sequencePause.Icon = _activeCueBar.GetThemeIcon("Play", "AtlasIcons");
        }

        if (_inPreWait)
            PreWaitPause();

        // Marks _isPaused so components that start later also come up paused, and syncs icons.
        PauseAll(propagateToChildren: false);
    }

    private void GlobalResumeAll()
    {
        if (_inIncomingWait && _incomingWaitTimer != null && IsInstanceValid(_incomingWaitTimer))
        {
            _incomingWaitTimer.SetPaused(false);
            if (_sequencePause != null && _activeCueBar != null)
                _sequencePause.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
        }

        if (_inPreWait)
            PreWaitResume();

        ResumeAll(propagateToChildren: false);
    }
    
    
    
    private void HandleOscComponentCompleted(PanelContainer componentPanel)
    {
        RemoveInstantComponent(componentPanel, _activeOscComponents);
    }

    private void HandleCueLightComponentCompleted(PanelContainer componentPanel)
    {
        RemoveInstantComponent(componentPanel, _activeCueLightComponents);
    }

    private void HandleControlComponentCompleted(PanelContainer componentPanel)
    {
        RemoveInstantComponent(componentPanel, _activeControlComponents);
    }

    private void HandleMidiOutputComponentCompleted(PanelContainer componentPanel)
    {
        RemoveInstantComponent(componentPanel, _activeMidiOutputComponents);
    }

    /// <summary>
    /// Removes a short-lived component row (OSC / MIDI / cue light / control) and checks cue completion.
    /// </summary>
    private void RemoveInstantComponent<T>(PanelContainer componentPanel, Dictionary<PanelContainer, T> map)
    {
        if (!IsInstanceValid(this) || _isCleaned) return;
        if (componentPanel == null || !IsInstanceValid(componentPanel) || !map.ContainsKey(componentPanel))
            return;

        map.Remove(componentPanel);
        componentPanel.QueueFree();
        CheckForCueCompletion();
    }

    private void CheckForCueCompletion()
    {
        if (_isCleaned) return;

        if (_activeAudioComponents.Count == 0 
            && _activeVideoComponents.Count == 0
            && _activeTextComponents.Count == 0
            && _activeOscComponents.Count == 0
            && _activeMidiOutputComponents.Count == 0
            && _activeCueLightComponents.Count == 0
            && _activeControlComponents.Count == 0)
        {
            _isFinished = true;
            if (_childActiveCues.Count == 0)
            {
                // Defer finish so we leave Godot's call lock before Cleanup / Free.
                Callable.From(HandleNaturalContentFinished).CallDeferred();
            }
        }
    }

    private void OnChildCompleted(ActiveCue child)
    {
        int childId = -1;
        try { childId = child?.Cue?.Id ?? -1; } catch { /* disposed */ }

        _childActiveCues.Remove(child);

        // Permanent finish for this activation (not a scrub-rewind teardown).
        if (!_isRewindingContent && childId >= 0)
            _finishedChildCueIds.Add(childId);

        if (_childActiveCues.Count > 0 || _isCleaned || _isRewindingContent)
            return;

        // Stop/panic path: parent may have been waiting for child stop-fades before freeing UI.
        if (_suppressContentCompleted || _isStopFading)
        {
            Callable.From(TryCleanupAfterStop).CallDeferred();
            return;
        }

        // Natural completion: parent content is done when components finished and last child ends.
        if (_isFinished)
            Callable.From(HandleNaturalContentFinished).CallDeferred();
    }

    /// <summary>
    /// Reports natural content completion once. Ignored after stop/panic or for looping media.
    /// </summary>
    private void RaiseContentCompleted()
    {
        if (_contentCompletedRaised || _suppressContentCompleted || _isCleaned)
            return;
        // Looping media never truly completes for sequence purposes.
        if (_cue != null && _cue.Duration < 0)
            return;

        _contentCompletedRaised = true;
        try
        {
            ContentCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveCue:RaiseContentCompleted - {_cue?.Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Queues <see cref="Cleanup"/> for the next idle frame so Free is never attempted
    /// while this object is still locked by a Godot method/signal dispatch.
    /// </summary>
    private void ScheduleCleanup()
    {
        if (_isCleaned || !IsInstanceValid(this)) return;
        // Pending chain members stay until armed / cancelled.
        if (_chainMember != null && !_contentStarted && !_suppressContentCompleted)
            return;
        Callable.From(Cleanup).CallDeferred();
    }

    /// <summary>
    /// Frees this <see cref="GodotObject"/> after the call stack has unwound.
    /// Prefer this over <c>CallDeferred(MethodName.Free)</c>: C# Object subclasses often
    /// report "Nonexistent function 'free'" for the engine free via CallDeferred.
    /// </summary>
    private void FreeDeferred()
    {
        if (IsInstanceValid(this))
            Free();
    }

    /// <summary>
    /// Cleans up resources and removes the cue from the UI.
    /// </summary>
    public void Cleanup()
    {
        lock (_lock) // Add lock for thread safety
        {
            if (_isCleaned)
            {
                GD.Print("ActiveCue:Cleanup - Already cleaned");
                return;
            }

            _isCleaned = true;
            _isPlaying = false;
        }

        // Disconnect completion handlers BEFORE Clean/Stop so they cannot touch freed panels.
        DisconnectPlaybackCompletionHandlers();

        // Hard-stop any remaining playbooks so decoders/fill loops do not leak.
        foreach (var playback in _activeAudioComponents.Values.ToList())
        {
            try { playback.Clean(); } catch (Exception ex)
            {
                GD.PrintErr($"ActiveCue:Cleanup - Audio clean failed: {ex.Message}");
            }
        }
        foreach (var playback in _activeVideoComponents.Values.ToList())
        {
            try { _ = playback.Stop(0); } catch (Exception ex)
            {
                GD.PrintErr($"ActiveCue:Cleanup - Video stop failed: {ex.Message}");
            }
        }
        foreach (var playback in _activeTextComponents.Values.ToList())
        {
            try { _ = playback.Stop(0); } catch (Exception ex)
            {
                GD.PrintErr($"ActiveCue:Cleanup - Text stop failed: {ex.Message}");
            }
        }
        _activeAudioComponents.Clear();
        _componentToAudio.Clear();
        _activeVideoComponents.Clear();
        _componentToVideo.Clear();
        _activeTextComponents.Clear();
        _componentToText.Clear();
        _activeOscComponents.Clear();
        _activeMidiOutputComponents.Clear();
        _activeCueLightComponents.Clear();
        _activeControlComponents.Clear();

        HookIncomingWaitUpdate(false);
        FreeIncomingWaitTimer();

        if (_updateTimer != null && IsInstanceValid(_updateTimer))
        {
            _updateTimer.Stop();
            _updateTimer.Timeout -= UpdateUi;
            HookPreWaitUpdate(false);
            _updateTimer.QueueFree();
            _updateTimer = null;
        }
        if (_preWaitTimer != null && IsInstanceValid(_preWaitTimer))
        {
            _preWaitTimer.QueueFree();
            _preWaitTimer = null;
        }
        if (_fadeTimer != null && IsInstanceValid(_fadeTimer))
        {
            _fadeTimer.QueueFree();
            _fadeTimer = null;
        }

        // Propagate cancel to unstarted followers if we never completed naturally.
        if (_suppressContentCompleted)
            NextInChain?.CancelPendingFromPredecessor();

        if (_globalSignals != null)
        {
            _globalSignals.StopAll -= GlobalStopAll;
            _globalSignals.PauseAll -= GlobalPauseAll;
            _globalSignals.ResumeAll -= GlobalResumeAll;
        }

        // Nested active cues own UI under this bar — clean them before freeing the parent tree.
        // Prefer waiting for stop-fades via TryCleanupAfterStop; this is the hard teardown path.
        foreach (var child in _childActiveCues.ToList())
        {
            if (child == null || !IsInstanceValid(child)) continue;
            try
            {
                child.Cleanup();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ActiveCue:Cleanup - Child cleanup failed on {_cue?.Name}: {ex.Message}");
            }
        }
        _childActiveCues.Clear();

        // Do not manually disconnect child-bar button handlers (PreWaitPause etc.): rebinding
        // for the scheduled path makes -= of the original handler throw "nonexistent connection".
        // QueueFree of the active bar tears down those signals with the nodes.

        if (_activeCueBar != null && IsInstanceValid(_activeCueBar))
        {
            _activeCueBar.QueueFree();
        }
        
        try
        {
            if (IsInstanceValid(this))
                EmitSignal(SignalName.Completed);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveCue:Cleanup - Completed signal: {ex.Message}");
        }

        GD.Print($"ActiveCue:Cleanup - Cleaned up active cue: {_cue?.Name}");

        // Free must not run while this GodotObject is locked (signal/method dispatch).
        // Callable.From invokes the C# Free() after the idle frame — reliable for GodotObject.
        if (IsInstanceValid(this))
            Callable.From(FreeDeferred).CallDeferred();
    }

    /// <summary>
    /// Detaches Completed handlers so Clean/Stop cannot re-enter UI after free.
    /// </summary>
    private void DisconnectPlaybackCompletionHandlers()
    {
        foreach (var (playback, handler) in _audioCompleteHandlers)
        {
            try
            {
                if (playback != null && IsInstanceValid(playback))
                    playback.Completed -= handler;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ActiveCue:DisconnectPlaybackCompletionHandlers - Audio: {ex.Message}");
            }
        }
        _audioCompleteHandlers.Clear();

        foreach (var (playback, handler) in _videoCompleteHandlers)
        {
            try
            {
                if (playback != null && IsInstanceValid(playback))
                    playback.Completed -= handler;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ActiveCue:DisconnectPlaybackCompletionHandlers - Video: {ex.Message}");
            }
        }
        _videoCompleteHandlers.Clear();

        foreach (var (playback, handler) in _textCompleteHandlers)
        {
            try
            {
                if (playback != null && IsInstanceValid(playback))
                    playback.Completed -= handler;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ActiveCue:DisconnectPlaybackCompletionHandlers - Text: {ex.Message}");
            }
        }
        _textCompleteHandlers.Clear();
    }

    /// <summary>
    /// True when the stored media path resolves to an existing file on disk.
    /// </summary>
    private bool MediaFileAvailable(string storedPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(storedPath))
                return false;
            var globalData = Engine.GetMainLoop() is SceneTree tree
                ? tree.Root.GetNodeOrNull<GlobalData>("/root/GlobalData")
                : null;
            return MediaPaths.Exists(storedPath, globalData?.SessionDir);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reports a missing media file for this cue via <see cref="MediaHealthService"/> (shell ✕ + inspector styling).
    /// </summary>
    private void ReportMissingMedia(string storedUrl)
    {
        try
        {
            var health = Engine.GetMainLoop() is SceneTree tree
                ? tree.Root.GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")
                : null;
            health?.ReportFileMissing(_cue.Id, storedUrl);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveCue:ReportMissingMedia - {ex.Message}");
        }
    }

    /// <summary>
    /// Heuristic: decoder/open exceptions that indicate a missing or unreadable file.
    /// </summary>
    private static bool IsMissingFileException(Exception ex, string storedPath)
    {
        if (ex == null)
            return false;
        string msg = ex.Message ?? string.Empty;
        if (msg.Contains("No such file", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("open_input failed", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("File not found", StringComparison.OrdinalIgnoreCase))
            return true;

        // Also treat as missing if path clearly does not exist
        try
        {
            if (!string.IsNullOrEmpty(storedPath) &&
                Engine.GetMainLoop() is SceneTree tree)
            {
                var globalData = tree.Root.GetNodeOrNull<GlobalData>("/root/GlobalData");
                string resolved = globalData?.ResolveMediaPath(storedPath) ?? storedPath;
                if (!File.Exists(resolved))
                    return true;
            }
        }
        catch { /* ignore */ }

        return false;
    }
}