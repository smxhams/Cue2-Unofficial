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
using Godot;
using SDL3;
using Image = Godot.Image;

namespace Cue2.Base.Classes;

/// <summary>
/// Software control layer for an active video cue with optional embedded audio.
/// <para>
/// Video: pull-based <see cref="VideoSourceDecoder"/>; presentation is driven on the main
/// thread by a master clock (audio position when available, else wall clock).
/// Audio: same pull-based path as pure audio cues (<see cref="AudioSourceDecoder"/> + SDL fill).
/// </para>
/// A/V sync strategy: audio is the master clock when present; video presents or drops frames
/// to stay within a tolerance of that clock. On seek/pause/loop both streams are reset to
/// the same media timestamp.
/// </summary>
public partial class ActiveVideoPlayback : Node, IAudioPlayback
{
    private const int FadeUpdateIntervalMs = 16;
    private const long MicrosecondsPerSecond = 1_000_000;
    private const int AudioTargetBufferMs = 80;
    private const int AudioLowWaterMs = 40;
    private const int AudioFillSleepMs = 4;
    private const int VideoPrefetchTarget = 6;
    private const int VideoPrefetchLowWater = 3;
    /// <summary>Drop frames if more than this late vs master clock.</summary>
    private const long MaxVideoLatenessUs = 80_000; // 80 ms
    /// <summary>Present frame if within this early of master (half-frame-ish).</summary>
    private const long PresentEarlyToleranceUs = 8_000;

    private VideoSourceDecoder _videoDecoder;
    private ImageTexture _godotTexture;
    private Image _godotImage;
    private byte[] _displayRgba;

    public AudioOutputPatch Patch { get; set; }
    public CuePatch Routing { get; set; }
    public string DirectOutput { get; set; }
    public Dictionary<uint, IntPtr> DeviceStreams { get; set; }
    public Dictionary<uint, int> DeviceStreamChannels { get; set; }
    public int SourceChannels { get; set; }
    public int SourceSampleRate { get; set; }
    public int SourceBytesPerFrame { get; set; }
    public SDL.AudioFormat SourceFormat { get; set; }

    private readonly VideoComponent _videoComponent;
    private readonly AudioDevices _audioDevices;
    private VideoTargetLayer _targetLayer;

    private AudioSourceDecoder _audioDecoder;
    private CancellationTokenSource _audioFillCts;
    private Task _audioFillTask;
    private float[] _audioSrcBuffer;
    private float[] _audioMixBuffer;

    private CancellationTokenSource _videoPrefetchCts;
    private Task _videoPrefetchTask;

    private Dictionary<Control, TextureRect> _targetLayers = new();

    private readonly object _lock = new object();
    private float _volume = 1.0f;
    private bool _isFadingOut;
    private bool _isFadingIn;
    public bool IsStopped;
    public bool IsPaused;
    public bool IsSeeking;
    public bool IsExiting;
    private float _fadeAlpha = 1.0f;
    private CancellationTokenSource _fadeCts;

    private long _startTimeUs;
    private long _endTimeUs;
    private bool _useCustomEnd;
    public int EffectivePlayCount;
    private int _currentPlayCount = 1;
    private long _pausedAtUs;
    private bool _isExiting;
    private bool _completedEmitted;
    private bool _isDisposed;
    private bool _isPlaying;

    // Master clock (wall path when no audio)
    private readonly Stopwatch _wallClock = new Stopwatch();
    private long _wallMediaOriginUs; // media time corresponding to wall start

    [Signal] public delegate void CompletedEventHandler();
    [Signal] public delegate void TimeUpdatedEventHandler(double time);

    public ActiveVideoPlayback()
    {
    }

    /// <summary>
    /// True when the component wants embedded audio and the file has an audio track.
    /// Does not guarantee an output device is bound — see <see cref="HasBoundAudioStreams"/>.
    /// </summary>
    public bool UseAudio =>
        _videoComponent.UseAudio &&
        _videoComponent.Metadata != null &&
        _videoComponent.Metadata.AudioChannels > 0;

    /// <summary>
    /// True when at least one SDL audio stream is bound and can drive the A/V master clock.
    /// </summary>
    public bool HasBoundAudioStreams =>
        DeviceStreams != null && DeviceStreams.Count > 0;

