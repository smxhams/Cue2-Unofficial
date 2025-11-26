using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Godot;
using SDL3;
using Image = Godot.Image;

namespace Cue2.Base.Classes;

/// <summary>
/// Encapsulates an active video playback session for control (volume, pause, stop, fade).
/// Supports embedded audio playback with routing and patching capabilities.
/// Thread-safe for multi-threaded access (e.g., UI updates).
/// </summary>
public partial class ActiveVideoPlayback : Node, IAudioPlayback
{
    private const int FadeUpdateIntervalMs = 16;
    private const int TaskWaitTimeoutMs = 5000;
    private const long MicrosecondsPerSecond = 1_000_000;

    private FFmpegVideoDecoder _videoDecoder;
    private ImageTexture _godotTexture;
    private Image _godotImage;
    
    
    // For embedded audio playback
    /// <summary>
    /// Gets or sets the audio output patch for routing audio to devices.
    /// </summary>
    /// <value>The audio output patch configuration.</value>
    public AudioOutputPatch Patch { get; set; }
    /// <summary>
    /// Gets or sets the cue patch for audio routing and volume adjustments.
    /// </summary>
    /// <value>The cue patch configuration.</value>
    public CuePatch Routing { get; set; }
    /// <summary>
    /// Gets or sets the direct output device name for audio playback.
    /// </summary>
    /// <value>The name of the direct output device.</value>
    public string DirectOutput { get; set; }
    /// <summary>
    /// Gets or sets the dictionary of audio device streams keyed by device ID.
    /// </summary>
    /// <value>The device streams for audio output.</value>
    public Dictionary<uint, IntPtr> DeviceStreams { get; set; }
    /// <summary>
    /// Gets or sets the number of audio source channels.
    /// </summary>
    /// <value>The number of source channels.</value>
    public int SourceChannels { get; set; }
    /// <summary>
    /// Gets or sets the audio source sample rate in Hz.
    /// </summary>
    /// <value>The source sample rate.</value>
    public int SourceSampleRate { get; set; }
    /// <summary>
    /// Gets or sets the number of bytes per audio frame.
    /// </summary>
    /// <value>The bytes per frame.</value>
    public int SourceBytesPerFrame { get; set; }
    /// <summary>
    /// Gets or sets the audio source format.
    /// </summary>
    /// <value>The source audio format.</value>
    public SDL.AudioFormat SourceFormat { get; set; }
    
    private readonly VideoComponent _videoComponent;
    private AudioDevices _audioDevices;
    private VideoTargetLayer _targetLayer;

    // Embedded audio handling
    private FFmpegAudioDecoder _audioDecoder;
    private float[] _audioChannelGains;
    private CancellationTokenSource _audioCts;
    private Task _audioConsumerTask;
    private Dictionary<uint, string> _deviceNameCache = new();

    private Dictionary<Control, TextureRect> _targetLayers = new();

    
    private readonly object _lock = new object(); // For thread safety
    private float _volume = 1.0f; // Normalized [0-1], global multiplier
    private float[] _channelGains; // Per-channel volume multipliers
    private bool _isFadingOut = false;
    private bool _isFadingIn = false;
    /// <summary>
    /// Gets or sets a value indicating whether the playback is stopped.
    /// </summary>
    /// <value>true if stopped; otherwise, false.</value>
    public bool IsStopped = false;
    /// <summary>
    /// Gets or sets a value indicating whether the playback is paused.
    /// </summary>
    /// <value>true if paused; otherwise, false.</value>
    public bool IsPaused = false;
    /// <summary>
    /// Gets or sets a value indicating whether the playback is currently seeking.
    /// </summary>
    /// <value>true if seeking; otherwise, false.</value>
    public bool IsSeeking = false;
    /// <summary>
    /// Gets or sets a value indicating whether the playback is exiting.
    /// </summary>
    /// <value>true if exiting; otherwise, false.</value>
    public bool IsExiting = false;
    private float _startAlpha = 1.0f;
    private float _fadeAlpha = 1.0f;
    private CancellationTokenSource _fadeCts;
    
