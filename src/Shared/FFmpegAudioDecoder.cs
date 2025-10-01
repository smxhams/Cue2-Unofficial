using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using SDL3;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Godot;

namespace Cue2.Shared;

/// <summary>
/// Decodes audio from FFmpeg to PCM buffers for SDL streaming.
/// Manages packet/frame lifecycle, threading for low-latency, and controls (pause, stop, fade).
/// Designed for cue playback: Preload packets, decode on-demand.
/// </summary>
public class FFmpegAudioDecoder : IDisposable
{
    private readonly AudioComponent _component; // For metadata/start/end times
    private readonly ActiveAudioPlayback _playback;
    private unsafe AVFormatContext* _formatCtx;
    private unsafe AVCodecContext* _codecCtx;
    private unsafe SwrContext* _swrCtx;
    private AVChannelLayout _inChLayout, _outChLayout;
    private readonly object _lock = new object(); // Thread safety for state/controls

    private volatile bool _isPlaying = false; // Volatile for thread visibility
    private volatile bool _isPaused = false;
    private volatile bool _isStopped = false;
    private float _currentVolume = 1.0f; // For fade multipliers
    private CancellationTokenSource _cts; // For async cancel
    
    private AVRational _timeBase;
    private int _bytesPerSample = sizeof(float); // 4 for F32
    private int _audioStreamIndex = -1;
    private long _currentTs = 0; // In AV_TIME_BASE units (us)
    private int _channels; // Cached from codecCtx after init
    
    private readonly ConcurrentQueue<byte[]> _preloadBuffer = new ConcurrentQueue<byte[]>(); // For preloading PCM chunks
    public const int PreloadMs = 1000; // Configurable preload time (ms) for low-latency start
    
    private BlockingCollection<byte[]> _pcmQueue; // Bounded queue for producer-consumer
    private const int MaxBufferedChunks = 1000; // Cap buffered chunks
    
    private int _outputSampleRate;
    public int OutputSampleRate => _outputSampleRate;
    
    /// <summary>
    /// Gets the target audio format for SDL, set to Float 32-bit Little Endian for easy volume manipulation.
    /// Can be resampled to S16 if needed.
    /// </summary>
    public SDL.AudioFormat TargetFormat { get; } = SDL.AudioFormat.AudioF32LE; // Float for easy volume (resample to S16 if needed)
    
    /// <summary>
    /// Gets the current playback time in milliseconds.
    /// </summary>
    public long CurrentTime => _currentTs / 1000; // ms

    /// <summary>
    /// Event raised when the end of the audio is reached.
    /// </summary>
    public event EventHandler EndReached;
    
    /// <summary>
    /// Event raised when the length of the audio changes.
    /// </summary>
    public event EventHandler<long> LengthChanged;

    
    /// <summary>
    /// Initializes a new instance of the <see cref="FFmpegAudioDecoder"/> class.
    /// </summary>
    /// <param name="component">The audio component containing metadata and file information.</param>
    /// <param name="playback">The active audio playback instance.</param>
    /// <exception cref="ArgumentNullException">Thrown if component or playback is null.</exception>
    /// <exception cref="Exception">Thrown if metadata is missing or file doesn't exist.</exception>
    public FFmpegAudioDecoder(AudioComponent component, ActiveAudioPlayback playback)
    {
        _component = component ?? throw new ArgumentNullException(nameof(component));
        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        if (_component.Metadata == null) throw new Exception("Metadata required for decoder setup.");
        if (!System.IO.File.Exists(_component.AudioFile)) throw new Exception($"Audio file not found: {_component.AudioFile}");
    }

