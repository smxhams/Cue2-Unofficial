using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using SDL3;
using System.Runtime.InteropServices;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Godot;

namespace Cue2.Shared;

/// <summary>
/// Decodes audio from FFmpeg to PCM buffers for SDL streaming.
/// Manages packet/frame lifecycle, threading for low-latency, and controls (pause, stop, fade).
/// Designed for cue playback: Preload packets, decode on-demand.
/// </summary>
public class FFmpegAudioDecoderOld : IDisposable
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

    public int TargetSampleRate { get; } = 44100; // SDL default; configurable via settings
    public SDL.AudioFormat TargetFormat { get; } = SDL.AudioFormat.AudioF32LE; // Float for easy volume (resample to S16 if needed)
    private int _bytesPerSample = sizeof(float); // 4 for F32
    private int _audioStreamIndex = -1;

    private long _currentTs = 0; // In AV_TIME_BASE units (us)
    public long CurrentTime => _currentTs / 1000; // ms

    public event EventHandler EndReached;
    public event EventHandler<long> LengthChanged;

    public FFmpegAudioDecoderOld(AudioComponent component, ActiveAudioPlayback playback)
    {
        _component = component ?? throw new ArgumentNullException(nameof(component));
        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        if (_component.Metadata == null) throw new Exception("Metadata required for decoder setup.");
    }

    /// <summary>
    /// Async init: Open file, find stream, setup decoder/resampler.
    /// Preloads initial packets for low-latency start.
    /// </summary>
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
                    if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - Open failed: {GetError(ret)}"); 
                }

                ret = ffmpeg.avformat_find_stream_info(_formatCtx, null); 
                if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - Stream info failed: {GetError(ret)}");

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
                AVCodec* codec = ffmpeg.avcodec_find_decoder(stream->codecpar->codec_id); 
                if (codec == null) throw new Exception("FFmpegAudioDecoder:InitAsync - Unsupported codec."); 

                fixed (AVCodecContext** pCodecCtx = &_codecCtx)
                {
                    _codecCtx = ffmpeg.avcodec_alloc_context3(codec);
                    ret = ffmpeg.avcodec_parameters_to_context(_codecCtx, stream->codecpar);
                    if (ret < 0) throw new Exception($"Params to context failed: {GetError(ret)}");

                    ret = ffmpeg.avcodec_open2(_codecCtx, codec, null);
                    if (ret < 0) throw new Exception($"Open codec failed: {GetError(ret)}");
                }
                

                // Setup resampler to target (mono? No, keep channels, but float)
                fixed (AVChannelLayout* pInChLayout = &_inChLayout)
                {
                    fixed (AVChannelLayout* pOutChLayout = &_outChLayout)
                    {
                        ffmpeg.av_channel_layout_copy(pInChLayout, &_codecCtx->ch_layout);
                        _outChLayout = _inChLayout; // Keep channels
                        ret = ffmpeg.av_channel_layout_copy(pOutChLayout, pInChLayout);
                        if (ret < 0) throw new Exception("Channel layout copy failed");

                        fixed (SwrContext** ppSwr = &_swrCtx)
                        {
                            ffmpeg.swr_alloc_set_opts2(
                                ppSwr, pOutChLayout, AVSampleFormat.AV_SAMPLE_FMT_FLT, 
                                TargetSampleRate, pInChLayout, _codecCtx->sample_fmt,
                                _codecCtx->sample_rate, 0, null);
                            if (_swrCtx == null) throw new Exception("Swr alloc failed");

                            ret = ffmpeg.swr_init(_swrCtx);
                            if (ret < 0) throw new Exception($"Swr init failed: {GetError(ret)}");
                        }
                    }
                }

                // Invoke length changed with total duration ms
                long totalDurationMs = (long)(_formatCtx->duration * 1000 / ffmpeg.AV_TIME_BASE);
                LengthChanged?.Invoke(this, totalDurationMs);

                GD.Print("FFmpegAudioDecoder:InitAsync - Initialized successfully.");
            }
        });
    }

    public void Play()
    {
        lock (_lock)
        {
            if (_isPlaying) return;
            _isPlaying = true;
            _isStopped = false;
            _cts = new CancellationTokenSource();
        }

        Task.Run(() =>
        {
            unsafe
            {
                AVPacket* packet = ffmpeg.av_packet_alloc();
                AVFrame* frame = ffmpeg.av_frame_alloc();
                try
                {
                    // Validate pre-start
                    if (_audioStreamIndex == -1 || _component.Metadata.Duration <= 0)
                    {
                        GD.Print("FFmpegAudioDecoder:Play - No audio stream or invalid duration; aborting playback.");
                        EndReached?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                    
                    AVStream* stream = _formatCtx->streams[(uint)_audioStreamIndex];
                    var tb = stream->time_base; // Cache timebase
                    
                    // Calculate seek timestamp in stream timebase
                    long startTimeUs = (long)(_component.StartTime * 1000000);
                    long seekTs = ffmpeg.av_rescale_q(startTimeUs, new AVRational { num = 1, den = 1000000 }, tb);
                    GD.Print($"FFmpegAudioDecoder:Play - Seeking to {seekTs} in stream TB (num/den: {tb.num}/{tb.den}, startTime: {_component.StartTime}s)");

                    int ret = ffmpeg.av_seek_frame(_formatCtx, _audioStreamIndex, seekTs, ffmpeg.AVSEEK_FLAG_BACKWARD);
                    if (ret < 0)
                    {
                        GD.PrintErr($"FFmpegAudioDecoder:Play - Seek failed (ret: {ret}): {GetError(ret)}");
                        EndReached?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                    GD.Print($"FFmpegAudioDecoder:Play - Seek successful (ret: {ret})");
                    
                    // Flush decoder after seek
                    ffmpeg.avcodec_flush_buffers(_codecCtx);
                    GD.Print("FFmpegAudioDecoder:Play - Flushed decoder buffers post-seek");
                    
                    // Discard packets until we reach or pass seek time
                    long discardUntilTs = seekTs;
                    while ((ret = ffmpeg.av_read_frame(_formatCtx, packet)) >= 0)
                    {
                        if (packet->stream_index != _audioStreamIndex || (packet->pts != ffmpeg.AV_NOPTS_VALUE && packet->pts < discardUntilTs))
                        {
                            GD.Print($"FFmpegAudioDecoder:Play - Discarding packet (stream: {packet->stream_index}, PTS: {packet->pts} < {discardUntilTs})"); //!!!
                            ffmpeg.av_packet_unref(packet);
                            continue;
                        }
                        GD.Print($"FFmpegAudioDecoder:Play - Accepted first packet (PTS: {packet->pts})");
                        break;
                    }
                    if (ret < 0)
                    {
                        GD.PrintErr($"FFmpegAudioDecoder:Play - No valid packet after seek (ret: {ret}): {GetError(ret)}");
                        EndReached?.Invoke(this, EventArgs.Empty);
                        return;
                    }

                    // Calculate end in stream timebase (relative to start for clips)
                    long streamDuration = stream->duration > 0 ? stream->duration : ffmpeg.av_rescale_q((long)(_component.Metadata.Duration * 1000000), new AVRational { num = 1, den = 1000000 }, tb); // Fallback rescaled //!!!
                    long endTimeUs = _component.EndTime >= 0 ? (long)(_component.EndTime * 1000000) : (long)(_component.Metadata.Duration * 1000000);
                    long relativeEndUs = endTimeUs - startTimeUs;
                    long endTsStream = seekTs + ffmpeg.av_rescale_q(relativeEndUs, new AVRational { num = 1, den = 1000000 }, tb);
                    GD.Print($"FFmpegAudioDecoder:Play - End TS: {endTsStream} (stream dur: {streamDuration}, metadata dur: {_component.Metadata.Duration}s, relative end us: {relativeEndUs})");

                    int playCount = 0;
                    long currentStreamTs = seekTs; // Track in stream TB
                    _currentTs = startTimeUs; // Start from actual seek time

                    while (!_isStopped && playCount < _component.PlayCount && !_cts.IsCancellationRequested) // (handle PlayCount, Loop via infinite if Loop)
                    {
                        if (_component.Loop) playCount = 0; // Reset for infinite loop

                        bool hasAudio = false; // Track if any audio processed this iteration
                        
                        while ((ret = ffmpeg.av_read_frame(_formatCtx, packet)) >= 0)
                        {
                            if (_isStopped || _cts.IsCancellationRequested) break;
                            // Pause with Thread.Sleep (non-async, but safe in unsafe)
                            while (_isPaused)
                            {
                                Thread.Sleep(10);
                                if (_isStopped || _cts.IsCancellationRequested) break;
                            }

                            if (packet->stream_index == _audioStreamIndex)
                            {
                                // Send packet
                                ret = ffmpeg.avcodec_send_packet(_codecCtx, packet);
                                if (ret < 0 && ret != ffmpeg.AVERROR(ffmpeg.EAGAIN)) 
                                {
                                    GD.PrintErr($"FFmpegAudioDecoder:Play - Send packet failed (ret: {ret}): {GetError(ret)}");
                                    break;
                                }
                                
                                // Receive frames
                                while ((ret = ffmpeg.avcodec_receive_frame(_codecCtx, frame)) >= 0)
                                {
                                    
                                    if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF) break;
                                    if (ret < 0) 
                                    {
                                        GD.PrintErr($"FFmpegAudioDecoder:Play - Receive frame failed: {GetError(ret)}");
                                        break;
                                    }

                                    hasAudio = true;
                                    GD.Print($"FFmpegAudioDecoder:Play - Decoded frame: {frame->nb_samples} samples, PTS: {frame->pts}");
                                    
                                    // Resample
                                    long maxOutSamples = ffmpeg.av_rescale_rnd(ffmpeg.swr_get_delay(_swrCtx, (int)frame->sample_rate) + frame->nb_samples, TargetSampleRate, (int)frame->sample_rate, AVRounding.AV_ROUND_UP);
                                    byte* outBuffer = (byte*)ffmpeg.av_malloc((ulong)(maxOutSamples * _component.Metadata.Channels * _bytesPerSample));
                                    byte** outBuffers = &outBuffer;

                                    int outSamples = ffmpeg.swr_convert(_swrCtx, outBuffers, (int)maxOutSamples, frame->extended_data, frame->nb_samples);
                                    if (outSamples < 0)
                                    {
                                        GD.PrintErr($"FFmpegAudioDecoder:Play - Resample error: {GetError(outSamples)}");
                                        ffmpeg.av_freep(&outBuffer);
                                        continue;
                                    }

                                    if (outSamples > 0)
                                    {
                                        GD.Print($"FFmpegAudioDecoder:Play - Resampled: {outSamples} samples");
                                        // Apply volume
                                        float* samples = (float*)outBuffer;
                                        for (int i = 0; i < outSamples * _component.Metadata.Channels; i++)
                                        {
                                            samples[i] *= _currentVolume;
                                        }

                                        // Update timestamps
                                        long frameDurationUs = (long)(outSamples * 1000000L / TargetSampleRate);
                                        _currentTs += frameDurationUs;
                                        currentStreamTs += ffmpeg.av_rescale_q(frameDurationUs, new AVRational { num = 1, den = 1000000 }, tb); //!!!
                                        GD.Print($"FFmpegAudioDecoder:Play - Updated TS: us {_currentTs} (frame dur: {frameDurationUs}), stream {currentStreamTs} (end: {endTsStream})");

                                        // Check for custom end
                                        if (_component.EndTime >= 0 && currentStreamTs >= endTsStream)
                                        {
                                            GD.Print($"FFmpegAudioDecoder:Play - Reached custom end (stream TS: {currentStreamTs} >= {endTsStream})");
                                            ffmpeg.av_freep(&outBuffer);
                                            break;
                                        }

                                        // Create byte[] pcm and push to streams
                                        byte[] pcm = new byte[outSamples * _component.Metadata.Channels * _bytesPerSample];
                                        Marshal.Copy((IntPtr)outBuffer, pcm, 0, pcm.Length);
                                        _playback.PushPcm(pcm);
                                        GD.Print($"FFmpegAudioDecoder:Play - Pushed {pcm.Length} bytes");
                                        
                                    }

                                    ffmpeg.av_freep(&outBuffer);
                                }
                            }
                            ffmpeg.av_packet_unref(packet);
                        }
                        if (!hasAudio)
                        {
                            GD.Print("FFmpegAudioDecoder:Play - No audio processed this iteration; possible empty segment");
                        }

                        // Check EOF or end (in stream TB)
                        if (ret == ffmpeg.AVERROR_EOF || currentStreamTs >= endTsStream)
                        {
                            GD.Print($"FFmpegAudioDecoder:Play - Hit EOF (ret: {ret}) or end (stream TS: {currentStreamTs} >= {endTsStream})");
                            playCount++;
                            if (playCount < _component.PlayCount || _component.Loop)
                            {
                                // Seek back for loop/replay
                                ret = ffmpeg.av_seek_frame(_formatCtx, _audioStreamIndex, seekTs, ffmpeg.AVSEEK_FLAG_BACKWARD);
                                if (ret < 0) 
                                {
                                    GD.PrintErr($"FFmpegAudioDecoder:Play - Loop seek failed (ret: {ret}): {GetError(ret)}");
                                    break;
                                }
                                ffmpeg.avcodec_flush_buffers(_codecCtx);
                                currentStreamTs = seekTs;
                                _currentTs = startTimeUs;
                                GD.Print($"FFmpegAudioDecoder:Play - Looped to start (playCount: {playCount}, new TS: {currentStreamTs})");
                            }
                            else
                            {
                                EndReached?.Invoke(this, EventArgs.Empty);
                                break;
                            }
                        }
                        else if (ret < 0 && ret != ffmpeg.AVERROR_EOF)
                        {
                            GD.PrintErr($"FFmpegAudioDecoder:Play - Unexpected read error (ret: {ret}): {GetError(ret)}");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"FFmpegAudioDecoder:Play - Decoding error: {ex.Message}");
                }
                finally
                {
                    ffmpeg.av_packet_free(&packet);
                    ffmpeg.av_frame_free(&frame);
                }
            }
        });
    }

    public void Pause()
    {
        lock (_lock)
        {
            _isPaused = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _isStopped = true;
            _isPlaying = false;
            _cts?.Cancel();
        }
    }

    public void SetVolume(float volume)
    {
        lock (_lock)
        {
            _currentVolume = volume;
        }
    }
    
    public void Seek(long timestampUs)
    {
        unsafe
        {
            int ret = ffmpeg.av_seek_frame(_formatCtx, -1, timestampUs, ffmpeg.AVSEEK_FLAG_BACKWARD);
            if (ret < 0)
            {
                GD.PrintErr($"FFmpegAudioDecoder:Seek - Failed: {GetError(ret)}");
            }
            else
            {
                _currentTs = timestampUs;
            }
        }
    }

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

    private static unsafe string GetError(int ret) => MediaEngine.GetFFmpegError(ret);

    public void Dispose()
    {
        Stop();
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