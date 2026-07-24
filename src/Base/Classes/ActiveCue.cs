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
    private readonly Dictionary<PanelContainer, CueLightComponent> _activeCueLightComponents = new();
    private readonly Dictionary<PanelContainer, OscComponent> _activeOscComponents = new();
    private readonly Dictionary<PanelContainer, MidiOutputComponent> _activeMidiOutputComponents = new();
    private readonly Dictionary<PanelContainer, ControlComponent> _activeControlComponents = new();

    /// <summary>Keeps handler refs so we can disconnect before freeing UI (avoids disposed-panel callbacks).</summary>
    private readonly List<(ActiveAudioPlayback Playback, ActiveAudioPlayback.CompletedEventHandler Handler)> _audioCompleteHandlers = new();
    private readonly List<(ActiveVideoPlayback Playback, ActiveVideoPlayback.CompletedEventHandler Handler)> _videoCompleteHandlers = new();
    
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
    /// Seeks all active audio/video components on this cue.
    /// </summary>
    /// <param name="timeSeconds">Absolute media time, or relative offset when <paramref name="relative"/> is true.</param>
    /// <param name="relative">When true, offset from each component's current playback position.</param>
    public void RequestSeek(double timeSeconds, bool relative)
    {
        if (_isCleaned) return;

        foreach (var playback in _activeAudioComponents.Values.ToList())
        {
            if (playback == null) continue;
            try
            {
                double current = playback.GetPlaybackTimeMs() / 1000.0;
                double target = relative ? current + timeSeconds : timeSeconds;
                if (target < 0) target = 0;
                playback.Seek((long)(target * 1_000_000.0));
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ActiveCue:RequestSeek - Audio seek failed on {_cue?.Name}: {ex.Message}");
            }
        }

        foreach (var playback in _activeVideoComponents.Values.ToList())
        {
            if (playback == null) continue;
            try
            {
                double current = playback.GetPlaybackTimeSeconds();
                double target = relative ? current + timeSeconds : timeSeconds;
                if (target < 0) target = 0;
                playback.Seek(target);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ActiveCue:RequestSeek - Video seek failed on {_cue?.Name}: {ex.Message}");
            }
        }
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
        
        _headLabelTimeLeft.Text = UiUtilities.FormatTime(_cue.Duration);
        _headLabelTimeRight.Text = $"-({UiUtilities.FormatTime(_cue.Duration)})";
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
    /// Children, components, optional pre-wait, then content trigger.
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

        foreach (var childId in _cue.ChildCues)
        {
            var child = CueList.FetchCueFromId(childId);
            if (child == null)
            {
                GD.PrintErr($"ActiveCue:StartPlaybackCoreAsync - Child cue {childId} not found");
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                    $"Child cue {childId} not found for parent {_cue.Name}", 2);
                continue;
            }
            var childCueList = _activeCueBar.GetNode<VBoxContainer>("%ChildCuelist");
            var activeCue = new ActiveCue(child, childCueList, _mediaEngine, _audioDevices, _globalSignals);
            _childActiveCues.Add(activeCue);
            activeCue.Completed += () => OnChildCompleted(activeCue);
            _ = activeCue.StartAsync();
        }
        
        // Set up components
        await SetupComponents();

        // If no components or children, mark as finished and tear down immediately
        if (_activeComponentCount == 0)
        {
            _isFinished = true;
            if (_childActiveCues.Count == 0)
            {
                RaiseContentPhaseStarted();
                HandleNaturalContentFinished();
                return;
            }
            // Parent with only children: content phase starts when children start.
            RaiseContentPhaseStarted();
            _isPlaying = true;
            return;
        }

        _isPlaying = true;

        bool doPreWait = includePreWait && !_skipPreWait && _cue.PreWait > 0;
        if (doPreWait)
        {
            PreWait();
        }
        else
        {
            HidePreWaitPanel();
            RaiseContentPhaseStarted();
            await TriggerComponents();
            EnsureAliveOrCleanup();
        }
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
        HookPreWaitUpdate(true);
        
        // Pause logic
        if (!_preWaitTimeoutHooked)
        {
            _preWaitTimer.Timeout += OnPreWaitTimerTimeout;
            _preWaitTimeoutHooked = true;
        }
        _preWaitTimer.Start();
    }

    private void PreWaitUpdate()
    {
        if (_preWaitTimer == null || !IsInstanceValid(_preWaitTimer)) return;
        _preWaitTimerLabel.Text = UiUtilities.FormatTime(_preWaitTimer.TimeLeft);
        var preWaitPercentage = (_preWaitTimer.TimeLeft / (float)_cue.PreWait) * 100;
        _preWaitProgress.Value = preWaitPercentage;
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
        _preWaitPause.Icon = _activeCueBar.GetThemeIcon("Play", "AtlasIcons");
    }

    private void PreWaitResume()
    {
        if (_preWaitTimer == null || !IsInstanceValid(_preWaitTimer)) return;
        _preWaitTimer.SetPaused(false);
        _preWaitPause.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
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

        RaiseContentPhaseStarted();
        await TriggerComponents();
        EnsureAliveOrCleanup();
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
    
                    }
                    else
                    {
                        // Release: perform the seek
                        if (playback.IsSeeking)
                        {
                            long timestampUs = (long)(pendingSeekTimeSec * 1_000_000);
                            playback.Seek(timestampUs);
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

            // Preload metadata if not already (assuming done in inspector/load)
            if (videoComponent.Metadata == null)
            {
                videoComponent.Metadata = await _mediaEngine.GetVideoFileMetadataAsync(videoComponent.VideoFile);
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
            timeLabel.Text = UiUtilities.FormatTime(videoComponent.TotalDuration);

            typeIcon.Icon = _activeCueBar.GetThemeIcon("Video", "AtlasIcons");
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
                        float percent = Mathf.Clamp(localPos.X / progressBar.Size.X, 0f, 1f);
                        pendingSeekTimeSec = videoComponent.StartTime + percent * playback.GetDuration();
                        progressBar.Value = percent * 100; // Preview
                        timeLabel.Text = UiUtilities.FormatTime(pendingSeekTimeSec); // Preview time
                    }
                    else
                    {
                        // Release: perform the seek
                        if (playback.IsSeeking)
                        {
                            double time = pendingSeekTimeSec;
                            playback.Seek(time);
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
                    pendingSeekTimeSec = videoComponent.StartTime + percent * playback.GetDuration();
                    progressBar.Value = percent * 100; // Update preview
                    timeLabel.Text = UiUtilities.FormatTime(pendingSeekTimeSec); // Update preview time
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
        CheckForCueCompletion();
    }

    private void UpdateVideoUiByPanelId(double time, ulong panelId)
    {
        if (_isCleaned || !IsInstanceValid(this)) return;
        PanelContainer panel = FindLivePanelKey(_activeVideoComponents.Keys, panelId);
        if (panel == null) return;
        UpdateVideoUi(time, panel);
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
        float progressPercentage = ((trackTime - (float)videoComponent.StartTime) / (float)videoPlayback.GetDuration() * 100f);
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

        if (propagateToChildren)
        {
            foreach (var child in _childActiveCues.ToList())
                child.ResumeAll(true);
        }

        foreach (var playback in _activeAudioComponents.Values)
            playback.Resume();

        foreach (var playback in _activeVideoComponents.Values)
            playback.Resume();

        if (_updateTimer != null && IsInstanceValid(_updateTimer))
            _updateTimer.Start();

        // Keep head + component + nested transport icons in sync with playback state.
        SyncPauseTransportUi();
    }

    private void PauseAll(bool propagateToChildren = true)
    {
        _isPaused = true;

        if (propagateToChildren)
        {
            foreach (var child in _childActiveCues.ToList())
                child.PauseAll(true);
        }

        foreach (var playback in _activeAudioComponents.Values)
            playback.Pause();

        foreach (var playback in _activeVideoComponents.Values)
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
            if (!_contentStarted || _inIncomingWait || _inPreWait || _isPaused)
            {
                Cleanup();
                return;
            }

            // Second Stop while a fade is in progress → hard stop.
            hardStop = _isStopFading;
            _isStopFading = true;
        }

        // Stop child cues if propagating (same fade override).
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

        if (tasks.Count == 0)
        {
            // Nothing left to await — remove the active bar immediately.
            Cleanup();
            return;
        }

        await Task.WhenAll(tasks);
        _isPlaying = false;

        // Completion handlers normally clean up; if they didn't (e.g. already stopped), force it.
        if (!_isCleaned)
        {
            EnsureAliveOrCleanup();
            if (!_isCleaned &&
                _activeAudioComponents.Count == 0 &&
                _activeVideoComponents.Count == 0)
            {
                Cleanup();
            }
        }
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
        _childActiveCues.Remove(child);
        if (_childActiveCues.Count == 0 && _isFinished)
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
        _activeAudioComponents.Clear();
        _componentToAudio.Clear();
        _activeVideoComponents.Clear();
        _componentToVideo.Clear();
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