    private long _startTimeMs = 0;
    private long _endTimeMs;
    private bool _useCustomEnd;
    private int _currentPlayCount = 1;
    public int EffectivePlayCount;
    private bool _hasStarted = false;
    private bool _reachedEnd = false;
    
    private long _pausedAtUs = 0; // Stored pause position in us for resume seek
    private bool _isExiting = false;
    private bool _completedEmitted = false;
    private bool _isDisposed = false;
    private int _playCountRemaining = 0;
    
    [Signal] public delegate void CompletedEventHandler();
    [Signal] public delegate void TimeUpdatedEventHandler(double time);

    public ActiveVideoPlayback()
    {
        // Blank constructor for Godot
    }

    public bool UseAudio => _videoComponent.UseAudio && _videoComponent.Metadata.AudioChannels > 0;

    public ActiveVideoPlayback(VideoComponent videoComponent, AudioDevices audioDevices)
    {
        _videoComponent = videoComponent ?? throw new ArgumentNullException(nameof(videoComponent));
        _audioDevices = audioDevices ?? throw new ArgumentNullException(nameof(audioDevices));

        LoadVideoDecoder();
        if (UseAudio)
        {
            LoadAudioDecoder();
        }
        
        // Load needed parameters from component
        if (UseAudio)
        {
            Patch = videoComponent.Patch;
            Routing = videoComponent.Routing;
            DirectOutput = videoComponent.DirectOutput;
        }

        // Find target layer
        _targetLayer = DisplaysManager.Layers.Find(l => l.LayerId == _videoComponent.TargetLayerId);
        if (_targetLayer == null)
        {
            GD.PrintErr($"ActiveVideoPlayback:Constructor - Target layer {_videoComponent.TargetLayerId} not found.");
            return;
        }
        
        _godotImage = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        _godotTexture = ImageTexture.CreateFromImage(_godotImage);

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
    }

    private void LoadVideoDecoder()
    {
        if (_videoDecoder != null)
        {
            ClearVideoDecoder();
        }

        _videoDecoder = new FFmpegVideoDecoder(this);
        _videoDecoder.FrameReady += OnFrameReady;
        _videoDecoder.TimeUpdated += OnTimeUpdated;
        _videoDecoder.EndReached += OnEndReached;
    }

    private void LoadAudioDecoder()
    {
        if (_audioDecoder != null)
        {
            ClearAudioDecoder();
        }
        
        _audioDecoder = new FFmpegAudioDecoder(_videoComponent, this);

    }

    private async Task InitVideoAsync()
    {
        if (_videoDecoder == null)
        {
            LoadVideoDecoder();
        }

        await _videoDecoder.StartDecodingAsync(_videoComponent.VideoFile);

        _videoDecoder.TimeUpdated += OnVideoTimeUpdated;

        if (_videoComponent.StartTime > 0)
        {
            _videoDecoder.Seek(_videoComponent.StartTime);
        }

        _godotImage = Image.CreateEmpty(_videoDecoder.Width, _videoDecoder.Height, false, Image.Format.Rgba8);
        _godotTexture = ImageTexture.CreateFromImage(_godotImage);

        foreach (var display in DisplaysManager.Outputs)
        {
            var layerControl = display.AddLayer(_videoComponent.TargetLayerId);
            var layerTextRect = layerControl.GetNode<TextureRect>("%LayerOutput");
            layerTextRect.Texture = _godotTexture;
            _targetLayers.Add(layerControl, layerTextRect);
            // Connect to TreeExited to remove reference when layer is destroyed
            layerTextRect.TreeExited += () => OnLayerExited(layerTextRect);
        }

        if (_targetLayers.Count == 0)
        {
            _isExiting = true;
            EmitSignalCompleted();
            Clean();
            return;
        }
    }

