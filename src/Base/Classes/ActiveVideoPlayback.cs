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
/// Thread-safe for multi-threaded access (e.g., UI updates).
/// </summary>
public partial class ActiveVideoPlayback : Node, IAudioPlayback
{
    private FFmpegVideoDecoder _videoDecoder;
    private ImageTexture _godotTexture;
    private Image _godotImage;
    
    
    // For embedded audio playback
    public AudioOutputPatch Patch { get; set; }
    public CuePatch Routing { get; set; }
    public string DirectOutput { get; set; }
    public Dictionary<uint, IntPtr> DeviceStreams { get; set; }
    public int SourceChannels { get; set; }
    public int SourceSampleRate { get; set; }
    public int SourceBytesPerFrame { get; set; }
    public SDL.AudioFormat SourceFormat { get; set; }
    
    private readonly VideoComponent _videoComponent;
    private AudioDevices _audioDevices;
    private VideoTargetLayer _targetLayer;
    private TextureRect _videoRect;
    private Image _videoImage;
    private ImageTexture _videoTexture;

    // Embedded audio handling
    private FFmpegAudioDecoder _audioDecoder;
    private Dictionary<uint, IntPtr> _audioStreams = new();
    private float[] _audioChannelGains;
    private CancellationTokenSource _audioCts;
    private Task _audioConsumerTask;

    private Dictionary<Control, TextureRect> _targetLayers = new();

    
    private readonly object _lock = new object(); // For thread safety
    private float _volume = 1.0f; // Normalized [0-1], global multiplier
    private float[] _channelGains; // Per-channel volume multipliers
    private bool _isFadingOut = false;
    private bool _isFadingIn = false;
    public bool IsStopped = false;
    public bool IsPaused = false;
    public bool IsSeeking = false;
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

    public async Task InitAsync()
    {
        GD.Print($"ActiveVideoPlayback:InitAsync - Initializing...");
        if (_videoDecoder == null)
        {
            LoadVideoDecoder();
        }

        await _videoDecoder.StartDecodingAsync(_videoComponent.VideoFile);

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
        }

        //await PlayAsync();

        GD.Print($"ActiveVideoPlayback:InitAsync - Initializing complete");
    }

    /// <summary>
    /// InvokeFrameReady is called by the decoder on the main thread. 
    /// </summary>
    /// <param name="data"></param>
    public void InvokeFrameReady(byte[] data)
    {
        OnFrameReady(data);
    }

    public void InvokeTimeUpdated(double time)
    {
        OnTimeUpdated(time);
    }

    public void InvokeEndReached()
    {
        OnEndReached();
    }

    private void OnLayerExited(TextureRect layer)
    {
        // Remove the layer from _targetLayers to prevent invalid references
        foreach (var kv in _targetLayers)
        {
            if (kv.Value == layer)
            {
                _targetLayers.Remove(kv.Key);
                GD.Print($"ActiveVideoPlayback:OnLayerExited - Removed reference to destroyed layer");
                break;
            }
        }
        if (_targetLayers.Count == 0 && !_isExiting)
        {
            EmitSignalCompleted();
            Clean();
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
        if (_audioDecoder != null && Math.Abs(videoPtsSec - _audioDecoder.CurrentTime / 1_000_000.0) > 0.1)
        {
            _audioDecoder.Seek((long)(videoPtsSec * 1_000_000));
            _audioDecoder.ClearQueues();
            GD.Print($"ActiveVideoPlayback:DriftCheck - Video audio resync to {videoPtsSec:F2}s");
        }
    }

    private void OnEndReached()
    {
        GD.Print($"ActiveVideoPlayback:OnEndReached");
        EmitSignalCompleted();
    }
    
    /// <summary>
    /// Pushes a decoded RGB frame to the video output.
    /// </summary>
    /// <param name="rgbaData">The frame data.</param>
    public void PushFrame(byte[] rgbaData)
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
            // Apply fade by modifying alpha channel
            for (int i = 3; i < rgbaData.Length; i += 4)
            {
                rgbaData[i] = (byte)(rgbaData[i] * _fadeAlpha);
            }
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

    public void Pause()
    {
        lock (_lock)
        {
            _videoDecoder.Pause();
            if (_audioDecoder != null)
            {
                _audioDecoder.Pause();
                foreach (var stream in _audioStreams.Values) SDL.ClearAudioStream(stream);
                _audioDecoder.ClearQueues();
            }
            //_pausedAtUs = Decoder.CurrentTime - GetQueuedUs(); // Estimate actual position at pause
            IsPaused = true;
            //Decoder.ClearQueues(); // Clear frame queue to avoid stale data on resume
            GD.Print($"ActiveVideoPlayback:Pause - Playback paused at estimated {_pausedAtUs / 1000} ms");
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (_pausedAtUs > 0)
            {
                _videoDecoder.Seek(_pausedAtUs / 1_000_000.0); // Seek back to paused position
                if (_audioDecoder != null) _audioDecoder.Seek(_pausedAtUs);
                _pausedAtUs = 0;
            }
            _videoDecoder.Resume();
            if (_audioDecoder != null) _audioDecoder.Resume();
            IsPaused = false;
            GD.Print($"ActiveVideoPlayback:Resume - Playback resumed");
        }
    }

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

    public unsafe void PushPcm(byte[] pcm)
    {
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

    private void ApplyChannelVolumes(byte[] pcm, uint deviceId, byte[] outputBuffer)
    {
        // Simplified: assume mono or something, but copy from ActiveAudioPlayback
        // For now, just copy pcm to outputBuffer
        Array.Copy(pcm, outputBuffer, pcm.Length);
    }

    private void UpdateChannelGains()
    {
        lock (_lock)
        {
            if (Routing != null) // (apply CuePatch for both direct and patched output)
            {
                // Apply AudioComponent.Volume * _volume * CuePatch matrix
                for (int i = 0; i < SourceChannels; i++)
                {
                    float gain = (float)_videoComponent.Volume * _volume;
                    float cuePatchGain = 0f;
                    for (int j = 0; j < Routing.OutputChannels; j++)
                    {
                        cuePatchGain += Routing.GetVolume(i, j); // Sum contributions to patch channels
                    }
                    _channelGains[i] = gain * cuePatchGain;
                }
            }
            else // (fallback if no CuePatch)
            {
                // Apply AudioComponent.Volume * _volume only
                for (int i = 0; i < SourceChannels; i++)
                {
                    _channelGains[i] = (float)_videoComponent.Volume * _volume;
                }
            }
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
                await Task.Delay(16, _fadeCts.Token); // ~60fps
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
                await Task.Delay(16, _fadeCts.Token); // ~60fps
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
        _videoDecoder.Seek(time);
        _audioDecoder?.Seek((long)(time * 1_000_000));
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
        _isExiting = true; // Prevent further operations
        _audioCts?.Cancel();
        _audioConsumerTask?.Wait(5000);
        foreach (var stream in _audioStreams.Values) SDL.DestroyAudioStream(stream);
        _audioDecoder?.Dispose();
        ClearVideoDecoder();
        ClearTargetLayers();
        EmitSignal(SignalName.Completed); // Emit signal immediately before freeing
        CallDeferred("free");
    }

    public override void _ExitTree()
    {
        _isExiting = true;
        ClearVideoDecoder();
        ClearTargetLayers();
    }


}