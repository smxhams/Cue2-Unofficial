using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Godot;

namespace Cue2.Shared;

/// <summary>
/// Decodes video from FFmpeg to RGB frames for texture rendering.
/// Manages packet/frame lifecycle, threading for smooth playback, and controls (pause, stop, fade).
/// Designed for cue playback: Preload frames, decode on-demand.
/// </summary>
public class FFmpegVideoDecoderOld : IDisposable
{
    private readonly VideoComponent _component; // For metadata/start/end times
    private readonly ActiveVideoPlayback _playback;
    private unsafe AVFormatContext* _formatCtx;
    private unsafe AVCodecContext* _codecCtx;
    private unsafe SwsContext* _swsCtx; // For video scaling/conversion to RGB
    private unsafe AVBufferRef* _hwDeviceCtx; // Hardware device context for GPU decoding
    private readonly object _lock = new object(); // Thread safety for state/controls

    public volatile bool IsPlaying = false; // Volatile for thread visibility
    public volatile bool IsPaused = false;
    public volatile bool IsStopped = false;
    private CancellationTokenSource _cts; // For async cancel

    private AVRational _timeBase;
    private int _videoStreamIndex = -1;
    private long _currentTs = 0; // In AV_TIME_BASE units (us)
    private int _width, _height; // Cached from codecCtx after init
    private float _frameRate; // Cached frame rate

    private readonly ConcurrentQueue<byte[]> _preloadBuffer = new ConcurrentQueue<byte[]>(); // For preloading RGB frames
    public const int PreloadMs = 1000; // Configurable preload time (ms) for low-latency start

    private BlockingCollection<byte[]> _frameQueue; // Bounded queue for producer-consumer
    private const int MaxBufferedFrames = 50; // Cap buffered frames
    private long _queuedBytes = 0; // Track total bytes in _frameQueue for estimation

    private ManualResetEventSlim _pauseEvent = new ManualResetEventSlim(true); // For pause waiting

    /// <summary>
    /// Gets the target pixel format for video, set to RGB24 for easy texture upload.
    /// </summary>
    public AVPixelFormat TargetFormat { get; } = AVPixelFormat.AV_PIX_FMT_RGB24;

    /// <summary>
    /// Gets the current playback time in microseconds.
    /// </summary>
    public long CurrentTime => _currentTs;

    /// <summary>
    /// Event raised when the end of the video is reached.
    /// </summary>
    public event EventHandler EndReached;

    /// <summary>
    /// Event raised when the length of the video changes.
    /// </summary>
    public event EventHandler<long> LengthChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="FFmpegVideoDecoderOld"/> class.
    /// </summary>
    /// <param name="component">The video component containing metadata and file information.</param>
    /// <param name="playback">The active video playback instance.</param>
    /// <exception cref="ArgumentNullException">Thrown if component or playback is null.</exception>
    /// <exception cref="Exception">Thrown if metadata is missing or file doesn't exist.</exception>
    public FFmpegVideoDecoderOld(VideoComponent component, ActiveVideoPlayback playback)
    {
        _component = component ?? throw new ArgumentNullException(nameof(component));
        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        if (_component.Metadata == null) throw new Exception("Metadata required for decoder setup.");
        if (!System.IO.File.Exists(_component.VideoFile)) throw new Exception($"Video file not found: {_component.VideoFile}");
    }