    private async Task InitAudioAsync()
    {
        // Init embedded audio if present
        if (_audioDecoder != null)
        {
            await _audioDecoder.InitAsync();
            SourceChannels = _videoComponent.Metadata.AudioChannels;
            SourceSampleRate = _audioDecoder.OutputSampleRate;
            SourceFormat = _audioDecoder.TargetFormat;
            SourceBytesPerFrame = SourceChannels * (AudioDevices.GetBitDepth(SourceFormat) / 8);
            _channelGains = new float[SourceChannels];
            UpdateChannelGains();

            if (_videoComponent.StartTime > 0)
            {
                _audioDecoder.Seek((long)(_videoComponent.StartTime * MicrosecondsPerSecond));
            }
        }
    }

    /// <summary>
    /// Initializes the playback asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitAsync()
    {
        GD.Print($"ActiveVideoPlayback:InitAsync - Initializing...");
        _playCountRemaining = _videoComponent.PlayCount;
        await InitVideoAsync();
        if (_isExiting) return; // If video init failed
        await InitAudioAsync();
        GD.Print($"ActiveVideoPlayback:InitAsync - Initializing complete");
    }

    /// <summary>
    /// Invokes the frame ready event on the main thread.
    /// </summary>
    /// <param name="data">The frame data.</param>
    public void InvokeFrameReady(byte[] data)
    {
        OnFrameReady(data);
    }

    /// <summary>
    /// Invokes the time updated event.
    /// </summary>
    /// <param name="time">The current time.</param>
    public void InvokeTimeUpdated(double time)
    {
        OnTimeUpdated(time);
    }

    /// <summary>
    /// Invokes the end reached event.
    /// </summary>
    public void InvokeEndReached()
    {
        OnEndReached();
    }

    private void OnLayerExited(TextureRect layer)
    {
        // Remove the layer from _targetLayers to prevent invalid references
        lock (_lock)
        {
            foreach (var kv in _targetLayers)
            {
                if (kv.Value == layer)
                {
                    _targetLayers.Remove(kv.Key);
                    GD.Print($"ActiveVideoPlayback:OnLayerExited - Removed reference to destroyed layer");
                    break;
                }
            }
            // Layer removed, no action needed as cleanup is handled by end-reached
        }
    }

    private void OnFrameReady(byte[] data)
    {
        // Pushs data back onto the main thread in Godot sync
        CallDeferred(nameof(PushFrame), data);
    }

    private void OnTimeUpdated(double time)
    {
        EmitSignal(SignalName.TimeUpdated, time);
        //DriftCheck(time);
    }

    private void DriftCheck(double videoPtsSec)
    {
        // This code is currently unused, will need to implements a better version of drift checking / aligning that doesnt wreck everything.
        // Uncomment DriftCheck in OnTimeUpdated to use. 
        if (_audioDecoder != null && Math.Abs(videoPtsSec - _audioDecoder.CurrentTime / (double)MicrosecondsPerSecond) > 0.1)
        {
            _audioDecoder.Seek((long)(videoPtsSec * MicrosecondsPerSecond));
            _audioDecoder.ClearQueues();
            GD.Print($"ActiveVideoPlayback:DriftCheck - Video audio resync to {videoPtsSec:F2}s");
        }
    }

    private void OnVideoTimeUpdated(double time)
    {
        if (_videoComponent.EndTime > 0 && time >= _videoComponent.EndTime)
        {
            if (_videoComponent.Loop && (_playCountRemaining > 1 || _playCountRemaining == 0))
            {
                if (_playCountRemaining > 0)
                {
                    _playCountRemaining--;
                }
                _videoDecoder.Seek(_videoComponent.StartTime);
                if (_audioDecoder != null)
                {
                    _audioDecoder.Seek((long)(_videoComponent.StartTime * MicrosecondsPerSecond));
                }
            }
            else
            {
                Clean();
            }
        }
    }

    private void OnEndReached()
    {
        GD.Print($"ActiveVideoPlayback:OnEndReached");
        Clean();
    }
    
