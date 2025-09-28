using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Cue2.UI.Utilities;
using Godot;

namespace Cue2.Base.Classes;


/// <summary>
/// Manages the active state of a playing cue, including timers for fades/delays, UI updates, and interaction with playback engines.
/// Encapsulates playback logic to keep CueCommandExecutor clean and allow easy pause/stop/fade control.
/// Supports minimal latency by preloading and triggering on demand.
/// </summary>
public partial class ActiveCue : GodotObject
{
    private readonly Cue _cue;
    private readonly PanelContainer _activeCueBar;
    private readonly GlobalSignals _globalSignals;
    private readonly MediaEngine _mediaEngine;
    private readonly AudioDevices _audioDevices;
    private readonly Settings _settings;

    private Timer _fadeTimer; // For fade-in/out
    private Timer _updateTimer;
    private bool _isPlaying;
    private ActiveAudioPlayback _audioPlayback;
    private bool _inPreWait = false;
    
    private readonly object _lock = new object(); // For thread safety

    private Timer _preWaitTimer;
    
    private Dictionary<PanelContainer, ActiveAudioPlayback> _activeAudioComponents = new Dictionary<PanelContainer, ActiveAudioPlayback>();
    private Dictionary<PanelContainer, AudioComponent> _componentToAudio = new Dictionary<PanelContainer, AudioComponent>();
    private Dictionary<PanelContainer, CueLightComponent> _activeCueLightComponents = new Dictionary<PanelContainer, CueLightComponent>();
    
    [Signal]
    public delegate void CompletedEventHandler();
    
    
    // UI
    private VBoxContainer _progressBarContainer;
    
    private ProgressBar _headProgressBar;
    private Label _headLabelName;
    private Label _headLabelTimeLeft;
    private Label _headLabelTimeRight;

    private Button _headPause;
    private Button _headStop;
        
    private Label _preWaitTimerLabel;
    private ProgressBar _preWaitProgress;
    private PanelContainer _preWaitPanel;
    private Button _preWaitPause;
    private Button _preWaitSkip;
    
    private PackedScene _componentProgressBarScene;

    private bool _isPaused = false;
    private bool _isCleaned = false;
    

    public ActiveCue()
    {
        // Blank constructor for Godot
    }
    
    public ActiveCue(Cue cue, PanelContainer activeCueBar, MediaEngine mediaEngine, AudioDevices audioDevices, GlobalSignals globalSignals)
    {
        _cue = cue ?? throw new ArgumentNullException(nameof(cue));
        _activeCueBar = activeCueBar ?? throw new ArgumentNullException(nameof(activeCueBar));
        _mediaEngine = mediaEngine ?? throw new ArgumentNullException(nameof(mediaEngine));
        _audioDevices = audioDevices ?? throw new ArgumentNullException(nameof(audioDevices));
        _globalSignals = globalSignals ?? throw new ArgumentNullException(nameof(globalSignals));
        _settings = _activeCueBar.GetNode<GlobalData>("/root/GlobalData").Settings;

        _componentProgressBarScene = SceneLoader.LoadPackedScene("uid://cb7g4xgryo2dg", out string error); 
        if (!string.IsNullOrEmpty(error))
        {
            GD.Print($"ActiveCue:Constructor - Failed to load component progress bar scene: {error}");
        }
        
        SetupUi();
        SetupSignals();
        SetupTimers();
    }

    private void SetupUi()
    {
        _progressBarContainer = _activeCueBar.GetNode<VBoxContainer>("%ProgressBarContainer");
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
        
        _headPause.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
        _headStop.Icon = _activeCueBar.GetThemeIcon("Stop", "AtlasIcons");
        
        _headProgressBar.Value = 0;
        _headLabelName.Text = _cue.Name;
        
        _headLabelTimeLeft.Text = UiUtilities.FormatTime(_cue.Duration);
        _headLabelTimeRight.Text = $"-({UiUtilities.FormatTime(_cue.Duration)})";
    }

    private void SetupSignals()
    {
        _globalSignals.StopAll += StopAll;
        _globalSignals.PauseAll += GlobalPauseAll;
        _globalSignals.ResumeAll += GlobalResumeAll;

        _headPause.Pressed += TogglePauseAll;
        _headStop.Pressed += StopAll;

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
    
    
    public async Task StartAsync()
    {
        if (_isPlaying) return;
        GD.Print($"ActiveCue:StartAsync - Starting: {_cue.Name}");
        
        // Set up components
        //await SetupComponents();
        
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
            // Add other component types (e.g., Video, OSC) as implemented
        }

        await Task.WhenAll(tasks);
    }

