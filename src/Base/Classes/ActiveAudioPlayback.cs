using System;
using System.Threading;
using System.Threading.Tasks;
using Cue2.Base.Classes;
using Godot;
using LibVLCSharp.Shared;

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

    private object _lock = new object(); // For thread safety
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
    

    [Signal]
    public delegate void CompletedEventHandler();
    
    public ActiveAudioPlayback()
    {
        // Blank constructor for Godot
    }
    
    public ActiveAudioPlayback(MediaPlayer mediaPlayer, uint sdlDevice, IntPtr audioStream, AudioOutputPatch patch, int outputChannel, AudioComponent audioComponent)
    {
        MediaPlayer = mediaPlayer;
        _sdlDevice = sdlDevice;
        _audioStream = audioStream;
        _patch = patch;
        _outputChannel = outputChannel;
        _audioComponent = audioComponent;

        _startTimeMs = (long)(_audioComponent.StartTime * 1000);
        _useCustomEnd = _audioComponent.EndTime >= 0;
        _endTimeMs = _useCustomEnd ? (long)(_audioComponent.EndTime * 1000) : (long)(_audioComponent.FileDuration * 1000);
        _effectivePlayCount = _audioComponent.Loop ? int.MaxValue : _audioComponent.PlayCount;
        
        MediaPlayer.EndReached += OnEndReached;
        if (_useCustomEnd)
        {
            MediaPlayer.TimeChanged += OnTimeChanged;
        }

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
            if (_isStopped) return;
            if (_isFadingOut)
            {
                // Immediate stop if already fading
                _fadeCts?.Cancel();
                MediaPlayer?.Stop();
                _isStopped = true;
                Clean();
                return;
            }
            _isFadingOut = true;
            _fadeCts = new CancellationTokenSource();
        }

        try
        {
            await SetVolumeAsync(0f, stopFadeDuration, _fadeCts.Token);
            
            lock (_lock)
            {
                if (!_isStopped)
                {
                    MediaPlayer?.Stop();
                    _isStopped = true;
                    Clean();
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
                    _isStopped = true;
                    MediaPlayer?.Stop();
                    Clean();
                }
            }
        }
        catch (Exception ex)
        {
            GD.Print($"ActiveAudioPlayback:Stop - Exception during fade-out: {ex.Message}");
            lock (_lock)
            {
                _isStopped = true;
                MediaPlayer?.Stop();
                Clean();
            }
        }
        finally
        {
            _fadeCts?.Dispose();
            _fadeCts = null;
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
            if (!_isStopped && !_isFadingOut) // Prevent pause during fade or after stop
            {
                MediaPlayer?.Pause();
            }
        }
    }

    /// <summary>
    /// Resumes the playback.
    /// </summary>
    public void Resume()
    {
        lock (_lock)
        {
            if (!_isStopped && !_isFadingOut) //Prevent resume during fade or after stop
            {
                MediaPlayer?.Play();
                
                // Set start time if audio hasn't started yet
                if (_hasStarted) return;
                if (MediaPlayer != null) MediaPlayer.Time = _startTimeMs;
                _hasStarted = true;
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
            await Task.Delay(25, ct);
            elapsed += 0.05f;
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
    
    private void OnTimeChanged(object sender, MediaPlayerTimeChangedEventArgs e)
    {
        lock (_lock)
        {
            if (_isStopped || _isFadingOut) return;
            if (e.Time >= _endTimeMs && !_reachedEnd)
            {
                _reachedEnd = true;
                GD.Print($"HANDLING THE END REACH");
                HandleEndReached();
            }
        }
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
                _endTimeMs = e.Length;
            }
        }
    }

    private void HandleEndReached()
    {
        if (_currentPlayCount < _effectivePlayCount)
        {
            _currentPlayCount++;
            GD.Print($"ActiveAudioPlayback:HandleEndReached - Setting media player time");
            if (_useCustomEnd)
            {
                CallDeferred(nameof(SetMediaTime), _startTimeMs);
            }
            else
            {
                CallDeferred(nameof(SetMediaTimeAndPlay), _startTimeMs);
            }
        }
        else
        {
            GD.Print($"ActiveAudioPlayback:HandleEndReached - Ooooooopsie");
            Clean();
        }
    }

    private void SetMediaTime(long time)
    {
        GD.Print($"ActiveAudioPlayback:SetMediaTime - Setting time to {time}");
        MediaPlayer.Time = time;
        _reachedEnd = false;
        GD.Print($"ActiveAudioPlayback:SetMediaTime - Time set successfully");
    }

    private void SetMediaTimeAndPlay(long time)
    {
        GD.Print($"ActiveAudioPlayback:SetMediaTimeAndPlay - Setting time to {time} and playing");
        MediaPlayer.Time = time;
        MediaPlayer.Play();
        _reachedEnd = false;
        GD.Print($"ActiveAudioPlayback:SetMediaTimeAndPlay - Time set and playback resumed");
    }

    // Dispose: Call Stop()
    public void Clean()
    {
        GD.Print($"ActiveAudioPlayback:Clean - {_audioComponent.AudioFile}");
        EmitSignal(SignalName.Completed);
        MediaPlayer?.Dispose();
    }
}