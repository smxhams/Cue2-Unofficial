using Godot;
using FFmpeg.AutoGen;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;


namespace Cue2.Shared;

/// <summary>
/// Simple FFmpeg video decoder for basic video playback.
/// Decodes video frames to RGBA8 format and provides them via events.
/// </summary>
public class FFmpegVideoDecoder : IDisposable
{
    // FFmpeg scaling algorithm constant for bilinear interpolation
    private const int SWS_BILINEAR = 2;
    // Number of channels in RGBA format (Red, Green, Blue, Alpha)
    private const int RGBA_CHANNELS = 4;
    // Minimum timestamp for seeking (start of file)
    private const long SEEK_MIN_TS = 0;
    // Maximum timestamp for seeking (end of file)
    private const long SEEK_MAX_TS = long.MaxValue;
    // Timeout in milliseconds for stopping decoding gracefully
    private const int STOP_TIMEOUT_MS = 5000;
    // Fallback frames per second if not detectable from video
    private const double FALLBACK_FPS = 30.0;
    // Size of the frame buffer queue for recycling byte arrays
    private const int FRAME_QUEUE_SIZE = 5;

    /// <summary>
    /// Initializes a new instance of the FFmpegVideoDecoder class.
    /// </summary>
    /// <param name="godotNode">A Godot Node reference for thread-safe event invocation.</param>
    public FFmpegVideoDecoder(Node godotNode)
    {
        _godotNode = godotNode ?? throw new ArgumentNullException(nameof(godotNode));
    }

    // FFmpeg-related pointers and contexts (managed via Dispose)
    private unsafe AVFormatContext* _formatCtx; // Container format context
    private unsafe AVCodecContext* _codecCtx;   // Codec context for decoding
    private unsafe SwsContext* _swsCtx;         // Software scaling context for RGBA conversion
    private unsafe AVCodec* _codec;             // Video codec
    private unsafe AVStream* _stream;           // Video stream
    private int _videoStreamIndex = -1;         // Index of the video stream in the container
    private int _width, _height;                // Video dimensions
    private double _timeBase;                   // Time base for timestamp calculations
    // Threading and synchronization primitives
    private ManualResetEventSlim _pauseEvent = new ManualResetEventSlim(true);      // Controls pause/resume
    private ManualResetEventSlim _disposeEvent = new ManualResetEventSlim(false);   // Signals disposal completion
    private CancellationTokenSource _cts;           // For cancelling decoding operations
    private volatile bool _forceStop = false;       // Force stop flag for immediate termination
    private int _frameDurationMs;                   // Duration of each frame in milliseconds
    private Stopwatch _stopwatch = new Stopwatch(); // For precise timing
    private Node _godotNode;                        // Godot node for thread-safe event invocation
    private long _nextFrameTime = 0;                // Timestamp for next frame emission
    private ConcurrentQueue<byte[]> _frameQueue = new ConcurrentQueue<byte[]>();            // Queue for recycling frame buffers
    private long _pauseStartTime = 0;               // Timestamp when pause started
    private ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();                        // Protects FFmpeg contexts during seek/dispose
    private bool _isDisposed = false;               // Tracks disposal state
    private volatile bool _pausedAtEnd = false;     // Paused at end of file

