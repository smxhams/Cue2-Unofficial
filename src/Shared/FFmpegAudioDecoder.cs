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

    public int TargetSampleRate { get; } = 44100; // SDL default; configurable via settings
    public SDL.AudioFormat TargetFormat { get; } = SDL.AudioFormat.AudioF32LE; // Float for easy volume (resample to S16 if needed)
    private int _bytesPerSample = sizeof(float); // 4 for F32
    private int _audioStreamIndex = -1;

    private long _currentTs = 0; // In AV_TIME_BASE units (us)
    public long CurrentTime => _currentTs / 1000; // ms

    public event EventHandler EndReached;
    public event EventHandler<long> LengthChanged;

    public FFmpegAudioDecoder(AudioComponent component, ActiveAudioPlayback playback)
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
                    if (_codecCtx == null) throw new Exception("FFmpegAudioDecoder:InitAsync - Alloc codec context failed."); 
                    
                    ret = ffmpeg.avcodec_parameters_to_context(_codecCtx, stream->codecpar);
                    if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - Params to context failed: {GetError(ret)}");

                    ret = ffmpeg.avcodec_open2(_codecCtx, codec, null);
                    if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - Open codec failed: {GetError(ret)}");
                }
                

                // Setup resampler
                fixed (SwrContext** ppSwr = &_swrCtx)
                {
                    _swrCtx = ffmpeg.swr_alloc();
                    if (_swrCtx == null) throw new Exception("FFmpegAudioDecoder:InitAsync - Alloc Swr failed.");

                    _inChLayout = _codecCtx->ch_layout;

                    fixed (AVChannelLayout* pIn = &_inChLayout)
                    fixed (AVChannelLayout* pOut = &_outChLayout)
                    {
                        ffmpeg.av_channel_layout_default(pOut, 2);
                        
                        ret = ffmpeg.av_opt_set_chlayout((void*)_swrCtx, "in_chlayout", pIn, 0);
                        if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - in_chlayout init failed: {GetError(ret)}");
                        ret = ffmpeg.av_opt_set_chlayout((void*)_swrCtx, "out_chlayout", pOut, 0);
                        if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - out_chlayout init failed: {GetError(ret)}");
                        ret = ffmpeg.av_opt_set_int((void*)_swrCtx, "in_sample_rate", _codecCtx->sample_rate, 0);
                        if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - in_sample_rate init failed: {GetError(ret)}");
                        ret = ffmpeg.av_opt_set_int((void*)_swrCtx, "out_sample_rate", TargetSampleRate, 0);
                        if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - out_sample_rate init failed: {GetError(ret)}");
                        ret = ffmpeg.av_opt_set_sample_fmt((void*)_swrCtx, "in_sample_fmt", _codecCtx->sample_fmt, 0);
                        if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - in_sample_fmt init failed: {GetError(ret)}");
                        ret = ffmpeg.av_opt_set_sample_fmt((void*)_swrCtx, "out_sample_fmt", AVSampleFormat.AV_SAMPLE_FMT_FLT, 0); // F32
                        if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - out_sample_fmt init failed: {GetError(ret)}");

                        ret = ffmpeg.swr_init(_swrCtx);
                        if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - Swr init failed: {GetError(ret)}");
                    } 
                }

                // Preload initial packets if needed (for low-latency start)
                // For now, skip preload for simplicity; decode on play
            }
        });
    }

    /// <summary>
    /// Starts decoding and pushing PCM to playback's SDL streams in a background task.
    /// Handles loop/playcount via seek on end.
    /// </summary>
    public async Task Play()
    {
        _cts = new CancellationTokenSource();
        _isPlaying = true;
        _isPaused = false;
        _isStopped = false;

        await Task.Run(() =>
        {
            unsafe
            {
                AVPacket* packet = ffmpeg.av_packet_alloc();
                AVFrame* frame = ffmpeg.av_frame_alloc();

                try
                {
                    int ret;
                    long startTsUs = (long)(_component.StartTime * 1_000_000);
                    long endTsUs = _component.EndTime >= 0 ? (long)(_component.EndTime * 1_000_000) : (long)(_component.Metadata.Duration * 1_000_000); 
                    long relativeEndUs = endTsUs - startTsUs; 
                    bool useCustomEnd = _component.EndTime >= 0; 

                    AVStream* stream = _formatCtx->streams[(uint)_audioStreamIndex];
                    AVRational tb = stream->time_base;
                    long startTs = ffmpeg.av_rescale_q(startTsUs, ffmpeg.av_get_time_base_q(), tb);
                    long endTs = ffmpeg.av_rescale_q(endTsUs, ffmpeg.av_get_time_base_q(), tb);

                    GD.Print($"FFmpegAudioDecoder:Play - Seeking to {startTs} in stream TB (num/den: {tb.num}/{tb.den}, startTime: {_component.StartTime}s)");

                    ret = ffmpeg.av_seek_frame(_formatCtx, -1, startTsUs, ffmpeg.AVSEEK_FLAG_BACKWARD);
                    GD.Print($"FFmpegAudioDecoder:Play - Seek successful (ret: {ret})");
                    _currentTs = startTsUs;

                    ffmpeg.avcodec_flush_buffers(_codecCtx);
                    GD.Print("FFmpegAudioDecoder:Play - Flushed decoder buffers post-seek");

                    GD.Print($"FFmpegAudioDecoder:Play - End TS: {endTs} (stream dur: {stream->duration}, metadata dur: {_component.Metadata.Duration}s, relative end us: {relativeEndUs})");

                    int playCount = 1; // Local tracking, but sync with playback if needed

                    while (!_isStopped && !_cts.Token.IsCancellationRequested)
                    {
                        lock (_lock)
                        {
                            if (_isPaused)
                            {
                                Thread.Sleep(10);
                                continue;
                            }
                        }

                        ret = ffmpeg.av_read_frame(_formatCtx, packet);
                        if (ret >= 0)
                        {
                            if (packet->stream_index != _audioStreamIndex)
                            {
                                ffmpeg.av_packet_unref(packet);
                                continue;
                            }

                            GD.Print($"FFmpegAudioDecoder:Play - Accepted packet (PTS: {packet->pts})"); // For debug

                            ret = ffmpeg.avcodec_send_packet(_codecCtx, packet);
                            if (ret < 0)
                            {
                                GD.PrintErr($"FFmpegAudioDecoder:Play - Send failed: {GetError(ret)}");
                                break;
                            }
                        }
                        else if (ret == ffmpeg.AVERROR_EOF)
                        {
                            GD.Print("FFmpegAudioDecoder:Play - Hit EOF (ret: {ret})");
                            _playback.CurrentPlayCount++;
                            if (_playback.CurrentPlayCount < _playback.EffectivePlayCount)
                            {
                                ret = ffmpeg.av_seek_frame(_formatCtx, -1, startTsUs, ffmpeg.AVSEEK_FLAG_BACKWARD);
                                if (ret < 0)
                                {
                                    GD.PrintErr($"FFmpegAudioDecoder:Play - Loop seek failed: {GetError(ret)}");
                                    break;
                                }
                                _currentTs = startTsUs;
                                GD.Print($"FFmpegAudioDecoder:Play - Looped to start (playCount: {_playback.CurrentPlayCount}, new TS: {startTs})");
                                continue;
                            }
                            else
                            {
                                EndReached?.Invoke(this, EventArgs.Empty);
                                break;
                            }
                        }
                        else
                        {
                            GD.PrintErr($"FFmpegAudioDecoder:Play - Unexpected read error (ret: {ret}): {GetError(ret)}");
                            break;
                        }

                        ffmpeg.av_packet_unref(packet);

                        while (true)
                        {
                            ret = ffmpeg.avcodec_receive_frame(_codecCtx, frame);
                            if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                                break;
                            if (ret < 0)
                            {
                                GD.PrintErr($"FFmpegAudioDecoder:Play - Receive failed: {GetError(ret)}");
                                break;
                            }

                            GD.Print($"FFmpegAudioDecoder:Play - Decoded frame: {frame->nb_samples} samples, PTS: {frame->pts}");

                            long currentStreamTs = frame->pts;

                            if (useCustomEnd && currentStreamTs >= endTs) 
                            {
                                GD.Print($"FFmpegAudioDecoder:Play - Reached custom end (stream TS: {currentStreamTs} >= {endTs})");
                                ffmpeg.av_frame_unref(frame); 
                                _playback.CurrentPlayCount++; 
                                if (_playback.CurrentPlayCount < _playback.EffectivePlayCount) 
                                {
                                    ret = ffmpeg.av_seek_frame(_formatCtx, -1, startTsUs, ffmpeg.AVSEEK_FLAG_BACKWARD); 
                                    if (ret < 0) 
                                    {
                                        GD.PrintErr($"FFmpegAudioDecoder:Play - Loop seek failed: {GetError(ret)}"); 
                                        break; 
                                    } 
                                    _currentTs = startTsUs; 
                                    GD.Print($"FFmpegAudioDecoder:Play - Looped to start (playCount: {_playback.CurrentPlayCount}, new TS: {startTs})"); 
                                    break; // Break inner to continue outer 
                                } 
                                else 
                                {
                                    EndReached?.Invoke(this, EventArgs.Empty); 
                                    _isStopped = true; // Ensure stop 
                                    break; // Break inner 
                                } 
                            } 

                            // Resample
                            int bufferSize = ffmpeg.av_samples_get_buffer_size(null, _outChLayout.nb_channels, frame->nb_samples, (AVSampleFormat)TargetFormat, 1);
                            byte[] outBuffer = new byte[bufferSize];
                            fixed (byte* pOut = outBuffer) 
                            { 
                                byte** ppOut = stackalloc byte* [_outChLayout.nb_channels];
                                ppOut[0] = pOut; 
                                for (int i = 1; i < _outChLayout.nb_channels; i++) ppOut[i] = null; 

                                byte** ppIn = &(frame->data.item0); //!!!

                                int outSamples = ffmpeg.swr_convert(_swrCtx, ppOut, frame->nb_samples, ppIn, frame->nb_samples); 
                                GD.Print($"FFmpegAudioDecoder:Play - Resampled: {outSamples} samples"); 

                                // Apply volume
                                lock (_lock)
                                {
                                    for (int i = 0; i < bufferSize / _bytesPerSample; i++)
                                    {
                                        float* fPtr = (float*)pOut + i;
                                        *fPtr *= _currentVolume;
                                    }
                                }
                            } 

                            // Push to SDL streams via playback
                            _playback.PushPcm(outBuffer);
                            GD.Print($"FFmpegAudioDecoder:Play - Pushed {bufferSize} bytes");

                            // Update TS
                            long frameDurUs = (long)frame->nb_samples * 1_000_000 / TargetSampleRate;
                            _currentTs += frameDurUs;
                            GD.Print($"FFmpegAudioDecoder:Play - Updated TS: us {_currentTs} (frame dur: {frameDurUs}), stream {currentStreamTs} (end: {endTs})");

                            ffmpeg.av_frame_unref(frame);
                        }

                        if (_isStopped) break; // Check after inner loop 
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

            _isPlaying = false;
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