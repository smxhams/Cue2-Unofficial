using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Cue2.Base.Classes;
using Godot;
using LibVLCSharp.Shared;
using SDL3;

namespace Cue2.Base.Classes;

/// <summary>
/// Encapsulates an active audio playback session for control (volume, pause, stop, fade).
/// Thread-safe for multi-threaded access (e.g., UI updates).
/// </summary>
public partial class ActiveAudioPlayback : GodotObject
{
    public readonly MediaPlayer MediaPlayer;
    private readonly uint _sdlDevice;
    private readonly IntPtr _audioStream;
    private readonly AudioOutputPatch _patch;
    private readonly int _outputChannel;
    private readonly AudioComponent _audioComponent;

    private readonly object _lock = new object(); // For thread safety
    private float _volume = 1.0f; // Normalized [0-1]
    private bool _isFadingOut = false;
    private bool _isStopped = false;
    private CancellationTokenSource _fadeCts;
    
    private long _startTimeMs;
    private long _endTimeMs;
    private bool _useCustomEnd;
    private int _currentPlayCount = 1;
    private int _effectivePlayCount;
    private bool _hasStarted = false;
    private bool _reachedEnd = false;

    private readonly Stopwatch _playTimer = new Stopwatch();

    [Signal]
    public delegate void CompletedEventHandler();
    
    public ActiveAudioPlayback()
    {
        // Blank constructor for Godot
    }
    
    public ActiveAudioPlayback(MediaPlayer mediaPlayer, uint sdlDevice, IntPtr audioStream, AudioOutputPatch patch, int outputChannel, AudioComponent audioComponent)
    {
        MediaPlayer = mediaPlayer ?? throw new ArgumentNullException(nameof(mediaPlayer));
        _sdlDevice = sdlDevice;
        _audioStream = audioStream;
        _patch = patch;
        _outputChannel = outputChannel;
        _audioComponent = audioComponent ?? throw new ArgumentNullException(nameof(audioComponent));
        
        // Validate and set start time
        if (_audioComponent.StartTime < 0)
        {
            GD.Print($"ActiveAudioPlayback:Constructor - Invalid start time: {_audioComponent.StartTime}");
        }
        else
        {
            _startTimeMs = (long)(_audioComponent.StartTime * 1000);
        }
        _useCustomEnd = _audioComponent.EndTime >= 0;
        _endTimeMs = _useCustomEnd ? (long)(_audioComponent.EndTime * 1000) : (long)(_audioComponent.FileDuration * 1000);
        _effectivePlayCount = _audioComponent.Loop ? int.MaxValue : _audioComponent.PlayCount;
        
        // Validate start time against file duration if availible
        if (_audioComponent.FileDuration > 0 && _startTimeMs > (long)(_audioComponent.FileDuration * 1000))
        {
            _startTimeMs = 0;
        }
        
        MediaPlayer.EndReached += OnEndReached;
        MediaPlayer.LengthChanged += OnLengthChanged;
    }
    
    /// <summary>
    /// Thread safe get of current fade out to stop state
    /// </summary>
    public bool IsFadingOut
    {
        get
        {
            lock (_lock)
            {
                return _isFadingOut;
            }
        }
    }

    /// <summary>
    /// Thread safe get of current volume level
    /// </summary>
    public float CurrentVolume
    {
        get
        {
            lock (_lock)
            {
                return _volume;
            }
        }
    }

    /// <summary>
    /// Thread safe get of current play count
    /// </summary>
    public int CurrentPlayCount
    {
        get
        {
            lock (_lock)
            {
                return _currentPlayCount;
            }
        }
    }
    

