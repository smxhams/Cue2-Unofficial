using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Cue2.Shared.Audio;
using Cue2.Shared.Decoders;
// MediaMemory used on Clean for LOH reclaim after large PCM stores
using Godot;
using SDL3;

namespace Cue2.Base.Classes;

/// <summary>
/// Software control layer for an active audio cue.
/// Owns transport (play/pause/seek/loop/playcount), volume/fades, and matrix mixing.
/// Pulls PCM from <see cref="AudioSourceDecoder"/> and tops up SDL streams by queue watermark.
/// </summary>
public partial class ActiveAudioPlayback : GodotObject, IAudioPlayback
{
    private const int TargetBufferMs = 80;
    private const int LowWaterMs = 40;
    private const int FillLoopSleepMs = 4;
    private const int PrefetchMs = 800;

    /// <summary>Pull-based audio source decoder.</summary>
    public AudioSourceDecoder Decoder { get; private set; }

    public AudioOutputPatch Patch { get; set; }
    public CuePatch Routing { get; set; }
    public string DirectOutput { get; set; }
    public Dictionary<uint, IntPtr> DeviceStreams { get; set; }
    public Dictionary<uint, int> DeviceStreamChannels { get; set; }
    public int SourceChannels { get; set; }
    public int SourceSampleRate { get; set; }
    public int SourceBytesPerFrame { get; set; }
    public SDL.AudioFormat SourceFormat { get; set; } = SDL.AudioFormat.AudioF32LE;

    private readonly AudioComponent _audioComponent;
    private readonly AudioDevices _audioDevices;
    private readonly object _lock = new object();

    private float _volume = 1.0f;
    private bool _isFadingOut;
    private bool _isFadingIn;
    public bool IsStopped;
    public bool IsPaused;
    public bool IsSeeking;

    private CancellationTokenSource _fadeCts;
    private CancellationTokenSource _fillCts;
    private Task _fillTask;

    private long _startTimeUs;
    private long _endTimeUs;
    private bool _useCustomEnd;
    private int _currentPlayCount = 1;
    public int EffectivePlayCount;
    private bool _hasStarted;
    private long _pausedAtUs;
    private long _framesDelivered; // sample-frames delivered to mix since last seek/start

    private float[] _srcBuffer;
    private float[] _mixBuffer;

    [Signal] public delegate void CompletedEventHandler();

    public ActiveAudioPlayback()
    {
    }

    public ActiveAudioPlayback(AudioComponent audioComponent, AudioDevices audioDevices)
    {
        _audioComponent = audioComponent ?? throw new ArgumentNullException(nameof(audioComponent));
        _audioDevices = audioDevices ?? throw new ArgumentNullException(nameof(audioDevices));
        Patch = _audioComponent.Patch;
        Routing = _audioComponent.Routing;
        DirectOutput = _audioComponent.DirectOutput;
        DeviceStreams = new Dictionary<uint, IntPtr>();
        DeviceStreamChannels = new Dictionary<uint, int>();

        Decoder = new AudioSourceDecoder();

        _startTimeUs = (long)(Math.Max(0, _audioComponent.StartTime) * 1_000_000.0);
        _useCustomEnd = _audioComponent.EndTime >= 0;
        if (_useCustomEnd)
            _endTimeUs = (long)(_audioComponent.EndTime * 1_000_000.0);
        else if (_audioComponent.Metadata != null && _audioComponent.Metadata.Duration > 0)
            _endTimeUs = (long)(_audioComponent.Metadata.Duration * 1_000_000.0);
        else
            _endTimeUs = long.MaxValue;

        EffectivePlayCount = _audioComponent.Loop ? int.MaxValue : Math.Max(1, _audioComponent.PlayCount);
    }

    /// <summary>
    /// Opens the decoder, seeks to start, and prefetches PCM for low-latency GO.
    /// </summary>
    public async Task InitAsync()
    {
        // Prefer sample-accurate PCM store for lossy codecs (fixes MP3 loop drift),
        // subject to decoder size/duration caps. Short looping cues stay exact.
        await Decoder.OpenAsync(_audioComponent.AudioFile, preferSampleAccurateStore: true);
        SourceChannels = Decoder.Info.Channels;
        SourceSampleRate = Decoder.Info.SampleRate;
        SourceFormat = SDL.AudioFormat.AudioF32LE;
        SourceBytesPerFrame = SourceChannels * sizeof(float);

        if (!_useCustomEnd && Decoder.Info.DurationUs > 0)
            _endTimeUs = Decoder.Info.DurationUs;

        if (_startTimeUs > 0)
            Decoder.Seek(_startTimeUs);
        else
            Decoder.Prefetch(PrefetchMs);

        // Prefetch after seek as well
        Decoder.Prefetch(PrefetchMs);

        int maxFrames = Math.Max(SourceSampleRate / 10, 1024); // ~100 ms chunk
        _srcBuffer = new float[maxFrames * SourceChannels];
        _mixBuffer = new float[maxFrames * 16]; // up to 16 out channels

        GD.Print($"ActiveAudioPlayback:InitAsync - rate={SourceSampleRate} ch={SourceChannels} codec={Decoder.Info.CodecName}");
    }

