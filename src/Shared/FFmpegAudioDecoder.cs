using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using SDL3;
using System.Runtime.InteropServices;
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

    private readonly int _targetSampleRate = 44100; // SDL default; configurable via settings
    private readonly SDL.AudioFormat _targetFormat = SDL.AudioFormat.AudioF32LE; // Float for easy volume (resample to S16 if needed)
    private int _bytesPerSample = sizeof(float); // 4 for F32
    private Queue<byte[]> _pcmBufferQueue = new Queue<byte[]>(); // Pre-decoded PCM chunks (thread-safe with lock)
    private readonly int _bufferChunkSize = 4096 * 4; // ~92ms @44.1kHz mono; tune for latency vs memory

    private int _audioStreamIndex = -1; //!!! Added: Store audio stream index for use in decode
    private bool _isFadingOut;

    // Events mimicking VLC (for ActiveAudioPlayback compat)
    public event EventHandler EndReached; 
    public event EventHandler LengthChanged; 

    public FFmpegAudioDecoder(AudioComponent component)
    {
        _component = component ?? throw new ArgumentNullException(nameof(component));
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

                for (uint i = 0; i < _formatCtx->nb_streams; i++) 
                { 
                    if (_formatCtx->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO) 
                    { 
                        _audioStreamIndex = (int)i; 
                        break; 
                    } 
                } 
                if (_audioStreamIndex == -1) throw new Exception("FFmpegAudioDecoder:InitAsync - No audio stream."); 

                AVCodecParameters* par = _formatCtx->streams[(uint)_audioStreamIndex]->codecpar; 
                AVCodec* codec = ffmpeg.avcodec_find_decoder(par->codec_id); 
                if (codec == null) throw new Exception("FFmpegAudioDecoder:InitAsync - Unsupported codec."); 

                _codecCtx = ffmpeg.avcodec_alloc_context3(codec); 
                ret = ffmpeg.avcodec_parameters_to_context(_codecCtx, par); 
                if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - Param copy failed: {GetError(ret)}"); 

                ret = ffmpeg.avcodec_open2(_codecCtx, codec, null); 
                if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - Codec open failed: {GetError(ret)}"); 

                // Channel layouts 
                fixed (AVChannelLayout* pIn = &_inChLayout) 
                {
                    ffmpeg.av_channel_layout_default(pIn, (int)_codecCtx->ch_layout.nb_channels); 
                }
                fixed (AVChannelLayout* pOut = &_outChLayout) 
                {
                    ffmpeg.av_channel_layout_default(pOut, _component.Metadata.Channels); 
                }

                // Resampler setup
                _swrCtx = ffmpeg.swr_alloc();
                if (_swrCtx == null) throw new Exception("FFmpegAudioDecoder:InitAsync - Swr alloc failed.");

                fixed (AVChannelLayout* pInLayout = &_inChLayout)
                {
                    ret = ffmpeg.av_opt_set_chlayout(_swrCtx, "in_chlayout", pInLayout, 0);
                    if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - Set in chlayout failed: {GetError(ret)}");
                }

                fixed (AVChannelLayout* pOutLayout = &_outChLayout)
                {
                    ret = ffmpeg.av_opt_set_chlayout(_swrCtx, "out_chlayout", pOutLayout, 0);
                    if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - Set out chlayout failed: {GetError(ret)}");
                }

                ret = ffmpeg.av_opt_set_int(_swrCtx, "in_sample_rate", _codecCtx->sample_rate, 0);
                if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - Set in sample rate failed: {GetError(ret)}");

                ret = ffmpeg.av_opt_set_int(_swrCtx, "out_sample_rate", _targetSampleRate, 0);
                if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - Set out sample rate failed: {GetError(ret)}");

                ret = ffmpeg.av_opt_set_sample_fmt(_swrCtx, "in_sample_fmt", _codecCtx->sample_fmt, 0);
                if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - Set in sample fmt failed: {GetError(ret)}");

                ret = ffmpeg.av_opt_set_sample_fmt(_swrCtx, "out_sample_fmt", AVSampleFormat.AV_SAMPLE_FMT_FLT, 0);
                if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - Set out sample fmt failed: {GetError(ret)}");

                ret = ffmpeg.swr_init(_swrCtx);
                if (ret < 0) throw new Exception($"FFmpegAudioDecoder:InitAsync - Swr init failed: {GetError(ret)}");
            }
        });
    }

    public void Play()
    {
        lock (_lock)
        {
            if (_isPlaying) return;
            _isPlaying = true;
            _isPaused = false;
            _isStopped = false;
        }
        _cts = new CancellationTokenSource();
        Task.Run(DecodeLoopAsync);
    }

    public void Pause()
    {
        lock (_lock) _isPaused = true;
    }

    public void Resume()
    {
        lock (_lock) _isPaused = false;
    }

    public void Stop()
    {
        lock (_lock)
        {
            _isStopped = true;
            _isPlaying = false;
        }
        _cts?.Cancel();
    }

    private async Task DecodeLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            if (_isPaused || _isStopped) 
            {
                await Task.Delay(10);
                continue;
            }

            unsafe
            {
                AVPacket* pkt = ffmpeg.av_packet_alloc();
                int ret = ffmpeg.av_read_frame(_formatCtx, pkt);
                if (ret < 0)
                {
                    if (ret == ffmpeg.AVERROR_EOF)
                    {
                        if (_component.Loop)
                        {
                            ffmpeg.avformat_seek_file(_formatCtx, -1, long.MinValue, 0L, long.MaxValue, 0);
                        }
                        else
                        {
                            EndReached?.Invoke(this, EventArgs.Empty);
                            break;
                        }
                    }
                    else
                    {
                        GD.PrintErr($"FFmpegAudioDecoder:DecodeLoopAsync - Read frame error: {GetError(ret)}"); // Prefixed error
                        break;
                    }
                }

                if (pkt->stream_index == _audioStreamIndex)
                {
                    DecodePacketToPcm(pkt);
                }
                ffmpeg.av_packet_unref(pkt);
                ffmpeg.av_packet_free(&pkt); //!!! Altered: Free pkt inside unsafe
            }

            await Task.Delay(1); // Yield to avoid spin
        }
    }

    private unsafe void DecodeNextChunk() // Altered: Mark method unsafe
    {
        AVPacket* pkt = ffmpeg.av_packet_alloc();
        int ret = ffmpeg.av_read_frame(_formatCtx, pkt);
        if (ret < 0) return;

        DecodePacketToPcm(pkt);
        ffmpeg.av_packet_unref(pkt);
        ffmpeg.av_packet_free(&pkt); //!!! Altered: Free pkt inside unsafe
    }

    private unsafe void DecodePacketToPcm(AVPacket* pkt) // Altered: Mark method unsafe
    {
        int ret = ffmpeg.avcodec_send_packet(_codecCtx, pkt);
        if (ret < 0)
        {
            GD.PrintErr($"FFmpegAudioDecoder:DecodePacketToPcm - Send packet error: {GetError(ret)}");
            return;
        }

        AVFrame* frame = ffmpeg.av_frame_alloc(); // Local frame per call to avoid shared state

        while (ffmpeg.avcodec_receive_frame(_codecCtx, frame) >= 0)
        {
            // Resample to target
            long maxOutSamples = ffmpeg.av_rescale_rnd((long)frame->nb_samples, _targetSampleRate, _codecCtx->sample_rate, AVRounding.AV_ROUND_UP) + 256;
            byte* outBuffer = null;
            int outLinesize = 0;

            // Use arrays to pin
            byte*[] outArray = new byte*[1];
            int[] linesizeArray = new int[1];

            fixed (byte** pOutBuffer = outArray)
            fixed (int* pOutLinesize = linesizeArray)
            {
                ret = ffmpeg.av_samples_alloc(pOutBuffer, pOutLinesize, _component.Metadata.Channels, (int)maxOutSamples, AVSampleFormat.AV_SAMPLE_FMT_FLT, 0);
            }

            if (ret < 0)
            {
                GD.PrintErr($"FFmpegAudioDecoder:DecodePacketToPcm - Sample alloc error: {GetError(ret)}");
                continue;
            }

            outBuffer = outArray[0];
            outLinesize = linesizeArray[0];

            byte*[] outBuffers = { outBuffer };
            fixed (byte** ppOut = outBuffers)
            {
                int outSamples = ffmpeg.swr_convert(_swrCtx, ppOut, (int)maxOutSamples, frame->extended_data, frame->nb_samples);
                if (outSamples < 0)
                {
                    GD.PrintErr($"FFmpegAudioDecoder:DecodePacketToPcm - Resample error: {GetError(outSamples)}");
                    ffmpeg.av_freep(&outBuffer);
                    continue;
                }

                // Apply volume to buffer
                float* samples = (float*)outBuffer;
                for (int i = 0; i < outSamples * _component.Metadata.Channels; i++)
                {
                    samples[i] *= _currentVolume;
                }

                // Queue as byte[] chunk
                byte[] pcmChunk = new byte[outSamples * _component.Metadata.Channels * _bytesPerSample];
                Marshal.Copy((IntPtr)outBuffer, pcmChunk, 0, pcmChunk.Length);
                lock (_lock)
                {
                    _pcmBufferQueue.Enqueue(pcmChunk);
                }

                ffmpeg.av_freep(&outBuffer);
            }
        }

        ffmpeg.av_frame_free(&frame); // Free local frame
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

    private string GetError(int ret) => GetFFmpegError(ret);

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