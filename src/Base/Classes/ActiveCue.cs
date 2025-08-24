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
    private readonly VBoxContainer _activeCueBar;
    private readonly GlobalSignals _globalSignals;
    private readonly MediaEngine _mediaEngine;
    private readonly AudioDevices _audioDevices;

    private Timer _fadeTimer; // For fade-in/out
    private Timer _updateTimer;
    private bool _isPlaying;
    private ActiveAudioPlayback _audioPlayback;


    public ActiveCue()
    {
        // Blank constructor for Godot
    }
    
    public ActiveCue(Cue cue, VBoxContainer activeCueBar, MediaEngine mediaEngine, AudioDevices audioDevices, GlobalSignals globalSignals)
    {
        _cue = cue;
        _activeCueBar = activeCueBar;
        _mediaEngine = mediaEngine;
        _audioDevices = audioDevices;
        _globalSignals = globalSignals;

        // Setup optional fade timer (use Godot Timer for cross-platform)
        _fadeTimer = new Timer();
        _fadeTimer.OneShot = true;
        

    }
    

    public async Task StartAsync()
    {
        if (_isPlaying) return;
        GD.Print($"ActiveCue:StartAsync - Starting: {_cue.Name}");
        
        try
        {
            var audioComponent = _cue.GetAudioComponent();
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
            _updateTimer = new Timer()
            {
                WaitTime = 0.1f,
                OneShot = false,
                Autostart = true
            };
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