using System;
using System.Threading.Tasks;
using Cue2.Shared;
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
    private readonly VBoxContainer _activeCueBar;
    private readonly GlobalSignals _globalSignals;
    private readonly MediaEngine _mediaEngine;
    private readonly AudioDevices _audioDevices;

    private Timer _fadeTimer; // For fade-in/out
    private bool _isPlaying;
    //private ActiveAudioPlayback _audioPlayback;
    
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
        
        // Init it's ui
        _activeCueBar.GetNode<Label>("%LabelName").Text = _cue.Name;
        
    }
    
    public async Task StartAsync()
    {
        if (_isPlaying) return;
        GD.Print($"ActiveCue:StartAsync - Starting: {_cue.Name}");
        
        var audioComponent = _cue.GetAudioComponent();
        try
        {
            if (audioComponent != null)
            {
                var audioPath = audioComponent.AudioFile;
                var patch = audioComponent.Patch;
                
                
                GD.Print($"Trying audio playback");
            }
        }
        catch (Exception ex)
        {
            
        }
        
        
    }
}