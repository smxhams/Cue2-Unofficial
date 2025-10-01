using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Cue2.Base.Classes;
using Godot;
using FFmpeg.AutoGen;

namespace Cue2.Shared;

/// <summary>
/// Singleton manager for all LibVLCSharp operations. Handles a single LibVLC instance
/// and provides methods for creating MediaPlayers, preloading media, and cleanup.
/// Ensures thread safety and minimal latency for cue triggering.
/// </summary>
public partial class MediaEngine : Node
{
    private GlobalSignals _globalSignals;
    
    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        try
        {
            GD.Print("MediaEngine:_Ready - Loading FFmpeg libs.");
            LoadFFmpegLibraries(); // From integration guide
            GD.Print($"MediaEngine:_Ready - FFmpeg version: {ffmpeg.av_version_info()}");
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"MediaEngine:_Ready - Failed to initialize MediaEngine: {ex.Message}", 2);
            GD.PrintErr($"MediaEngine:_Ready - Initialization error: {ex.Message}");
        }
        
    }
    
    /// <summary>
    /// Loads FFmpeg native libraries manually for cross-platform compatibility in Godot Mono.
    /// Ensures core DLLs (avcodec, avformat, etc.) are resolved before any FFmpeg calls.
    /// </summary>
    private void LoadFFmpegLibraries() 
    {
        try 
        {
            string basePath = "res://ffmpeg/bin/"; 
            string platformDir = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : 
                                  RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" : 
                                  "linux"; 
            string libPath = ProjectSettings.GlobalizePath($"{basePath}{platformDir}/"); // Absolute path

            GD.Print($"MediaEngine:LoadFFmpegLibraries - Loading from: {libPath}"); // Prefixed debug path (minimal)

            // Set RootPath for dynamic fallback 
            ffmpeg.RootPath = libPath; 

            // Base library names without prefix/extension (major versions from FFmpeg 7.1.x)
            string[] baseLibs = { "avutil.59", "avcodec.61", "avformat.61", "swresample.5", "swscale.8" }; //!!!

            foreach (string baseLib in baseLibs) 
            { 
                // Platform-specific naming 
                string libName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                    ? baseLib.Replace(".", "-")  // Windows: avutil-59 (replace . with -) //!!!
                    : $"lib{baseLib}";           // macOS/Linux: libavutil.59 (major symlink) //!!!

                string ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".dll" : 
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" : ".so"; 
            
                string fullPath = $"{libPath}{libName}{ext}"; 

                nint handle = NativeLibrary.Load(fullPath); // Explicit Load (throws on fail for early error)
                GD.Print($"MediaEngine:LoadFFmpegLibraries - Loaded {libName}{ext} (handle: {handle})");
            } 

            GD.Print("MediaEngine:LoadFFmpegLibraries - All FFmpeg libs loaded successfully.");
        } 
        catch (DllNotFoundException ex) 
        { 
            GD.PrintErr($"MediaEngine:LoadFFmpegLibraries - DLL not found: {ex.Message}");
        } 
        catch (Exception ex) 
        { 
            GD.PrintErr($"MediaEngine:LoadFFmpegLibraries - Load error: {ex.Message}");
        } 
    } 
    
    
    /// <summary>
    /// Gets metadata for an audio file using FFmpeg (duration, channels, sample rate, bit depth, codec/format).
    /// Fast extraction without full decoding; supports broad formats (MP3, FLAC, AAC, etc.).
    /// Returns default-initialized metadata on failure.
    /// </summary>
    /// <param name="path">Audio file path.</param>
    /// <returns>AudioFileMetadata with extracted values.</returns>
    public async Task<AudioFileMetadata> GetAudioFileMetadataAsync(string path)
    { 
        if (!File.Exists(path)) 
        { 
            GD.PrintErr("MediaEngine:GetAudioFileMetadataAsync - File not found.");
            return new AudioFileMetadata(); // Default empty on fail
        }

        GD.Print("MediaEngine:GetAudioFileMetadataAsync - Extracting metadata.");

        return await Task.Run(() =>
        {
            unsafe 
            { 
                AVFormatContext* formatCtx = null; 

                var metadata = new AudioFileMetadata(); 

                try 
                { 
                    // Open input 
                    int ret = ffmpeg.avformat_open_input(&formatCtx, path, null, null); 
                    if (ret < 0) throw new Exception($"Failed to open file: {GetFFmpegError(ret)}");

                    ret = ffmpeg.avformat_find_stream_info(formatCtx, null); 
                    if (ret < 0) throw new Exception($"Failed to find stream info: {GetFFmpegError(ret)}"); 

                    // Duration from container (in seconds; handle AV_NOPTS_VALUE) 
                    long durationTicks = formatCtx->duration; 
                    if (durationTicks != -9223372036854775807L) //  Use numeric literal for AV_NOPTS_VALUE (int64_t max -1; no symbol resolution needed)
                    {
                        metadata.Duration = durationTicks / (double)ffmpeg.AV_TIME_BASE; // AV_TIME_BASE = 1000000 (ticks/sec)
                    }
                    else 
                    {
                        GD.PrintErr("MediaEngine:GetAudioFileMetadataAsync - Duration unknown (NOPTS); returning 0.0.");
                    }// Else remains 0.0

                    int audioStreamIndex = -1; 
                    for (uint i = 0; i < formatCtx->nb_streams; i++) 
                    { 
                        if (formatCtx->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO) 
                        { 
                            audioStreamIndex = (int)i; 
                            break; 
                        } 
                    } 

                    if (audioStreamIndex == -1) 
                    { 
                        throw new Exception("No audio stream found."); 
                    } 

                    AVCodecParameters* codecPar = formatCtx->streams[(uint)audioStreamIndex]->codecpar; 

                    // Channels from layout (modern API) 
                    metadata.Channels = (int)codecPar->ch_layout.nb_channels; 

                    // Sample rate 
                    metadata.SampleRate = codecPar->sample_rate; 

                    // Bit depth from sample format 
                    AVSampleFormat sampleFmt = (AVSampleFormat)codecPar->format; 
                    int bytesPerSample = ffmpeg.av_get_bytes_per_sample(sampleFmt); 
                    metadata.BitDepth = bytesPerSample * 8; // Bytes to bits; 0 if unknown

                    // Codec name 
                    AVCodec* codec = ffmpeg.avcodec_find_decoder(codecPar->codec_id); 
                    metadata.Codec = codec != null ? ffmpeg.avcodec_get_name(codec->id) : "unknown"; 

                    // Format from container
                    string ext = Path.GetExtension(path).TrimStart('.');
                    AVOutputFormat* fmtPtr = ffmpeg.av_guess_format(null, ext, null); // Get pointer
                    if (fmtPtr != null)
                    { 
                        metadata.Format = Marshal.PtrToStringAnsi((IntPtr)fmtPtr->name) ?? "unknown"; //Dereference pointer's name (byte*) via Marshal; prefixed minimal
                    } 
                    else 
                    { 
                        metadata.Format = "unknown"; // Fallback for null guess
                    }
                    GD.Print("MediaEngine:GetAudioFileMetadataAsync - Metadata extracted successfully.");
                    return metadata; 
                } 
                catch (Exception ex) 
                { 
                    GD.PrintErr($"MediaEngine:GetAudioFileMetadataAsync - Error: {ex.Message}");
                    return new AudioFileMetadata(); // Default on fail
                } 
                finally 
                { 
                    if (formatCtx != null) ffmpeg.avformat_close_input(&formatCtx); // Cleanup
                } 
            } 
        }); 
    } 
    
    
    /// <summary>
    /// Generates a waveform byte array for an audio file using FFmpeg.
    /// Computes min/max amplitude per bin from mono-resampled PCM samples.
    /// Returns empty array on failure.
    /// </summary>
    /// <param name="path">Audio file path.</param>
    /// <returns>Byte array of min/max floats per bin.</returns>
    public async Task<byte[]> GenerateWaveformAsync(string path)
    {
        if (!File.Exists(path))
        {
            GD.PrintErr("MediaEngine:GenerateWaveformAsync - File not found.");
            return Array.Empty<byte>();
        }

        GD.Print("MediaEngine:GenerateWaveformAsync - Generating waveform for file.");

        return await Task.Run(() =>
        {
            unsafe
            {
                AVFormatContext* formatCtx = null;
                AVCodecContext* codecCtx = null;
                AVPacket* packet = ffmpeg.av_packet_alloc();
                AVFrame* frame = ffmpeg.av_frame_alloc();
                SwrContext* swrCtx = null; // Explicit null init for double-pointer alloc
                AVChannelLayout inChLayout = default;
                AVChannelLayout outChLayout = default;

                int ret = 0; // Declare outside for safety
                double totalSamples = 0; // For global binning
                List<float> allMonoSamples = new List<float>(); // Accumulate for accurate binning

                try
                {
                    // Open input
                    ret = ffmpeg.avformat_open_input(&formatCtx, path, null, null);
                    if (ret < 0) throw new Exception($"MediaEngine:GenerateWaveformAsync - Failed to open file: {GetFFmpegError(ret)}");

                    ret = ffmpeg.avformat_find_stream_info(formatCtx, null);
                    if (ret < 0) throw new Exception($"MediaEngine:GenerateWaveformAsync - Failed to find stream info: {GetFFmpegError(ret)}");

                    int audioStreamIndex = -1;
                    for (uint i = 0; i < formatCtx->nb_streams; i++)
                    {
                        if (formatCtx->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                        {
                            audioStreamIndex = (int)i;
                            break;
                        }
                    }
                    if (audioStreamIndex == -1) throw new Exception("MediaEngine:GenerateWaveformAsync - No audio stream found.");

                    AVCodecParameters* codecPar = formatCtx->streams[(uint)audioStreamIndex]->codecpar;
                    AVCodec* codec = ffmpeg.avcodec_find_decoder(codecPar->codec_id);
                    if (codec == null) throw new Exception("MediaEngine:GenerateWaveformAsync - Unsupported codec.");

                    codecCtx = ffmpeg.avcodec_alloc_context3(codec);
                    ret = ffmpeg.avcodec_parameters_to_context(codecCtx, codecPar);
                    if (ret < 0) throw new Exception($"MediaEngine:GenerateWaveformAsync - Failed to copy codec params: {GetFFmpegError(ret)}");

                    ret = ffmpeg.avcodec_open2(codecCtx, codec, null);
                    if (ret < 0) throw new Exception($"MediaEngine:GenerateWaveformAsync - Failed to open codec: {GetFFmpegError(ret)}");

                    // Setup input channel layout (modern API)
                    ffmpeg.av_channel_layout_default(&inChLayout, (int)codecCtx->ch_layout.nb_channels);

                    // Setup output: Mono
                    ffmpeg.av_channel_layout_default(&outChLayout, 1);

                    // Resample to mono float (44.1kHz)
                    ret = ffmpeg.swr_alloc_set_opts2(
                        &swrCtx,
                        &outChLayout, AVSampleFormat.AV_SAMPLE_FMT_FLT, 44100,
                        &inChLayout, codecCtx->sample_fmt, codecCtx->sample_rate,
                        0, null);
                    if (ret < 0) throw new Exception($"MediaEngine:GenerateWaveformAsync - Failed to allocate SwrContext: {GetFFmpegError(ret)}");
                    if (swrCtx == null) throw new Exception("MediaEngine:GenerateWaveformAsync - SwrContext allocation returned null.");

                    ret = ffmpeg.swr_init(swrCtx);
                    if (ret < 0) throw new Exception($"MediaEngine:GenerateWaveformAsync - Failed to init SwrContext: {GetFFmpegError(ret)}");

                    const int binCount = 1000; // From original
                    float[] minMaxPerBin = new float[binCount * 2];
                    for (int i = 0; i < binCount; i++)
                    {
                        minMaxPerBin[i * 2] = float.MaxValue;
                        minMaxPerBin[i * 2 + 1] = float.MinValue; // Use MinValue for max init
                    }

                    while (ffmpeg.av_read_frame(formatCtx, packet) >= 0)
                    {
                        if (packet->stream_index != audioStreamIndex)
                        {
                            ffmpeg.av_packet_unref(packet);
                            continue;
                        }

                        ret = ffmpeg.avcodec_send_packet(codecCtx, packet);
                        if (ret < 0 && ret != -11) // Use numeric -11 for EAGAIN
                            throw new Exception($"MediaEngine:GenerateWaveformAsync - Send packet error: {GetFFmpegError(ret)}");

                        while (ffmpeg.avcodec_receive_frame(codecCtx, frame) >= 0)
                        {
                            // Resample frame to mono float
                            int maxOutSamples = (int)(ffmpeg.av_rescale_rnd(frame->nb_samples, 44100, codecCtx->sample_rate, AVRounding.AV_ROUND_UP) * 2 + 256); // Estimate output samples
                            byte* outBuffer = null; // Simplified for mono: Single buffer instead of array
                            int linesize = 0; // Single linesize for mono
                            ret = ffmpeg.av_samples_alloc(&outBuffer, &linesize, 1, maxOutSamples, AVSampleFormat.AV_SAMPLE_FMT_FLT, 0); // Use av_samples_alloc
                            if (ret < 0) throw new Exception($"MediaEngine:GenerateWaveformAsync - Sample alloc error: {GetFFmpegError(ret)}");

                            int outSamples = ffmpeg.swr_convert(
                                swrCtx,
                                &outBuffer, maxOutSamples,
                                frame->extended_data, frame->nb_samples);
                            if (outSamples < 0) throw new Exception($"MediaEngine:GenerateWaveformAsync - Resample error: {GetFFmpegError(outSamples)}");

                            // Accumulate mono samples (interleaved for mono)
                            float* monoPtr = (float*)outBuffer; // Direct cast from single buffer
                            for (int j = 0; j < outSamples; j++)
                            {
                                allMonoSamples.Add(monoPtr[j]); // Collect all for global binning
                            }
                            totalSamples += outSamples;

                            // Free output buffer per frame
                            ffmpeg.av_freep(&outBuffer); // Simplified: Free single buffer for mono
                        }

                        ffmpeg.av_packet_unref(packet);
                    }

                    // Global binning from all samples
                    if (totalSamples > 0)
                    {
                        for (int j = 0; j < allMonoSamples.Count; j++)
                        {
                            float sample = allMonoSamples[j];
                            int binIdx = (int)((j / totalSamples) * binCount); // Proportional global binning
                            if (binIdx < binCount)
                            {
                                if (sample < minMaxPerBin[binIdx * 2]) minMaxPerBin[binIdx * 2] = sample;
                                if (sample > minMaxPerBin[binIdx * 2 + 1]) minMaxPerBin[binIdx * 2 + 1] = sample;
                            }
                        }
                    }

                    // Fill unfilled bins
                    for (int i = 0; i < binCount; i++)
                    {
                        if (minMaxPerBin[i * 2] == float.MaxValue)
                        {
                            minMaxPerBin[i * 2] = 0f;
                            minMaxPerBin[i * 2 + 1] = 0f;
                        }
                    }

                    // Serialize to byte[]
                    byte[] byteArray = new byte[minMaxPerBin.Length * sizeof(float)];
                    Buffer.BlockCopy(minMaxPerBin, 0, byteArray, 0, byteArray.Length);

                    GD.Print("MediaEngine:GenerateWaveformAsync - Waveform generated successfully.");

                    return byteArray;
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"MediaEngine:GenerateWaveformAsync - Error: {ex.Message}");
                    return Array.Empty<byte>();
                }
                finally
                {
                    // Cleanup
                    if (packet != null) ffmpeg.av_packet_free(&packet);
                    if (frame != null) ffmpeg.av_frame_free(&frame);
                    if (swrCtx != null) ffmpeg.swr_free(&swrCtx);
                    if (codecCtx != null) ffmpeg.avcodec_free_context(&codecCtx);
                    if (formatCtx != null) ffmpeg.avformat_close_input(&formatCtx);
                    ffmpeg.av_channel_layout_uninit(&inChLayout);
                    ffmpeg.av_channel_layout_uninit(&outChLayout);
                }
            }
        });
    }
    
    /// <summary>
    /// Retrieves a human-readable error message from an FFmpeg return code.
    /// </summary>
    /// <param name="ret">The FFmpeg error code (negative value).</param>
    /// <returns>Error string, or "Unknown error" if unavailable.</returns>
    public static unsafe string GetFFmpegError(int ret)
    {
        byte[] buffer = new byte[1024];
        fixed (byte* buf = buffer)
        {
            ffmpeg.av_strerror(ret, buf, (ulong)buffer.Length);
            return Marshal.PtrToStringAnsi((IntPtr)buf) ?? "Unknown error";
        }
    }
    


}