    /// <summary>
    /// Stops and cleans up the playback resources.
    /// </summary>
    public async Task Stop(float stopFadeDuration = 0.0f)
    {
        lock (_lock)
        {
            if (_isStopped)
            {
                GD.Print($"ActiveAudioPlayback:Stop - Playback already stopped");
                return;
            }
            if (_isFadingOut)
            {
                // Immediate stop if already fading
                _fadeCts?.Cancel();
                _fadeCts?.Dispose();
                _fadeCts = null;
                Clean();
                GD.Print($"ActiveAudioPlayback:Stop - Stopped during fade-out");
                return;
            }
            _isFadingOut = true;
            _fadeCts = new CancellationTokenSource();
        }
        
        // Fade and stop
        try
        {
            await SetVolumeAsync(0f, stopFadeDuration, _fadeCts.Token);
            
            lock (_lock)
            {
                if (!_isStopped)
                {
                    Clean();
                    GD.Print($"ActiveAudioPlayback:Stop - Playback stopped during fade-out");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Fade-out was cancelled for immediate stop
            GD.Print($"ActiveAudioPlayback:Stop - Fade-out cancelled for immediate stop");
            lock (_lock)
            {
                if (!_isStopped)
                {
                    Clean();
                }
            }
        }
        catch (Exception ex)
        {
            GD.Print($"ActiveAudioPlayback:Stop - Exception during fade-out: {ex.Message}");
            lock (_lock)
            {
                Clean();
            }
        }
        finally
        {
            lock (_lock)
            {
                _fadeCts?.Dispose();
                _fadeCts = null;
            }
        }
        
        GD.Print($"ActiveAudioPlayback:Stop - Made it to clean");
    }

    /// <summary>
    /// Pauses the playback.
    /// </summary>
    public void Pause()
    {
        lock (_lock)
        {
            if (_isStopped || _isFadingOut)
            {
                GD.Print($"ActiveAudioPlayback:Pause - Cannot pause: stopped or fading"); //!!!
                return;
            }
            MediaPlayer?.Pause();
            _playTimer.Stop();
            GD.Print($"ActiveAudioPlayback:Pause - Playback paused");
        }
    }

    /// <summary>
    /// Resumes the playback.
    /// </summary>
    public void Resume()
    {
        lock (_lock)
        {
            if (_isStopped && _isFadingOut)
            {
                GD.Print($"ActiveAudioPlayback:Resume - Cannot resume: Stopped or fading");
            }
            // First time startup
            if (!_hasStarted)
            {
                if (MediaPlayer.Media == null)
                {
                    GD.Print("ActiveAudioPlayback:Resume", "Cannot resume: MediaPlayer.Media is null");
                    return;
                }
                MediaPlayer?.Play();
                GD.Print($"Start time is: {_startTimeMs}");
                MediaPlayer.Time = _startTimeMs;
                GD.Print($"MediaPlayer.Time: {MediaPlayer.Time}");
                _hasStarted = true;
                _playTimer.Reset();
                _playTimer.Start();
                if (_useCustomEnd)
                {
                    CallDeferred(nameof(StartMonitorTimeAsync));
                }
            }
            else
            {
                MediaPlayer?.Play();
                _playTimer.Start();
            }
        }
    }

    /// <summary>
    /// Sets volume with fade over time (async for non-blocking).
    /// </summary>
    public async Task SetVolumeAsync(float targetVolume, float durationSeconds, CancellationToken ct = default)
    {
        if (durationSeconds <= 0)
        {
            lock (_lock) 
            { 
                _volume = targetVolume; 
                MediaPlayer.Volume = (int)(targetVolume * 100); 
            }
            return;
        }

        float startVolume;
        lock (_lock)
        {
            startVolume = _volume;
        }
        float elapsed = 0;
        while (elapsed < durationSeconds)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(25, ct);
            elapsed += 0.025f;
            float t = elapsed / durationSeconds;
            float newVolume = Mathf.Lerp(startVolume, targetVolume, t);
            lock (_lock) 
            { 
                if (_isStopped) return; // Early exit if stopped
                _volume = newVolume; 
                MediaPlayer.Volume = (int)(_volume * 100); 
            }
        }
    }
    
    private async void MonitorTimeAsync()
    {
        while (true)
        {
            lock (_lock)
            {
                if (_isStopped || _isFadingOut) break;
            }
            long elapsed = _playTimer.ElapsedMilliseconds;
            long current = _startTimeMs + elapsed;
            if (current >= _endTimeMs)
            {
                lock (_lock)
                {
                    if (_reachedEnd) continue;
                    _reachedEnd = true;
                    HandleEndReached();
                }
            }

            await Task.Delay(5);
        }
    }

    private void StartMonitorTimeAsync()
    {
        MonitorTimeAsync();
    }
    

    private void OnEndReached(object sender, EventArgs e)
    {
        lock (_lock)
        {
            if (_isStopped || _isFadingOut) return;
            HandleEndReached();
        }
    }

    private void OnLengthChanged(object sender, MediaPlayerLengthChangedEventArgs e)
    {
        lock (_lock)
        {
            if (!_useCustomEnd)
            {
                GD.Print($"ActiveAudioPlayback:OnLengthChanged");
                _endTimeMs = e.Length;
            }
        }
    }

    private void HandleEndReached()
    {
        lock (_lock)
        {
            if (_currentPlayCount < _effectivePlayCount)
            {
                _currentPlayCount++;
                GD.Print($"ActiveAudioPlayback:HandleEndReached - Setting media player time");
                CallDeferred(nameof(ResetForLoop));
            }
            else
            {
                GD.Print($"ActiveAudioPlayback:HandleEndReached - Playback completed");
                Clean();
            }
        }
    }


    private void ResetForLoop()
    {
        lock (_lock)
        {
            if (_useCustomEnd)
            {
                _playTimer.Stop();
                MediaPlayer.Time = _startTimeMs;
                _playTimer.Reset();
                _playTimer.Start();
                _reachedEnd = false;
                GD.Print($"ActiveAudioPlayback:ResetForLoop - Time reset for custom end");
            }
            else
            {
                MediaPlayer.Time = _startTimeMs;
                MediaPlayer.Play();
                _playTimer.Reset();
                _playTimer.Start();
                GD.Print($"ActiveAudioPlayback:ResetForLoop - Time set and playback resumed for full duration");
            }
        }
    }
    
    
    // Dispose: Call Stop()
    public void Clean()
    {
        lock (_lock)
        {
            if (_isStopped)
            {
                GD.Print("ActiveAudioPlayback:Clean - Already cleaned");
                return;
            }
            _isStopped = true;
            MediaPlayer?.Stop();
            if (MediaPlayer != null)
            {
                MediaPlayer.EndReached -= OnEndReached; // Detach event handler
                MediaPlayer.LengthChanged -= OnLengthChanged;
                MediaPlayer?.Dispose();
            }

            GD.Print($"ActiveAudioPlayback:Clean - {_audioComponent.AudioFile}");
            EmitSignal(SignalName.Completed);
        }
    }
    
}