    /// <summary>
    /// Pushes a decoded RGB frame to the video output.
    /// </summary>
    /// <param name="rgbaData">The frame data.</param>
    public void PushFrame(byte[] rgbaData)
    {
        lock (_lock)
        {
            if (_isExiting || !IsInstanceValid(_godotImage) || !IsInstanceValid(_godotTexture)) return;
        
            // Resize image if dimensions changed
            if (_godotImage.GetWidth() != _videoDecoder?.Width || _godotImage.GetHeight() != _videoDecoder?.Height)
            {
                _godotImage = Image.CreateEmpty(_videoDecoder.Width, _videoDecoder.Height, false, Image.Format.Rgba8);
                _godotTexture = ImageTexture.CreateFromImage(_godotImage);
            }
            
            // This is where video modifications / filters can take place.
            if (_fadeAlpha < 1.0f)
            {
                // Apply fade by modifying alpha channel on a copy to avoid altering original data
                byte[] fadedData = new byte[rgbaData.Length];
                Array.Copy(rgbaData, fadedData, rgbaData.Length);
                for (int i = 3; i < fadedData.Length; i += 4)
                {
                    fadedData[i] = (byte)(fadedData[i] * _fadeAlpha);
                }
                rgbaData = fadedData;
            }

            _godotImage.SetData(_videoDecoder.Width, _videoDecoder.Height, false, Image.Format.Rgba8, rgbaData);
            _godotTexture.Update(_godotImage);
            
            
            if (_targetLayers.Count == 0 && !_isExiting)
            {
                GD.Print($"ActiveVideoPlayback:PushFrame - No target layers present, calling completed");
                EmitSignalCompleted();
                Clean();
            }
            
            //Push to all layers on various outputs
            foreach (var layer in _targetLayers)
            {
                layer.Value.Texture = _godotTexture;
            }
        }
    }

    /// <summary>
    /// Pauses the playback.
    /// </summary>
    public void Pause()
    {
        lock (_lock)
        {
            _videoDecoder.Pause();
            if (_audioDecoder != null)
            {
                _audioDecoder.Pause();
                if (DeviceStreams != null)
                {
                    foreach (var stream in DeviceStreams.Values) SDL.ClearAudioStream(stream);
                }
                _audioDecoder.ClearQueues();
            }
            //_pausedAtUs = Decoder.CurrentTime - GetQueuedUs(); // Estimate actual position at pause
            IsPaused = true;
            //Decoder.ClearQueues(); // Clear frame queue to avoid stale data on resume
            GD.Print($"ActiveVideoPlayback:Pause - Playback paused at estimated {_pausedAtUs / 1000} ms");
        }
    }

    /// <summary>
    /// Resumes the playback.
    /// </summary>
    public void Resume()
    {
        lock (_lock)
        {
            if (_pausedAtUs > 0)
            {
                _videoDecoder.Seek(_pausedAtUs / (double)MicrosecondsPerSecond); // Seek back to paused position
                if (_audioDecoder != null) _audioDecoder.Seek(_pausedAtUs);
                _pausedAtUs = 0;
            }
            _videoDecoder.Resume();
            if (_audioDecoder != null) _audioDecoder.Resume();
            IsPaused = false;
            GD.Print($"ActiveVideoPlayback:Resume - Playback resumed");
        }
    }

    /// <summary>
    /// Starts the playback asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task PlayAsync()
    {
        _videoDecoder.Resume();
        if (_audioDecoder != null)
        {
            await _audioDecoder.PlayAsync();
            _audioCts = new CancellationTokenSource();
            _audioConsumerTask = Task.Run(() => AudioConsumerLoopAsync(_audioCts.Token));
        }
    }

    private async Task AudioConsumerLoopAsync(CancellationToken token)
    {
        if (_audioDecoder == null) return;
        foreach (var pcmChunk in _audioDecoder.PcmQueue.GetConsumingEnumerable(token))
        {
            if (!token.IsCancellationRequested && !IsPaused)
            {
                PushPcm(pcmChunk);
                // Pace: sleep(chunkDurationMs)
                int produced = pcmChunk.Length / (_videoComponent.Metadata.AudioChannels * 4); // F32LE
                long chunkMs = (long)(produced * 1000L / _videoComponent.Metadata.AudioSampleRate);
                await Task.Delay((int)chunkMs, token);
            }
        }
    }

