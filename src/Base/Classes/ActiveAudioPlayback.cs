using System;
using System.Threading.Tasks;
using Cue2.Base.Classes;
using Godot;
using LibVLCSharp.Shared;

namespace Cue2.Shared;

/// <summary>
/// Encapsulates an active audio playback session for control (volume, pause, stop, fade).
/// Thread-safe for multi-threaded access (e.g., UI updates).
/// </summary>
public partial class ActiveAudioPlayback : GodotObject
{
    private readonly MediaPlayer _mediaPlayer;
    private readonly uint _sdlDevice;
    private readonly IntPtr _audioStream;
    private readonly AudioOutputPatch _patch;
    private readonly int _outputChannel;

    private object _lock = new object(); // For thread safety
    private float _volume = 1.0f; // Normalized [0-1]

    public ActiveAudioPlayback(MediaPlayer mediaPlayer, uint sdlDevice, IntPtr audioStream, AudioOutputPatch patch, int outputChannel)
    {
        _mediaPlayer = mediaPlayer;
        _sdlDevice = sdlDevice;
        _audioStream = audioStream;
        _patch = patch;
        _outputChannel = outputChannel;

        // Event handlers for end/reached, errors
        _mediaPlayer.EndReached += OnEndReached;
        // ... (add more)
    }

    /// <summary>
    /// Stops and cleans up the playback resources.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            _mediaPlayer?.Stop();
            if (_audioStream != IntPtr.Zero)
            {
                SDL3.SDL.ClearAudioStream(_audioStream);
                SDL3.SDL.DestroyAudioStream(_audioStream);
            }
            if (_sdlDevice != 0)
            {
                SDL3.SDL.CloseAudioDevice(_sdlDevice);
            }
        }
    }

    /// <summary>
    /// Pauses the playback.
    /// </summary>
    public void Pause()
    {
        lock (_lock)
        {
            _mediaPlayer?.Pause();
            SDL3.SDL.PauseAudioDevice(_sdlDevice);
        }
    }

    /// <summary>
    /// Resumes the playback.
    /// </summary>
    public void Resume()
    {
        lock (_lock)
        {
            _mediaPlayer?.Play(); // If paused
            SDL3.SDL.ResumeAudioDevice(_sdlDevice);
        }
    }

    /// <summary>
    /// Sets volume with fade over time (async for non-blocking).
    /// </summary>
    public async Task SetVolumeAsync(float targetVolume, float durationSeconds)
    {
        if (durationSeconds <= 0)
        {
            lock (_lock) { _volume = targetVolume; _mediaPlayer.Volume = (int)(targetVolume * 100); }
            return;
        }

        float startVolume = _volume;
        float elapsed = 0;
        while (elapsed < durationSeconds)
        {
            await Task.Delay(50); // ~20Hz update; adjust for smoothness vs CPU
            elapsed += 0.05f;
            float t = elapsed / durationSeconds;
            _volume = Mathf.Lerp(startVolume, targetVolume, t);
            lock (_lock) { _mediaPlayer.Volume = (int)(_volume * 100); }
        }
    }

    private void OnEndReached(object sender, EventArgs e)
    {
        Stop();
        // Emit signal or callback to ActiveCue
    }

    // Dispose: Call Stop()
    public void Dispose()
    {
        Stop();
        _mediaPlayer?.Dispose();
    }
}