    public bool IsFadingOut
    {
        get { lock (_lock) return _isFadingOut; }
    }

    public bool IsFadingIn
    {
        get { lock (_lock) return _isFadingIn; }
    }

    public float CurrentVolume
    {
        get { lock (_lock) return _volume; }
    }

    public int CurrentPlayCount
    {
        get { lock (_lock) return _currentPlayCount; }
        set { lock (_lock) _currentPlayCount = value; }
    }

    /// <summary>
    /// Starts the demand-driven fill loop (optionally with fade-in).
    /// </summary>
    public async void Play()
    {
        lock (_lock)
        {
            if (_hasStarted || IsStopped) return;
            _hasStarted = true;
        }

        if (_audioComponent.FadeInDuration > 0)
        {
            await FadeInAsync(_audioComponent.FadeInDuration);
        }
        else
        {
            StartFillLoop();
            GD.Print("ActiveAudioPlayback:Play - Fill loop started");
        }
    }

    private void StartFillLoop()
    {
        _fillCts?.Cancel();
        _fillCts = new CancellationTokenSource();
        var token = _fillCts.Token;
        _fillTask = Task.Run(() => FillLoop(token), token);
    }

    private void StopFillLoop()
    {
        try
        {
            _fillCts?.Cancel();
            if (_fillTask != null)
            {
                try { _fillTask.Wait(500); } catch { /* ignore */ }
            }
        }
        catch { /* ignore */ }
        finally
        {
            _fillCts?.Dispose();
            _fillCts = null;
            _fillTask = null;
        }
    }

    /// <summary>
    /// Demand-driven fill: tops up each SDL stream when queued data is below the low-water mark.
    /// </summary>
    private void FillLoop(CancellationToken token)
    {
        // If streams never appear after start, abort rather than spinning forever.
        // (~2s at FillLoopSleepMs) — setup should already prevent this path.
        int emptyStreamIterations = 0;
        const int maxEmptyStreamIterations = 500;

        try
        {
            while (!token.IsCancellationRequested)
            {
                bool paused;
                lock (_lock) paused = IsPaused || IsStopped;
                if (paused)
                {
                    Thread.Sleep(FillLoopSleepMs);
                    continue;
                }

                if (DeviceStreams == null || DeviceStreams.Count == 0)
                {
                    emptyStreamIterations++;
                    if (emptyStreamIterations >= maxEmptyStreamIterations)
                    {
                        GD.PrintErr("ActiveAudioPlayback:FillLoop - No device streams after start; aborting playback.");
                        CallDeferred(nameof(CompleteFromEnd));
                        return;
                    }
                    Thread.Sleep(FillLoopSleepMs);
                    continue;
                }

                emptyStreamIterations = 0;

                bool anyNeed = false;
                int maxNeedFrames = 0;

                foreach (var kv in DeviceStreams)
                {
                    long queued = SDL.GetAudioStreamQueued(kv.Value);
                    int outCh = GetStreamChannels(kv.Key);
                    int bytesPerOutFrame = outCh * sizeof(float);
                    if (bytesPerOutFrame <= 0) continue;

                    long lowWater = SourceSampleRate * LowWaterMs / 1000L * bytesPerOutFrame;
                    long target = SourceSampleRate * TargetBufferMs / 1000L * bytesPerOutFrame;

                    if (queued < lowWater)
                    {
                        anyNeed = true;
                        int need = (int)Math.Max(1, (target - queued) / bytesPerOutFrame);
                        if (need > maxNeedFrames) maxNeedFrames = need;
                    }
                }

                if (!anyNeed)
                {
                    Thread.Sleep(FillLoopSleepMs);
                    continue;
                }

                // Cap read size to buffer capacity
                int maxFrames = _srcBuffer.Length / SourceChannels;
                int framesToRead = Math.Min(maxNeedFrames, maxFrames);

                // Respect custom end time
                long posUs = Decoder.PositionUs;
                if (posUs >= _endTimeUs)
                {
                    HandleSegmentEnd();
                    continue;
                }

                // Limit frames so we don't read past end
                if (_endTimeUs < long.MaxValue && SourceSampleRate > 0)
                {
                    long remainingUs = _endTimeUs - posUs;
                    int remainingFrames = (int)Math.Max(0, remainingUs * SourceSampleRate / 1_000_000L);
                    framesToRead = Math.Min(framesToRead, Math.Max(1, remainingFrames));
                }

                lock (_lock)
                {
                    if (IsPaused || IsStopped) continue;
                }

                // Decode outside playback lock to avoid blocking Pause/Seek
                int frames = Decoder.Read(_srcBuffer.AsSpan(), framesToRead, token);

                if (frames <= 0)
                {
                    if (Decoder.EndOfStream || Decoder.PositionUs >= _endTimeUs)
                    {
                        HandleSegmentEnd();
                    }
                    else
                    {
                        Thread.Sleep(FillLoopSleepMs);
                    }
                    continue;
                }

                _framesDelivered += frames;
                PushMixedFrames(frames);
            }
        }
        catch (OperationCanceledException)
        {
            // normal stop
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveAudioPlayback:FillLoop - {ex.Message}");
            // Ensure ActiveCue can tear down the UI if the fill loop dies unexpectedly.
            try { CallDeferred(nameof(CompleteFromEnd)); } catch { /* object may be freeing */ }
        }
    }