    /// <summary>
    /// Pushes PCM audio data to the device streams.
    /// </summary>
    /// <param name="pcm">The PCM audio data.</param>
    public unsafe void PushPcm(byte[] pcm)
    {
        lock (_lock)
        {
            if (_isExiting) return;
            if (DeviceStreams == null) return;
            foreach (var kv in DeviceStreams)
        {
            byte[] outputBuffer = ArrayPool<byte>.Shared.Rent(pcm.Length); // Rent buffer for output
            try
            {
                ApplyChannelVolumes(pcm, kv.Key, outputBuffer); // Apply volumes and routing
                fixed (byte* p = outputBuffer)
                {
                    SDL.PutAudioStreamData(kv.Value, (IntPtr)p, outputBuffer.Length);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(outputBuffer, clearArray: true); // Return to pool
            }
        }
        }
    }

    /// <summary>
    /// Applies per-channel volumes and routes PCM data to SDL streams, handling direct or patched output.
    /// </summary>
    /// <param name="pcm">The input PCM audio data as bytes.</param>
    /// <param name="deviceId">The ID of the audio device.</param>
    /// <param name="outputBuffer">The output buffer to write processed audio data to.</param>
    private unsafe void ApplyChannelVolumes(byte[] pcm, uint deviceId, byte[] outputBuffer)
    {
        int samples = pcm.Length / (SourceChannels * sizeof(float));
        Span<float> pcmSpan = MemoryMarshal.Cast<byte, float>(pcm.AsSpan());
        Span<float> outputSpan = MemoryMarshal.Cast<byte, float>(outputBuffer.AsSpan());
        outputSpan.Clear();

        string deviceName = GetDeviceName(deviceId);
        if (!string.IsNullOrEmpty(_videoComponent.DirectOutput))
        {
            // Direct output: Apply channel gains (including Routing if present)
            var device = _audioDevices.GetAudioDeviceByLogicalId(deviceId);
            if (device == null)
            {
                GD.PrintErr($"ActiveVideoPlayback:ApplyChannelVolumes - Device {deviceId} not found for direct output");
                return;
            }
            int deviceChannels = device.Channels;
            int outputChannels = Routing != null ? Routing.OutputChannels : SourceChannels;
            
            if (Routing != null)
            {
                // Route using CuePatch : Note, loops through devices channels and disregards extra outputs in Routing if there's a hotswap from Patch -> direct output
                for (int s = 0; s < samples; s++)
                {
                    for (int outCh = 0; outCh < deviceChannels; outCh++)
                    {
                        float sample = 0f;
                        for (int inCh = 0; inCh < SourceChannels; inCh++)
                        {
                            sample += pcmSpan[s * SourceChannels + inCh] * _channelGains[inCh] * Routing.GetVolume(inCh, outCh);
                        }
                        outputSpan[s * deviceChannels + outCh] = sample;
                    }
                }
            }
            else
            {
                // No routing, direct
                for (int s = 0; s < samples; s++)
                {
                    for (int ch = 0; ch < SourceChannels; ch++)
                    {
                        outputSpan[s * SourceChannels + ch] = pcmSpan[s * SourceChannels + ch] * _channelGains[ch];
                    }
                }
            }
        }
        else if (deviceName != null && Patch != null && Patch.OutputDevices.TryGetValue(deviceName, out var outputChannels)) // (patched output, no Routing check here)
        {
            // Patched output: Route via AudioOutputPatch, using _channelGains (includes Routing if present)
            for (int s = 0; s < samples; s++)
            {
                for (int outCh = 0; outCh < outputChannels.Count; outCh++)
                {
                    float sample = 0f;
                    foreach (int patchCh in outputChannels[outCh].RoutedChannels)
                    {
                        for (int inCh = 0; inCh < SourceChannels; inCh++)
                        {
                            float gain = _channelGains[inCh]; // (_channelGains includes Routing)
                            if (Routing != null) // (apply Routing matrix if present)
                            {
                                gain *= Routing.GetVolume(inCh, patchCh);
                            }
                            sample += pcmSpan[s * SourceChannels + inCh] * gain;
                        }
                    }
                    outputSpan[s * outputChannels.Count + outCh] = sample;
                }
            }
        }
        else
        {
            // Fallback: Apply _channelGains directly
            for (int s = 0; s < samples; s++)
            {
                for (int ch = 0; ch < SourceChannels; ch++)
                {
                    outputSpan[s * SourceChannels + ch] = pcmSpan[s * SourceChannels + ch] * _channelGains[ch];
                }
            }
        }
    }

    /// <summary>
    /// Retrieves the name of the audio device corresponding to the given device ID.
    /// </summary>
    /// <param name="deviceId">The device ID to look up.</param>
    /// <returns>The device name if found; otherwise, null.</returns>
    private string GetDeviceName(uint deviceId)
    {
        var device = _audioDevices.GetAudioDeviceByLogicalId(deviceId);
        return device?.Name;
    }

    /// <summary>
    /// Updates channel gains based on VideoComponent volume.
    /// </summary>
    private void UpdateChannelGains()
    {
        if (_isExiting) return;
        lock (_lock)
        {
            for (int i = 0; i < SourceChannels; i++)
            {
                _channelGains[i] = (float)_videoComponent.Volume * _volume;
            }
        }
    }

    /// <summary>
    /// Stops and cleans up the playback resources.
    /// </summary>
    /// <param name="fadeTime">The fade time in seconds; if greater than 0, fades out before stopping.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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
            //Decoder.Stop();
            if (_audioDecoder != null) _audioDecoder.Stop();
            Clean();
            return;
        }

