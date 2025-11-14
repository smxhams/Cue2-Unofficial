using System;
using System.Buffers;
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
public partial class ActiveVideoPlayback : Node
{
    public FFmpegVideoDecoderOld Decoder { get; private set; }
    public AudioOutputPatch Patch;
    public CuePatch CuePatch { get; set; }
    public Dictionary<uint, IntPtr> DeviceStreams { get; set; }
    public int SourceChannels { get; set; }
    public int SourceSampleRate { get; set; }
    public int SourceBytesPerFrame { get; set; }
    public SDL.AudioFormat SourceFormat { get; set; }
    
    private readonly VideoComponent _videoComponent;
    private AudioDevices _audioDevices;
    private VideoTargetLayer _targetLayer;
    private TextureRect _videoRect;
    private Godot.Image _videoImage;
    private ImageTexture _videoTexture;

    // Embedded audio handling
    private ActiveAudioPlayback _embeddedAudioPlayback;
    private AudioComponent _embeddedAudioComponent;

    
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
        Decoder = new FFmpegVideoDecoderOld(videoComponent, this);

        // Find target layer
        _targetLayer = DisplaysManager.Layers.Find(l => l.LayerId == _videoComponent.TargetLayerId);
        if (_targetLayer == null)
        {
            GD.PrintErr($"ActiveVideoPlayback:Constructor - Target layer {_videoComponent.TargetLayerId} not found.");
            return;
        }

        /*
        if (_videoComponent.UseAudio)
        {
            Patch = _videoComponent.Patch;
            CuePatch = _videoComponent.Routing;

            // Setup embedded audio if video has audio
            if (_videoComponent.Metadata.AudioChannels > 0)
            {
                _embeddedAudioComponent = new AudioComponent
                {
                    AudioFile = _videoComponent.VideoFile,
                    StartTime = _videoComponent.StartTime,
                    EndTime = _videoComponent.EndTime,
                    Volume = _videoComponent.Volume,
                    Loop = _videoComponent.Loop,
                    PlayCount = _videoComponent.PlayCount,
                    Metadata = new AudioFileMetadata
                    {
                        Duration = _videoComponent.Metadata.Duration,
                        Channels = _videoComponent.Metadata.AudioChannels,
                        SampleRate = _videoComponent.Metadata.AudioSampleRate,
                        BitDepth = _videoComponent.Metadata.AudioBitDepth,
                        Codec = _videoComponent.Metadata.AudioCodec,
                        Format = _videoComponent.Metadata.Format
                    }
                };
                _embeddedAudioPlayback = new ActiveAudioPlayback(_embeddedAudioComponent, _audioDevices);
                _embeddedAudioPlayback.Patch = Patch;
                _embeddedAudioPlayback.CuePatch = CuePatch;
            }
        }*/

        // Validate and set start time
        if (_videoComponent.StartTime < 0)
        {
            GD.Print($"ActiveVideoPlayback:Constructor - Invalid start time: {_videoComponent.StartTime}, defaulting to 0");
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
        
        //Decoder.EndReached += OnEndReached;
        //Decoder.LengthChanged += OnLengthChanged;
        
    }

    public async Task InitAsync()
    {
        GD.Print($"ActiveVideoPlayback:InitAsync - Initializing...");
        await Decoder.InitAsync();

        // Init embedded audio if present
        if (_embeddedAudioPlayback != null)
        {
            await _embeddedAudioPlayback.InitAsync();
        }

        // Setup video TextureRect on the target layer
        if (_targetLayer != null)
        {
            _videoRect = new TextureRect();
            _videoRect.Position = new Vector2(_videoComponent.OffsetX, _videoComponent.OffsetY);
            _videoRect.Size = new Vector2(_videoComponent.ScaledWidth, _videoComponent.ScaledHeight);
            _videoRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            _videoRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;

            _videoImage = Godot.Image.CreateEmpty(_videoComponent.ScaledWidth, _videoComponent.ScaledHeight, false, Godot.Image.Format.Rgba8);
            _videoTexture = ImageTexture.CreateFromImage(_videoImage);
            _videoRect.Texture = _videoTexture;

            _targetLayer.AddContent(_videoRect);
            GD.Print($"ActiveVideoPlayback:InitAsync - Added video rect to layer '{_targetLayer.LayerName}' at ({_videoComponent.OffsetX}, {_videoComponent.OffsetY}) size {_videoComponent.ScaledWidth}x{_videoComponent.ScaledHeight}");
        }

        GD.Print($"ActiveVideoPlayback:InitAsync - Initializing complete");
    }