    /// <summary>
    /// Asynchronously initializes the decoder: opens the file, finds the audio stream, sets up the decoder and resampler.
    /// Preloads initial PCM buffers for low-latency start.
    /// </summary>
    /// <returns>A task representing the asynchronous initialization.</returns>
    /// <exception cref="Exception">Thrown on FFmpeg errors during initialization.</exception>
    public async Task InitAsync() 
    { 
        await Task.Run(() => 
        {
            unsafe
            {
                int ret;
                fixed (AVFormatContext** pCtx = &_formatCtx) 
                {
                    ret = ffmpeg.avformat_open_input(pCtx, _component.AudioFile, null, null); 
                    if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - Open failed: {GetFFmpegError(ret)}");
                }

                ret = ffmpeg.avformat_find_stream_info(_formatCtx, null); 
                if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - Stream info failed: {GetFFmpegError(ret)}");

                _audioStreamIndex = -1;
                for (uint i = 0; i < _formatCtx->nb_streams; i++) 
                { 
                    if (_formatCtx->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO) 
                    { 
                        _audioStreamIndex = (int)i; 
                        break; 
                    } 
                } 
                if (_audioStreamIndex == -1) throw new Exception("FFmpegAudioDecoder:InitAsync - No audio stream."); 

                AVStream* stream = _formatCtx->streams[(uint)_audioStreamIndex];
                _timeBase = stream->time_base;
                AVCodec* codec = ffmpeg.avcodec_find_decoder(stream->codecpar->codec_id); 
                if (codec == null) throw new Exception("FFmpegAudioDecoder:InitAsync - Unsupported codec."); 

                fixed (AVCodecContext** pCodecCtx = &_codecCtx)
                {
                    _codecCtx = ffmpeg.avcodec_alloc_context3(codec);
                    ret = ffmpeg.avcodec_parameters_to_context(_codecCtx, stream->codecpar);
                    if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - Params to context failed: {GetFFmpegError(ret)}");

                    ret = ffmpeg.avcodec_open2(_codecCtx, codec, null);
                    if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - Open codec failed: {GetFFmpegError(ret)}");
                }
                
                _channels = _codecCtx->ch_layout.nb_channels; // Cache channels after open
                _outputSampleRate = _codecCtx->sample_rate;

                // Setup resampler to target (keep channels, convert to float)
                fixed (AVChannelLayout* pInChLayout = &_inChLayout)
                {
                    fixed (AVChannelLayout* pOutChLayout = &_outChLayout)
                    {
                        ffmpeg.av_channel_layout_copy(pInChLayout, &_codecCtx->ch_layout);
                        _outChLayout = _inChLayout; // Keep channels
                        ret = ffmpeg.av_channel_layout_copy(pOutChLayout, pInChLayout);
                        if (ret < 0) throw new Exception("FFmpegAudioDecoder:InitAsync - Channel layout copy failed");

                        fixed (SwrContext** ppSwr = &_swrCtx)
                        {
                            ffmpeg.swr_alloc_set_opts2(
                                ppSwr, pOutChLayout, AVSampleFormat.AV_SAMPLE_FMT_FLT, 
                                _outputSampleRate, pInChLayout, _codecCtx->sample_fmt,
                                _codecCtx->sample_rate, 0, null);
                            if (_swrCtx == null) throw new Exception("FFmpegAudioDecoder:InitAsync - Swr alloc failed");

                            ret = ffmpeg.swr_init(_swrCtx);
                            if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - Swr init failed: {GetFFmpegError(ret)}");
                        }
                    }
                }
                
                // Initial seek before preload
                long startUs = (long)(_component.StartTime * 1_000_000); // Assume seconds; revert to *1000 if ms
                long seekTs = ffmpeg.av_rescale_q(startUs, new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE }, _timeBase);
                ret = ffmpeg.av_seek_frame(_formatCtx, _audioStreamIndex, seekTs, ffmpeg.AVSEEK_FLAG_BACKWARD);
                if (ret < 0) 
                {
                    GD.PrintErr($"FFmpegAudioDecoder:InitAsync - Initial seek failed: {GetFFmpegError(ret)}");
                    return;
                }
                _currentTs = startUs;
                
                // Preload initial buffers after setup
                PreloadInitialBuffers();

                // Fire LengthChanged with stream duration (in ms)
                long durationMs = (long)(stream->duration * ffmpeg.av_q2d(_timeBase) * 1000);
                LengthChanged?.Invoke(this, durationMs);
                
                GD.Print($"FFmpegAudioDecoder:InitAsync - Decoded to original rate {_outputSampleRate} Hz, float32 format.");
            }
        });
    }

    /// <summary>
    /// Preloads initial PCM buffers equivalent to PreloadMs for low-latency start on Play.
    /// Decodes but doesn't stream yet. Advances the stream state.
    /// </summary>
    private unsafe void PreloadInitialBuffers()
    {
        // Calculate samples to preload
        long preloadSamples = (long)(PreloadMs / 1000.0 * _outputSampleRate);
        long preloaded = 0;
        
        AVPacket* packet = ffmpeg.av_packet_alloc();
        AVFrame* frame = ffmpeg.av_frame_alloc();
        try
        {
            int ret;
            while (preloaded < preloadSamples)
            {
                ret = ffmpeg.av_read_frame(_formatCtx, packet);
                if (ret < 0) break;
                if (packet->stream_index != _audioStreamIndex) { ffmpeg.av_packet_unref(packet); continue; }

                ret = ffmpeg.avcodec_send_packet(_codecCtx, packet);
                ffmpeg.av_packet_unref(packet);
                if (ret < 0) break;

                while ((ret = ffmpeg.avcodec_receive_frame(_codecCtx, frame)) >= 0)
                {
                    byte[] pcmBuffer = ProcessFrame(frame); // Reuse new method
                    if (pcmBuffer != null && pcmBuffer.Length > 0)
                    {
                        _preloadBuffer.Enqueue(pcmBuffer);
                        int produced = pcmBuffer.Length / (_channels * _bytesPerSample);
                        preloaded += produced;
                        _currentTs += (long)(produced * 1_000_000L / _outputSampleRate); // Advance ts during preload for sync
                    }
                    ffmpeg.av_frame_unref(frame);
                }
            }
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
            ffmpeg.av_frame_free(&frame);
        }
    }
    
    /// <summary>
    /// Processes a single AVFrame: resamples, applies volume, returns PCM buffer.
    /// </summary>
    /// <param name="frame">The input frame.</param>
    /// <returns>The processed PCM buffer, or null on error.</returns>
    private unsafe byte[] ProcessFrame(AVFrame* frame) 
    {
        long delay = ffmpeg.swr_get_delay(_swrCtx, _codecCtx->sample_rate);
    long maxOutSamples = ffmpeg.av_rescale_rnd(delay + frame->nb_samples, _outputSampleRate, _codecCtx->sample_rate, AVRounding.AV_ROUND_UP);
    int planeSize = (int)maxOutSamples * _bytesPerSample; // Per channel
    byte** outPlanes = stackalloc byte*[_channels]; // Planar output
    for (int ch = 0; ch < _channels; ch++) outPlanes[ch] = (byte*)ffmpeg.av_malloc((ulong)planeSize); // Alloc planes
    try
    {
        int produced = ffmpeg.swr_convert(_swrCtx, outPlanes, (int)maxOutSamples, (byte**)&frame->data, frame->nb_samples);
        if (produced < 0)
        {
            GD.PrintErr($"FFmpegAudioDecoder:ProcessFrame - Swr convert failed: {GetFFmpegError(produced)}");
            return null;
        }

        //!!! NEW: Interleave planar to single buffer for easy mixing
        int interleavedSize = produced * _channels * _bytesPerSample;
        byte[] pcmBuffer = ArrayPool<byte>.Shared.Rent(interleavedSize);
        Span<float> interleaved = MemoryMarshal.Cast<byte, float>(pcmBuffer);
        for (int i = 0; i < produced; i++)
        {
            for (int ch = 0; ch < _channels; ch++)
            {
                unsafe
                {
                    float* planeData = (float*)outPlanes[ch];
                    interleaved[i * _channels + ch] = planeData[i];
                }
            }
        }

        // Apply volume (on interleaved)
        for (int i = 0; i < interleaved.Length; i++)
        {
            interleaved[i] *= _currentVolume;
        }

        // Trimmed copy (no need; return rented, but copy for safety as pool may reuse)
        byte[] trimmed = new byte[interleavedSize];
        Buffer.BlockCopy(pcmBuffer, 0, trimmed, 0, interleavedSize);
        ArrayPool<byte>.Shared.Return(pcmBuffer, clearArray: true);
        return trimmed;
    }
    finally
    {
        // Free planes
        for (int ch = 0; ch < _channels; ch++) ffmpeg.av_free(outPlanes[ch]);
    }
    }


    /// <summary>
    /// Starts playback asynchronously, handling decoding, resampling, and streaming to SDL.
    /// Supports looping, play count, start/end times, and volume/fade controls.
    /// </summary>
    /// <returns>A task representing the asynchronous playback.</returns>
    public async Task PlayAsync()
    {
        _cts = new CancellationTokenSource(); 
        _pcmQueue = new BlockingCollection<byte[]>(MaxBufferedChunks);
        _isPlaying = true;
        _isPaused = false;
        _isStopped = false;
        
        // Start consumer task to dequeue and push paced
        var consumerTask = Task.Run(() => ConsumerLoopAsync(_cts.Token));

        // Producer runs decoding, adding to queue
        await Task.Run(() => ProducerLoop(_cts.Token));

        // Wait for consumer to finish (drains queue)
        await consumerTask;
    }
    
    private void ProducerLoop(CancellationToken token) // Producer decodes and adds to queue
    {
        unsafe
        {
            AVPacket* packet = ffmpeg.av_packet_alloc();
            AVFrame* frame = ffmpeg.av_frame_alloc();
            int ret;
            
            try
            {
                long startTimeUs = (long)(_component.StartTime * 1_000_000); // seconds to us
                long endTimeUs = (long)(_component.EndTime * 1_000_000);
                long endTsStream = ffmpeg.av_rescale_q(endTimeUs, new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE }, _timeBase);
                
                int playCount = 0;
                bool done = false;

                // Enqueue preloaded to queue for consumer
                while (!_preloadBuffer.IsEmpty && !_isStopped && !token.IsCancellationRequested)
                {
                    if (_preloadBuffer.TryDequeue(out byte[] preloadChunk))
                    {
                        _pcmQueue.Add(preloadChunk, token); // Blocks if full
                    }
                }

                while (!done && !_isStopped && !token.IsCancellationRequested)
                {
                    bool eof = false;

                    while (true)
                    {
                        lock (_lock)
                        {
                            while (_isPaused && !_isStopped) Thread.Sleep(10);
                            if (_isStopped || token.IsCancellationRequested) break;
                        }

                        if (!eof)
                        {
                            ret = ffmpeg.av_read_frame(_formatCtx, packet);
                            if (ret == ffmpeg.AVERROR_EOF)
                            {
                                eof = true;
                                ret = ffmpeg.avcodec_send_packet(_codecCtx, null); // Flush decoder
                                if (ret < 0)
                                {
                                    GD.PrintErr($"FFmpegAudioDecoder:ProducerLoop - Flush send failed: {GetFFmpegError(ret)}");
                                    break;
                                }
                            }
                            else if (ret < 0)
                            {
                                GD.PrintErr($"FFmpegAudioDecoder:ProducerLoop - Read frame failed: {GetFFmpegError(ret)}"); 
                                break;
                            }
                            else
                            {
                                if (packet->stream_index != _audioStreamIndex) { ffmpeg.av_packet_unref(packet); continue; }

                                long packetTs = packet->pts != ffmpeg.AV_NOPTS_VALUE ? packet->pts : packet->dts;
                                if (packetTs >= endTsStream) { ffmpeg.av_packet_unref(packet); eof = true; continue; } // Skip packets beyond end

                                ret = ffmpeg.avcodec_send_packet(_codecCtx, packet);
                                ffmpeg.av_packet_unref(packet);
                                if (ret < 0)
                                {
                                    GD.PrintErr($"FFmpegAudioDecoder:ProducerLoop - Send packet failed: {GetFFmpegError(ret)}"); 
                                    break;
                                }
                            }
                        }

                        ret = ffmpeg.avcodec_receive_frame(_codecCtx, frame);
                        if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                        {
                            if (eof) break;
                            Thread.Sleep(1); // Micro-sleep on EAGAIN to reduce CPU spin
                            continue;
                        }
                        else if (ret == ffmpeg.AVERROR_EOF) break;
                        else if (ret < 0)
                        {
                            GD.PrintErr($"FFmpegAudioDecoder:ProducerLoop - Receive frame failed: {GetFFmpegError(ret)}"); 
                            break;
                        }

                        byte[] pcmBuffer = ProcessFrame(frame); // Use extracted method
                        if (pcmBuffer != null && pcmBuffer.Length > 0)
                        {
                            _pcmQueue.Add(pcmBuffer, token);
                            // Advance ts for live chunk
                            int produced = pcmBuffer.Length / (_channels * _bytesPerSample);
                            _currentTs += (long)(produced * 1_000_000L / _outputSampleRate);
                        }
                        ffmpeg.av_frame_unref(frame);

                        // Check end after frame for accuracy
                        if (_currentTs >= endTimeUs)
                        {
                            eof = true;
                            break;
                        }
                    }

                    // Handle end/loop
                    playCount++;
                    if (_component.Loop || playCount < _component.PlayCount)
                    {
                        long seekTs = ffmpeg.av_rescale_q(startTimeUs, new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE }, _timeBase);
                        ret = ffmpeg.av_seek_frame(_formatCtx, _audioStreamIndex, seekTs, ffmpeg.AVSEEK_FLAG_BACKWARD); // Use AVSEEK_FLAG_ANY for non-keyframe seek
                        if (ret < 0)
                        {
                            GD.PrintErr($"FFmpegAudioDecoder:ProducerLoop - Loop seek failed: {GetFFmpegError(ret)}"); 
                            break;
                        }
                        ffmpeg.avformat_flush(_formatCtx); // Extra flush to clear demuxer buffers
                        ffmpeg.avcodec_flush_buffers(_codecCtx);
                        
                        // Drain and discard frames after seek until PTS >= seekTs (skip partial/skipped samples)
                        long targetPts = seekTs;
                        int discardedFrames = 0;
                        while (true)
                        {
                            ret = ffmpeg.av_read_frame(_formatCtx, packet);
                            if (ret < 0) break;
                            if (packet->stream_index != _audioStreamIndex) { ffmpeg.av_packet_unref(packet); continue; }

                            long packetPts = packet->pts != ffmpeg.AV_NOPTS_VALUE ? packet->pts : packet->dts;
                            if (packetPts < targetPts)
                            {
                                ffmpeg.av_packet_unref(packet);
                                continue; // Skip packet
                            }

                            ret = ffmpeg.avcodec_send_packet(_codecCtx, packet);
                            ffmpeg.av_packet_unref(packet);
                            if (ret < 0) 
                            {
                                GD.PrintErr($"FFmpegAudioDecoder:ProducerLoop - Send packet in drain failed: {GetFFmpegError(ret)}");
                                break;
                            }

                            while ((ret = ffmpeg.avcodec_receive_frame(_codecCtx, frame)) >= 0)
                            {
                                long framePts = frame->pts;
                                if (framePts < targetPts)
                                {
                                    // Advance logical ts by discarded frame duration without playing
                                    long discardedUs = ffmpeg.av_rescale_q(frame->nb_samples, new AVRational { num = 1, den = _codecCtx->sample_rate }, new AVRational { num = 1, den = 1_000_000 });
                                    _currentTs += discardedUs;
                                    discardedFrames++;
                                    ffmpeg.av_frame_unref(frame); // Discard frame
                                    continue;
                                }
                                else if (framePts > targetPts)
                                {
                                    // Trim overage from first frame if PTS > target
                                    long overageTicks = framePts - targetPts;// (fixed: ticks, not us)
                                    int overageSamples = (int)ffmpeg.av_rescale_q(overageTicks, _timeBase, new AVRational { num = _codecCtx->sample_rate, den = 1 });// (fixed rescale for samples)
                                    if (overageSamples > 0 && overageSamples < frame->nb_samples)
                                    {
                                        // Trim frame input samples
                                        frame->nb_samples -= overageSamples;
                                        for (uint ch = 0; ch < (uint)_channels; ch++)// (cast to uint for safety)
                                        {
                                            frame->data[ch] += overageSamples * _bytesPerSample;
                                        }
                                        // Now process the trimmed frame
                                        byte[] trimmedPcm = ProcessFrame(frame); // Process trimmed
                                        if (trimmedPcm != null)
                                        {
                                            _pcmQueue.Add(trimmedPcm); // Add trimmed to queue
                                            // Advance ts for trimmed part only
                                            int produced = trimmedPcm.Length / (_channels * _bytesPerSample);
                                            _currentTs += (long)(produced * 1_000_000L / _outputSampleRate);
                                        }
                                        ffmpeg.av_frame_unref(frame);
                                        goto DrainEnd; // Proceed with next frames normally
                                    }
                                } 
                                ffmpeg.av_frame_unref(frame); // Found good frame; break to normal decode
                                goto DrainEnd;
                            }
                        }
                        DrainEnd:;
                        if (discardedFrames > 0)
                        {
                            GD.Print($"FFmpegAudioDecoder:ProducerLoop - Discarded {discardedFrames} frames post-seek for sync.");
                        }
                        _currentTs = startTimeUs; // Reset to exact start
                        GC.Collect(2, GCCollectionMode.Forced, true); // Force full GC at loop end to reclaim memory
                    }
                    else
                    {
                        done = true;
                        _pcmQueue.CompleteAdding(); 
                        EndReached?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"FFmpegAudioDecoder:ProducerLoop - Decoding error: {ex.Message}");
                EndReached?.Invoke(this, EventArgs.Empty); // Early end on error
            }
            finally
            {
                ffmpeg.av_packet_free(&packet);
                ffmpeg.av_frame_free(&frame);
            }
        }
    }
    
    private async Task ConsumerLoopAsync(CancellationToken token) // Consumer dequeues and pushes with pacing
    {
        try
        {
            foreach (byte[] pcmChunk in _pcmQueue.GetConsumingEnumerable(token))
            {
                lock (_lock)
                {
                    while (_isPaused && !_isStopped) Thread.Sleep(10);
                    if (_isStopped || token.IsCancellationRequested) break;
                }

                _playback.PushPcm(pcmChunk); // Push to SDL

                // Pace to real-time: Sleep ~chunk duration (ms)
                int produced = pcmChunk.Length / (_channels * _bytesPerSample);
                long chunkMs = (long)(produced * 1000L / _outputSampleRate);
                await Task.Delay((int)chunkMs, token); // Approximate; for better, use Timer
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            GD.PrintErr($"FFmpegAudioDecoder:ConsumerLoopAsync - Error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Pauses the playback.
    /// </summary>
    public void Pause()
    {
        lock (_lock)
        {
            _isPaused = true;
        }
    }

    /// <summary>
    /// Stops the playback and cancels any ongoing operations.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            _isStopped = true;
            _isPlaying = false;
            _cts?.Cancel();
        }
    }

    /// <summary>
    /// Sets the current volume level.
    /// </summary>
    /// <param name="volume">The volume multiplier (0.0 to 1.0).</param>
    public void SetVolume(float volume)
    {
        lock (_lock)
        {
            _currentVolume = volume;
        }
    }
    
    /// <summary>
    /// Seeks to a specific timestamp in microseconds.
    /// </summary>
    /// <param name="timestampUs">The target timestamp in microseconds.</param>
    public void Seek(long timestampUs)
    {
        lock (_lock) // Added lock for thread safety
        {
            unsafe
            {
                long seekTs = ffmpeg.av_rescale_q(timestampUs, new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE }, _timeBase);
                int ret = ffmpeg.av_seek_frame(_formatCtx, _audioStreamIndex, seekTs, ffmpeg.AVSEEK_FLAG_BACKWARD); // Use AVSEEK_FLAG_ANY for non-keyframe seek
                if (ret < 0)
                {
                    GD.PrintErr($"FFmpegAudioDecoder:Seek - Failed: {GetFFmpegError(ret)}"); // GD.PrintErr
                }
                else
                {
                    ffmpeg.avformat_flush(_formatCtx);
                    ffmpeg.avcodec_flush_buffers(_codecCtx);
                    _currentTs = timestampUs;
                }
            }
        }
    }

    /// <summary>
    /// Gets the FFmpeg error message for a given error code.
    /// </summary>
    /// <param name="error">The FFmpeg error code.</param>
    /// <returns>The error message string.</returns>
    private static unsafe string GetFFmpegError(int error)
    {
        const int bufferSize = 1024;
        byte[] buffer = new byte[bufferSize];
        fixed (byte* pBuffer = buffer)
        {
            ffmpeg.av_strerror(error, pBuffer, (ulong)bufferSize);
        }
        int nullIndex = Array.IndexOf(buffer, (byte)0);
        if (nullIndex >= 0)
        {
            return System.Text.Encoding.ASCII.GetString(buffer, 0, nullIndex);
        }
        return System.Text.Encoding.ASCII.GetString(buffer);
    }
    
    /// <summary>
    /// Disposes of the decoder resources, stopping playback and freeing FFmpeg contexts.
    /// </summary>
    public void Dispose()
    {
        Stop();
        if (_pcmQueue != null)
        {
            _pcmQueue.CompleteAdding();
            while (_pcmQueue.TryTake(out byte[] buffer))
            {
                // Drain queue and return to ArrayPool on dispose
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
            _pcmQueue.Dispose();
        }
        lock (_lock)
        {
            unsafe
            {
                if (_swrCtx != null)
                {
                    fixed (SwrContext** ppSwr = &_swrCtx)
                    {
                        ffmpeg.swr_free(ppSwr);
                    }
                    _swrCtx = null;
                }

                if (_codecCtx != null)
                {
                    fixed (AVCodecContext** ppCodec = &_codecCtx)
                    {
                        ffmpeg.avcodec_free_context(ppCodec);
                    }
                    _codecCtx = null;
                }

                if (_formatCtx != null)
                {
                    fixed (AVFormatContext** ppFormat = &_formatCtx)
                    {
                        ffmpeg.avformat_close_input(ppFormat);
                    }
                    _formatCtx = null;
                }

                fixed (AVChannelLayout* pIn = &_inChLayout)
                {
                    ffmpeg.av_channel_layout_uninit(pIn);
                }

                fixed (AVChannelLayout* pOut = &_outChLayout)
                {
                    ffmpeg.av_channel_layout_uninit(pOut);
                }
            }
        }
        _cts?.Dispose();
        GD.Print("FFmpegAudioDecoder:Dispose - Cleaned up.");
    }
}