    public ActiveVideoPlayback(VideoComponent videoComponent, AudioDevices audioDevices)
    {
        _videoComponent = videoComponent ?? throw new ArgumentNullException(nameof(videoComponent));
        _audioDevices = audioDevices ?? throw new ArgumentNullException(nameof(audioDevices));

        _videoDecoder = new VideoSourceDecoder();
        // Only set up the embedded-audio path when an output is assigned. Without streams the
        // audio decoder position never advances and must not be used as the master clock.
        if (UseAudio && videoComponent.HasAudioOutputAssigned)
        {
            _audioDecoder = new AudioSourceDecoder();
            DeviceStreams = new Dictionary<uint, IntPtr>();
            DeviceStreamChannels = new Dictionary<uint, int>();
            Patch = videoComponent.Patch;
            Routing = videoComponent.Routing;
            DirectOutput = videoComponent.DirectOutput;
        }

        _targetLayer = DisplaysManager.Layers.Find(l => l.LayerId == _videoComponent.TargetLayerId);
        if (_targetLayer == null)
        {
            GD.PrintErr($"ActiveVideoPlayback:Constructor - Target layer {_videoComponent.TargetLayerId} not found.");
            return;
        }

        _godotImage = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        _godotTexture = ImageTexture.CreateFromImage(_godotImage);

        _startTimeUs = (long)(Math.Max(0, _videoComponent.StartTime) * MicrosecondsPerSecond);
        _useCustomEnd = _videoComponent.EndTime >= 0;
        if (_useCustomEnd)
            _endTimeUs = (long)(_videoComponent.EndTime * MicrosecondsPerSecond);
        else if (_videoComponent.Metadata != null && _videoComponent.Metadata.Duration > 0)
            _endTimeUs = (long)(_videoComponent.Metadata.Duration * MicrosecondsPerSecond);
        else
            _endTimeUs = long.MaxValue;

        EffectivePlayCount = _videoComponent.Loop ? int.MaxValue : Math.Max(1, _videoComponent.PlayCount);

        if (_videoComponent.Metadata?.Duration > 0 &&
            _startTimeUs > (long)(_videoComponent.Metadata.Duration * MicrosecondsPerSecond))
        {
            _startTimeUs = 0;
        }
    }

    /// <summary>
    /// Opens video (and audio) decoders, seeks to start, prefetches.
    /// </summary>
    public async Task InitAsync()
    {
        GD.Print("ActiveVideoPlayback:InitAsync - Initializing...");
        _currentPlayCount = 1;

        await _videoDecoder.OpenAsync(_videoComponent.VideoFile);
        if (!_useCustomEnd && _videoDecoder.Info.DurationUs > 0)
            _endTimeUs = _videoDecoder.Info.DurationUs;

        if (_startTimeUs > 0)
            _videoDecoder.Seek(_startTimeUs);
        _videoDecoder.Prefetch(VideoPrefetchTarget);

        int w = _videoDecoder.Info.Width;
        int h = _videoDecoder.Info.Height;
        _godotImage = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        _godotTexture = ImageTexture.CreateFromImage(_godotImage);
        _displayRgba = new byte[_videoDecoder.Info.FrameByteSize];

        foreach (var display in DisplaysManager.Outputs)
        {
            var layerControl = display.AddLayer(_videoComponent.TargetLayerId);
            var layerTextRect = layerControl.GetNode<TextureRect>("%LayerOutput");
            layerTextRect.Texture = _godotTexture;
            _targetLayers.Add(layerControl, layerTextRect);
            layerTextRect.TreeExited += () => OnLayerExited(layerTextRect);
        }

        if (_targetLayers.Count == 0)
        {
            _isExiting = true;
            EmitSignalCompleted();
            Clean();
            return;
        }

        if (_audioDecoder != null)
        {
            // Stream embedded audio (no full PCM expand) — long video soundtracks would
            // otherwise pin tens/hundreds of MB on the LOH for the whole cue lifetime.
            await _audioDecoder.OpenAsync(
                _videoComponent.VideoFile,
                preferSampleAccurateStore: false);
            SourceChannels = _audioDecoder.Info.Channels;
            SourceSampleRate = _audioDecoder.Info.SampleRate;
            SourceFormat = SDL.AudioFormat.AudioF32LE;
            SourceBytesPerFrame = SourceChannels * sizeof(float);

            if (_startTimeUs > 0)
                _audioDecoder.Seek(_startTimeUs);
            _audioDecoder.Prefetch(AudioSourceDecoder.DefaultPrefetchMs);

            int maxFrames = Math.Max(SourceSampleRate / 10, 1024);
            _audioSrcBuffer = new float[maxFrames * SourceChannels];
            _audioMixBuffer = new float[maxFrames * 16];
        }

        SetProcess(false); // enabled on Play
        GD.Print($"ActiveVideoPlayback:InitAsync - complete video={w}x{h} audio={_audioDecoder != null}");
    }

