<DOCUMENT filename="ActiveCue.cs">
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Cue2.Shared;
using Cue2.UI.Utilities;
using Godot;
using Godot.Collections;

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

    private Timer _fadeTimer; // For fade-in/out
    private Timer _updateTimer;
    private bool _isPlaying;
    private ActiveAudioPlayback _audioPlayback; //!!! (Note: This is deprecated in favor of _activeAudioComponents; will be removed in future refactoring)

    private Timer _preWaitTimer;
    
    private Dictionary<Node, ActiveAudioPlayback> _activeAudioComponents = new Dictionary<Node, ActiveAudioPlayback>();
    private Dictionary<Node, AudioComponent> _componentToAudio = new Dictionary<Node, AudioComponent>(); //!!!
    
    
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

    private bool _isPaused = false; //!!!



    public ActiveCue()
    {
        // Blank constructor for Godot
    }
    
    public ActiveCue(Cue cue, PanelContainer activeCueBar, MediaEngine mediaEngine, AudioDevices audioDevices, GlobalSignals globalSignals)
    {
        _cue = cue;
        _activeCueBar = activeCueBar;
        _mediaEngine = mediaEngine;
        _audioDevices = audioDevices;
        _globalSignals = globalSignals;

        // Setup optional fade timer (use Godot Timer for cross-platform)
        _fadeTimer = new Timer();
        _fadeTimer.OneShot = true;
        _preWaitTimer = new Timer();
        _preWaitTimer.IgnoreTimeScale = true;
        _activeCueBar.AddChild(_preWaitTimer);
        
        _updateTimer = new Timer();
        _activeCueBar.AddChild(_updateTimer);
        _updateTimer.OneShot = false;
        _updateTimer.WaitTime = 0.05; // Sets update rate for ui
        _updateTimer.Timeout += UpdateUi; //!!!
        _updateTimer.Start();
        
        _progressBarContainer = _activeCueBar.GetNode<VBoxContainer>("%ProgressBarContainer");
        
        _headProgressBar = _activeCueBar.GetNode<ProgressBar>("%ProgressBar"); 
        _headLabelName = _activeCueBar.GetNode<Label>("%LabelName"); 
        _headLabelTimeLeft = _activeCueBar.GetNode<Label>("%LabelTimeLeft"); 
        _headLabelTimeRight = _activeCueBar.GetNode<Label>("%LabelTimeRight");

        _headProgressBar.Value = 0;
        _headLabelName.Text = _cue.Name;
        _headLabelTimeLeft.Text = UiUtilities.FormatTime(_cue.Duration);
        _headLabelTimeRight.Text = $"-({UiUtilities.FormatTime(_cue.Duration)})";
        
        _headPause = _activeCueBar.GetNode<Button>("%HeadPause");
        _headStop = _activeCueBar.GetNode<Button>("%HeadStop");

        _componentProgressBarScene = SceneLoader.LoadPackedScene("uid://cb7g4xgryo2dg", out _);
        
    }
    

    public async Task StartAsync()
    {
        if (_isPlaying) return;
        GD.Print($"ActiveCue:StartAsync - Starting: {_cue.Name}");
        
        // Set-up Ui
        _headPause.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
        _headStop.Icon = _activeCueBar.GetThemeIcon("Stop", "AtlasIcons");
        
        _headPause.Pressed += TogglePauseAll; //!!!
        _headStop.Pressed += () => Stop(); //!!!
        
        // Set up components
        await SetupComponents();
        
        // Pre-wait
        if (_cue.PreWait > 0)
        {
            PreWait();
        }
        else
        {
            await PlayAllComponents();
        }
        
    }

    private void PreWait()
    {
        GD.Print($"ActiveCue:PreWait - Pre-wait of {_cue.PreWait} detected"); //!!!
        _preWaitTimer.OneShot = true;
        _preWaitTimer.WaitTime = _cue.PreWait;
        
        //Ui
        _preWaitPanel = _activeCueBar.GetNode<PanelContainer>("%PreWaitBar");
        var preWaitNameLabel = _activeCueBar.GetNode<Label>("%PreWaitNameLabel");
        preWaitNameLabel.Text = _cue.Name;
        _preWaitTimerLabel = _activeCueBar.GetNode<Label>("%PreWaitTimer");
        _preWaitTimerLabel.Text = UiUtilities.FormatTime(_cue.PreWait); //!!!
        _preWaitProgress = _activeCueBar.GetNode<ProgressBar>("%PreWaitProgress");
        _preWaitPause = _activeCueBar.GetNode<Button>("%PreWaitPause");
        _preWaitSkip = _activeCueBar.GetNode<Button>("%PreWaitSkip");
        
        _preWaitPause.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons"); //!!!
        
        bool preWaitPaused = false; //!!!
        double remainingPreWait = 0; //!!!
        
        _preWaitPause.Pressed += () => //!!!
        {
            preWaitPaused = !preWaitPaused;
            if (preWaitPaused)
            {
                remainingPreWait = _preWaitTimer.TimeLeft;
                _preWaitTimer.Stop();
                _preWaitPause.Icon = _activeCueBar.GetThemeIcon("Play", "AtlasIcons");
            }
            else
            {
                _preWaitTimer.WaitTime = remainingPreWait;
                _preWaitTimer.Start();
                _preWaitPause.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
            }
        };

        _preWaitSkip.Pressed += PreWaitComplete;

        _preWaitTimer.Timeout += PreWaitComplete; //!!!
        _preWaitTimer.Start(); //!!! (Moved here for clarity)
    }

    private void PreWaitUpdate()
    {
        _preWaitTimerLabel.Text = UiUtilities.FormatTime(_preWaitTimer.TimeLeft);
        var preWaitPercentage = (_preWaitTimer.TimeLeft / (float)_cue.PreWait) * 100;
        _preWaitProgress.Value = preWaitPercentage;
    }
    
    private async void PreWaitComplete()
    {
        _preWaitTimer.Timeout -= PreWaitComplete; //!!!
        if (_preWaitPanel != null) _preWaitPanel.QueueFree(); //!!!
        await PlayAllComponents();
    }
    
    private void UpdateUi() //!!!
    {
        if (_preWaitPanel != null && IsInstanceValid(_preWaitPanel))
        {
            PreWaitUpdate();
        }
        
        foreach (var panel in _activeAudioComponents.Keys.ToArray())
        {
            if (IsInstanceValid(panel))
            {
                var audioComponent = _componentToAudio[panel];
                UpdateComponentUiState(panel, audioComponent);
            }
        }
        
        // TODO: Implement head progress bar update if needed (e.g., based on max component duration)
    }

    private async Task SetupComponents()
    {
        var tasks = new List<Task>(); //!!!
        foreach (var component in _cue.Components)
        {
            if (component is AudioComponent audioComponent)
            {
                tasks.Add(ActivateAudioComponent(audioComponent)); //!!!
            }
        }
        await Task.WhenAll(tasks); //!!!
    }


    private async Task PlayAllComponents()
    {
        foreach (var audio in _activeAudioComponents.Values) //!!!
        {
            audio.Resume();
        }
        _isPlaying = true; //!!! (Moved here as components are now playing)
        _isPaused = false; //!!!
    }


    private async Task ActivateAudioComponent(AudioComponent audioComponent)
    {
        try
        {
            if (audioComponent != null)
            {
                var audioPath = audioComponent.AudioFile;
                var patch = audioComponent.Patch;
                
                _audioPlayback = await _audioDevices.PlayAudio(audioPath, audioComponent.DirectOutput, 1, patch);
                GD.Print($"ActiveCue:ActivateAudioComponent - Trying audio playback"); //!!!
                if (_audioPlayback == null)
                {
                    throw new Exception("Failed to start audio playback.");
                }
            }
            
            // UI
            PanelContainer componentPanel = _componentProgressBarScene.Instantiate<PanelContainer>();
            _progressBarContainer.AddChild(componentPanel);
            componentPanel.GetNode<Label>("%ComponentLabel").Text = Path.GetFileName(audioComponent.AudioFile);
            var typeIcon = componentPanel.GetNode<Button>("%ComponentIcon");
            typeIcon.Icon = _activeCueBar.GetThemeIcon("Audio", "AtlasIcons");
            var pauseButton = componentPanel.GetNode<Button>("%ComponentPause");
            pauseButton.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
            var stopButton = componentPanel.GetNode<Button>("%ComponentStop");
            stopButton.Icon = _activeCueBar.GetThemeIcon("Stop", "AtlasIcons");
            var timeLabel = componentPanel.GetNode<Label>("%ComponentTime");
            timeLabel.Text = UiUtilities.FormatTime(audioComponent.TotalDuration);
            
            _activeAudioComponents.Add(componentPanel, _audioPlayback);
            _componentToAudio.Add(componentPanel, audioComponent); //!!!
            
            // Implement pause toggle for component
            bool componentPaused = false; //!!!
            pauseButton.Pressed += () => //!!!
            {
                var playback = _activeAudioComponents[componentPanel];
                componentPaused = !componentPaused;
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
            
            // Implement stop for component
            stopButton.Pressed += () => //!!!
            {
                var playback = _activeAudioComponents[componentPanel];
                playback.Stop();
            };
            
            // Implement seek on progress bar (click and drag)
            var progressBar = componentPanel.GetNode<ProgressBar>("ComponentProgress"); //!!!
            bool isSeeking = false; //!!!
            progressBar.GuiInput += (@event) => //!!!
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
                    GD.Print($"ActiveCue:ActivateAudioComponent - Seeking to {seekTime}");
                }
            };

            // Handle completion and cleanup for this component
            _audioPlayback.Completed += () => //!!!
            {
                if (_activeAudioComponents.ContainsKey(componentPanel))
                {
                    _activeAudioComponents.Remove(componentPanel);
                }
                if (_componentToAudio.ContainsKey(componentPanel))
                {
                    _componentToAudio.Remove(componentPanel);
                }
                if (IsInstanceValid(componentPanel))
                {
                    componentPanel.QueueFree();
                }
                if (_activeAudioComponents.Count == 0)
                {
                    Cleanup();
                }
            };

        }
        catch (Exception ex)
        {
            GD.Print($"ActiveCue:ActivateAudioComponent - Exception: {ex.Message}"); //!!!
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Error activating audio component: {ex.Message}", 2); //!!!
            Stop();
        }
        
    }
    
    private void UpdateComponentUiState(Node componentPanel, AudioComponent audioComponent)
    {
        var progressBar = componentPanel.GetNode<ProgressBar>("ComponentProgress");
        var audioPlayback = _activeAudioComponents[componentPanel];
        
        float trackTime = audioPlayback.MediaPlayer.Time / 1000f;
        float progressPercentage = ((trackTime - (float)audioComponent.StartTime) / (float)audioComponent.Duration) * 100f;
        var timeLabel = componentPanel.GetNode<Label>("ComponentProgress/MarginContainer/HBoxContainer/ComponentTime");
        timeLabel.Text = UiUtilities.FormatTime(trackTime);
        progressBar.Value = progressPercentage;
    }

    private void TogglePauseAll() //!!!
    {
        _isPaused = !_isPaused;
        if (_isPaused)
        {
            foreach (var playback in _activeAudioComponents.Values)
            {
                playback.Pause();
            }
            _headPause.Icon = _activeCueBar.GetThemeIcon("Play", "AtlasIcons");
        }
        else
        {
            foreach (var playback in _activeAudioComponents.Values)
            {
                playback.Resume();
            }
            _headPause.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
        }
    }

    /// <summary>
    /// Stops playback with optional fade-out.
    /// </summary>
    public void Stop(bool fadeOut = true)
    {
        if (!_isPlaying) return;
        // TODO: Implement fade-out logic if fadeOut is true (e.g., await SetVolumeAsync on all playbacks)
        if (fadeOut)
        {
            GD.Print($"ActiveCue:Stop - Fade-out not yet implemented; stopping immediately"); //!!!
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Fade-out for cue stop is not implemented yet", 1); //!!!
        }
        InternalStop();
    }
    
    private void InternalStop()
    {
        foreach (var playback in _activeAudioComponents.Values) //!!!
        {
            playback.Stop();
        }
        _isPlaying = false;
        //UpdateUiState("Stopped");
        // Emit signal if needed
    }
    
    private void Cleanup() //!!!
    {
        _updateTimer.Stop();
        _updateTimer.Timeout -= UpdateUi;
        _updateTimer.QueueFree();
        _preWaitTimer.QueueFree();
        _fadeTimer.QueueFree();
        _activeCueBar.QueueFree();
        QueueFree();
        GD.Print($"ActiveCue:Cleanup - Cleaned up active cue: {_cue.Name}");
    }
    
    // What I want to do - take audio file, split the 2 ch's and compose to audio streams according to the device.
}
</DOCUMENT>