    /// <summary>
    /// Pushes a decoded RGB frame to the video output.
    /// </summary>
    /// <param name="rgbData">The RGB24 frame data.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    public void PushFrame(byte[] rgbData, int width, int height)
    {
        if (_videoRect == null || _videoImage == null || _videoTexture == null) return;

        // Create temporary image from RGB data
        Godot.Image tempImage = Godot.Image.CreateFromData(width, height, false, Godot.Image.Format.Rgb8, rgbData);
        try
        {
            // Resize to scaled dimensions if necessary
            if (tempImage.GetWidth() != _videoComponent.ScaledWidth || tempImage.GetHeight() != _videoComponent.ScaledHeight)
            {
                tempImage.Resize(_videoComponent.ScaledWidth, _videoComponent.ScaledHeight, Godot.Image.Interpolation.Bilinear);
            }

            // Convert to RGBA8
            tempImage.Convert(Godot.Image.Format.Rgba8);

            // Update the video image and texture
            _videoImage.SetData(_videoComponent.ScaledWidth, _videoComponent.ScaledHeight, false, Godot.Image.Format.Rgba8, tempImage.GetData());
            _videoTexture.Update(_videoImage);

            // Update texture on main thread if needed
            this.CallDeferred(nameof(UpdateTexture));
        }
        finally
        {
            tempImage.Dispose();
        }
    }

    private void UpdateTexture()
    {
        // Texture is already updated, but if needed for deferred
    }

    public async void Play()
    {
        lock (_lock)
        {
            if (_hasStarted) return;
            _hasStarted = true;
        }

        if (_videoComponent.FadeInDuration > 0) // start with fade-in if specified
        {
            await FadeInAsync(_videoComponent.FadeInDuration);
        }
        else
        {
            Decoder.PlayAsync();
            if (_embeddedAudioPlayback != null) _embeddedAudioPlayback.Play();
            _playTimer.Start();
            GD.Print($"ActiveVideoPlayback:Play - Playback started without fade-in");
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            Decoder.Pause();
            if (_embeddedAudioPlayback != null) _embeddedAudioPlayback.Pause();
            _pausedAtUs = Decoder.CurrentTime - GetQueuedUs(); // Estimate actual position at pause
            IsPaused = true;
            Decoder.ClearQueues(); // Clear frame queue to avoid stale data on resume
            _playTimer.Stop();
            GD.Print($"ActiveVideoPlayback:Pause - Playback paused at estimated {_pausedAtUs / 1000} ms");
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (_pausedAtUs > 0)
            {
                Decoder.Seek(_pausedAtUs); // Seek back to paused position
                if (_embeddedAudioPlayback != null) _embeddedAudioPlayback.Seek(_pausedAtUs);
                _pausedAtUs = 0;
            }
            Decoder.Resume();
            if (_embeddedAudioPlayback != null) _embeddedAudioPlayback.Resume();
            IsPaused = false;
            _playTimer.Start();
            GD.Print($"ActiveVideoPlayback:Resume - Playback resumed");
        }
    }

