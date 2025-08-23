using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Godot;
using LibVLCSharp.Shared;
using NAudio.Wave;

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
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "MediaEngine:_Ready - LibVLC initialized successfully.",
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

    // New: Sync version for simplicity in some cases
    public Media PreloadMedia(string path)
    {
        return PreloadMediaAsync(path).Result; // Use with caution; prefer async
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
    /// Generates waveform data (min/max per bin) from audio file asynchronously using NAudio.
    /// </summary>
    /// <param name="path">Audio file path.</param>
    /// <param name="binCount">Number of bins for resolution (e.g., 4096).</param>
    /// <param name="subsampleFactor">Factor to skip samples for speedup (1 = exact, >1 = approx).</param>
    /// <returns>Byte array of interleaved min/max floats.</returns>
    public async Task<byte[]> GenerateWaveformAsync(string path, int binCount = 4096)
    {
        try
        {
            if (!File.Exists(path))
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"MediaEngine:GenerateWaveformAsync - File not found: {path}", 2);
                return Array.Empty<byte>();
            }

            return await Task.Run(() => // Off-main thread for UI
            {
                using var reader = new AudioFileReader(path); // This is NAudio library at the moment
                int channels = reader.WaveFormat.Channels;
                long totalSamples = reader.Length / (reader.WaveFormat.BitsPerSample / 8); // Raw samples
                long monoSamples = totalSamples / channels;

                if (monoSamples <= 0)
                {
                    _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"MediaEngine:GenerateWaveformAsync - Invalid samples for {path}", 2);
                    return Array.Empty<byte>();
                }

                long binSize = monoSamples / binCount;
                if (binSize < 1) binSize = 1;

                var minMaxPerBin = new float[binCount * 2];
                for (int i = 0; i < binCount; i++)
                {
                    minMaxPerBin[i * 2] = float.MaxValue; // min init
                    minMaxPerBin[i * 2 + 1] = float.MinValue; // max init
                }

                var buffer = new float[channels * 4096]; // Read buffer
                long currentMono = 0;
                int read;

                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < read; i += channels)
                    {
                        if (i + channels > read) break;

                        float mono = 0f;
                        for (int ch = 0; ch < channels; ch++)
                        {
                            mono += buffer[i + ch]; // Normalized [-1,1]
                        }
                        mono /= channels;

                        int binIdx = (int)(currentMono / binSize);
                        if (binIdx < binCount)
                        {
                            float currentMin = minMaxPerBin[binIdx * 2];
                            float currentMax = minMaxPerBin[binIdx * 2 + 1];
                            if (mono < currentMin) minMaxPerBin[binIdx * 2] = mono;
                            if (mono > currentMax) minMaxPerBin[binIdx * 2 + 1] = mono;
                        }

                        currentMono++;
                    }
                }

                // Fill unfilled bins
                for (int i = 0; i < binCount; i++)
                {
                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    if (minMaxPerBin[i * 2] == float.MaxValue)
                    {
                        minMaxPerBin[i * 2] = 0f;
                        minMaxPerBin[i * 2 + 1] = 0f;
                    }
                } 

                // Serialize to byte[]
                byte[] byteArray = new byte[minMaxPerBin.Length * sizeof(float)];
                Buffer.BlockCopy(minMaxPerBin, 0, byteArray, 0, byteArray.Length);

                return byteArray;
            });
        }
        catch (DllNotFoundException ex) // Specific for missing codecs/DLLs
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"MediaEngine:GenerateWaveformAsync - Missing codec or DLL for {path}: {ex.Message}. Ensure OS supports the format.", 2); //!!!
            return Array.Empty<byte>();
        }
        catch (InvalidOperationException ex) // Common for unsupported formats
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"MediaEngine:GenerateWaveformAsync - Unsupported format or codec issue for {path}: {ex.Message}. Try converting the file.", 2); //!!!
            return Array.Empty<byte>();
        }
        catch (Exception ex) // General fallback
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"MediaEngine:GenerateWaveformAsync - Error generating waveform for {path}: {ex.Message}", 2);
            return Array.Empty<byte>();
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