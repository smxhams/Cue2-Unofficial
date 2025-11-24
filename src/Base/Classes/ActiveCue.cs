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

    private PanelContainer _activeCueBar;
    private Timer _fadeTimer; // For fade-in/out
    private Timer _updateTimer;
    private bool _isPlaying;
    private bool _inPreWait = false;
    
    private readonly object _lock = new object(); // For thread safety

    private Timer _preWaitTimer;
    
    private Dictionary<PanelContainer, ActiveAudioPlayback> _activeAudioComponents = new Dictionary<PanelContainer, ActiveAudioPlayback>();
    private Dictionary<PanelContainer, AudioComponent> _componentToAudio = new Dictionary<PanelContainer, AudioComponent>();
    private Dictionary<PanelContainer, ActiveVideoPlayback> _activeVideoComponents = new Dictionary<PanelContainer, ActiveVideoPlayback>();
    private Dictionary<PanelContainer, VideoComponent> _componentToVideo = new Dictionary<PanelContainer, VideoComponent>();
    private Dictionary<PanelContainer, CueLightComponent> _activeCueLightComponents = new Dictionary<PanelContainer, CueLightComponent>();

    private int _activeComponentCount = 0;
    
    /// <summary>
    /// Event raised when the cue playback is completed.
    /// </summary>
    [Signal]
    public delegate void CompletedEventHandler();
    
    
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
    
    // Main cue progress scene
    private PackedScene _activeCueBarScene = SceneLoader.LoadPackedScene("uid://dt7rlfag7yr2c", out string error); 
    // Component progress scene
    private PackedScene _componentProgressBarScene = SceneLoader.LoadPackedScene("uid://cb7g4xgryo2dg", out string error);
    
    private bool _isPaused = false;
    private bool _isCleaned = false;
    private bool _isFinished = false;

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
    public ActiveCue(Cue cue, VBoxContainer activeCueList, MediaEngine mediaEngine, AudioDevices audioDevices, GlobalSignals globalSignals)
    {
        _cue = cue ?? throw new ArgumentNullException(nameof(cue));
        _activeCueList = activeCueList;
        _mediaEngine = mediaEngine ?? throw new ArgumentNullException(nameof(mediaEngine));
        _audioDevices = audioDevices ?? throw new ArgumentNullException(nameof(audioDevices));
        _globalSignals = globalSignals ?? throw new ArgumentNullException(nameof(globalSignals));
        _settings = _activeCueList.GetNode<GlobalData>("/root/GlobalData").Settings;
        
        
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

    private void SetupSignals()
    {
        _globalSignals.StopAll += GlobalStopAll;
        _globalSignals.PauseAll += () => PauseAll(false);
        _globalSignals.ResumeAll += () => ResumeAll(false);

        _headPause.Pressed += TogglePauseAll;
        _headStop.Pressed += () => StopAll();

        _preWaitPause.Pressed += TogglePreWaitPause;
        _preWaitSkip.Pressed += PreWaitComplete;
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
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartAsync()
    {
        if (_isPlaying) return;
        GD.Print($"ActiveCue:StartAsync - Starting: {_cue.Name}");
        
        // Setup UI
        SetupUi();
        SetupSignals();
        SetupTimers();
        
        foreach (var childId in _cue.ChildCues)
        {
            var child = CueList.FetchCueFromId(childId);
            var childCueList = _activeCueBar.GetNode<VBoxContainer>("%ChildCuelist");
            var activeCue = new ActiveCue(child, childCueList, _mediaEngine, _audioDevices, _globalSignals);
            _childActiveCues.Add(activeCue);
            activeCue.Completed += () => OnChildCompleted(activeCue);
            _ = activeCue.StartAsync();
        }
        
        // Set up components
        await SetupComponents();

        // If no components or children, mark as finished
        if (_activeComponentCount == 0)
        {
            _isFinished = true;
            if (_childActiveCues.Count == 0)
            {
                Cleanup();
            }
        }

        // Pre-wait
        if (_cue.PreWait > 0)
        {
            PreWait();
        }
        else
        {
            await TriggerComponents();
        }
    }

    private async Task TriggerComponents()
    {
        var tasks = new List<Task>();

        foreach (var comp in _cue.Components)
        {
            if (comp is AudioComponent audioComp)
            {
                tasks.Add(TriggerAudioComponent(audioComp));
            }
            else if (comp is VideoComponent videoComp)
            {
                tasks.Add(TriggerVideoComponent(videoComp));
            }
            // Add other component types (e.g., OSC) as implemented
        }

        await Task.WhenAll(tasks);
    }

    private async Task TriggerAudioComponent(AudioComponent audioComp)
    {
        try
        {
            // Find specific playback for this audioComp
            var panel = _componentToAudio.FirstOrDefault(kv => kv.Value == audioComp).Key;
            if (panel == null)
            {
                GD.PrintErr($"ActiveCue:TriggerAudioComponent - No playback found for {audioComp.AudioFile}");
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"No playback for audio component in cue {_cue.Name}", 2);
                return;
            }
            var playback = _activeAudioComponents[panel];

            await _audioDevices.StartAudioPlayback(playback, audioComp);
            playback.Play();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveCue:TriggerAudioComponent - Error triggering {audioComp.AudioFile}: {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Trigger failed for audio in {_cue.Name}: {ex.Message}", 2);
        }
    }

    private async Task TriggerVideoComponent(VideoComponent videoComp)
    {
        try
        {
            // Find specific playback for this videoComp
            var panel = _componentToVideo.FirstOrDefault(kv => kv.Value == videoComp).Key;
            if (panel == null)
            {
                GD.PrintErr($"ActiveCue:TriggerVideoComponent - No playback found for {videoComp.VideoFile}");
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"No playback for video component in cue {_cue.Name}", 2);
                return;
            }
            var playback = _activeVideoComponents[panel];

            if (videoComp.UseAudio)
            {
                await _audioDevices.StartAudioPlayback(playback, videoComp);
            }

            playback.Play();

        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveCue:TriggerVideoComponent - Error triggering {videoComp.VideoFile}: {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Trigger failed for video in {_cue.Name}: {ex.Message}", 2);
        }
    }


    private void PreWait()
    {
        GD.Print($"ActiveCue:PreWait - Pre-wait of {_cue.PreWait} detected");
        
        //Ui
        var preWaitNameLabel = _activeCueBar.GetNode<Label>("%PreWaitNameLabel");
        preWaitNameLabel.Text = _cue.Name;
        _preWaitTimerLabel.Text = _preWaitTimer.TimeLeft.ToString();

        _preWaitPause.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
        _preWaitSkip.Icon = _activeCueBar.GetThemeIcon("Skip", "AtlasIcons");
        
        _preWaitPanel.Visible = true;

        _inPreWait = true;
        _updateTimer.Timeout += PreWaitUpdate;
        
        // Pause logic
        _preWaitTimer.Timeout += PreWaitComplete;
        _preWaitTimer.Start();
    }

    private void PreWaitUpdate()
    {
        _preWaitTimerLabel.Text = UiUtilities.FormatTime(_preWaitTimer.TimeLeft);
        var preWaitPercentage = (_preWaitTimer.TimeLeft / (float)_cue.PreWait) * 100;
        _preWaitProgress.Value = preWaitPercentage;
    }

    private void TogglePreWaitPause()
    {
        if (_preWaitTimer.Paused)
        {
            PreWaitResume();
        }
        else
        {
            PreWaitPause();
        }
    }

    private void PreWaitPause()
    {
        _preWaitTimer.SetPaused(true);
        _preWaitPause.Icon = _activeCueBar.GetThemeIcon("Play", "AtlasIcons");
    }

    private void PreWaitResume()
    {
        _preWaitTimer.SetPaused(false);
        _preWaitPause.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
    }
    
    private async void PreWaitComplete()
    {
        _updateTimer.Timeout -= PreWaitUpdate;
        _preWaitTimer.Timeout -= PreWaitComplete;
        _preWaitPause.Pressed -= TogglePreWaitPause;
        if (_preWaitPanel != null) _preWaitPanel.QueueFree();
        _inPreWait = false;
        await TriggerComponents();
    }


    private void UpdateUi()
    {
        foreach (var panel in _activeAudioComponents.Keys)
        {
            if (IsInstanceValid(panel))
            {
                var audioComponent = _componentToAudio[panel];
                UpdateComponentUiState(panel, audioComponent);
            }
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
        }
        await Task.WhenAll(tasks);
    }
    
    
    private async Task SetupAudioComponent(AudioComponent audioComponent)
    {
        try
        {
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
            pauseButton.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
            stopButton.Icon = _activeCueBar.GetThemeIcon("Stop", "AtlasIcons");
            
            
            // Component Logic
            _activeAudioComponents.Add(componentPanel, playback);
            _componentToAudio.Add(componentPanel, audioComponent);
            
            
            pauseButton.Pressed += () => 
            {
                
                var playback = _activeAudioComponents[componentPanel];
                bool componentPaused = playback.IsPaused; //playback.MediaPlayer.IsPlaying;
                
                if (!componentPaused)
                {
                    playback.Pause();
                    pauseButton.Icon = _activeCueBar.GetThemeIcon("Play", "AtlasIcons");
                }
                else
                {
                    GD.Print($"ActiveCue:SetupAudioComponent: Resuming component {componentPanel.Name}");
                    playback.Resume();
                    pauseButton.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
                }
            };
            
            // Stop
            stopButton.Pressed += async () => await StopComponent(componentPanel);
            
            
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
            
            // Cleanup
            playback.Completed += () => CallDeferred(nameof(HandleAudioComponentCompleted), componentPanel); // Defer to main thread
        }
        catch (Exception ex)
        {
            GD.Print($"ActiveCue:SetupAudioComponent - Exception: {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Error activating audio component for cue {_cue.Name}: {ex.Message}", 2);
            //StopAll(); //Can't remember why this will call a stopall in catch
        }
    }

    /// <summary>
    /// Creates Ui for VideoComponent and handles input.
    /// </summary>
    /// <param name="videoComponent"></param>
    private async Task SetupVideoComponent(VideoComponent videoComponent)
    {
        try
        {
            // Preload metadata if not already (assuming done in inspector/load)
            if (videoComponent.Metadata == null)
            {
                videoComponent.Metadata = await _mediaEngine.GetVideoFileMetadataAsync(videoComponent.VideoFile);
            }
            
            var playback = new ActiveVideoPlayback(videoComponent, _audioDevices);
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
            pauseButton.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
            stopButton.Icon = _activeCueBar.GetThemeIcon("Stop", "AtlasIcons");

            // Component Logic
            _activeVideoComponents.Add(componentPanel, playback);
            _componentToVideo.Add(componentPanel, videoComponent);
            
            pauseButton.Pressed += () => {
                bool componentPaused = playback.IsPaused;
                if (!componentPaused)
                {
                    playback.Pause();
                    pauseButton.Icon = _activeCueBar.GetThemeIcon("Play", "AtlasIcons");
                }
                else
                {
                    GD.Print($"ActiveCue:SetupVideoComponent: Resuming component {componentPanel.Name}");
                    playback.Resume();
                    pauseButton.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
                }
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
            playback.TimeUpdated += (time) => CallDeferred(nameof(UpdateVideoUi), time, componentPanel); // Defer to main thread
            
            _activeComponentCount++;

            // Cleanup
            playback.Completed += () => CallDeferred(nameof(HandleVideoComponentCompleted), componentPanel); // Defer to main thread
        }
        catch (Exception ex)
        {
            GD.Print($"ActiveCue:SetupVideoComponent - Exception: {ex.Message}, Stack: {ex.StackTrace}, Target: {ex.TargetSite}, {ex.InnerException}, {ex.Source}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Error activating video component for cue {_cue.Name}: {ex.Message}", 2);
        }
    }


    private async Task SetupCueLightComponent(CueLightComponent cueLightComponent)
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
            
        }
        catch (Exception ex)
        {
            GD.Print($"ActiveCue:StartAsync - Exception: {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Error activating audio component for cue {_cue.Name}: {ex.Message}", 2);
        }
    }

    private async Task StopComponent(PanelContainer componentPanel)
    {
        var playback = _activeAudioComponents[componentPanel];
        var tasks = new List<Task>();
        tasks.Add(playback.Stop(_settings.StopFadeDuration));
        await Task.WhenAll(tasks);
    }

    private async Task StopVideoComponent(PanelContainer componentPanel)
    {
        var playback = _activeVideoComponents[componentPanel];
        var tasks = new List<Task>();
        tasks.Add(playback.Stop(_settings.StopFadeDuration));
        await Task.WhenAll(tasks);
    }
    
    private void UpdateComponentUiState(PanelContainer componentPanel, AudioComponent audioComponent)
    {

        var progressBar = componentPanel.GetNode<ProgressBar>("ComponentProgress");
        var audioPlayback = _activeAudioComponents[componentPanel];
        if (audioPlayback.IsStopped || audioPlayback.IsPaused) return;
        //GD.Print($"ActiveCue:UpdateComponentUiState - Current time ms: {audioPlayback.Decoder.CurrentTime}");
        float trackTime = audioPlayback.GetPlaybackTimeMs() / 1000f; // ms to seconds
        float progressPercentage = ((trackTime - (float)audioComponent.StartTime) / (float)audioComponent.Duration) * 100f;
        var timeLabel = componentPanel.GetNode<Label>("ComponentProgress/MarginContainer/HBoxContainer/ComponentTime");
        if (!audioPlayback.IsSeeking)
        {
            timeLabel.Text = UiUtilities.FormatTime(trackTime);
            progressBar.Value = progressPercentage;
        }


        // Update fade-out progress
        var fadeProgress = componentPanel.GetNode<ProgressBar>("%ComponentFadeProgress");
        if (audioPlayback.IsFadingOut)
        {
            fadeProgress.Visible = true;
            fadeProgress.Value = (1 - audioPlayback.CurrentVolume) * 100;
        }
        else
        {
            fadeProgress.Visible = false;
        }

    }



    private void UpdateVideoUi(double time, PanelContainer componentPanel)
    {
        if (!IsInstanceValid(componentPanel) || !_activeVideoComponents.ContainsKey(componentPanel) || !_componentToVideo.ContainsKey(componentPanel)) return;

        var videoComponent = _componentToVideo[componentPanel];
        var progressBar = componentPanel.GetNode<ProgressBar>("ComponentProgress");
        var videoPlayback = _activeVideoComponents[componentPanel];
        if (videoPlayback.IsStopped || videoPlayback.IsPaused) return;
        float trackTime = (float)time;
        float progressPercentage = ((trackTime - (float)videoComponent.StartTime) / (float)videoPlayback.GetDuration() * 100f);
        var timeLabel = componentPanel.GetNode<Label>("ComponentProgress/MarginContainer/HBoxContainer/ComponentTime");
        if (!videoPlayback.IsSeeking)
        {
            timeLabel.Text = UiUtilities.FormatTime(trackTime);
            progressBar.Value = progressPercentage;
        }

        // Update fade-out progress
        var fadeProgress = componentPanel.GetNode<ProgressBar>("%ComponentFadeProgress");
        if (videoPlayback.IsFadingOut)
        {
            fadeProgress.Visible = true;
            fadeProgress.Value = (1 - videoPlayback.CurrentVolume) * 100;
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
            if (!_isPlaying || !_isPaused) return;
            _isPaused = false;
        }

        if (propagateToChildren)
        {
            foreach (var child in _childActiveCues)
            {
                child.ResumeAll(true);
            }
        }

        foreach (var playback in _activeAudioComponents.Values)
        {
            playback.Resume(); // Resumes if paused
        }

        foreach (var playback in _activeVideoComponents.Values)
        {
            playback.Resume(); // Resumes if paused
        }

        _updateTimer.Start();
        _headPause.Text = "Pause";
    }

    private void PauseAll(bool propagateToChildren = true)
    {
        if (propagateToChildren)
        {
            foreach (var child in _childActiveCues)
            {
                child.PauseAll(true);
            }
        }

        foreach (var playback in _activeAudioComponents)
        {
            playback.Value.Pause();
            playback.Key.GetNode<Button>("%ComponentPause").Icon = playback.Key.GetThemeIcon("Play", "AtlasIcons");
        }
        foreach (var playback in _activeVideoComponents)
        {
            playback.Value.Pause();
            playback.Key.GetNode<Button>("%ComponentPause").Icon = playback.Key.GetThemeIcon("Play", "AtlasIcons");
        }
        _headPause.Icon = _activeCueBar.GetThemeIcon("Play", "AtlasIcons");
        _isPaused = true;
    }
    

    /// <summary>
    /// Stops all playback for this cue with optional fade-out based on settings.
    /// </summary>
    /// <param name="propagateToChildren">Whether to stop child cues as well.</param>
    public async void StopAll(bool propagateToChildren = true)
    {
        lock (_lock)
        {
            if (_inPreWait || _isPaused)
            {
                Cleanup();
                return;
            }
        }

        // Stop child cues if propagating
        if (propagateToChildren)
        {
            foreach (var child in _childActiveCues)
            {
                child.StopAll(true);
            }
        }

        var tasks = new List<Task>();
        var fadeDuration = _settings.StopFadeDuration;
        foreach (var audioComp in _activeAudioComponents.Values.ToList())
        {
            tasks.Add(audioComp.Stop(fadeDuration));
        }
        foreach (var videoComp in _activeVideoComponents.Values.ToList())
        {
            tasks.Add(videoComp.Stop(fadeDuration));
        }
        await Task.WhenAll(tasks);
        _isPlaying = false;
    }

    private void GlobalStopAll()
    {
        StopAll(false); // Don't propagate, as all cues receive the global signal
    }
    
    private void GlobalPauseAll()
    {
        if (_inPreWait == true)
        {
            PreWaitPause();
        }
        else PauseAll(false);
    }

    private void GlobalResumeAll()
    {
        if (_inPreWait == true)
        {
            PreWaitResume();
        }
        else ResumeAll(false);
    }
    
    
    
    private void HandleAudioComponentCompleted(PanelContainer componentPanel)
    {
        if (!IsInstanceValid(this) || !_activeAudioComponents.ContainsKey(componentPanel))
        {
            GD.Print("ActiveCue:HandleAudioComponentCompleted - Component already cleaned or invalid");
            return;
        }

        _activeAudioComponents.Remove(componentPanel);
        _componentToAudio.Remove(componentPanel);
        componentPanel.QueueFree();
        if (_activeAudioComponents.Count == 0 && _activeVideoComponents.Count == 0)
        {
            _isFinished = true;
            if (_childActiveCues.Count == 0)
            {
                Cleanup();
            }
        }
    }

    private void HandleVideoComponentCompleted(PanelContainer componentPanel)
    {
        if (!IsInstanceValid(this) || !_activeVideoComponents.ContainsKey(componentPanel))
        {
            GD.Print("ActiveCue:HandleVideoComponentCompleted - Component already cleaned or invalid");
            return;
        }

        _activeVideoComponents.Remove(componentPanel);
        _componentToVideo.Remove(componentPanel);
        componentPanel.QueueFree();
        if (_activeAudioComponents.Count == 0 && _activeVideoComponents.Count == 0)
        {
            _isFinished = true;
            if (_childActiveCues.Count == 0)
            {
                Cleanup();
            }
        }
    }

    private void OnChildCompleted(ActiveCue child)
    {
        _childActiveCues.Remove(child);
        if (_childActiveCues.Count == 0 && _isFinished)
        {
            Cleanup();
        }
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
        }

        _updateTimer.Stop();
        _updateTimer.Timeout -= UpdateUi;

        _globalSignals.StopAll -= GlobalStopAll;
        _globalSignals.PauseAll -= GlobalPauseAll;
        _globalSignals.ResumeAll -= GlobalResumeAll;
        _headPause.Pressed -= TogglePauseAll;

        if (IsInstanceValid(_updateTimer))
            _updateTimer.QueueFree();
        if (IsInstanceValid(_preWaitTimer))
            _preWaitTimer.QueueFree();
        if (IsInstanceValid(_fadeTimer))
            _fadeTimer.QueueFree();
        if (IsInstanceValid(_activeCueBar))
        {
            _activeCueBar.QueueFree();
        }
        
        EmitSignal(SignalName.Completed);
        CallDeferred("free");
        GD.Print($"ActiveCue:Cleanup - Cleaned up active cue: {_cue.Name}");
    }
}