    /// <summary>
    /// Asynchronously initializes the decoder: opens the file, finds the video stream, sets up the decoder and scaler.
    /// Preloads initial RGB frames for low-latency start.
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
                    ret = ffmpeg.avformat_open_input(pCtx, _component.VideoFile, null, null);
                    if (ret < 0) throw new Exception($"FFmpegVideoDecoder:InitAsync - Open failed: {GetFFmpegError(ret)}");
                }

                ret = ffmpeg.avformat_find_stream_info(_formatCtx, null);
                if (ret < 0) throw new Exception($"FFmpegVideoDecoder:InitAsync - Stream info failed: {GetFFmpegError(ret)}");

                _videoStreamIndex = -1;
                for (uint i = 0; i < _formatCtx->nb_streams; i++)
                {
                    if (_formatCtx->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                    {
                        _videoStreamIndex = (int)i;
                        break;
                    }
                }
                if (_videoStreamIndex == -1) throw new Exception("FFmpegVideoDecoder:InitAsync - No video stream.");

                 AVStream* stream = _formatCtx->streams[(uint)_videoStreamIndex];
                 _timeBase = stream->time_base;
                 AVCodec* codec = ffmpeg.avcodec_find_decoder(stream->codecpar->codec_id);
                 if (codec == null) throw new Exception("FFmpegVideoDecoder:InitAsync - Unsupported codec.");

                 fixed (AVCodecContext** pCodecCtx = &_codecCtx)
                 {
                     _codecCtx = ffmpeg.avcodec_alloc_context3(codec);
                     ret = ffmpeg.avcodec_parameters_to_context(_codecCtx, stream->codecpar);
                     if (ret < 0) throw new Exception($"FFmpegVideoDecoder:InitAsync - Params to context failed: {GetFFmpegError(ret)}");

                     // Try to enable hardware acceleration before opening codec
                     AVHWDeviceType hwType = MediaEngine.GetBestHardwareDeviceType();
                     if (hwType != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
                     {
                         _hwDeviceCtx = MediaEngine.CreateHardwareDeviceContext(hwType);
                         if (_hwDeviceCtx != null)
                         {
                             // Check if codec supports this hardware type
                             for (int i = 0; ; i++)
                             {
                                 AVCodecHWConfig* config = ffmpeg.avcodec_get_hw_config(codec, i);
                                 if (config == null) break;
                                 if ((config->methods & 0x01) != 0 &&
                                     config->device_type == hwType)
                                 {
                                     _codecCtx->hw_device_ctx = ffmpeg.av_buffer_ref(_hwDeviceCtx);
                                     GD.Print($"FFmpegVideoDecoder:InitAsync - Hardware acceleration enabled: {ffmpeg.av_hwdevice_get_type_name(hwType)}");
                                     break;
                                 }
                             }
                         }
                     }

                     ret = ffmpeg.avcodec_open2(_codecCtx, codec, null);
                     if (ret < 0) throw new Exception($"FFmpegVideoDecoder:InitAsync - Open codec failed: {GetFFmpegError(ret)}");
                 }

                _width = _codecCtx->width;
                _height = _codecCtx->height;
                _frameRate = (float)stream->r_frame_rate.num / stream->r_frame_rate.den;

                // Setup scaler to target RGB24
                _swsCtx = ffmpeg.sws_getContext(
                    _width, _height, _codecCtx->pix_fmt,
                    _width, _height, TargetFormat,
                    2, null, null, null); // SWS_BILINEAR = 2
                if (_swsCtx == null) throw new Exception("FFmpegVideoDecoder:InitAsync - Sws context failed");

                // Initial seek before preload
                long startUs = (long)(_component.StartTime * 1_000_000);
                long seekTs = ffmpeg.av_rescale_q(startUs, new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE }, _timeBase);
                ret = ffmpeg.av_seek_frame(_formatCtx, _videoStreamIndex, seekTs, ffmpeg.AVSEEK_FLAG_BACKWARD);
                if (ret < 0)
                {
                    GD.PrintErr($"FFmpegVideoDecoder:InitAsync - Initial seek failed: {GetFFmpegError(ret)}");
                    return;
                }
                _currentTs = startUs;

                // Preload initial frames after setup
                PreloadInitialFrames();

                // Fire LengthChanged with stream duration (in ms)
                long durationMs = (long)(stream->duration * ffmpeg.av_q2d(_timeBase) * 1000);
                LengthChanged?.Invoke(this, durationMs);

                GD.Print($"FFmpegVideoDecoder:InitAsync - Decoded to {_width}x{_height} RGB24, frame rate {_frameRate:F1} fps.");
            }
        });
    }

    /// <summary>
    /// Transfers a hardware frame to software if necessary.
    /// </summary>
    /// <param name="frame">The frame to transfer.</param>
    /// <returns>The software frame, or the original if already software.</returns>
    private unsafe AVFrame* TransferHardwareFrame(AVFrame* frame)
    {
        if (frame->hw_frames_ctx == null)
        {
            return frame; // Already software
        }

        AVFrame* swFrame = ffmpeg.av_frame_alloc();
        int ret = ffmpeg.av_hwframe_transfer_data(swFrame, frame, 0);
        if (ret < 0)
        {
            GD.PrintErr($"FFmpegVideoDecoder:TransferHardwareFrame - Transfer failed: {MediaEngine.GetFFmpegError(ret)}");
            ffmpeg.av_frame_free(&swFrame);
            return frame; // Fallback to original
        }
        swFrame->pts = frame->pts;
        return swFrame;
    }

    /// <summary>
    /// Preloads initial RGB frames equivalent to PreloadMs for low-latency start on Play.
    /// Decodes but doesn't render yet. Advances the stream state.
    /// </summary>
    private unsafe void PreloadInitialFrames()
    {
        // Calculate frames to preload
        long preloadFrames = (long)(PreloadMs / 1000.0 * _frameRate);
        long preloaded = 0;

        AVPacket* packet = ffmpeg.av_packet_alloc();
        AVFrame* frame = ffmpeg.av_frame_alloc();
            AVFrame* rgbFrame = ffmpeg.av_frame_alloc();
            try
            {
                int ret;
                // Allocate RGB buffer
                int rgbBufferSize = _width * _height * 3; // RGB24: 3 bytes per pixel
                byte* rgbBuffer = (byte*)ffmpeg.av_malloc((ulong)rgbBufferSize);
                rgbFrame->data[0] = rgbBuffer;
                rgbFrame->linesize[0] = _width * 3;

                while (preloaded < preloadFrames)
            {
                ret = ffmpeg.av_read_frame(_formatCtx, packet);
                if (ret < 0) break;
                if (packet->stream_index != _videoStreamIndex) { ffmpeg.av_packet_unref(packet); continue; }

                ret = ffmpeg.avcodec_send_packet(_codecCtx, packet);
                ffmpeg.av_packet_unref(packet);
                if (ret < 0) break;

                while ((ret = ffmpeg.avcodec_receive_frame(_codecCtx, frame)) >= 0)
                {
                    AVFrame* swFrame = TransferHardwareFrame(frame);
                    bool transferred = (swFrame != frame);

                    // Scale to RGB
                    byte*[] srcSlice = { swFrame->data[0], swFrame->data[1], swFrame->data[2], swFrame->data[3] };
                    int[] srcStride = { swFrame->linesize[0], swFrame->linesize[1], swFrame->linesize[2], swFrame->linesize[3] };
                    byte*[] dstSlice = { rgbFrame->data[0], rgbFrame->data[1], rgbFrame->data[2], rgbFrame->data[3] };
                    int[] dstStride = { rgbFrame->linesize[0], rgbFrame->linesize[1], rgbFrame->linesize[2], rgbFrame->linesize[3] };
                    ffmpeg.sws_scale(_swsCtx, srcSlice, srcStride, 0, _height, dstSlice, dstStride);

                    // Copy RGB data to byte[]
                    int frameSize = _width * _height * 3; // RGB24: 3 bytes per pixel
                    byte[] rgbData = new byte[frameSize];
                    Marshal.Copy((IntPtr)rgbFrame->data[0], rgbData, 0, frameSize);

                    _preloadBuffer.Enqueue(rgbData);
                    preloaded++;
                    _currentTs += (long)(1_000_000 / _frameRate); // Advance ts per frame

                    if (transferred) ffmpeg.av_frame_free(&swFrame);
                    ffmpeg.av_frame_unref(frame);
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"FFmpegVideoDecoder:PreloadInitialFrames - Preload error: {ex.Message}");
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
            ffmpeg.av_frame_free(&frame);
            ffmpeg.av_frame_free(&rgbFrame);
        }
    }

    /// <summary>
    /// Processes a single AVFrame: scales to RGB24, returns RGB buffer.
    /// </summary>
    /// <param name="frame">The input frame.</param>
    /// <returns>The processed RGB buffer, or null on error.</returns>
    private unsafe byte[] ProcessFrame(AVFrame* frame)
    {
        AVFrame* swFrame = TransferHardwareFrame(frame);
        bool transferred = (swFrame != frame);

        try
        {
            AVFrame* rgbFrame = ffmpeg.av_frame_alloc();
            try
            {
                // Allocate RGB buffer
                int rgbBufferSize = _width * _height * 3; // RGB24: 3 bytes per pixel
                byte* rgbBuffer = (byte*)ffmpeg.av_malloc((ulong)rgbBufferSize);
                rgbFrame->data[0] = rgbBuffer;
                rgbFrame->linesize[0] = _width * 3;

                // Scale
                byte*[] srcSlice = { swFrame->data[0], swFrame->data[1], swFrame->data[2], swFrame->data[3] };
                int[] srcStride = { swFrame->linesize[0], swFrame->linesize[1], swFrame->linesize[2], swFrame->linesize[3] };
                byte*[] dstSlice = { rgbFrame->data[0], rgbFrame->data[1], rgbFrame->data[2], rgbFrame->data[3] };
                int[] dstStride = { rgbFrame->linesize[0], rgbFrame->linesize[1], rgbFrame->linesize[2], rgbFrame->linesize[3] };
                ffmpeg.sws_scale(_swsCtx, srcSlice, srcStride, 0, _height, dstSlice, dstStride);

                // Copy to byte[]
                int frameSize = _width * _height * 3;
                byte[] rgbData = new byte[frameSize];
                Marshal.Copy((IntPtr)rgbFrame->data[0], rgbData, 0, frameSize);

                return rgbData;
            }
            finally
            {
                ffmpeg.av_frame_free(&rgbFrame);
            }
        }
        finally
        {
            if (transferred) ffmpeg.av_frame_free(&swFrame);
        }
    }

    /// <summary>
    /// Starts playback asynchronously, handling decoding, scaling, and rendering to texture.
    /// Supports looping, play count, start/end times.
    /// </summary>
    /// <returns>A task representing the asynchronous playback.</returns>
    public async Task PlayAsync()
    {
        _cts = new CancellationTokenSource();
        _frameQueue = new BlockingCollection<byte[]>(MaxBufferedFrames);
        IsPlaying = true;
        IsPaused = false;
        IsStopped = false;

        // Start consumer task to dequeue and render
        var consumerTask = Task.Run(() => ConsumerLoopAsync(_cts.Token));

        // Producer runs decoding, adding to queue
        await Task.Run(() => ProducerLoop(_cts.Token));

        // Wait for consumer
        await consumerTask;
    }

    private void ProducerLoop(CancellationToken token)
    {
        unsafe
        {
            AVPacket* packet = ffmpeg.av_packet_alloc();
            AVFrame* frame = ffmpeg.av_frame_alloc();
            int ret;

            try
            {
                long startTimeUs = (long)(_component.StartTime * 1_000_000);
                long endTimeUs = (long)(_component.EndTime * 1_000_000);
                long endTsStream = ffmpeg.av_rescale_q(endTimeUs, new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE }, _timeBase);

                int playCount = 0;
                bool done = false;

                // Enqueue preloaded to queue
                while (!_preloadBuffer.IsEmpty && !IsStopped && !token.IsCancellationRequested)
                {
                    if (_preloadBuffer.TryDequeue(out byte[] preloadFrame))
                    {
                        _frameQueue.Add(preloadFrame, token);
                        Interlocked.Add(ref _queuedBytes, preloadFrame.Length);
                    }
                }

                while (!done && !IsStopped && !token.IsCancellationRequested)
                {
                    bool eof = false;

                    while (true)
                    {
                        lock (_lock)
                        {
                            if (IsStopped || token.IsCancellationRequested) break;
                        }

                        _pauseEvent.Wait();

                        if (!eof)
                        {
                            ret = ffmpeg.av_read_frame(_formatCtx, packet);
                            if (ret == ffmpeg.AVERROR_EOF)
                            {
                                eof = true;
                                ret = ffmpeg.avcodec_send_packet(_codecCtx, null); // Flush decoder
                                if (ret < 0)
                                {
                                    GD.PrintErr($"FFmpegVideoDecoder:ProducerLoop - Flush send failed: {GetFFmpegError(ret)}");
                                    break;
                                }
                            }
                            else if (ret < 0)
                            {
                                GD.PrintErr($"FFmpegVideoDecoder:ProducerLoop - Read frame failed: {GetFFmpegError(ret)}");
                                break;
                            }
                            else
                            {
                                if (packet->stream_index != _videoStreamIndex) { ffmpeg.av_packet_unref(packet); continue; }

                                long packetTs = packet->pts != ffmpeg.AV_NOPTS_VALUE ? packet->pts : packet->dts;
                                if (packetTs >= endTsStream) { ffmpeg.av_packet_unref(packet); eof = true; continue; }

                                ret = ffmpeg.avcodec_send_packet(_codecCtx, packet);
                                ffmpeg.av_packet_unref(packet);
                                if (ret < 0)
                                {
                                    GD.PrintErr($"FFmpegVideoDecoder:ProducerLoop - Send packet failed: {GetFFmpegError(ret)}");
                                    break;
                                }
                            }
                        }

                        ret = ffmpeg.avcodec_receive_frame(_codecCtx, frame);
                        if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                        {
                            if (eof) break;
                            Thread.Sleep(1);
                            continue;
                        }
                        else if (ret == ffmpeg.AVERROR_EOF) break;
                        else if (ret < 0)
                        {
                            GD.PrintErr($"FFmpegVideoDecoder:ProducerLoop - Receive frame failed: {GetFFmpegError(ret)}");
                            break;
                        }

                        byte[] rgbBuffer = ProcessFrame(frame);
                        if (rgbBuffer != null && rgbBuffer.Length > 0)
                        {
                            _frameQueue.Add(rgbBuffer, token);
                            Interlocked.Add(ref _queuedBytes, rgbBuffer.Length);
                            _currentTs += (long)(1_000_000 / _frameRate);
                        }
                        ffmpeg.av_frame_unref(frame);

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
                        ret = ffmpeg.av_seek_frame(_formatCtx, _videoStreamIndex, seekTs, ffmpeg.AVSEEK_FLAG_BACKWARD);
                        if (ret < 0)
                        {
                            GD.PrintErr($"FFmpegVideoDecoder:ProducerLoop - Loop seek failed: {GetFFmpegError(ret)}");
                            break;
                        }
                        ffmpeg.avformat_flush(_formatCtx);
                        ffmpeg.avcodec_flush_buffers(_codecCtx);
                        _currentTs = startTimeUs;
                    }
                    else
                    {
                        done = true;
                        _frameQueue.CompleteAdding();
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"FFmpegVideoDecoder:ProducerLoop - Decoding error: {ex.Message}");
                EndReached?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                ffmpeg.av_packet_free(&packet);
                ffmpeg.av_frame_free(&frame);
            }
        }
    }

    private async Task ConsumerLoopAsync(CancellationToken token)
    {
        try
        {
            foreach (byte[] rgbFrame in _frameQueue.GetConsumingEnumerable(token))
            {
                lock (_lock)
                {
                    if (IsStopped || token.IsCancellationRequested) break;
                }
                _pauseEvent.Wait();
                _playback.PushFrame(rgbFrame, _width, _height); // Push to texture
                Interlocked.Add(ref _queuedBytes, -rgbFrame.Length);

                // Pace to frame rate
                long frameMs = (long)(1000 / _frameRate);
                await Task.Delay((int)frameMs, token);
            }
            if (!IsStopped)
            {
                EndReached?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            GD.PrintErr($"FFmpegVideoDecoder:ConsumerLoopAsync - Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears the frame queue, returning buffers to the pool.
    /// </summary>
    public void ClearQueues()
    {
        while (_frameQueue.TryTake(out byte[] buffer))
        {
            Interlocked.Add(ref _queuedBytes, -buffer.Length);
        }
    }

    /// <summary>
    /// Gets the total queued bytes in the frame queue.
    /// </summary>
    public long QueuedBytes => Interlocked.Read(ref _queuedBytes);
    public int QueuedFrames => _frameQueue?.Count ?? 0;

    /// <summary>
    /// Pauses the playback.
    /// </summary>
    public void Pause()
    {
        GD.Print($"FFmpegVideoDecoder:Pause - Pause called.");
        lock (_lock)
        {
            if (IsPaused) return;
            IsPaused = true;
        }
        _pauseEvent.Reset();
    }

    /// <summary>
    /// Resumes paused playback.
    /// </summary>
    public void Resume()
    {
        lock (_lock)
        {
            if (!IsPaused) return;
            IsPaused = false;
        }
        _pauseEvent.Set();
    }

    /// <summary>
    /// Stops the playback and cancels operations.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            IsStopped = true;
            IsPlaying = false;
            _cts?.Cancel();
        }
        _pauseEvent.Set();
    }

    /// <summary>
    /// Seeks to a specific timestamp in microseconds.
    /// </summary>
    /// <param name="timestampUs">The target timestamp in microseconds.</param>
    public void Seek(long timestampUs)
    {
        if (Monitor.TryEnter(_lock, TimeSpan.FromSeconds(2)))
        {
            try
            {
                unsafe
                {
                    long seekTs = ffmpeg.av_rescale_q(timestampUs, new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE }, _timeBase);
                    int ret = ffmpeg.av_seek_frame(_formatCtx, _videoStreamIndex, seekTs, ffmpeg.AVSEEK_FLAG_BACKWARD);
                    if (ret < 0)
                    {
                        GD.PrintErr($"FFmpegVideoDecoder:Seek - Failed: {GetFFmpegError(ret)}");
                    }
                    else
                    {
                        ffmpeg.avformat_flush(_formatCtx);
                        ffmpeg.avcodec_flush_buffers(_codecCtx);
                        _currentTs = timestampUs;
                        GD.Print($"FFmpegVideoDecoder:Seek - Seek successful to {timestampUs}");
                    }
                }
            }
            finally
            {
                Monitor.Exit(_lock);
            }
        }
        else
        {
            GD.PrintErr($"FFmpegVideoDecoder:Seek - Lock acquisition timeout");
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
    /// Disposes of the decoder resources.
    /// </summary>
    public void Dispose()
    {
        Stop();
        if (_frameQueue != null)
        {
            _frameQueue.CompleteAdding();
            while (_frameQueue.TryTake(out byte[] buffer))
            {
                Interlocked.Add(ref _queuedBytes, -buffer.Length);
            }
            _frameQueue.Dispose();
        }
        lock (_lock)
        {
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

                if (_hwDeviceCtx != null)
                {
                    fixed (AVBufferRef** pHw = &_hwDeviceCtx)
                    {
                        ffmpeg.av_buffer_unref(pHw);
                    }
                    _hwDeviceCtx = null;
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
        }
        _cts?.Dispose();
        _pauseEvent.Dispose();
        GD.Print("FFmpegVideoDecoder:Dispose - Cleaned up.");
    }
}