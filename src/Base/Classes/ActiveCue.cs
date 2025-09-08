using System;
using System.Diagnostics;
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
    private ActiveAudioPlayback _audioPlayback;

    private Timer _preWaitTimer;
    
    // UI
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
        _updateTimer.Start();
        
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

    }
    

    public async Task StartAsync()
    {
        if (_isPlaying) return;
        GD.Print($"ActiveCue:StartAsync - Starting: {_cue.Name}");
        
        // Set-up Ui
        _headPause.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
        _headStop.Icon = _activeCueBar.GetThemeIcon("Stop", "AtlasIcons");
        
        // Pre-wait
        if (_cue.PreWait > 0)
        {
            PreWait();
        }
        else
        {
            await RunComponents();
        }
        
    }

    private void PreWait()
    {
        GD.Print($"Pre-wait of {_cue.PreWait} detected");
        _preWaitTimer.OneShot = true;
        _preWaitTimer.WaitTime = _cue.PreWait;
        
        //Ui
        _preWaitPanel = _activeCueBar.GetNode<PanelContainer>("%PreWaitBar");
        var preWaitNameLabel = _activeCueBar.GetNode<Label>("%PreWaitNameLabel");
        preWaitNameLabel.Text = _cue.Name;
        _preWaitTimerLabel = _activeCueBar.GetNode<Label>("%PreWaitTimer");
        _preWaitTimerLabel.Text = _preWaitTimer.TimeLeft.ToString();
        _preWaitProgress = _activeCueBar.GetNode<ProgressBar>("%PreWaitProgress");
        _preWaitPause = _activeCueBar.GetNode<Button>("%PreWaitPause");
        _preWaitPause.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
        _preWaitSkip = _activeCueBar.GetNode<Button>("%PreWaitSkip");
        _preWaitSkip.Icon = _activeCueBar.GetThemeIcon("Skip", "AtlasIcons");

        _preWaitPanel.Visible = true;

        _updateTimer.Timeout += PreWaitUpdate;
        
        _preWaitTimer.Timeout += PreWaitComplete;
        
        _preWaitTimer.Start();
        
        // Pause logic
        _preWaitPause.Pressed += () =>
        {
            if (_preWaitTimer.Paused)
            {
                // Play prewait
                _preWaitTimer.SetPaused(false);
                _preWaitPause.Icon = _activeCueBar.GetThemeIcon("Pause", "AtlasIcons");
            }
            else
            {
                // Pause prewait
                _preWaitTimer.SetPaused(true);
                _preWaitPause.Icon = _activeCueBar.GetThemeIcon("Play", "AtlasIcons");
            }
        };

        _preWaitSkip.Pressed += PreWaitComplete;

    }

    private void PreWaitUpdate()
    {
        _preWaitTimerLabel.Text = UiUtilities.FormatTime(_preWaitTimer.TimeLeft);
        var preWaitPercentage = (_preWaitTimer.TimeLeft / (float)_cue.PreWait) * 100;
        _preWaitProgress.Value = preWaitPercentage;
    }
    
    private async void PreWaitComplete()
    {
        _updateTimer.Timeout -= PreWaitUpdate;
        _preWaitTimer.Timeout -= PreWaitComplete;
        _preWaitPanel.QueueFree();
        await RunComponents();
    }
    

    private async Task RunComponents()
    {
        foreach (var component in _cue.Components)
        {
            if (component is AudioComponent audioComponent)
            {
                await ActivateAudioComponent(audioComponent);
            }
        }
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
                GD.Print($"Trying audio playback");
                if (_audioPlayback == null)
                {
                    throw new Exception("Failed to start audio playback.");
                }
            }
            _isPlaying = true;
            InitialiseUi();
            _activeCueBar.AddChild(_updateTimer);
            _updateTimer.Timeout += UpdateUiState;
            
        }
        catch (Exception ex)
        {
            GD.Print($"ActiveCue:StartAsync - Exception: {ex.Message}");
            Stop();
        }
        
    }

    private void InitialiseUi()
    {
        _activeCueBar.GetNode<Label>("%LabelName").Text = _cue.Name;
    }
    
    private void UpdateUiState()
    {
        var cueTime = _audioPlayback?.MediaPlayer.Time;
        GD.Print($"{cueTime}");
        var cueTimeSeconds = (double)(cueTime / 1000f);
        _activeCueBar.GetNode<Label>("%LabelTimeLeft").Text = UiUtilities.FormatTime(cueTimeSeconds);

        var progressBar = _activeCueBar.GetNode<ProgressBar>("%ProgressBar");
        //_activeCueBar.GetNode<Label>("%LabelTimeLeft").Text = _audioPlayback?.MediaPlayer.Duration.ToString("mm:ss");
    }

    /// <summary>
    /// Stops playback with optional fade-out.
    /// </summary>
    public void Stop(bool fadeOut = true)
    {
        if (!_isPlaying) return;
    }
    
    private void InternalStop()
    {
        _audioPlayback?.Stop(); // Cleanup SDL/VLC resources
        _isPlaying = false;
        //UpdateUiState("Stopped");
        // Emit signal if needed
    }
    
    // What I want to do - take audio file, split the 2 ch's and compose to audio streams according to the device.
}