    /// <summary>
    /// Stops and cleans up the playback resources.
    /// </summary>
    public async Task Stop(double fadeTime = 0.0)
    {
        bool needFade;
        double fadeDuration;
        bool wasFadingOut;
        lock (_lock)
        {
            if (IsStopped) return;
            wasFadingOut = _isFadingOut;
            _fadeCts?.Cancel();
            needFade = fadeTime > 0 || _videoComponent.FadeOutDuration > 0;
            fadeDuration = fadeTime > 0 ? fadeTime : _videoComponent.FadeOutDuration;
        }

        if (wasFadingOut)
        {
            // If already fading out, immediately stop and clean
            Decoder.Stop();
            if (_embeddedAudioPlayback != null) _embeddedAudioPlayback.Stop();
            Clean();
            return;
        }

        if (needFade) // Use component duration if set
        {
            await FadeOutAsync(fadeDuration);
            return;
        }
        Decoder.Stop();
        if (_embeddedAudioPlayback != null) _embeddedAudioPlayback.Stop();
        Clean();
    }

    public async Task FadeInAsync(double duration)
    {
        lock (_lock)
        {
            if (_isFadingIn || _isFadingOut) return;
            _isFadingIn = true;
            _fadeCts = new CancellationTokenSource();
        }

        float startVol = 0f;
        float endVol = 1.0f;
        Stopwatch timer = Stopwatch.StartNew();

        try
        {
            Decoder.PlayAsync(); // Start playback
            if (_embeddedAudioPlayback != null) _embeddedAudioPlayback.Play();
            _playTimer.Start();
            while (timer.Elapsed.TotalSeconds < duration && !_fadeCts.Token.IsCancellationRequested)
            {
                float t = (float)(timer.Elapsed.TotalSeconds / duration);
                SetVolume(Mathf.Lerp(startVol, endVol, t));
                await Task.Delay(16, _fadeCts.Token); // ~60fps
            }

            if (!_fadeCts.Token.IsCancellationRequested)
            {
                SetVolume(endVol);
                GD.Print($"ActiveVideoPlayback:FadeInAsync - Fade-in completed to {endVol} over {duration} seconds");
            }
        }
        catch (OperationCanceledException)
        {
            GD.Print($"ActiveVideoPlayback:FadeInAsync - Fade-in cancelled");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveVideoPlayback:FadeInAsync - Error: {ex.Message}");
        }
        finally
        {
            lock (_lock)
            {
                _isFadingIn = false;
                _fadeCts?.Dispose();
                _fadeCts = null;
            }
        }
    }

    public async Task FadeOutAsync(double duration)
    {
        lock (_lock)
        {
            if (_isFadingOut || _isFadingIn) return; // Prevent concurrent fades
            _isFadingOut = true;
            _fadeCts = new CancellationTokenSource();
        }

        float startVol = _volume;
        Stopwatch timer = Stopwatch.StartNew();

        try
        {
            while (timer.Elapsed.TotalSeconds < duration && !_fadeCts.Token.IsCancellationRequested)
            {
                float t = (float)(timer.Elapsed.TotalSeconds / duration);
                SetVolume(Mathf.Lerp(startVol, 0f, t));
                await Task.Delay(16, _fadeCts.Token); // ~60fps
            }

            if (!_fadeCts.Token.IsCancellationRequested)
            {
                SetVolume(0f);
                Decoder.Stop();
                if (_embeddedAudioPlayback != null) _embeddedAudioPlayback.Stop();
                Clean();
                GD.Print($"ActiveVideoPlayback:FadeOutAsync - Fade-out completed over {duration} seconds");
            }
        }
        catch (OperationCanceledException)
        {
            GD.Print($"ActiveVideoPlayback:FadeOutAsync - Fade-out cancelled");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveVideoPlayback:FadeOutAsync - Error: {ex.Message}");
        }
        finally
        {
            lock (_lock)
            {
                _isFadingOut = false;
                _fadeCts?.Dispose();
                _fadeCts = null;
            }
        }
    }

    public void SetVolume(float volume)
    {
        lock (_lock)
        {
            _volume = Mathf.Clamp(volume, 0f, 1f);
            if (_embeddedAudioPlayback != null) _embeddedAudioPlayback.SetVolume(volume);
        }
    }