    /// <summary>
    /// Main-thread presentation: compare master clock to next frame PTS.
    /// Requires this node to be inside the scene tree (see ActiveCue.SetupVideoComponent).
    /// </summary>
    public override void _Process(double delta)
    {
        PresentCatchUpFrames();
    }

    private void PresentFrame(VideoFrame frame)
    {
        if (frame?.Rgba == null || _isExiting) return;
        if (!IsInstanceValid(_godotImage) || !IsInstanceValid(_godotTexture)) return;

        int needed = frame.Width * frame.Height * 4;
        if (_displayRgba == null || _displayRgba.Length < needed)
            _displayRgba = new byte[needed];
        Buffer.BlockCopy(frame.Rgba, 0, _displayRgba, 0, needed);

        // Optional fade via alpha
        if (_fadeAlpha < 1.0f)
        {
            for (int i = 3; i < needed; i += 4)
                _displayRgba[i] = (byte)(_displayRgba[i] * _fadeAlpha);
        }

        if (_godotImage.GetWidth() != frame.Width || _godotImage.GetHeight() != frame.Height)
        {
            _godotImage = Image.CreateEmpty(frame.Width, frame.Height, false, Image.Format.Rgba8);
            _godotTexture = ImageTexture.CreateFromImage(_godotImage);
        }

        _godotImage.SetData(frame.Width, frame.Height, false, Image.Format.Rgba8, _displayRgba);
        _godotTexture.Update(_godotImage);

        foreach (var layer in _targetLayers)
            layer.Value.Texture = _godotTexture;
    }

    /// <summary>
    /// Master media clock in microseconds.
    /// With bound audio streams: audio decode position minus average SDL stream latency.
    /// Without audio output (silent video / failed bind): wall clock from play start.
    /// </summary>
    /// <remarks>
    /// Must not use the audio decoder as master when no streams are bound — the fill loop
    /// never advances <see cref="AudioSourceDecoder.PositionUs"/>, so presentation freezes.
    /// </remarks>
    private long GetMasterClockUs()
    {
        if (_audioDecoder != null && UseAudio && HasBoundAudioStreams)
        {
            long audioUs = _audioDecoder.PositionUs;
            long queuedUs = 0;
            int count = 0;
            foreach (var kv in DeviceStreams)
            {
                long qb = SDL.GetAudioStreamQueued(kv.Value);
                int outCh = GetStreamChannels(kv.Key);
                if (outCh <= 0 || SourceSampleRate <= 0) continue;
                queuedUs += qb * MicrosecondsPerSecond / (SourceSampleRate * outCh * sizeof(float));
                count++;
            }
            if (count > 0) queuedUs /= count;
            long master = audioUs - queuedUs;
            return master < _startTimeUs ? _startTimeUs : master;
        }

        if (!_wallClock.IsRunning)
            return _wallMediaOriginUs;
        return _wallMediaOriginUs + _wallClock.ElapsedMilliseconds * 1000;
    }

    private void HandleSegmentEnd()
    {
        lock (_lock)
        {
            if (IsStopped || _isExiting) return;

            if (_videoComponent.Loop || _currentPlayCount < EffectivePlayCount)
            {
                _currentPlayCount++;
                GD.Print($"ActiveVideoPlayback:HandleSegmentEnd - Loop/play {_currentPlayCount}/{EffectivePlayCount}");
                SeekInternal(_startTimeUs, restartClock: true);
                return;
            }
        }

        GD.Print("ActiveVideoPlayback:HandleSegmentEnd - Completed");
        CallDeferred(nameof(CompleteFromEnd));
    }

