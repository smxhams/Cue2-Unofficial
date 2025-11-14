using Godot;
using FFmpeg.AutoGen;
using System;
using System.Collections.Concurrent;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Cue2.Shared;

/// <summary>
/// Simple FFmpeg video decoder for basic video playback.
/// Decodes video frames to RGBA8 format and provides them via events.
/// </summary>
public class FFmpegVideoDecoder : IDisposable
{
    // FFmpeg fields
    private unsafe AVFormatContext* _formatCtx;
    private unsafe AVCodecContext* _codecCtx;
    private unsafe SwsContext* _swsCtx;
    private int _videoStreamIndex = -1;
    private int _width, _height;
    private double _timeBase;
    private ManualResetEventSlim _pauseEvent = new ManualResetEventSlim(true);
    private CancellationTokenSource _cts;
    private Task _decodeTask;
    private BlockingCollection<(byte[], double)> _frameQueue;
    private object _ffmpegLock = new object();
    private long _currentBufferSize = 0;
    private double _currentBufferedTime = 0;
    private double _lastPlayedTime = 0;
    private bool _playbackReady = false;

    /// <summary>
    /// Event raised when a new frame is decoded and ready.
    /// </summary>
    public event Action<byte[]> FrameReady;

    /// <summary>
    /// Event raised when the current playback time is updated.
    /// </summary>
    public event Action<double> TimeUpdated;

    /// <summary>
    /// Event raised when the end of the video is reached.
    /// </summary>
    public event Action EndReached;

    /// <summary>
    /// Event raised when the initial buffer is ready for playback.
    /// </summary>
    public event Action PlaybackReady;

    /// <summary>
    /// Gets the video width.
    /// </summary>
    public int Width => _width;

    /// <summary>
    /// Gets the video height.
    /// </summary>
    public int Height => _height;

    /// <summary>
    /// Gets the video duration in seconds.
    /// </summary>
    public double Duration { get; private set; }

    /// <summary>
    /// Gets whether the decoder is currently paused.
    /// </summary>
    public bool IsPaused { get; private set; } = false;

    /// <summary>
    /// Gets the current decoding task, if any.
    /// </summary>
    public Task DecodingTask { get; private set; }

    /// <summary>
    /// Maximum buffer time in seconds.
    /// </summary>
    public double MaxBufferTime { get; set; } = 5.0;

    /// <summary>
    /// Maximum buffer size in bytes.
    /// </summary>
    public long MaxBufferSize { get; set; } = 500 * 1024 * 1024; // 500MB

    /// <summary>
    /// Gets the current buffered time in seconds.
    /// </summary>
    public double BufferedTime { get; private set; }

    /// <summary>
    /// Gets the current buffer size in bytes.
    /// </summary>
    public long BufferedSize { get; private set; }

    /// <summary>
    /// Gets whether the decoder is currently buffering.
    /// </summary>
    public bool IsBuffering { get; private set; }

    /// <summary>
    /// Starts decoding the specified video file asynchronously.
    /// </summary>
    /// <param name="filePath">Path to the video file.</param>
    /// <returns>A task representing the decoding operation.</returns>
    public async Task StartDecodingAsync(string filePath)
    {
        if (DecodingTask != null && !DecodingTask.IsCompleted)
        {
            GD.PrintErr("SimpleVideoDecoder: Decoding already in progress");
            return;
        }

        DecodingTask = Task.Run(() => InitDecoder(filePath));
        await DecodingTask;
    }

    /// <summary>
    /// Stops the decoding process asynchronously.
    /// </summary>
    /// <returns>A task that completes when decoding has stopped.</returns>
    public async Task StopDecodingAsync()
    {
        if (DecodingTask == null || DecodingTask.IsCompleted)
            return;

        _cts?.Cancel();
        await DecodingTask;
    }

    /// <summary>
    /// Pauses the decoding process.
    /// </summary>
    public void Pause()
    {
        _pauseEvent.Reset();
        IsPaused = true;
    }

    /// <summary>
    /// Resumes the decoding process.
    /// </summary>
    public void Resume()
    {
        _pauseEvent.Set();
        IsPaused = false;
    }

    /// <summary>
    /// Seeks to the specified time in seconds.
    /// </summary>
    /// <param name="time">The time to seek to.</param>
    public unsafe void Seek(double time)
    {
        if (_formatCtx == null || _codecCtx == null) return;

        lock (this)
        {
            // Clear buffer
            while (_frameQueue.TryTake(out _)) { }
            _currentBufferSize = 0;
            _currentBufferedTime = 0;
            BufferedSize = 0;
            BufferedTime = 0;
            _lastPlayedTime = time;
            _playbackReady = true; // Resume playing as soon as ready after seek
            Monitor.PulseAll(this);
        }

        lock (_ffmpegLock)
        {
            long timestamp = (long)(time / _timeBase); // Convert to stream time_base units
            int ret = ffmpeg.av_seek_frame(_formatCtx, _videoStreamIndex, timestamp, 1); // AVSEEK_FLAG_BACKWARD
            if (ret >= 0)
            {
                ffmpeg.avformat_flush(_formatCtx);
                ffmpeg.avcodec_flush_buffers(_codecCtx);
                GD.Print($"Seeked to {time}s");
            }
            else
            {
                GD.PrintErr($"Seek failed: {GetFFmpegError(ret)}");
            }
        }
    }