    private int GetStreamChannels(uint deviceLogicalId)
    {
        if (DeviceStreamChannels != null &&
            DeviceStreamChannels.TryGetValue(deviceLogicalId, out int ch) &&
            ch > 0)
        {
            return ch;
        }
        return SourceChannels;
    }

    private unsafe void PushMixedFrames(int frames)
    {
        if (DeviceStreams == null) return;

        float masterVol;
        lock (_lock) masterVol = _volume;
        float componentVol = (float)_audioComponent.Volume;
        bool isDirect = !string.IsNullOrEmpty(DirectOutput);

        foreach (var kv in DeviceStreams)
        {
            int outCh = GetStreamChannels(kv.Key);
            int outSamples = frames * outCh;
            if (outSamples > _mixBuffer.Length)
                _mixBuffer = new float[outSamples];

            string deviceName = _audioDevices.GetAudioDeviceByLogicalId(kv.Key)?.Name;

            AudioMixMatrix.Mix(
                _srcBuffer.AsSpan(0, frames * SourceChannels),
                frames,
                SourceChannels,
                _mixBuffer.AsSpan(0, outSamples),
                outCh,
                masterVol,
                componentVol,
                Routing,
                Patch,
                deviceName,
                isDirect);

            int byteCount = outSamples * sizeof(float);
            fixed (float* p = _mixBuffer)
            {
                SDL.PutAudioStreamData(kv.Value, (IntPtr)p, byteCount);
            }
        }
    }

    private void HandleSegmentEnd()
    {
        bool scheduleComplete = false;
        lock (_lock)
        {
            // _completedEmitted also covers "already finishing"
            if (IsStopped || _completedEmitted) return;

            if (_audioComponent.Loop || _currentPlayCount < EffectivePlayCount)
            {
                _currentPlayCount++;
                GD.Print($"ActiveAudioPlayback:HandleSegmentEnd - Loop/play {_currentPlayCount}/{EffectivePlayCount}");
                Decoder.Seek(_startTimeUs);
                Decoder.Prefetch(PrefetchMs);
                _framesDelivered = 0;
                return;
            }

            // Mark finishing so fill loop stops re-entering (IsStopped set in Clean)
            _completedEmitted = true;
            scheduleComplete = true;
        }

        if (!scheduleComplete) return;

        try { _fillCts?.Cancel(); } catch { /* ignore */ }

        GD.Print("ActiveAudioPlayback:HandleSegmentEnd - Playback completed");
        try
        {
            CallDeferred(nameof(CompleteFromEnd));
        }
        catch
        {
            CompleteFromEnd();
        }
    }

    private void CompleteFromEnd()
    {
        if (!IsInstanceValid(this)) return;
        // Natural end: always Clean (do not use Stop — it treats IsStopped/fade paths)
        // Reset flag so Clean emits Completed once and frees
        lock (_lock)
        {
            _completedEmitted = false;
        }
        Clean();
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (IsPaused || IsStopped) return;
            IsPaused = true;
            _pausedAtUs = GetPlaybackPositionUs();
        }

