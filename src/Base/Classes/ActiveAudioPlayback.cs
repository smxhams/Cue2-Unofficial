using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Godot;
using SDL3;

namespace Cue2.Base.Classes;

/// <summary>
/// Encapsulates an active audio playback session for control (volume, pause, stop, fade).
/// Thread-safe for multi-threaded access (e.g., UI updates).
/// </summary>
public partial class ActiveAudioPlayback : GodotObject
{
    public FFmpegAudioDecoder Decoder { get; private set; }
    public AudioOutputPatch Patch;
    public CuePatch CuePatch { get; set; }
    public Dictionary<uint, IntPtr> DeviceStreams { get; set; }
    public int SourceChannels { get; set; }
    public int SourceSampleRate { get; set; }
    public int SourceBytesPerFrame { get; set; }
    public SDL.AudioFormat SourceFormat { get; set; }
    
    private readonly AudioComponent _audioComponent;
        
    
    private readonly object _lock = new object(); // For thread safety
    private float _volume = 1.0f; // Normalized [0-1]
    private bool _isFadingOut = false;
    public bool IsStopped = false;
    private CancellationTokenSource _fadeCts;
    
    private long _startTimeMs;
    private long _endTimeMs;
    private bool _useCustomEnd;
    private int _currentPlayCount = 1;
    public int EffectivePlayCount;
    private bool _hasStarted = false;
    private bool _reachedEnd = false;

    private readonly Stopwatch _playTimer = new Stopwatch();

    [Signal] public delegate void CompletedEventHandler();
    
    public ActiveAudioPlayback()
    {
        // Blank constructor for Godot
    }
    
    public ActiveAudioPlayback(AudioComponent audioComponent)
    {
        _audioComponent = audioComponent ?? throw new ArgumentNullException(nameof(audioComponent));
        Decoder = new FFmpegAudioDecoder(audioComponent, this);
        
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
        _endTimeMs = _useCustomEnd ? (long)(_audioComponent.EndTime * 1000) : (long)(_audioComponent.Metadata.Duration * 1000);
        EffectivePlayCount = _audioComponent.Loop ? int.MaxValue : _audioComponent.PlayCount;
        
        // Validate start time against file duration if availible
        if (_audioComponent.Metadata.Duration > 0 && _startTimeMs > (long)(_audioComponent.Metadata.Duration * 1000))
        {
            _startTimeMs = 0;
        }
        
        //Decoder.EndReached += OnEndReached;
        //Decoder.LengthChanged += OnLengthChanged;
    }

    public async Task InitAsync()
    {
        await Decoder.InitAsync();
        SourceChannels = _audioComponent.Metadata.Channels;
        SourceSampleRate = Decoder.TargetSampleRate;
        SourceFormat = Decoder.TargetFormat;
        SourceBytesPerFrame = SourceChannels * (GetBitDepth(SourceFormat) / 8);
        GD.Print("ActiveAudioPlayback:InitAsync - Initialized FFmpeg decoder with sample rate " + SourceSampleRate);
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
        set
        {
            lock (_lock)
            {
                _currentPlayCount = value;
            }
        }
    }
    

    public void Play()
    {
        lock (_lock)
        {
            if (_hasStarted) return;

            Decoder.PlayAsync();
            _playTimer.Start();
            _hasStarted = true;
            GD.Print($"ActiveAudioPlayback:Play - Playback started");
        }
    }
    
    public void Pause()
    {
        lock (_lock)
        {
            Decoder.Pause();
            _playTimer.Stop();
            GD.Print($"ActiveAudioPlayback:Pause - Playback paused");
        }
    }
    
    /// <summary>
    /// Stops and cleans up the playback resources.
    /// </summary>
    public async Task Stop(double fadeTime = 0.0)
    {
        lock (_lock)
        {
            if (IsStopped) return;
            if (fadeTime > 0)
            {
                _ = FadeOutAsync(fadeTime);
                return;
            }
        }
        Decoder.Stop();
        Clean();
    }
    
    public async Task FadeOutAsync(double duration)
    {
        lock (_lock)
        {
            if (_isFadingOut) return;
            _isFadingOut = true;
            _fadeCts = new CancellationTokenSource();
        }

        float startVol = _volume;
        Stopwatch timer = Stopwatch.StartNew();

        while (timer.Elapsed.TotalSeconds < duration)
        {
            if (_fadeCts.Token.IsCancellationRequested) break;

            float t = (float)(timer.Elapsed.TotalSeconds / duration);
            SetVolume(Mathf.Lerp(startVol, 0f, t));
            await Task.Delay(16); // ~60fps
        }

        SetVolume(0f);
        Decoder.Stop();
        Clean();

        lock (_lock)
        {
            _isFadingOut = false;
        }
    }
    

    public void SetVolume(float volume)
    {
        lock (_lock)
        {
            _volume = Mathf.Clamp(volume, 0f, 1f);
            //Decoder.SetVolume(_volume);
        }
    }
    