        if (needFade) // Use component duration if set
        {
            await FadeOutAsync(fadeDuration);
            return;
        }
        _videoDecoder.Stop();
        if (_audioDecoder != null) _audioDecoder.Stop();
        Clean();
    }

    /// <summary>
    /// Fades in the playback over the specified duration.
    /// </summary>
    /// <param name="duration">The fade duration in seconds.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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
        float startAlpha = _fadeAlpha;
        float endAlpha = 1.0f;
        Stopwatch timer = Stopwatch.StartNew();

        try
        {
            //Decoder.PlayAsync(); // Start playback
            if (_audioDecoder != null) await _audioDecoder.PlayAsync();
            while (timer.Elapsed.TotalSeconds < duration && !_fadeCts.Token.IsCancellationRequested)
            {
                float t = (float)(timer.Elapsed.TotalSeconds / duration);
                SetVolume(Mathf.Lerp(startVol, endVol, t));
                _fadeAlpha = Mathf.Lerp(startAlpha, endAlpha, t);
                await Task.Delay(FadeUpdateIntervalMs, _fadeCts.Token); // ~60fps
            }

            if (!_fadeCts.Token.IsCancellationRequested)
            {
                SetVolume(endVol);
                _fadeAlpha = endAlpha;
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

    /// <summary>
    /// Fades out the playback over the specified duration.
    /// </summary>
    /// <param name="duration">The fade duration in seconds.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task FadeOutAsync(double duration)
    {
        lock (_lock)
        {
            if (_isFadingOut || _isFadingIn) return; // Prevent concurrent fades
            _isFadingOut = true;
            _fadeCts = new CancellationTokenSource();
        }

        float startVol = _volume;
        float startAlpha = _fadeAlpha;
        float endAlpha = 0.0f;
        Stopwatch timer = Stopwatch.StartNew();

        try
        {
            while (timer.Elapsed.TotalSeconds < duration && !_fadeCts.Token.IsCancellationRequested)
            {
                float t = (float)(timer.Elapsed.TotalSeconds / duration);
                SetVolume(Mathf.Lerp(startVol, 0f, t));
                _fadeAlpha = Mathf.Lerp(startAlpha, endAlpha, t);
                await Task.Delay(FadeUpdateIntervalMs, _fadeCts.Token); // ~60fps
            }

            if (!_fadeCts.Token.IsCancellationRequested)
            {
                SetVolume(0f);
                _fadeAlpha = endAlpha;
                //Decoder.Stop();
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

    /// <summary>
    /// Sets the global volume level for the playback.
    /// </summary>
    /// <param name="volume">The volume level, clamped between 0.0 and 1.0.</param>
    public void SetVolume(float volume)
    {
        lock (_lock)
        {
            _volume = Mathf.Clamp(volume, 0f, 1f);
            UpdateChannelGains(); // Recompute volume with new levels
        }
    }
    

    /// <summary>
    /// Seeks to the specified timestamp in seconds, clearing queues to avoid stale data.
    /// Works while playing or paused; playback state is preserved.
    /// </summary>
    /// <param name="time">Target timestamp in seconds.</param>
    public void Seek(double time)
    {
        lock (_lock)
        {
            _videoDecoder.Seek(time);
            if (_audioDecoder != null)
            {
                bool wasPlaying = _audioDecoder.IsPlaying && !_audioDecoder.IsPaused;
                _audioDecoder.Pause();
                if (DeviceStreams != null)
                {
                    foreach (var stream in DeviceStreams.Values) SDL.ClearAudioStream(stream);
                }
                _audioDecoder.ClearQueues();
                _audioDecoder.Seek((long)(time * MicrosecondsPerSecond));
                if (wasPlaying) _audioDecoder.Resume();
            }
        }
    }

    

    /// <summary>
    /// Gets a value indicating whether the playback is currently fading out.
    /// </summary>
    /// <value>true if fading out; otherwise, false.</value>
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
    /// Gets a value indicating whether the playback is currently fading in.
    /// </summary>
    /// <value>true if fading in; otherwise, false.</value>
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
    /// Gets the current volume level.
    /// </summary>
    /// <value>The current volume level between 0.0 and 1.0.</value>
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
    /// Gets the current play count.
    /// </summary>
    /// <value>The current play count.</value>
    public int CurrentPlayCount
    {
        get
        {
            lock (_lock)
            {
                return _playCountRemaining;
            }
        }
    }

    /// <summary>
    /// Gets the duration of the video in seconds.
    /// </summary>
    /// <returns>The duration in seconds.</returns>
    public double GetDuration()
    {
        return _videoDecoder.Duration;
    }
    
    private void ClearVideoDecoder()
    {
        if (_videoDecoder != null)
        {
            _videoDecoder.FrameReady -= OnFrameReady;
            _videoDecoder.TimeUpdated -= OnTimeUpdated;
            _videoDecoder.EndReached -= OnEndReached;
            _videoDecoder.StopDecodingAsync().Wait();
            _videoDecoder.Dispose();
            _videoDecoder = null;
        }
    }

    private void ClearAudioDecoder()
    {
        if (_audioDecoder != null)
        {
            _audioDecoder.Stop();
            _audioDecoder.Dispose();
            _audioDecoder = null;
        }
        
    }

    private void ClearTargetLayers()
    {
        // Clear all generated target layers
        foreach (var layerControl in _targetLayers.Keys)
        {
            layerControl.QueueFree();
        }
        _targetLayers.Clear();
    }
    
    public void Clean()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isExiting = true; // Prevent further operations
            _audioCts?.Cancel();
            _audioConsumerTask?.Wait(TaskWaitTimeoutMs);
            if (DeviceStreams != null)
            {
                foreach (var stream in DeviceStreams.Values) SDL.DestroyAudioStream(stream);
                DeviceStreams.Clear();
            }
            _audioDecoder?.Stop();
            _audioDecoder?.Dispose();
            ClearVideoDecoder();
            ClearTargetLayers();
            if (!_completedEmitted)
            {
                EmitSignal(SignalName.Completed);
                _completedEmitted = true;
            } // Emit signal immediately before freeing
            _isDisposed = true;
            CallDeferred("free");
        }
    }

    public override void _ExitTree()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isExiting = true;
            _audioCts?.Cancel();
            _audioConsumerTask?.Wait(TaskWaitTimeoutMs);
            if (_audioDecoder != null)
            {
                _audioDecoder.Stop();
                _audioDecoder.Dispose();
                _audioDecoder = null;
            }
            if (DeviceStreams != null)
            {
                foreach (var stream in DeviceStreams.Values) SDL.DestroyAudioStream(stream);
                DeviceStreams.Clear();
            }
            ClearVideoDecoder();
            ClearTargetLayers();
            _isDisposed = true;
        }
    }


}