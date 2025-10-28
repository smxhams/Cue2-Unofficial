using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
    private AudioDevices _audioDevices;
        
    
    private readonly object _lock = new object(); // For thread safety
    private float _volume = 1.0f; // Normalized [0-1], global multiplier
    private float[] _channelGains; // Per-channel volume multipliers
    private bool _isFadingOut = false;
    private bool _isFadingIn = false;
    public bool IsStopped = false;
    public bool IsPaused = false;
    private CancellationTokenSource _fadeCts;
    
    private long _startTimeMs;
    private long _endTimeMs;
    private bool _useCustomEnd;
    private int _currentPlayCount = 1;
    public int EffectivePlayCount;
    private bool _hasStarted = false;
    private bool _reachedEnd = false;

    private readonly Stopwatch _playTimer = new Stopwatch();
    private long _pausedAtUs = 0; // Stored pause position in us for resume seek

    [Signal] public delegate void CompletedEventHandler();
    
    public ActiveAudioPlayback()
    {
        // Blank constructor for Godot
    }
    
    public ActiveAudioPlayback(AudioComponent audioComponent)
    {
        _audioComponent = audioComponent ?? throw new ArgumentNullException(nameof(audioComponent));
        Patch = _audioComponent.Patch;
        CuePatch = _audioComponent.Routing;
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
        
        // Validate start time against file duration if available
        if (_audioComponent.Metadata.Duration > 0 && _startTimeMs > (long)(_audioComponent.Metadata.Duration * 1000))
        {
            _startTimeMs = 0;
        }
        
        Decoder.EndReached += OnEndReached;
        Decoder.LengthChanged += OnLengthChanged;
    }

    public async Task InitAsync()
    {
        await Decoder.InitAsync();
        SourceChannels = _audioComponent.Metadata.Channels;
        SourceSampleRate = Decoder.OutputSampleRate;
        SourceFormat = Decoder.TargetFormat;
        SourceBytesPerFrame = SourceChannels * (GetBitDepth(SourceFormat) / 8);
        _channelGains = new float[SourceChannels]; // Initialize channel gains
        UpdateChannelGains(); // Set initial volumes
        GD.Print("ActiveAudioPlayback:InitAsync - Initialized FFmpeg decoder with sample rate " + SourceSampleRate);
    }
    
    /// <summary>
    /// Updates channel gains based on AudioComponent, CuePatch, and AudioOutputPatch.
    /// </summary>
    private void UpdateChannelGains()
    {
        lock (_lock)
        {
            if (_audioComponent.DirectOutput != null && !string.IsNullOrEmpty(_audioComponent.DirectOutput))
            {
                // Direct output: Apply AudioComponent.Volume only
                for (int i = 0; i < SourceChannels; i++)
                {
                    _channelGains[i] = (float)_audioComponent.Volume * _volume;
                }
            }
            else if (Patch != null && CuePatch != null)
            {
                // Patched output: Apply AudioComponent.Volume, CuePatch, and Patch volumes
                for (int i = 0; i < SourceChannels; i++)
                {
                    float gain = (float)_audioComponent.Volume * _volume; // Start with master volume
                    float cuePatchGain = 0f;
                    for (int j = 0; j < CuePatch.OutputChannels; j++)
                    {
                        cuePatchGain += CuePatch.GetVolume(i, j); // Sum contributions to patch channels
                    }
                    gain *= cuePatchGain;
                    _channelGains[i] = gain;
                }
            }
            else
            {
                // Fallback: Apply AudioComponent.Volume only
                for (int i = 0; i < SourceChannels; i++)
                {
                    _channelGains[i] = (float)_audioComponent.Volume * _volume;
                }
            }
            GD.Print($"ActiveAudioPlayback:UpdateChannelGains - Updated gains for {SourceChannels} channels");
        }
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
    

    public async void Play()
    {
        lock (_lock)
        {
            if (_hasStarted) return;
            _hasStarted = true;
        }

        if (_audioComponent.FadeInDuration > 0) // start with fade-in if specified
        {
            await FadeInAsync(_audioComponent.FadeInDuration);
        }
        else
        {
            Decoder.PlayAsync();
            _playTimer.Start();
            GD.Print($"ActiveAudioPlayback:Play - Playback started without fade-in");
        }
    }
    
    public void Pause()
    {
        lock (_lock)
        {
            Decoder.Pause();
            _pausedAtUs = Decoder.CurrentTime - GetQueuedUs(); // Estimate actual position at pause
            IsPaused = true;
            foreach (var stream in DeviceStreams.Values)
            {
                SDL.ClearAudioStream(stream); // Stop sound immediately
            }
            Decoder.ClearQueues(); // Clear PCM queue to avoid stale data on resume
            _playTimer.Stop();
            GD.Print($"ActiveAudioPlayback:Pause - Playback paused at estimated {_pausedAtUs / 1000} ms");
        }
    }
    
    public void Resume()
    {
        lock (_lock)
        {
            if (_pausedAtUs > 0)
            {
                GD.Print($"ActiveAudioPlayback:Resume - Seeking to {_pausedAtUs / 1000} ms");
                Decoder.Seek(_pausedAtUs); // Seek back to paused position
                _pausedAtUs = 0; // Reset
            }
            
            Decoder.Resume();
            IsPaused = false;
            _playTimer.Start();
            GD.Print($"ActiveAudioPlayback:Resume - Playback resumed");
        }
    }
    
    /// <summary>
    /// Stops and cleans up the playback resources.
    /// </summary>
    public async Task Stop(double fadeTime = 0.0)
    {
        bool needFade;
        double fadeDuration;
        lock (_lock)
        {
            if (IsStopped) return;
            _fadeCts?.Cancel();
            needFade = fadeTime > 0 || _audioComponent.FadeOutDuration > 0;
            fadeDuration = fadeTime > 0 ? fadeTime : _audioComponent.FadeOutDuration;
        }

        if (needFade) // Use component duration if set
        {
            await FadeOutAsync(fadeDuration);
            return;
        }
        foreach (var stream in DeviceStreams.Values) // clear any pending SDL data
        {
            SDL.ClearAudioStream(stream);
        }
        Decoder.Stop();
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
                GD.Print($"ActiveAudioPlayback:FadeInAsync - Fade-in completed to {endVol} over {duration} seconds");
            }
        }
        catch (OperationCanceledException)
        {
            GD.Print($"ActiveAudioPlayback:FadeInAsync - Fade-in cancelled");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveAudioPlayback:FadeInAsync - Error: {ex.Message}");
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
                foreach (var stream in DeviceStreams.Values) // Clear streams
                {
                    SDL.ClearAudioStream(stream);
                }
                Decoder.Stop();
                Clean();
                GD.Print($"ActiveAudioPlayback:FadeOutAsync - Fade-out completed over {duration} seconds");
            }
        }
        catch (OperationCanceledException)
        {
            GD.Print($"ActiveAudioPlayback:FadeOutAsync - Fade-out cancelled");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveAudioPlayback:FadeOutAsync - Error: {ex.Message}");
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
    
    public double GetRemainingTime()
    {
        lock (_lock)
        {
            if (_audioComponent.Loop) return -1.0;

            double segmentDuration = _useCustomEnd ? (_endTimeMs - _startTimeMs) / 1000.0 : _audioComponent.Metadata.Duration - _audioComponent.StartTime;
            double remainingInSegment = segmentDuration - (GetPlaybackTimeMs() / 1000.0);
            int remainingCounts = EffectivePlayCount - _currentPlayCount;

            return remainingInSegment + remainingCounts * segmentDuration;
        }
    }
    
    /// <summary>
    /// Gets the estimated actual playback time in milliseconds, accounting for buffered/queued data.
    /// This is the decoded time minus queued in PCM queue and SDL streams (averaged across devices).
    /// </summary>
    /// <returns>Actual playback time in ms.</returns>
    public long GetPlaybackTimeMs()
    {
        if (Decoder.QueuedBytes < 1) return 0;
        long queuedPcmUs = Decoder.QueuedBytes * 1_000_000L / (SourceSampleRate * SourceBytesPerFrame);
        long queuedSdlUs = 0;
        foreach (var stream in DeviceStreams.Values)
        {
            long queuedBytes = (long)SDL.GetAudioStreamQueued(stream);
            queuedSdlUs += queuedBytes * 1_000_000L / (SourceSampleRate * SourceBytesPerFrame);
        }
        if (DeviceStreams.Count > 0) queuedSdlUs /= DeviceStreams.Count; // Average for multi-device
        
        long totalQueuedUs = queuedPcmUs + queuedSdlUs;
        return (Decoder.CurrentTime - totalQueuedUs) / 1000;
    }
    
    /// <summary>
    /// Gets the total queued time in microseconds (PCM queue + SDL streams average).
    /// Used internally for actual time calculation.
    /// </summary>
    /// <returns>Queued time in us.</returns>
    private long GetQueuedUs() 
    {
        long queuedPcmUs = Decoder.QueuedBytes * 1_000_000L / (SourceSampleRate * SourceBytesPerFrame);
        long queuedSdlUs = 0; 
        foreach (var stream in DeviceStreams.Values)
        {
            long queuedBytes = (long)SDL.GetAudioStreamQueued(stream);
            queuedSdlUs += queuedBytes * 1_000_000L / (SourceSampleRate * SourceBytesPerFrame);
        }
        if (DeviceStreams.Count > 0) queuedSdlUs /= DeviceStreams.Count;
        return queuedPcmUs + queuedSdlUs;
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
        {
            _ = FadeInAsync(_audioComponent.FadeInDuration);
        }
        GD.Print($"ActiveAudioPlayback:ResetForLoop - Reset for loop and cleared SDL streams");
    }
    
    /// <summary>
    /// Seeks to the specified timestamp in microseconds, clearing queues and SDL streams to avoid stale data.
    /// Works while playing or paused; playback state is preserved.
    /// </summary>
    /// <param name="timestampUs">Target timestamp in us.</param>
    public void Seek(long timestampUs)
    {
        try
        {
            bool wasPlaying = Decoder.IsPlaying && !Decoder.IsPaused; // (preserve state)
            Pause(); // (pause to safely seek)
            foreach (var stream in DeviceStreams.Values)
            {
                SDL.ClearAudioStream(stream); //(clear SDL queues)
            }
            Decoder.ClearQueues(); // (clear PCM queue)
            Decoder.Seek(timestampUs);
            if (wasPlaying) Resume(); // (resume if was playing)
            GD.Print($"ActiveAudioPlayback:Seek - Sought to {timestampUs} us");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveAudioPlayback:Seek - Seek error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Applies per-channel volumes and routes PCM data to SDL streams, handling direct or patched output.
    /// </summary>
    private unsafe void ApplyChannelVolumes(byte[] pcm, uint deviceId, byte[] outputBuffer)
    {
        int samples = pcm.Length / (SourceChannels * sizeof(float));
        Span<float> pcmSpan = MemoryMarshal.Cast<byte, float>(pcm.AsSpan());
        
        if (_audioComponent.DirectOutput != null && !string.IsNullOrEmpty(_audioComponent.DirectOutput))
        {
            // Direct output: Apply global volume to all channels
            for (int i = 0; i < pcmSpan.Length; i++)
            {
                pcmSpan[i] *= _channelGains[i % SourceChannels];
            }
            Buffer.BlockCopy(pcm, 0, outputBuffer, 0, pcm.Length);
        }
        else if (Patch != null && CuePatch != null)
        {
            // Patched output: Route audio channels to device channels via Patch
            Span<float> outputSpan = MemoryMarshal.Cast<byte, float>(outputBuffer.AsSpan());
            outputSpan.Fill(0f); // Clear output buffer
            for (int s = 0; s < samples; s++)
            {
                for (int outCh = 0; outCh < outputChannels.Count; outCh++)
                {
                    float sample = 0f;
                    foreach (int patchCh in outputChannels[outCh].RoutedChannels)
                    {
                        for (int inCh = 0; inCh < SourceChannels; inCh++)
                        {
                            //float gain = _channelGains[inCh] * CuePatch.GetVolume(inCh, patchCh) * outputChannels[outCh].Volume;
                            //sample += pcmSpan[s * SourceChannels + inCh] * gain;
                        }
                    }
                    outputSpan[s * outputChannels.Count + outCh] = sample;
                }
            }
        }
        else
        {
            // Fallback: Copy input to output with global volume
            for (int i = 0; i < pcmSpan.Length; i++)
            {
                pcmSpan[i] *= _channelGains[i % SourceChannels];
            }
            Buffer.BlockCopy(pcm, 0, outputBuffer, 0, pcm.Length);
        }
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
}