    public double GetRemainingTime()
    {
        lock (_lock)
        {
            if (_audioComponent.Loop) return -1.0;

            double segmentDuration = _useCustomEnd ? (_endTimeMs - _startTimeMs) / 1000.0 : _audioComponent.Metadata.Duration - _audioComponent.StartTime;
            double remainingInSegment = segmentDuration - (Decoder.CurrentTime / 1000.0);
            int remainingCounts = EffectivePlayCount - _currentPlayCount;

            return remainingInSegment + remainingCounts * segmentDuration;
        }
    }
    
    private void OnLengthChanged(object sender, long length)
    {
        lock (_lock)
        {
            if (!_useCustomEnd)
            {
                _endTimeMs = length;
                GD.Print($"ActiveAudioPlayback:OnLengthChanged - Length set to {_endTimeMs} ms");
            }
        }
    }
    
    private void OnEndReached(object sender, EventArgs e)
    {
        lock (_lock)
        {
            _reachedEnd = true;
        }
        CallDeferred(nameof(HandleEndReached));
    }

    private void HandleEndReached()
    {
        lock (_lock)
        {
            if (_reachedEnd && _currentPlayCount < EffectivePlayCount)
            {
                _currentPlayCount++;
                ResetForLoop();
                GD.Print($"ActiveAudioPlayback:HandleEndReached - Looping to play count {_currentPlayCount}");
            }
            else
            {
                GD.Print($"ActiveAudioPlayback:HandleEndReached - Playback completed");
                CallDeferred(nameof(Clean));
            }
        }
    }


    public void ResetForLoop()
    {
        Decoder.Seek(_startTimeMs * 1000);
        _playTimer.Reset();
        _playTimer.Start();
        _reachedEnd = false;
        foreach (var stream in DeviceStreams.Values)
        {
            SDL.ClearAudioStream(stream); // New: Flush SDL buffers to prevent old data garbling loop
        }
        GD.Print($"ActiveAudioPlayback:ResetForLoop - Reset for loop and cleared SDL streams");
    }
    
    public void Clean()
    {
        lock (_lock)
        {
            GD.Print($"ActiveAudioPlayback:Clean - Clean Start");
            if (IsStopped)
            {
                GD.Print("ActiveAudioPlayback:Clean - Already cleaned");
                return;
            }

            IsStopped = true;
            _playTimer.Stop(); // Stop timer first

            // Stop and dispose decoder
            if (Decoder != null)
            {
                try
                {
                    Decoder.Stop();
                    GD.Print($"ActiveAudioPlayback:Clean - Decoder stopped");
                }
                catch (Exception ex)
                {
                    GD.Print($"ActiveAudioPlayback:Clean - Exception stopping Decoder: {ex.Message}");
                    GD.Print($"ActiveAudioPlayback:Clean - Exception stopping Decoder: {ex.Message}");
                }

                try
                {
                    Decoder.EndReached -= OnEndReached;
                    //Decoder.LengthChanged -= OnLengthChanged;
                    Decoder.Dispose();
                    GD.Print($"ActiveAudioPlayback:Clean - Decoder disposed");
                }
                catch (Exception ex)
                {
                    GD.Print($"ActiveAudioPlayback:Clean - Exception disposing Decoder: {ex.Message}");
                    GD.Print($"ActiveAudioPlayback:Clean - Exception disposing Decoder: {ex.Message}");
                }
                Decoder = null; // Prevent accidental reuse
            }

            // Clean up SDL audio streams
            foreach (var stream in DeviceStreams.Values)
            {
                try
                {
                    SDL.DestroyAudioStream(stream);
                    GD.Print($"ActiveAudioPlayback:Clean - Destroyed SDL stream");
                }
                catch (Exception ex)
                {
                    GD.Print($"ActiveAudioPlayback:Clean - Exception destroying SDL stream: {ex.Message}");
                }
            }
            DeviceStreams.Clear();
            GD.Print($"ActiveAudioPlayback:Clean - DeviceStreams cleared");
        }
    
        CallDeferred(nameof(EmitCompletedSignal)); // Defer signal emission
    }
    
    private void EmitCompletedSignal()
    {
        EmitSignal(SignalName.Completed);
        GD.Print($"ActiveAudioPlayback:EmitCompletedSignal - Completed signal emitted");
    }

    public unsafe void PushPcm(byte[] pcm)
    {
        foreach (var kv in DeviceStreams)
        {
            fixed (byte* p = pcm)
            {
                SDL.PutAudioStreamData(kv.Value, (IntPtr)p, pcm.Length);
            }
        }
    }

    private static int GetBitDepth(SDL.AudioFormat format) // Moved from AudioDevices for reuse
    {
        switch (format)
        {
            case SDL.AudioFormat.AudioU8:
            case SDL.AudioFormat.AudioS8:
                return 8;
            case SDL.AudioFormat.AudioS16BE:
            case SDL.AudioFormat.AudioS16LE:
                return 16;
            case SDL.AudioFormat.AudioF32BE:
            case SDL.AudioFormat.AudioF32LE:
            case SDL.AudioFormat.AudioS32BE:
            case SDL.AudioFormat.AudioS32LE:
                return 32;
            default:
                return 0; // Unknown or unsupported format
        }
    }
    
}