    private unsafe void InitDecoder(string filePath)
    {
        try
        {
            int ret;
            fixed (AVFormatContext** pCtx = &_formatCtx)
            {
                ret = ffmpeg.avformat_open_input(pCtx, filePath, null, null);
                if (ret < 0) throw new Exception($"Open failed: {GetFFmpegError(ret)}");
            }

            ret = ffmpeg.avformat_find_stream_info(_formatCtx, null);
            if (ret < 0) throw new Exception($"Stream info failed: {GetFFmpegError(ret)}");

            Duration = _formatCtx->duration != ffmpeg.AV_NOPTS_VALUE ? _formatCtx->duration / (double)ffmpeg.AV_TIME_BASE : 0;

            for (uint i = 0; i < _formatCtx->nb_streams; i++)
            {
                if (_formatCtx->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    _videoStreamIndex = (int)i;
                    break;
                }
            }
            if (_videoStreamIndex == -1) throw new Exception("No video stream.");

            AVStream* stream = _formatCtx->streams[(uint)_videoStreamIndex];
            _timeBase = stream->time_base.num / (double)stream->time_base.den;
            AVCodec* codec = ffmpeg.avcodec_find_decoder(stream->codecpar->codec_id);
            if (codec == null) throw new Exception("Unsupported codec.");

            fixed (AVCodecContext** pCodecCtx = &_codecCtx)
            {
                _codecCtx = ffmpeg.avcodec_alloc_context3(codec);
                ret = ffmpeg.avcodec_parameters_to_context(_codecCtx, stream->codecpar);
                if (ret < 0) throw new Exception($"Params to context failed: {GetFFmpegError(ret)}");

                ret = ffmpeg.avcodec_open2(_codecCtx, codec, null);
                if (ret < 0) throw new Exception($"Open codec failed: {GetFFmpegError(ret)}");
            }

            _width = _codecCtx->width;
            _height = _codecCtx->height;

            // Setup scaler to RGB24
            _swsCtx = ffmpeg.sws_getContext(
                _width, _height, _codecCtx->pix_fmt,
                _width, _height, AVPixelFormat.AV_PIX_FMT_RGB24,
                2, null, null, null);
            if (_swsCtx == null) throw new Exception("Sws context failed");

            GD.Print($"SimpleVideoDecoder initialized: {_width}x{_height}");

            // Start decoding tasks
            _cts = new CancellationTokenSource();
            _frameQueue = new BlockingCollection<(byte[], double)>(1000); // Bounded queue
            var producerTask = Task.Run(() => ProducerLoop(_cts.Token));
            var consumerTask = Task.Run(() => ConsumerLoopAsync(_cts.Token));
            _decodeTask = Task.WhenAll(producerTask, consumerTask);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"SimpleVideoDecoder InitDecoder error: {ex.Message}");
        }
    }

    private unsafe void ProducerLoop(CancellationToken token)
    {
        AVPacket* packet = ffmpeg.av_packet_alloc();
        AVFrame* frame = ffmpeg.av_frame_alloc();
        AVFrame* rgbFrame = ffmpeg.av_frame_alloc();

        try
        {
            // Allocate RGB buffer
            int rgbBufferSize = _width * _height * 3;
            byte* rgbBuffer = (byte*)ffmpeg.av_malloc((ulong)rgbBufferSize);
            rgbFrame->data[0] = rgbBuffer;
            rgbFrame->linesize[0] = _width * 3;

            int ret;
            while (!token.IsCancellationRequested)
            {
                // Check buffer limits
                lock (this)
                {
                    while (BufferedTime >= MaxBufferTime || _currentBufferSize >= MaxBufferSize)
                    {
                        IsBuffering = true;
                        Monitor.Wait(this, 100); // Wait until space available
                        if (token.IsCancellationRequested) break;
                    }
                    IsBuffering = false;
                }

                lock (_ffmpegLock)
                {
                    ret = ffmpeg.av_read_frame(_formatCtx, packet);
                    if (ret < 0)
                    {
                        break;
                    }

                    if (packet->stream_index != _videoStreamIndex)
                    {
                        ffmpeg.av_packet_unref(packet);
                        continue;
                    }

                    ret = ffmpeg.avcodec_send_packet(_codecCtx, packet);
                    ffmpeg.av_packet_unref(packet);
                    if (ret < 0) continue;

                    while ((ret = ffmpeg.avcodec_receive_frame(_codecCtx, frame)) >= 0)
                    {
                    // Scale to RGB
                    byte*[] srcSlice = { frame->data[0], frame->data[1], frame->data[2], frame->data[3] };
                    int[] srcStride = { frame->linesize[0], frame->linesize[1], frame->linesize[2], frame->linesize[3] };
                    byte*[] dstSlice = { rgbFrame->data[0], rgbFrame->data[1], rgbFrame->data[2], rgbFrame->data[3] };
                    int[] dstStride = { rgbFrame->linesize[0], rgbFrame->linesize[1], rgbFrame->linesize[2], rgbFrame->linesize[3] };
                    ffmpeg.sws_scale(_swsCtx, srcSlice, srcStride, 0, _height, dstSlice, dstStride);

                    // Copy RGB to byte[]
                    byte[] rgbData = new byte[rgbBufferSize];
                    Marshal.Copy((IntPtr)rgbFrame->data[0], rgbData, 0, rgbBufferSize);

                    // Convert to RGBA8
                    byte[] rgbaData = ConvertRgb24ToRgba8(rgbData);

                    // Calculate time
                    double currentTime = frame->pts != ffmpeg.AV_NOPTS_VALUE ? frame->pts * _timeBase : 0;

                    // Add to queue
                    _frameQueue.Add((rgbaData, currentTime), token);

                    lock (this)
                    {
                        _currentBufferSize += rgbaData.Length;
                        _currentBufferedTime = Math.Max(_currentBufferedTime, currentTime);
                        BufferedSize = _currentBufferSize;
                        BufferedTime = _currentBufferedTime - _lastPlayedTime;
                        Monitor.Pulse(this); // Notify producer wait
                    }

                        ffmpeg.av_frame_unref(frame);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"SimpleVideoDecoder ProducerLoop error: {ex.Message}");
        }
        finally
        {
            try
            {
                ffmpeg.av_packet_free(&packet);
                ffmpeg.av_frame_free(&frame);
                ffmpeg.av_frame_free(&rgbFrame);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Error freeing FFmpeg resources: {ex.Message}");
            }
        }
    }

    private async Task ConsumerLoopAsync(CancellationToken token)
    {
        try
        {
            double lastTime = 0;
            foreach (var (frameData, time) in _frameQueue.GetConsumingEnumerable(token))
            {
                // Pre-load check
                if (!_playbackReady)
                {
                    if (BufferedTime >= 1.0) // Pre-load 1 second
                    {
                        _playbackReady = true;
                        PlaybackReady?.Invoke();
                    }
                    else
                    {
                        continue; // Skip until ready
                    }
                }

                // Wait if paused
                _pauseEvent.Wait(token);

                // Raise events
                FrameReady?.Invoke(frameData);
                TimeUpdated?.Invoke(time);
                _lastPlayedTime = time;

                // Calculate delay
                double delayMs = (time - lastTime) * 1000;
                if (delayMs > 0 && delayMs < 1000) // Reasonable delay
                {
                    await Task.Delay((int)delayMs, token);
                }
                lastTime = time;

                // Update buffer
                lock (this)
                {
                    _currentBufferSize -= frameData.Length;
                    BufferedSize = _currentBufferSize;
                    BufferedTime = _currentBufferedTime - _lastPlayedTime;
                    Monitor.Pulse(this); // Notify producer
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"SimpleVideoDecoder ConsumerLoopAsync error: {ex.Message}");
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                EndReached?.Invoke();
            }
        }
    }

    private byte[] ConvertRgb24ToRgba8(byte[] rgbData)
    {
        byte[] rgbaData = new byte[_width * _height * 4];
        for (int i = 0, j = 0; i < rgbData.Length; i += 3, j += 4)
        {
            rgbaData[j] = rgbData[i];     // R
            rgbaData[j + 1] = rgbData[i + 1]; // G
            rgbaData[j + 2] = rgbData[i + 2]; // B
            rgbaData[j + 3] = 255;        // A
        }
        return rgbaData;
    }

    private unsafe string GetFFmpegError(int error)
    {
        const int bufferSize = 1024;
        byte[] buffer = new byte[bufferSize];
        fixed (byte* pBuffer = buffer)
        {
            ffmpeg.av_strerror(error, pBuffer, (ulong)bufferSize);
        }
        int nullIndex = Array.IndexOf(buffer, (byte)0);
        return nullIndex >= 0 ? System.Text.Encoding.ASCII.GetString(buffer, 0, nullIndex) : System.Text.Encoding.ASCII.GetString(buffer);
    }

    public void Dispose()
    {
        _pauseEvent.Set(); // Ensure not paused to allow exit
        StopDecodingAsync().Wait();
        _frameQueue?.Dispose();
        unsafe
        {
            if (_swsCtx != null)
            {
                ffmpeg.sws_freeContext(_swsCtx);
                _swsCtx = null;
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
        }
        _cts?.Dispose();
        _pauseEvent?.Dispose();
        GD.Print("SimpleVideoDecoder disposed");
    }
}