    private void CompleteFromEnd()
    {
        _ = Stop(0);
    }

    private void OnLayerExited(TextureRect layer)
    {
        lock (_lock)
        {
            foreach (var kv in _targetLayers)
            {
                if (kv.Value == layer)
                {
                    _targetLayers.Remove(kv.Key);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Tears down the embedded-audio path so presentation can continue silently on the wall clock.
    /// Call when audio output fails to bind after the decoder was opened.
    /// </summary>
    public void DisableEmbeddedAudio()
    {
        StopAudioFillLoop();

        if (DeviceStreams != null)
        {
            try { _audioDevices?.NotifyPlaybackCompleted(this); } catch { /* ignore */ }
            foreach (var stream in DeviceStreams.Values)
            {
                try { SDL.DestroyAudioStream(stream); } catch { /* ignore */ }
            }
            DeviceStreams.Clear();
        }
        DeviceStreamChannels?.Clear();

        try { _audioDecoder?.Dispose(); } catch { /* ignore */ }
        _audioDecoder = null;

        GD.Print("ActiveVideoPlayback:DisableEmbeddedAudio - Audio path disabled; using wall-clock master.");
    }

    public async Task PlayAsync()
    {
        await Task.Yield();
        lock (_lock)
        {
            if (IsStopped || _isExiting) return;
            _isPlaying = true;
            IsPaused = false;
            // Wall-clock origin = current media position (used when no audio master)
            _wallMediaOriginUs = _videoDecoder?.PositionUs ?? _startTimeUs;
            _wallClock.Restart();
        }

        if (!IsInsideTree())
        {
            GD.PrintErr("ActiveVideoPlayback:PlayAsync - Node is not in the scene tree; _Process will not run and video will not display. Parent must AddChild(this) before Play.");
        }

        SetProcess(true);
        StartVideoPrefetchLoop();
        // Only drive audio when streams were bound; otherwise wall-clock masters silent video.
        if (_audioDecoder != null && HasBoundAudioStreams)
            StartAudioFillLoop();

        // Present first frame immediately so output isn't blank for a tick
        PresentCatchUpFrames();

        bool audioMaster = _audioDecoder != null && HasBoundAudioStreams;
        GD.Print($"ActiveVideoPlayback:PlayAsync - Playing (audio-master={audioMaster}, silent={!audioMaster && UseAudio}, inTree={IsInsideTree()})");
    }

    /// <summary>
    /// Shared present logic used by _Process and the first Play tick.
    /// </summary>
    private void PresentCatchUpFrames()
    {
        if (!_isPlaying || IsPaused || IsStopped || _isExiting || _videoDecoder == null)
            return;

        long masterUs = GetMasterClockUs();
        EmitSignal(SignalName.TimeUpdated, masterUs / (double)MicrosecondsPerSecond);

        if (masterUs >= _endTimeUs)
        {
            HandleSegmentEnd();
            return;
        }

        int presented = 0;
        const int maxPresentPerTick = 4;
        while (presented < maxPresentPerTick)
        {
            if (!_videoDecoder.TryPeekPts(out long nextPts))
            {
                if (_videoDecoder.EndOfStream)
                    HandleSegmentEnd();
                break;
            }

            long lateness = masterUs - nextPts;
            if (lateness < -PresentEarlyToleranceUs)
                break;

            if (!_videoDecoder.ReadFrame(out VideoFrame frame))
                break;

            if (lateness > MaxVideoLatenessUs && _videoDecoder.TryPeekPts(out long peek2) && peek2 <= masterUs)
            {
                _videoDecoder.ReleaseFrameBuffer(frame.Rgba);
                presented++;
                continue;
            }

            PresentFrame(frame);
            _videoDecoder.ReleaseFrameBuffer(frame.Rgba);
            presented++;
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (IsPaused || IsStopped) return;
            IsPaused = true;
            _pausedAtUs = GetMasterClockUs();
            _wallClock.Stop();
        }

        if (DeviceStreams != null)
        {
            foreach (var stream in DeviceStreams.Values)
                SDL.ClearAudioStream(stream);
        }
        _audioDecoder?.FlushBuffers();
        GD.Print($"ActiveVideoPlayback:Pause - at {_pausedAtUs / 1000} ms");
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (!IsPaused || IsStopped) return;
            if (_pausedAtUs > 0)
            {
                SeekInternal(_pausedAtUs, restartClock: true);
                _pausedAtUs = 0;
            }
            IsPaused = false;
            _wallClock.Restart();
        }
        GD.Print("ActiveVideoPlayback:Resume");
    }

    /// <summary>Seeks both video and audio to the same media time (seconds).</summary>
    public void Seek(double timeSeconds)
    {
        long us = (long)(timeSeconds * MicrosecondsPerSecond);
        us = Math.Max(_startTimeUs, us);
        if (_endTimeUs < long.MaxValue)
            us = Math.Min(us, _endTimeUs);

        lock (_lock)
        {
            IsSeeking = true;
        }
        SeekInternal(us, restartClock: !IsPaused && _isPlaying);
        lock (_lock)
        {
            IsSeeking = false;
            if (IsPaused)
                _pausedAtUs = us;
        }
    }

    private void SeekInternal(long timestampUs, bool restartClock)
    {
        if (DeviceStreams != null)
        {
            foreach (var stream in DeviceStreams.Values)
                SDL.ClearAudioStream(stream);
        }

        _videoDecoder?.Seek(timestampUs);
        _videoDecoder?.Prefetch(VideoPrefetchTarget);

        if (_audioDecoder != null)
        {
            _audioDecoder.FlushBuffers();
            _audioDecoder.Seek(timestampUs);
            _audioDecoder.Prefetch(400);
        }

        if (restartClock)
        {
            _wallMediaOriginUs = timestampUs;
            if (_isPlaying && !IsPaused)
                _wallClock.Restart();
        }
    }

    private void StartVideoPrefetchLoop()
    {
        StopVideoPrefetchLoop();
        _videoPrefetchCts = new CancellationTokenSource();
        var token = _videoPrefetchCts.Token;
        _videoPrefetchTask = Task.Run(() =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    bool paused;
                    lock (_lock) paused = IsPaused || IsStopped || _isExiting || !_isPlaying;
                    if (paused)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    if (_videoDecoder != null &&
                        _videoDecoder.BufferedFrames < VideoPrefetchLowWater &&
                        !_videoDecoder.EndOfStream)
                    {
                        _videoDecoder.Prefetch(VideoPrefetchTarget);
                    }
                    Thread.Sleep(4);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                GD.PrintErr($"ActiveVideoPlayback:VideoPrefetch - {ex.Message}");
            }
        }, token);
    }

    private void StopVideoPrefetchLoop()
    {
        try
        {
            _videoPrefetchCts?.Cancel();
            _videoPrefetchTask?.Wait(500);
        }
        catch { /* ignore */ }
        finally
        {
            _videoPrefetchCts?.Dispose();
            _videoPrefetchCts = null;
            _videoPrefetchTask = null;
        }
    }

    private void StartAudioFillLoop()
    {
        StopAudioFillLoop();
        _audioFillCts = new CancellationTokenSource();
        var token = _audioFillCts.Token;
        _audioFillTask = Task.Run(() => AudioFillLoop(token), token);
    }

    private void StopAudioFillLoop()
    {
        try
        {
            _audioFillCts?.Cancel();
            _audioFillTask?.Wait(500);
        }
        catch { /* ignore */ }
        finally
        {
            _audioFillCts?.Dispose();
            _audioFillCts = null;
            _audioFillTask = null;
        }
    }

    private void AudioFillLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                bool paused;
                lock (_lock) paused = IsPaused || IsStopped || _isExiting;
                if (paused)
                {
                    Thread.Sleep(AudioFillSleepMs);
                    continue;
                }

                if (DeviceStreams == null || DeviceStreams.Count == 0 || _audioDecoder == null)
                {
                    Thread.Sleep(AudioFillSleepMs);
                    continue;
                }

                bool anyNeed = false;
                int maxNeedFrames = 0;
                foreach (var kv in DeviceStreams)
                {
                    long queued = SDL.GetAudioStreamQueued(kv.Value);
                    int outCh = GetStreamChannels(kv.Key);
                    int bpf = outCh * sizeof(float);
                    if (bpf <= 0) continue;
                    long lowWater = SourceSampleRate * AudioLowWaterMs / 1000L * bpf;
                    long target = SourceSampleRate * AudioTargetBufferMs / 1000L * bpf;
                    if (queued < lowWater)
                    {
                        anyNeed = true;
                        int need = (int)Math.Max(1, (target - queued) / bpf);
                        if (need > maxNeedFrames) maxNeedFrames = need;
                    }
                }

                if (!anyNeed)
                {
                    Thread.Sleep(AudioFillSleepMs);
                    continue;
                }

                int maxFrames = _audioSrcBuffer.Length / SourceChannels;
                int framesToRead = Math.Min(maxNeedFrames, maxFrames);

                // Don't fill audio past video end region
                long pos = _audioDecoder.PositionUs;
                if (pos >= _endTimeUs)
                {
                    Thread.Sleep(AudioFillSleepMs);
                    continue;
                }

                int frames = _audioDecoder.Read(_audioSrcBuffer.AsSpan(), framesToRead, token);
                if (frames <= 0)
                {
                    Thread.Sleep(AudioFillSleepMs);
                    continue;
                }

                PushMixedAudioFrames(frames);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveVideoPlayback:AudioFillLoop - {ex.Message}");
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

    private unsafe void PushMixedAudioFrames(int frames)
    {
        if (DeviceStreams == null || _isExiting) return;

        float masterVol;
        lock (_lock) masterVol = _volume;
        float componentVol = (float)_videoComponent.Volume;
        bool isDirect = !string.IsNullOrEmpty(DirectOutput);

        foreach (var kv in DeviceStreams)
        {
            int outCh = GetStreamChannels(kv.Key);
            int outSamples = frames * outCh;
            if (outSamples > _audioMixBuffer.Length)
                _audioMixBuffer = new float[outSamples];

            string deviceName = _audioDevices.GetAudioDeviceByLogicalId(kv.Key)?.Name;
            AudioMixMatrix.Mix(
                _audioSrcBuffer.AsSpan(0, frames * SourceChannels),
                frames,
                SourceChannels,
                _audioMixBuffer.AsSpan(0, outSamples),
                outCh,
                masterVol,
                componentVol,
                Routing,
                Patch,
                deviceName,
                isDirect);

            int byteCount = outSamples * sizeof(float);
            fixed (float* p = _audioMixBuffer)
            {
                SDL.PutAudioStreamData(kv.Value, (IntPtr)p, byteCount);
            }
        }
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
            _fadeCts?.Cancel();
            if (!wasFadingOut)
            {
                needFade = fadeTime > 0 || (_videoComponent != null && _videoComponent.FadeOutDuration > 0);
                fadeDuration = fadeTime > 0
                    ? fadeTime
                    : (_videoComponent?.FadeOutDuration ?? 0);
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
            _isPlaying = false;
        }
        SetProcess(false);
        StopVideoPrefetchLoop();
        StopAudioFillLoop();
        _wallClock.Stop();
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
        Stopwatch timer = Stopwatch.StartNew();
        SetVolume(0f);
        await PlayAsync();

        try
        {
            while (timer.Elapsed.TotalSeconds < duration && !_fadeCts.Token.IsCancellationRequested)
            {
                float t = (float)(timer.Elapsed.TotalSeconds / duration);
                SetVolume(Mathf.Lerp(startVol, endVol, t));
                _fadeAlpha = Mathf.Lerp(startAlpha, 1.0f, t);
                await Task.Delay(FadeUpdateIntervalMs, _fadeCts.Token);
            }
            if (!_fadeCts.Token.IsCancellationRequested)
            {
                SetVolume(endVol);
                _fadeAlpha = 1.0f;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveVideoPlayback:FadeInAsync - {ex.Message}");
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
        float startAlpha = _fadeAlpha;
        var token = _fadeCts.Token;
        Stopwatch timer = Stopwatch.StartNew();

        try
        {
            while (timer.Elapsed.TotalSeconds < duration && !token.IsCancellationRequested)
            {
                float t = (float)(timer.Elapsed.TotalSeconds / duration);
                SetVolume(Mathf.Lerp(startVol, 0f, t));
                _fadeAlpha = Mathf.Lerp(startAlpha, 0f, t);
                await Task.Delay(FadeUpdateIntervalMs, token);
            }
            if (!token.IsCancellationRequested)
            {
                SetVolume(0f);
                _fadeAlpha = 0f;
                HardStop();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            GD.PrintErr($"ActiveVideoPlayback:FadeOutAsync - {ex.Message}");
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
        lock (_lock) _volume = Mathf.Clamp(volume, 0f, 1f);
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
    }

    public double GetDuration()
    {
        if (_videoDecoder?.Info != null && _videoDecoder.Info.DurationUs > 0)
            return _videoDecoder.Info.DurationUs / (double)MicrosecondsPerSecond;
        return _videoComponent.Metadata?.Duration ?? 0;
    }

    public void Clean()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isExiting = true;
            IsStopped = true;
            _isPlaying = false;
        }

        SetProcess(false);
        StopVideoPrefetchLoop();
        StopAudioFillLoop();
        _wallClock.Stop();

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

        ReleaseMediaBuffers();

        lock (_lock)
        {
            if (!_completedEmitted)
            {
                EmitSignal(SignalName.Completed);
                _completedEmitted = true;
            }
            _isDisposed = true;
        }

        // Return LOH memory after large frame/PCM releases
        MediaMemory.ReclaimIfNeeded();

        // Defer free — Clean often runs inside signal/method dispatch (object locked).
        // QueueFree is safe for Nodes in the tree; otherwise use C# Free via Callable.
        if (IsInstanceValid(this))
        {
            if (IsInsideTree())
                CallDeferred(Node.MethodName.QueueFree);
            else
                Callable.From(FreeDeferred).CallDeferred();
        }
    }

    /// <summary>
    /// Invokes <see cref="GodotObject.Free"/> if this instance is still valid (not in tree).
    /// </summary>
    private void FreeDeferred()
    {
        if (IsInstanceValid(this))
            Free();
    }

    /// <summary>
    /// Drops decoder, display, and mix buffers so large arrays become collectible.
    /// </summary>
    private void ReleaseMediaBuffers()
    {
        try { _audioDecoder?.Dispose(); } catch { /* ignore */ }
        _audioDecoder = null;
        try { _videoDecoder?.Dispose(); } catch { /* ignore */ }
        _videoDecoder = null;

        if (_displayRgba != null)
        {
            MediaMemory.NoteReleased(MediaMemory.ByteBufferBytes(_displayRgba));
            _displayRgba = null;
        }
        if (_audioSrcBuffer != null)
        {
            MediaMemory.NoteReleased(MediaMemory.FloatBufferBytes(_audioSrcBuffer));
            _audioSrcBuffer = null;
        }
        if (_audioMixBuffer != null)
        {
            MediaMemory.NoteReleased(MediaMemory.FloatBufferBytes(_audioMixBuffer));
            _audioMixBuffer = null;
        }

        // Detach textures from output layers so Godot can free GPU resources
        foreach (var kv in _targetLayers)
        {
            try
            {
                if (IsInstanceValid(kv.Value))
                    kv.Value.Texture = null;
                if (IsInstanceValid(kv.Key))
                    kv.Key.QueueFree();
            }
            catch { /* ignore */ }
        }
        _targetLayers.Clear();

        if (_godotTexture != null && IsInstanceValid(_godotTexture))
        {
            try { _godotTexture.Dispose(); } catch { /* ignore */ }
        }
        _godotTexture = null;
        if (_godotImage != null && IsInstanceValid(_godotImage))
        {
            try { _godotImage.Dispose(); } catch { /* ignore */ }
        }
        _godotImage = null;
    }

    public override void _ExitTree()
    {
        if (_isDisposed) return;
        _isExiting = true;
        IsStopped = true;
        _isPlaying = false;
        SetProcess(false);
        StopVideoPrefetchLoop();
        StopAudioFillLoop();
        if (DeviceStreams != null)
        {
            foreach (var stream in DeviceStreams.Values)
            {
                try { SDL.DestroyAudioStream(stream); } catch { /* ignore */ }
            }
            DeviceStreams.Clear();
        }
        ReleaseMediaBuffers();
        MediaMemory.ReclaimIfNeeded();
        _isDisposed = true;
    }
}
