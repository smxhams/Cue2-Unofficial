using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LibVLCSharp.Shared;

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

}