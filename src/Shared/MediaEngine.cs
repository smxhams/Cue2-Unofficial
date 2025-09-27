using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Godot;
using LibVLCSharp.Shared;
using NAudio.Wave;
using FFmpeg.AutoGen;

namespace Cue2.Shared;

/// <summary>
/// Singleton manager for all LibVLCSharp operations. Handles a single LibVLC instance
/// and provides methods for creating MediaPlayers, preloading media, and cleanup.
/// Ensures thread safety and minimal latency for cue triggering.
/// </summary>
public partial class MediaEngine : Node
{
    private static LibVLC _libVlc;
    private GlobalSignals _globalSignals;

    
    // Cache for preloaded media to reduce load times
    private Dictionary<string, Media> _preloadedMedia = new Dictionary<string, Media>(); // Dictionary of type system (not Godot)

    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

        try
        {
            // Initialize single LibVLC instance with options (e.g., logging, hardware accel)
            //_libVlc = new LibVLC("--verbose=2", "--no-video-title-show"); // Customize flags as needed
            _libVlc = new LibVLC(); // Customize flags as needed
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                "MediaEngine:_Ready - LibVLC initialized successfully.",
                0);
            GD.Print("MediaEngine:_Ready - LibVLC initialized.");
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"MediaEngine:_Ready - Failed to initialize MediaEngine: {ex.Message}", 2);
            GD.PrintErr($"MediaEngine:_Ready - Initialization error: {ex.Message}");
            // Fallback: Disable VLC features or notify user
        }
        
        GD.Print("MediaEngine:_Ready - Loading FFmpeg libs."); 
        LoadFFmpegLibraries(); // From integration guide
        GD.Print($"MediaEngine:_Ready - FFmpeg version: {ffmpeg.av_version_info()}");
        
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

            // Manual load order: avutil first (base), then dependents 
            string[] coreLibs = { "avutil-59", "avcodec-61", "avformat-61", "swresample-5", "swscale-8" }; // Versions from gyan.dev FFmpeg 7.0 full static (adjust if your build differs)

            foreach (string lib in coreLibs) 
            { 
                string ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".dll" : 
                             RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" : ".so"; 
                string fullPath = $"{libPath}{lib}{ext}"; 

                nint handle = NativeLibrary.Load(fullPath); // Explicit Load (throws on fail for early error)
                GD.Print($"MediaEngine:LoadFFmpegLibraries - Loaded {lib}{ext} (handle: {handle})");
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


    // Async preload for non-blocking
    public async Task<Media> PreloadMediaAsync(string path)
    {
        if (_preloadedMedia.TryGetValue(path, out var media))
        {
            return media;
        }

        try
        {
            media = new Media(_libVlc, path);
            await media.Parse();
            if (media.ParsedStatus != MediaParsedStatus.Done)
            {
                throw new Exception("Failed to parse media.");
            }
            _preloadedMedia[path] = media;
            return media;
        }
        catch (Exception ex)
        {
            GD.Print($"Failed to preload {path}: {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to preload {path}: {ex.Message}", 2);
            return null;
        }
    }

    // Sync version for simplicity in some cases
    public Media PreloadMedia(string path)
    {
        return PreloadMediaAsync(path).Result; // Use with caution; prefer async
    }
    
    // Create MediaPlayer from preloaded media
    public MediaPlayer CreateMediaPlayer(Media media)
    {
        return new MediaPlayer(_libVlc) { Media = media };
    }
    
    
    
    
    /// <summary>
    /// Retrieves metadata from an audio file using LibVLCSharp.
    /// Returns a dictionary containing file length (duration in milliseconds), number of audio channels,
    /// and other available metadata such as sample rate, bitrate, and tags (e.g., Title, Artist).
    /// If no audio track is found or an error occurs, an empty dictionary is returned.
    /// </summary>
    /// <param name="filePath">The full path to the audio file.</param>
    /// <returns>A dictionary with metadata key-value pairs.</returns>
    public Dictionary<string, object> GetAudioFileMetadata(string filePath)
    {
        var metadata = new Dictionary<string, object>();
        try
        {
            using var media = new Media(_libVlc, filePath, FromType.FromPath);

            // Parse media metadata (synchronous for simplicity; consider async in production)
            media.Parse(MediaParseOptions.ParseLocal).Wait();

            // Duration in milliseconds
            if (media.Duration > 0)
            {
                GD.Print($"Media Duration: {media.Duration} ms");
                metadata.Add("DurationMs", media.Duration);
                metadata.Add("DurationSeconds", media.Duration / 1000.0);
            }

            // Find audio tracks
            var audioTracks = media.Tracks.Where(t => t.TrackType == TrackType.Audio).ToList();
            if (audioTracks.Count > 0)
            {
                var primaryAudio = audioTracks.First();

                // Number of channels
                metadata.Add("Channels", (int)primaryAudio.Data.Audio.Channels);

                // Sample rate (Hz)
                if (primaryAudio.Data.Audio.Rate > 0)
                {
                    metadata.Add("SampleRate", primaryAudio.Data.Audio.Rate);
                }

                // Bitrate (if available; may require playback for accurate value, but parse gives estimate)
                if (primaryAudio.Bitrate > 0)
                {
                    metadata.Add("Bitrate", primaryAudio.Bitrate);
                }

                // Codec description
                metadata.Add("Codec", primaryAudio.Description ?? "Unknown");
            }
            else
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"No audio tracks found in file: {filePath}", 1); // Warning
                GD.Print($"MediaHelper:GetAudioFileMetadata - No audio tracks in {filePath}");
            }

            // Additional metadata tags
            foreach (MetadataType metaType in Enum.GetValues(typeof(MetadataType)))
            {
                string value = media.Meta(metaType);
                if (!string.IsNullOrEmpty(value))
                {
                    metadata.Add(metaType.ToString(), value);
                }
            }

            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Successfully retrieved metadata for {filePath}", 0); // Info
            GD.Print($"MediaHelper:GetAudioFileMetadata - Metadata retrieved for {filePath}");
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Error retrieving metadata for {filePath}: {ex.Message}", 2); // Error
            GD.PrintErr($"MediaHelper:GetAudioFileMetadata - Error: {ex.Message}");
        }

        return metadata;
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
    private unsafe string GetFFmpegError(int ret)
    {
        byte[] buffer = new byte[1024];
        fixed (byte* buf = buffer)
        {
            ffmpeg.av_strerror(ret, buf, (ulong)buffer.Length);
            return Marshal.PtrToStringAnsi((IntPtr)buf) ?? "Unknown error";
        }
    }
    
    
    
    /// <summary>
    /// Gets the duration of an audio file in seconds using NAudio.
    /// </summary>
    /// <param name="path">Audio file path.</param>
    /// <returns>Duration in seconds, or 0 on failure.</returns>
    public async Task<double> GetFileDurationAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"MediaEngine:GetFileDurationAsync - File not found: {path}", 2);
                return 0.0;
            }

            return await Task.Run(() =>
            {
                using var reader = new AudioFileReader(path);
                return reader.TotalTime.TotalSeconds;
            });
        }
        catch (DllNotFoundException ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"MediaEngine:GetFileDurationAsync - Missing codec or DLL for {path}: {ex.Message}. Ensure OS supports the format.", 2);
            return 0.0;
        }
        catch (InvalidOperationException ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"MediaEngine:GetFileDurationAsync - Unsupported format or codec issue for {path}: {ex.Message}. Try converting the file.", 2);
            return 0.0;
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"MediaEngine:GetFileDurationAsync - Error getting duration for {path}: {ex.Message}", 2);
            return 0.0;
        }
    }


}