    private async Task TriggerAudioComponent(AudioComponent audioComp)
    {
        // Preload metadata/waveform if not already (assuming done in inspector/load)
        if (audioComp.Metadata == null)
        {
            audioComp.Metadata = await _mediaEngine.GetAudioFileMetadataAsync(audioComp.AudioFile);
        }

        var playback = new ActiveAudioPlayback(audioComp);
        await playback.InitAsync();

        playback.Patch = audioComp.Patch;
        playback.CuePatch = audioComp.Routing;
            
        await _audioDevices.StartAudioPlayback(playback, audioComp);
            
        playback.Play();
        try
        {
            /*// Preload metadata/waveform if not already (assuming done in inspector/load)
            if (audioComp.Metadata == null)
            {
                audioComp.Metadata = await _mediaEngine.GetAudioFileMetadataAsync(audioComp.AudioFile);
            }

            var playback = new ActiveAudioPlayback(audioComp);
            await playback.InitAsync();

            playback.Patch = audioComp.Patch;
            playback.CuePatch = audioComp.Routing;
            
            await _audioDevices.StartAudioPlayback(playback, audioComp);
            
            playback.Play();*/
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveCue:TriggerAudioComponent - Error triggering audio {audioComp.AudioFile}: {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed audio component in {_cue.Name}: {ex.Message}", 2);
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
    }

    // pre ffmpeg code
    /*private async Task SetupComponents()
    {
        var tasks = new List<Task>();
        foreach (var component in _cue.Components)
        {
            if (component is AudioComponent audioComponent)
            {
                tasks.Add(ActivateAudioComponent(audioComponent));
            }
            else if (component is CueLightComponent cueLightComponent)
            {
                tasks.Add(SetupCueLightComponent(cueLightComponent));
                //var cueLightComp = component as CueLightComponent;
                //tasks.Add(cueLightComp.ExecuteAsync(_cue.CueNum));
            }
        }
        await Task.WhenAll(tasks);
    }*/
    

    // Pre ffmpeg code
    /*private async Task ActivateAudioComponent(AudioComponent audioComponent)
    {
        try
        {
            if (audioComponent != null)
            {
                var audioPath = audioComponent.AudioFile;
                var patch = audioComponent.Patch;
                
                _audioPlayback = await _audioDevices.PlayAudio(audioComponent);
                GD.Print($"ActiveCue:ActivateAudioComponent - Trying audio playback");
                if (_audioPlayback == null)
                {
                    throw new Exception("ActiveCue:ActivateAudioComponent - Failed to start audio playback.");
                }
            }
            
            // UI
            PanelContainer componentPanel = _componentProgressBarScene.Instantiate<PanelContainer>();
            _progressBarContainer.AddChild(componentPanel);
            componentPanel.GetNode<Label>("%ComponentLabel").Text = Path.GetFileName(audioComponent.AudioFile);
            var typeIcon = componentPanel.GetNode<Button>("%ComponentIcon");
            var pauseButton = componentPanel.GetNode<Button>("%ComponentPause");
            var stopButton = componentPanel.GetNode<Button>("%ComponentStop");
            var timeLabel = componentPanel.GetNode<Label>("%ComponentTime");
            timeLabel.Text = UiUtilities.FormatTime(audioComponent.TotalDuration);
            
            typeIcon.Icon = _activeCueBar.GetThemeIcon("Audio", "AtlasIcons");
            pauseButton.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
            stopButton.Icon = _activeCueBar.GetThemeIcon("Stop", "AtlasIcons");
            
            
            // Component Logic
            
            _activeAudioComponents.Add(componentPanel, _audioPlayback);
            _componentToAudio.Add(componentPanel, audioComponent);
            
            
            pauseButton.Pressed += () => 
            {
                
                var playback = _activeAudioComponents[componentPanel];
                bool componentPaused = playback.MediaPlayer.IsPlaying;
                
                if (componentPaused)
                {
                    playback.Pause();
                    pauseButton.Icon = _activeCueBar.GetThemeIcon("Play", "AtlasIcons");
                }
                else
                {
                    playback.Resume();
                    pauseButton.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
                }
            };
            
            // Stop
            stopButton.Pressed += async () => await StopComponent(componentPanel);
            
            
            // Progress bar seeking
            var progressBar = componentPanel.GetNode<ProgressBar>("ComponentProgress");
            bool isSeeking = false; 
            progressBar.GuiInput += (@event) => 
            {
                if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
                {
                    isSeeking = mb.Pressed;
                    if (isSeeking) UpdateSeek();
                }
                else if (@event is InputEventMouseMotion && isSeeking)
                {
                    UpdateSeek();
                }
                
                void UpdateSeek()
                {
                    var localPos = progressBar.GetLocalMousePosition();
                    float percent = Mathf.Clamp(localPos.X / progressBar.Size.X, 0f, 1f);
                    var playback = _activeAudioComponents[componentPanel];
                    double seekTime = audioComponent.StartTime + percent * audioComponent.Duration;
                    playback.MediaPlayer.Time = (long)(seekTime * 1000);
                    if (percent >= 1f) isSeeking = false;
                    GD.Print($"ActiveCue:ActivateAudioComponent - Seeking to {seekTime}");
                }
            };
            
            // Cleanup
            _audioPlayback.Completed += () => CallDeferred(nameof(HandleAudioComponentCompleted), componentPanel); // Defer to main thread
        }
        catch (Exception ex)
        {
            GD.Print($"ActiveCue:StartAsync - Exception: {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Error activating audio component for cue {_cue.Name}: {ex.Message}", 2);
            StopAll();
        }
        
    }*/

    private async Task SetupCueLightComponent(CueLightComponent cueLightComponent)
    {
        try
        {
            PanelContainer componentPanel = _componentProgressBarScene.Instantiate<PanelContainer>();
            _progressBarContainer.AddChild(componentPanel);
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
    
    private void UpdateComponentUiState(PanelContainer componentPanel, AudioComponent audioComponent)
    {
        var progressBar = componentPanel.GetNode<ProgressBar>("ComponentProgress");
        var audioPlayback = _activeAudioComponents[componentPanel];
        if (audioPlayback.IsStopped) return;
        //float trackTime = audioPlayback.MediaPlayer.Time / 1000f;
        //float progressPercentage = ((trackTime - (float)audioComponent.StartTime) / (float)audioComponent.Duration) * 100f;
        var timeLabel = componentPanel.GetNode<Label>("ComponentProgress/MarginContainer/HBoxContainer/ComponentTime");
        //timeLabel.Text = UiUtilities.FormatTime(trackTime);
        //progressBar.Value = progressPercentage;
        
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

    private void ResumeAll()
    {
        lock (_lock)
        {
            if (!_isPlaying || !_isPaused) return;
            _isPaused = false;
        }

        foreach (var playback in _activeAudioComponents.Values)
        {
            playback.Play(); // Resumes if paused
        }
        _updateTimer.Start();
        _headPause.Text = "Pause";
    }

    private void PauseAll()
    {
        foreach (var playback in _activeAudioComponents)
        {
            playback.Value.Pause();
            playback.Key.GetNode<Button>("%ComponentPause").Icon = playback.Key.GetThemeIcon("Play", "AtlasIcons");
        }
        _headPause.Icon = _activeCueBar.GetThemeIcon("Play", "AtlasIcons");
        _isPaused = true;
    }
    

    /// <summary>
    /// Stops playback with optional fade-out.
    /// </summary>
    public async void StopAll()
    {
        lock (_lock)
        {
            if (_inPreWait || _isPaused)
            {
                Cleanup();
                return;
            }
        }

        var tasks = new List<Task>();
        var fadeDuration = _settings.StopFadeDuration;
        foreach (var audioComp in _activeAudioComponents.Values.ToList())
        {
            tasks.Add(audioComp.Stop(fadeDuration));
        }
        await Task.WhenAll(tasks);
        _isPlaying = false;
    }
    
    private void GlobalPauseAll()
    {
        if (_inPreWait == true) 
        {
            PreWaitPause();
        }
        else PauseAll();
    }

    private void GlobalResumeAll()
    {
        if (_inPreWait == true)
        {
            PreWaitResume();
        }
        else ResumeAll();
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
        if (_activeAudioComponents.Count == 0)
        {
            Cleanup();
        }
    }
    
    
    private void Cleanup()
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
    
        _globalSignals.StopAll -= StopAll;
        _globalSignals.PauseAll -= GlobalPauseAll;
        _globalSignals.ResumeAll -= GlobalResumeAll;
        _headPause.Pressed -= TogglePauseAll;
        _headStop.Pressed -= StopAll;
    
        if (IsInstanceValid(_updateTimer))
            _updateTimer.QueueFree();
        if (IsInstanceValid(_preWaitTimer))
            _preWaitTimer.QueueFree();
        if (IsInstanceValid(_fadeTimer))
            _fadeTimer.QueueFree();
        if (IsInstanceValid(_activeCueBar))
            _activeCueBar.QueueFree();
        EmitSignal(SignalName.Completed, this);
        Free();
        GD.Print($"ActiveCue:Cleanup - Cleaned up active cue: {_cue.Name}");
    }
}