    public double GetRemainingTime()
    {
        lock (_lock)
        {
            if (_videoComponent.Loop) return -1.0;

            double segmentDuration = _useCustomEnd ? (_endTimeMs - _startTimeMs) / 1000.0 : _videoComponent.Metadata.Duration - _videoComponent.StartTime;
            double remainingInSegment = segmentDuration - (GetPlaybackTimeMs() / 1000.0);
            int remainingCounts = EffectivePlayCount - _currentPlayCount;

            return remainingInSegment + remainingCounts * segmentDuration;
        }
    }

    public long GetPlaybackTimeMs()
    {
        if (Decoder == null) return 0;
        long queuedUs = Decoder.QueuedFrames * 1_000_000L / (long)_videoComponent.Metadata.FrameRate; // Approximate
        return (Decoder.CurrentTime - queuedUs) / 1000;
    }

    private long GetQueuedUs()
    {
        long queuedUs = Decoder.QueuedFrames * 1_000_000L / (long)_videoComponent.Metadata.FrameRate;
        return queuedUs;
    }

    /// <summary>
    /// Seeks to the specified timestamp in microseconds, clearing queues to avoid stale data.
    /// Works while playing or paused; playback state is preserved.
    /// </summary>
    /// <param name="timestampUs">Target timestamp in us.</param>
    public void Seek(long timestampUs)
    {
        try
        {
            bool wasPlaying = Decoder.IsPlaying && !Decoder.IsPaused; // (preserve state)
            Pause(); // (pause to safely seek)
            Decoder.ClearQueues(); // (clear frame queue)
            Decoder.Seek(timestampUs);
            if (_embeddedAudioPlayback != null) _embeddedAudioPlayback.Seek(timestampUs);
            _pausedAtUs = timestampUs; // Set paused position to the seek target
            if (wasPlaying) Resume(); // (resume if was playing)
            GD.Print($"ActiveVideoPlayback:Seek - Sought to {timestampUs} us");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveVideoPlayback:Seek - Seek error: {ex.Message}");
        }
    }

    public void Clean()
    {
        lock (_lock)
        {
            GD.Print($"ActiveVideoPlayback:Clean - Clean Start");
            if (IsStopped)
            {
                GD.Print("ActiveVideoPlayback:Clean - Already cleaned");
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
                    GD.Print($"ActiveVideoPlayback:Clean - Decoder stopped");
                }
                catch (Exception ex)
                {
                    GD.Print($"ActiveVideoPlayback:Clean - Exception stopping Decoder: {ex.Message}");
                    GD.PrintErr($"ActiveVideoPlayback:Clean - Decoder stop failed: {ex.Message}");
                }

                try
                {
                    Decoder.Dispose();
                    GD.Print($"ActiveVideoPlayback:Clean - Decoder disposed");
                }
                catch (Exception ex)
                {
                    GD.Print($"ActiveVideoPlayback:Clean - Exception disposing Decoder: {ex.Message}");
                    GD.PrintErr($"ActiveVideoPlayback:Clean - Decoder dispose failed: {ex.Message}");
                }
                Decoder = null; // Prevent accidental reuse
            }

            // Stop and dispose embedded audio
            if (_embeddedAudioPlayback != null)
            {
                try
                {
                    _embeddedAudioPlayback.Stop();
                    GD.Print($"ActiveVideoPlayback:Clean - Embedded audio stopped");
                }
                catch (Exception ex)
                {
                    GD.Print($"ActiveVideoPlayback:Clean - Exception stopping embedded audio: {ex.Message}");
                }
                _embeddedAudioPlayback = null;
            }

            // Remove video rect from layer
            if (_videoRect != null && _targetLayer != null)
            {
                _targetLayer.RemoveContent(_videoRect);
                _videoRect.QueueFree();
                _videoRect = null;
            }
        }

        EmitSignal(SignalName.Completed); // Emit signal immediately before freeing
        CallDeferred("free");
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
    /// Thread safe get of current fade in state
    /// </summary>
    public bool IsFadingIn
    {
        get
        {
            lock (_lock)
            {
                return _isFadingIn;
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

}