        if (DeviceStreams != null)
        {
            foreach (var stream in DeviceStreams.Values)
                SDL.ClearAudioStream(stream);
        }
        Decoder?.FlushBuffers();
        GD.Print($"ActiveAudioPlayback:Pause - Paused at {_pausedAtUs / 1000} ms");
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (!IsPaused || IsStopped) return;
            if (_pausedAtUs > 0)
            {
                Decoder.Seek(_pausedAtUs);
                Decoder.Prefetch(PrefetchMs / 2);
                _pausedAtUs = 0;
            }
            IsPaused = false;
        }
        GD.Print("ActiveAudioPlayback:Resume - Resumed");
    }

    /// <summary>
    /// Stops playback. First call with <paramref name="fadeTime"/> &gt; 0 (or cue FadeOutDuration)
    /// starts a fade-out; a second call while fading hard-stops immediately.
    /// </summary>
    /// <param name="fadeTime">Stop-fade seconds from settings; 0 forces immediate stop on first press if the cue has no own fade.</param>
    public async Task Stop(double fadeTime = 0.0)
    {
        bool needFade = false;
        double fadeDuration = 0;
        bool wasFadingOut;
        lock (_lock)
        {
            if (IsStopped) return;
            wasFadingOut = _isFadingOut;
            // Cancel any in-progress fade-in/out so a second Stop can hard-stop,
            // or a first Stop can preempt fade-in and start fade-out.
            _fadeCts?.Cancel();
            if (!wasFadingOut)
            {
                needFade = fadeTime > 0 || (_audioComponent != null && _audioComponent.FadeOutDuration > 0);
                fadeDuration = fadeTime > 0
                    ? fadeTime
                    : (_audioComponent?.FadeOutDuration ?? 0);
                // Preempt fade-in so FadeOutAsync is allowed to start
                _isFadingIn = false;
            }
        }

        if (wasFadingOut)
        {
            HardStop();
            return;
        }

        if (needFade)
        {
            await FadeOutAsync(fadeDuration);
            return;
        }

        HardStop();
    }

    private void HardStop()
    {
        lock (_lock)
        {
            if (IsStopped) return;
            IsStopped = true;
            IsPaused = false;
        }

        StopFillLoop();

        if (DeviceStreams != null)
        {
            foreach (var stream in DeviceStreams.Values)
            {
                try { SDL.ClearAudioStream(stream); } catch { /* ignore */ }
            }
        }

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
        SetVolume(0f);
        StartFillLoop();

        try
        {
            while (timer.Elapsed.TotalSeconds < duration && !_fadeCts.Token.IsCancellationRequested)
            {
                float t = (float)(timer.Elapsed.TotalSeconds / duration);
                SetVolume(Mathf.Lerp(startVol, endVol, t));
                await Task.Delay(16, _fadeCts.Token);
            }
            if (!_fadeCts.Token.IsCancellationRequested)
                SetVolume(endVol);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveAudioPlayback:FadeInAsync - {ex.Message}");
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
        if (duration <= 0)
        {
            HardStop();
            return;
        }

        lock (_lock)
        {
            if (IsStopped) return;
            if (_isFadingOut) return;
            _isFadingIn = false;
            _isFadingOut = true;
            _fadeCts?.Dispose();
            _fadeCts = new CancellationTokenSource();
        }

        float startVol = _volume;
        var token = _fadeCts.Token;
        Stopwatch timer = Stopwatch.StartNew();

        try
        {
            while (timer.Elapsed.TotalSeconds < duration && !token.IsCancellationRequested)
            {
                float t = (float)(timer.Elapsed.TotalSeconds / duration);
                SetVolume(Mathf.Lerp(startVol, 0f, t));
                await Task.Delay(16, token);
            }
            if (!token.IsCancellationRequested)
            {
                SetVolume(0f);
                HardStop();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveAudioPlayback:FadeOutAsync - {ex.Message}");
        }
        finally
        {
            lock (_lock)
            {
                _isFadingOut = false;
                if (_fadeCts != null)
                {
                    try { _fadeCts.Dispose(); } catch { /* ignore */ }
                    _fadeCts = null;
                }
            }
        }
    }

    public void SetVolume(float volume)
    {
        lock (_lock)
        {
            _volume = Mathf.Clamp(volume, 0f, 1f);
        }
    }

    public double GetRemainingTime()
    {
        lock (_lock)
        {
            if (_audioComponent.Loop) return -1.0;
            double segmentDuration = (_endTimeUs - _startTimeUs) / 1_000_000.0;
            double posSec = GetPlaybackPositionUs() / 1_000_000.0;
            double startSec = _startTimeUs / 1_000_000.0;
            double remainingInSegment = Math.Max(0, segmentDuration - (posSec - startSec));
            int remainingCounts = Math.Max(0, EffectivePlayCount - _currentPlayCount);
            return remainingInSegment + remainingCounts * segmentDuration;
        }
    }

    public long GetPlaybackTimeMs()
    {
        return GetPlaybackPositionUs() / 1000;
    }

    /// <summary>
    /// Estimated audible position: decoder position minus average SDL queue latency.
    /// </summary>
    private long GetPlaybackPositionUs()
    {
        if (Decoder == null) return _startTimeUs;
        long decUs = Decoder.PositionUs;
        long queuedUs = 0;
        int count = 0;
        if (DeviceStreams != null)
        {
            foreach (var kv in DeviceStreams)
            {
                long queuedBytes = SDL.GetAudioStreamQueued(kv.Value);
                int outCh = GetStreamChannels(kv.Key);
                if (outCh <= 0 || SourceSampleRate <= 0) continue;
                queuedUs += queuedBytes * 1_000_000L / (SourceSampleRate * outCh * sizeof(float));
                count++;
            }
        }
        if (count > 0) queuedUs /= count;
        long pos = decUs - queuedUs;
        return pos < _startTimeUs ? _startTimeUs : pos;
    }

    public void Seek(long timestampUs)
    {
        try
        {
            bool wasPaused;
            lock (_lock)
            {
                wasPaused = IsPaused;
                IsSeeking = true;
                IsPaused = true;
            }

            if (DeviceStreams != null)
            {
                foreach (var stream in DeviceStreams.Values)
                    SDL.ClearAudioStream(stream);
            }

            long clamped = Math.Max(_startTimeUs, timestampUs);
            if (_endTimeUs < long.MaxValue)
                clamped = Math.Min(clamped, _endTimeUs);

            Decoder.Seek(clamped);
            Decoder.Prefetch(PrefetchMs / 2);
            _framesDelivered = 0;
            _pausedAtUs = clamped;

            lock (_lock)
            {
                IsSeeking = false;
                if (!wasPaused && !IsStopped)
                {
                    IsPaused = false;
                    _pausedAtUs = 0;
                }
            }

            GD.Print($"ActiveAudioPlayback:Seek - Sought to {clamped} us");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveAudioPlayback:Seek - {ex.Message}");
            lock (_lock) IsSeeking = false;
        }
    }

    private bool _completedEmitted;

    /// <summary>
    /// Tears down decoder, streams, and fill loop, then signals <see cref="Completed"/>.
    /// Safe to call multiple times; only the first call emits Completed.
    /// </summary>
    public void Clean()
    {
        bool alreadyDone;
        lock (_lock)
        {
            alreadyDone = _completedEmitted;
            if (IsStopped && Decoder == null && _completedEmitted)
            {
                return;
            }
            IsStopped = true;
            _completedEmitted = true;
        }

        StopFillLoop();
        _fadeCts?.Cancel();

        if (Decoder != null)
        {
            try
            {
                Decoder.Dispose();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ActiveAudioPlayback:Clean - Decoder dispose: {ex.Message}");
            }
            Decoder = null;
        }

        // Drop mix buffers
        if (_srcBuffer != null)
        {
            MediaMemory.NoteReleased(MediaMemory.FloatBufferBytes(_srcBuffer));
            _srcBuffer = null;
        }
        if (_mixBuffer != null)
        {
            MediaMemory.NoteReleased(MediaMemory.FloatBufferBytes(_mixBuffer));
            _mixBuffer = null;
        }

        if (DeviceStreams != null)
        {
            _audioDevices?.NotifyPlaybackCompleted(this);
            foreach (var stream in DeviceStreams.Values)
            {
                try { SDL.DestroyAudioStream(stream); } catch { /* ignore */ }
            }
            DeviceStreams.Clear();
        }
        DeviceStreamChannels?.Clear();

        MediaMemory.ReclaimIfNeeded();

        if (!alreadyDone)
        {
            try
            {
                if (IsInstanceValid(this))
                    EmitSignal(SignalName.Completed);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ActiveAudioPlayback:Clean - Completed signal: {ex.Message}");
            }

            // Free after call lock is released. CallDeferred(MethodName.Free) is unreliable
            // on C# GodotObject ("locked" / "Nonexistent function 'free'").
            if (IsInstanceValid(this))
                Callable.From(FreeDeferred).CallDeferred();
        }
    }

    /// <summary>
    /// Invokes <see cref="GodotObject.Free"/> if this instance is still valid.
    /// </summary>
    private void FreeDeferred()
    {
        if (IsInstanceValid(this))
            Free();
    }
}
