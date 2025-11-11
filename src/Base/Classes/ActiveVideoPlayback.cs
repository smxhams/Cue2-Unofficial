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
/// Encapsulates an active video playback session for control (volume, pause, stop, fade).
/// Thread-safe for multi-threaded access (e.g., UI updates).
/// </summary>
public partial class ActiveVideoPlayback : GodotObject
{
    public FFmpegAudioDecoder Decoder { get; private set; }
    public AudioOutputPatch Patch;
    public CuePatch CuePatch { get; set; }
    public Dictionary<uint, IntPtr> DeviceStreams { get; set; }
    public int SourceChannels { get; set; }
    public int SourceSampleRate { get; set; }
    public int SourceBytesPerFrame { get; set; }
    public SDL.AudioFormat SourceFormat { get; set; }
    
    private readonly VideoComponent _videoComponent;
    private AudioDevices _audioDevices;
        
    
    private readonly object _lock = new object(); // For thread safety
    private float _volume = 1.0f; // Normalized [0-1], global multiplier
    private float[] _channelGains; // Per-channel volume multipliers
    private bool _isFadingOut = false;
    private bool _isFadingIn = false;
    public bool IsStopped = false;
    public bool IsPaused = false;
    public bool IsSeeking = false;
    private CancellationTokenSource _fadeCts;
    
    private long _startTimeMs = 0;
    private long _endTimeMs;
    private bool _useCustomEnd;
    private int _currentPlayCount = 1;
    public int EffectivePlayCount;
    private bool _hasStarted = false;
    private bool _reachedEnd = false;

    private readonly Stopwatch _playTimer = new Stopwatch();
    private long _pausedAtUs = 0; // Stored pause position in us for resume seek

    [Signal] public delegate void CompletedEventHandler();
    
    public ActiveVideoPlayback()
    {
        // Blank constructor for Godot
    }

    public ActiveVideoPlayback(VideoComponent videoComponent, AudioDevices audioDevices)
    {
        _videoComponent = videoComponent ?? throw new ArgumentNullException(nameof(videoComponent));
        _audioDevices = audioDevices ?? throw new ArgumentNullException(nameof(audioDevices));

        if (_videoComponent.UseAudio)
        {
            Patch = _videoComponent.Patch;
            CuePatch = _videoComponent.Routing;
        }
        
        // Validate and set start time
        if (_videoComponent.StartTime < 0)
        {
            GD.Print($"ActiveAudioPlayback:Constructor - Invalid start time: {_videoComponent.StartTime}, defaulting to 0");
        }
        else
        {
           _startTimeMs = (long)(_videoComponent.StartTime * 1000); // Seconds to ms
        }

        _useCustomEnd = _videoComponent.EndTime >= 0;
        _endTimeMs = _useCustomEnd ? (long)(_videoComponent.EndTime * 1000) : (long)(_videoComponent.Metadata.Duration * 1000);
        EffectivePlayCount = _videoComponent.Loop ? int.MaxValue : _videoComponent.PlayCount;
        
        // Check start time is not later than file duration
        if (_videoComponent.Metadata.Duration > 0 && _startTimeMs > (long)(_videoComponent.Metadata.Duration * 1000))
        {
            _startTimeMs = 0;
        }
    }

    public async Task InitAsync()
    {

    }

    /// <summary>
    /// Pushes a decoded RGB frame to the video output.
    /// </summary>
    /// <param name="rgbData">The RGB24 frame data.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    public void PushFrame(byte[] rgbData, int width, int height)
    {
        // TODO: Update Godot texture (e.g., ImageTexture from rgbData)
        // For now, placeholder
        GD.Print($"ActiveVideoPlayback:PushFrame - Received frame {width}x{height}, size {rgbData.Length}");
    }
    
}