    // Frame buffers for RGBA conversion
    private unsafe AVFrame* _rgbFrame; // RGBA frame structure
    private unsafe byte* _rgbBuffer;   // Raw RGBA pixel data buffer
    private int _rgbBufferSize;        // Size of the RGBA buffer in bytes
    private unsafe AVFrame* _frame;    // Decoded frame from FFmpeg

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
    /// Starts decoding the specified video file asynchronously.
    /// </summary>
    /// <param name="filePath">Path to the video file.</param>
    /// <returns>A task representing the decoding operation.</returns>
    public async Task StartDecodingAsync(string filePath)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(FFmpegVideoDecoder));

        if (DecodingTask != null && !DecodingTask.IsCompleted)
        {
            GD.PrintErr("FFmpegVideoDecoder:StartDecodingAsync - Decoding already in progress");
            return;
        }

        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Video file not found: {filePath}");

        try
        {
            InitDecoder(filePath);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"FFmpegVideoDecoder:StartDecodingAsync - InitDecoder failed: {ex.Message}");
            // Cleanup partial initialization
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
            throw;
        }
        _cts = new CancellationTokenSource();
        _nextFrameTime = 0;
        _stopwatch.Restart();
        // Start decoding in background thread
        DecodingTask = Task.Run(() =>
        {
            try
            {
                DecodeLoop(_cts.Token);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"FFmpegVideoDecoder:DecodeLoop - Exception: {ex.Message}");
            }
        }, _cts.Token).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                GD.PrintErr($"FFmpegVideoDecoder:DecodingTask - Faulted: {t.Exception?.InnerException?.Message}");
            }
        }, TaskContinuationOptions.OnlyOnFaulted);

        // Pause by default
        Pause();
    }

    /// <summary>
    /// Stops the decoding process asynchronously by cancelling the token and awaiting completion.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous stop operation.</returns>
    public async Task StopDecodingAsync()
    {
        if (DecodingTask == null || DecodingTask.IsCompleted)
            return;

        _cts?.Cancel();
        try
        {
            // Await with timeout to prevent indefinite hang
            var timeoutTask = Task.Delay(STOP_TIMEOUT_MS, _cts.Token);
            var completedTask = await Task.WhenAny(DecodingTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                // Force stop if timeout
                _forceStop = true;
                GD.Print("FFmpegVideoDecoder:StopDecodingAsync - Forced stop after timeout");
            }
            else
            {
                await DecodingTask; // Ensure it's fully awaited if it completed first
            }
        }
        catch (OperationCanceledException)
        {
            GD.Print("FFmpegVideoDecoder:StopDecodingAsync - Await cancelled");
        }
        catch (Exception ex)
        {
            GD.PrintErr("FFmpegVideoDecoder:StopDecodingAsync - Error during await: " + ex.Message);
        }
    }

    /// <summary>
    /// Pauses the decoding process.
    /// </summary>
    public void Pause()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(FFmpegVideoDecoder));

        _pauseEvent.Reset();
        _pauseStartTime = _stopwatch.ElapsedMilliseconds;
        IsPaused = true;
    }

    /// <summary>
    /// Resumes the decoding process.
    /// </summary>
    public void Resume()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(FFmpegVideoDecoder));

        long pausedDuration = _stopwatch.ElapsedMilliseconds - _pauseStartTime;
        _nextFrameTime += pausedDuration;
        _pauseEvent.Set();
        IsPaused = false;
    }

    public void Stop()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(FFmpegVideoDecoder));
        StopDecodingAsync().Wait();
    }

    /// <summary>
    /// Seeks to the specified time in seconds.
    /// </summary>
    /// <param name="time">The time to seek to.</param>
    public unsafe void Seek(double time)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(FFmpegVideoDecoder));

        _lock.EnterWriteLock();
        try
        {
            if (_formatCtx == null || _codecCtx == null) return;

            if (_timeBase <= 0 || double.IsNaN(_timeBase) || double.IsInfinity(_timeBase))
            {
                GD.PrintErr("FFmpegVideoDecoder:Seek - Invalid time base");
                return;
            }
            time = Math.Max(0, time); // Clamp to valid range
            if (Duration > 0) time = Math.Min(time, Duration);
            long timestamp = (long)(time / _timeBase); // Convert seconds to stream timebase units
            long min_ts = SEEK_MIN_TS;
            long max_ts = SEEK_MAX_TS;
            int ret = ffmpeg.avformat_seek_file(_formatCtx, _videoStreamIndex, min_ts, timestamp, max_ts, ffmpeg.AVSEEK_FLAG_BACKWARD); // Seek to nearest keyframe before timestamp on video stream
            if (ret < 0)
            {
                throw new InvalidOperationException($"FFmpegVideoDecoder:Seek - Seek failed: {GetFFmpegError(ret)}");
            }
            ffmpeg.avcodec_flush_buffers(_codecCtx);
            if (_frame != null) ffmpeg.av_frame_unref(_frame); // Reset frame state after flush
            if (_pausedAtEnd) { _pausedAtEnd = false; _nextFrameTime = 0; _stopwatch.Restart(); }
            GD.Print($"FFmpegVideoDecoder:Seek - Seeked to {time}s");
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Initializes the FFmpeg decoder with the specified video file.
    /// Sets up format context, finds video stream, initializes codec, and prepares scaler.
    /// </summary>
    /// <param name="filePath">Path to the video file to decode.</param>
    private unsafe void InitDecoder(string filePath)
    {
        InitializeFormatContext(filePath);
        FindVideoStream();
        InitializeCodec();
        SetupScaler();
    }

    /// <summary>
    /// Initializes the FFmpeg format context and opens the input file.
    /// </summary>
    /// <param name="filePath">Path to the video file.</param>
    private unsafe void InitializeFormatContext(string filePath)
    {
        int ret;
        AVDictionary* options = null;
        ffmpeg.av_dict_set(&options, "fflags", "+genpts", 0); // Generate timestamps if missing
        fixed (AVFormatContext** pCtx = &_formatCtx)
        {
            ret = ffmpeg.avformat_open_input(pCtx, filePath, null, &options);
            if (ret < 0) throw new Exception($"Open failed: {GetFFmpegError(ret)}");
        }
        ffmpeg.av_dict_free(&options);

        ret = ffmpeg.avformat_find_stream_info(_formatCtx, null);
        if (ret < 0) throw new Exception($"Stream info failed: {GetFFmpegError(ret)}");
    }

    /// <summary>
    /// Finds the video stream in the format context and sets up timing parameters.
    /// </summary>
    private unsafe void FindVideoStream()
    {
        for (uint i = 0; i < _formatCtx->nb_streams; i++)
        {
            if (_formatCtx->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
            {
                _videoStreamIndex = (int)i;
                break;
            }
        }
        if (_videoStreamIndex == -1) throw new Exception("No video stream.");

        _stream = _formatCtx->streams[(uint)_videoStreamIndex];
        _timeBase = _stream->time_base.num / (double)_stream->time_base.den;
        if (double.IsNaN(_timeBase) || double.IsInfinity(_timeBase) || _timeBase <= 0)
        {
            _timeBase = 1.0 / FALLBACK_FPS;
        }
        double fps = ffmpeg.av_q2d(_stream->r_frame_rate);
        if (fps == 0) fps = ffmpeg.av_q2d(_stream->avg_frame_rate);
        if (fps == 0) fps = 30;
        _frameDurationMs = (int)(1000.0 / fps);
        Duration = _formatCtx->duration != ffmpeg.AV_NOPTS_VALUE ? (double)_formatCtx->duration / ffmpeg.AV_TIME_BASE : 0;
        // Fallback to stream if container invalid, but scale properly
        if (Duration == 0 && _stream->duration != ffmpeg.AV_NOPTS_VALUE)
        {
            Duration = (double)_stream->duration * _timeBase;
        }
    }

    /// <summary>
    /// Initializes the FFmpeg codec context for decoding.
    /// </summary>
    private unsafe void InitializeCodec()
    {
        _codec = ffmpeg.avcodec_find_decoder(_stream->codecpar->codec_id);
        if (_codec == null) throw new Exception("Unsupported codec.");

        fixed (AVCodecContext** pCodecCtx = &_codecCtx)
        {
            _codecCtx = ffmpeg.avcodec_alloc_context3(_codec);
            _codecCtx->thread_count = System.Environment.ProcessorCount;
            int ret = ffmpeg.avcodec_parameters_to_context(_codecCtx, _stream->codecpar);
            if (ret < 0) throw new Exception($"Params to context failed: {GetFFmpegError(ret)}");

            ret = ffmpeg.avcodec_open2(_codecCtx, _codec, null);
            if (ret < 0) throw new Exception($"Open codec failed: {GetFFmpegError(ret)}");
        }

        _width = _codecCtx->width;
        _height = _codecCtx->height;

        if (_width <= 0 || _height <= 0) throw new InvalidOperationException("Invalid video dimensions");
    }

    /// <summary>
    /// Sets up the FFmpeg scaler for converting frames to RGBA format.
    /// </summary>
    private unsafe void SetupScaler()
    {
        // Setup scaler to RGBA
        _swsCtx = ffmpeg.sws_getContext(
            _width, _height, _codecCtx->pix_fmt,
            _width, _height, AVPixelFormat.AV_PIX_FMT_RGBA,
            SWS_BILINEAR, null, null, null);
        if (_swsCtx == null) throw new Exception("Sws context failed");

        _rgbBufferSize = _width * _height * RGBA_CHANNELS;
        _rgbBuffer = (byte*)ffmpeg.av_malloc((ulong)_rgbBufferSize);
        _rgbFrame = ffmpeg.av_frame_alloc();
        _rgbFrame->data[0] = _rgbBuffer;
        _rgbFrame->linesize[0] = _width * RGBA_CHANNELS;
        _rgbFrame->format = (int)AVPixelFormat.AV_PIX_FMT_RGBA;
        _rgbFrame->width = _width;
        _rgbFrame->height = _height;
        for (uint i = 1; i < RGBA_CHANNELS; i++)
        {
            _rgbFrame->data[i] = null;
            _rgbFrame->linesize[i] = 0;
        }

        _frame = ffmpeg.av_frame_alloc();
        for (int i = 0; i < FRAME_QUEUE_SIZE; i++) _frameQueue.Enqueue(new byte[_rgbBufferSize]);

        GD.Print($"FFmpegVideoDecoder:InitDecoder - initialized: {_width}x{_height}");
    }

    /// <summary>
    /// Main decoding loop that reads packets, decodes frames, and emits them at correct timing.
    /// Runs in a background thread and handles pause/resume and cancellation.
    /// </summary>
    /// <param name="token">Cancellation token to stop decoding.</param>
    private unsafe void DecodeLoop(CancellationToken token)
    {
        AVPacket* packet = ffmpeg.av_packet_alloc();

        try
        {
            while (!token.IsCancellationRequested && !_forceStop)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    _pauseEvent.Wait(token); // Wait if paused, or proceed
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested during pause
                }

                if (_pausedAtEnd)
                {
                    Thread.Sleep(100); // Wait when paused at end
                    continue;
                }

                _lock.EnterReadLock();
                try
                {
                    if (_formatCtx == null) break; // Safety check in case disposed
                    bool success = ReadPacket(packet, out bool isEof);
                    if (!success)
                    {
                        if (isEof)
                        {
                            _godotNode.CallDeferred("InvokeEndReached"); // Notify end of video
                            _pausedAtEnd = true;
                        }
                        else
                        {
                            break; // Error, stop
                        }
                    }

                    if (packet->stream_index != _videoStreamIndex)
                    {
                        ffmpeg.av_packet_unref(packet); // Skip non-video packets
                        continue;
                    }

                    DecodeFrame(packet, token); // Decode and emit the frame
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            _disposeEvent.Set(); // Signal that decoding has stopped
        }
        catch (Exception ex)
        {
            GD.PrintErr($"FFmpegVideoDecoder:DecodeLoop - Error: {ex.Message}");
        }
        finally
        {
            _disposeEvent.Set(); // Ensure signal is set even on error
            ffmpeg.av_packet_free(&packet); // Clean up packet resources
        }
    }

    /// <summary>
    /// Reads the next packet from the FFmpeg format context.
    /// </summary>
    /// <param name="packet">Pointer to the packet structure to fill.</param>
    /// <param name="isEof">Set to true if end of file is reached.</param>
    /// <returns>True if a packet was successfully read, false otherwise.</returns>
    private unsafe bool ReadPacket(AVPacket* packet, out bool isEof)
    {
        int ret = ffmpeg.av_read_frame(_formatCtx, packet);
        if (ret < 0)
        {
            if (ret == ffmpeg.AVERROR_EOF)
            {
                isEof = true;
                return false; // End of file reached
            }
            else
            {
                GD.PrintErr($"FFmpegVideoDecoder:ReadPacket - Read frame error: {GetFFmpegError(ret)}");
                isEof = false;
                return false; // Error reading packet
            }
        }
        isEof = false;
        return true; // Packet read successfully
    }

    /// <summary>
    /// Sends a packet to the decoder and receives decoded frames, emitting them via events.
    /// Handles the FFmpeg send/receive pattern for decoding.
    /// </summary>
    /// <param name="packet">The packet to decode.</param>
    /// <param name="token">Cancellation token for stopping.</param>
    private unsafe void DecodeFrame(AVPacket* packet, CancellationToken token)
    {
        try
        {
            int ret = ffmpeg.avcodec_send_packet(_codecCtx, packet);
            if (ret < 0)
            {
                GD.PrintErr($"FFmpegVideoDecoder:DecodeFrame - Send packet error: {GetFFmpegError(ret)}");
                return;
            }
            // Packet data is now owned by the decoder; unref after receive

            while ((ret = ffmpeg.avcodec_receive_frame(_codecCtx, _frame)) >= 0)
            {
                EmitFrame(_frame, token); // Process and emit the decoded frame
                ffmpeg.av_frame_unref(_frame); // Release frame buffers
            }
            if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                return; // Expected: need more data or end of stream
            else if (ret < 0)
            {
                GD.PrintErr($"FFmpegVideoDecoder:DecodeFrame - Receive frame error: {GetFFmpegError(ret)}");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"FFmpegVideoDecoder:DecodeFrame - Exception: {ex.Message}");
        }
        finally
        {
            ffmpeg.av_packet_unref(packet); // Always unref the packet
        }
    }

    /// <summary>
    /// Scales the decoded frame to RGBA, copies it to a buffer, emits it via events, and handles timing for frame rate.
    /// </summary>
    /// <param name="frame">The decoded frame from FFmpeg.</param>
    /// <param name="token">Cancellation token.</param>
    private unsafe void EmitFrame(AVFrame* frame, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();

            // Scale the frame from its native pixel format to RGBA using the software scaler
            byte*[] srcSlice = { frame->data[0], frame->data[1], frame->data[2], frame->data[3] }; // Source plane pointers
            int[] srcStride = { frame->linesize[0], frame->linesize[1], frame->linesize[2], frame->linesize[3] }; // Source strides
            byte*[] dstSlice = { _rgbFrame->data[0], _rgbFrame->data[1], _rgbFrame->data[2], _rgbFrame->data[3] }; // Destination plane pointers
            int[] dstStride = { _rgbFrame->linesize[0], _rgbFrame->linesize[1], _rgbFrame->linesize[2], _rgbFrame->linesize[3] }; // Destination strides
            ffmpeg.sws_scale(_swsCtx, srcSlice, srcStride, 0, _height, dstSlice, dstStride);

            // Recycle frame buffers from the queue to avoid allocations
            if (_frameQueue.TryDequeue(out byte[] frameBuffer))
            {
                Marshal.Copy((IntPtr)_rgbFrame->data[0], frameBuffer, 0, _rgbBufferSize); // Copy RGBA data to managed array
                _godotNode.CallDeferred("InvokeFrameReady", frameBuffer); // Emit frame on main thread
                _frameQueue.Enqueue(frameBuffer); // Return buffer to queue
            }

            // Calculate current playback time from frame timestamps or fallback to stopwatch
            double currentTime = frame->pts != ffmpeg.AV_NOPTS_VALUE ? frame->pts * _timeBase :
                                 (frame->pkt_dts != ffmpeg.AV_NOPTS_VALUE ? frame->pkt_dts * _timeBase :
                                  _stopwatch.Elapsed.TotalSeconds);
            _godotNode.CallDeferred("InvokeTimeUpdated", currentTime);

            // Handle variable frame rate (VFR): adjust duration based on repeat_pict for repeated fields
            double durationMs = _frameDurationMs;
            if (frame->repeat_pict > 0) durationMs *= (frame->repeat_pict + 1);
            _nextFrameTime += (long)durationMs;

            // Sleep to maintain frame timing, ensuring smooth playback
            long targetTime = _nextFrameTime;
            long currentTimeMs = _stopwatch.ElapsedMilliseconds;
            if (currentTimeMs < targetTime && !token.IsCancellationRequested)
            {
                int sleepMs = (int)(targetTime - currentTimeMs);
                if (sleepMs > 0) Thread.Sleep(sleepMs);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"FFmpegVideoDecoder:EmitFrame - Exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Converts an FFmpeg error code to a human-readable string.
    /// </summary>
    /// <param name="error">The FFmpeg error code.</param>
    /// <returns>A string describing the error.</returns>
    private unsafe string GetFFmpegError(int error)
    {
        const int bufferSize = 1024;
        byte[] buffer = new byte[bufferSize];
        fixed (byte* pBuffer = buffer)
        {
            ffmpeg.av_strerror(error, pBuffer, (ulong)bufferSize); // Fill buffer with error string
        }
        int nullIndex = Array.IndexOf(buffer, (byte)0); // Find null terminator
        return nullIndex >= 0 ? System.Text.Encoding.ASCII.GetString(buffer, 0, nullIndex) : System.Text.Encoding.ASCII.GetString(buffer);
    }

    /// <summary>
    /// Disposes FFmpeg resources, cancels ongoing tasks, and cleans up events.
    /// Ensures graceful shutdown without leaks.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _lock.EnterWriteLock();
        try
        {
            _pauseEvent.Set(); // Ensure not paused to allow exit
            _cts?.Cancel();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
        if (!_disposeEvent.Wait(5000))
        {
            GD.PrintErr("FFmpegVideoDecoder:Dispose - Dispose event timeout");
        }
        _lock.EnterWriteLock();
        try
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

                if (_formatCtx != null)
                {
                    fixed (AVFormatContext** ppFormat = &_formatCtx)
                    {
                        ffmpeg.avformat_close_input(ppFormat);
                    }
                    _formatCtx = null;
                }

                if (_rgbFrame != null)
                {
                    fixed (AVFrame** ppRgbFrame = &_rgbFrame)
                    {
                        ffmpeg.av_frame_free(ppRgbFrame);
                    }
                    _rgbFrame = null;
                }

                if (_rgbBuffer != null)
                {
                    ffmpeg.av_free(_rgbBuffer);
                    _rgbBuffer = null;
                }

                if (_frame != null)
                {
                    fixed (AVFrame** ppFrame = &_frame)
                    {
                        ffmpeg.av_frame_free(ppFrame);
                    }
                    _frame = null;
                }
            }
            _cts?.Dispose();
            _pauseEvent?.Dispose();
            _disposeEvent?.Dispose();
            _isDisposed = true;
            GD.Print("FFmpegVideoDecoder:Dispose - disposed");
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
}