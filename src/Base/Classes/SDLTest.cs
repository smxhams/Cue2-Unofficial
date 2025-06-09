using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Godot;
using LibVLCSharp.Shared;
using SDL3;

namespace Cue2.Base.Classes;

public class SdlTest
{
	
	private static MediaPlayer _mediaPlayer;
	private Media _media;
	private SDL.AudioSpec _audioSpec;
	private static uint _audioDevice;
	private static IntPtr _audioStream;
	private bool _isPlaying = false;
	private int _bytesPerFrame;
	

    public void PlayAudio(string filePath, LibVLC libVLC)
    {
		GD.Print(" --- --- Starting audio playback with test implementation of SDL --- --- ");
		if (SDL.Init(SDL.InitFlags.Audio) == false)
		{
			GD.Print($"SDL Init failed: {SDL.GetError()}");
			return;
		}
		
		// Check for audio Devices
		var devices = SDL.GetAudioPlaybackDevices(out int deviceCount);
		if (deviceCount == 0 || devices == null)
		{
			GD.Print("No audio devices found");
			SDL.Quit();
			return;
		}

		GD.Print("Availible audio devices:");
		for (int i = 0; i < deviceCount; i++)
		{
			GD.Print($"   {i}: {SDL.GetAudioDeviceName(devices[i])}");
		}
		
		// Default spec
		
		// Open SDL3 audio device
		var deviceId = devices[1]; // Using first availble device for now
		GD.Print($"Using device: {SDL.GetAudioDeviceName(deviceId)} for this test.");

		_audioDevice = SDL.OpenAudioDevice(deviceId, (nint)0); // Zero is a null, this means device will open with its own settings
		if (_audioDevice == 0)
		{
			Console.WriteLine($"Failed to open audio device: {SDL.GetError()}");
			SDL.Quit();
			return;
		}

		

		// Get media paramters
		_media = new Media(libVLC, filePath);
		_mediaPlayer = new MediaPlayer(libVLC);
		if (_media == null || _mediaPlayer == null)
		{
			GD.Print("Failed to initialize media or media player.");
			Cleanup();
			return;
		}
		GD.Print($"Loaded media: {_media.Mrl}");
		
		_media.Parse().Wait();
		if (_media.ParsedStatus != MediaParsedStatus.Done)
		{
			GD.Print("Failed to parse media file.");
			Cleanup();
			return;
		}
		
		
		// Audio file specs from media
		SDL.AudioSpec sourceSpec = new SDL.AudioSpec();
		bool audioTrackFound = false;
		var audioTrack = _media.Tracks;
		if (audioTrack.Length > 0)
		{
			foreach (var track in audioTrack)
			{
				if (track.TrackType == TrackType.Audio)
				{
					sourceSpec.Freq = (int)track.Data.Audio.Rate;
					sourceSpec.Channels = (int)track.Data.Audio.Channels;
					string codecName = track.Codec != 0
						? System.Text.Encoding.ASCII.GetString(BitConverter.GetBytes(track.Codec))
						: "Unnknown";
					GD.Print($"Audio track: codec={codecName}, rate={track.Data.Audio.Rate}, channels={track.Data.Audio.Channels}, bitrate={track.Bitrate}");
					switch (codecName.ToLower())
					{
						case "s16l":
							sourceSpec.Format = SDL.AudioFormat.AudioS16LE;
							break;
						case "s16b":
							sourceSpec.Format = SDL.AudioFormat.AudioS16BE;
							break;
						case "s32l":
							sourceSpec.Format = SDL.AudioFormat.AudioS32LE;
							break;
						case "s32b":
							sourceSpec.Format = SDL.AudioFormat.AudioS32BE;
							break;
						case "f32l":
							sourceSpec.Format = SDL.AudioFormat.AudioF32LE;
							break;
						case "f32b":
							sourceSpec.Format = SDL.AudioFormat.AudioF32BE;
							break;
						case "mp3":
						case "mpeg":
							sourceSpec.Format = SDL.AudioFormat.AudioS16LE; // Common for MP3
							break;
						case "flac":
						case "pcm":
							sourceSpec.Format = SDL.AudioFormat.AudioS32LE; // Common for FLAC, high-quality PCM
							break;
						default:
							sourceSpec.Format = SDL.AudioFormat.AudioS16LE; // Fallback
							GD.Print($"Warning: Unknown codec, defaulting to AudioS16LE. Codec found was: {codecName}");
							break;
					}
					GD.Print($"Inferred audio file specs: rate={sourceSpec.Freq}, channels={sourceSpec.Channels}, format={SDL.GetAudioFormatName(sourceSpec.Format)}");
					audioTrackFound = true;
					break;
				}
			}
		}

		if (!audioTrackFound)
		{
			GD.Print("Warning: No audio track found, using default specs.");
			sourceSpec.Freq = 44100; // Common default
			sourceSpec.Channels = 2; // Stereo
			sourceSpec.Format = SDL.AudioFormat.AudioS16LE; // Default
		}

		int bytesPerSample = sourceSpec.Format switch
		{
			SDL.AudioFormat.AudioS16LE or SDL.AudioFormat.AudioS16BE => 2,
			SDL.AudioFormat.AudioS32LE or SDL.AudioFormat.AudioS32BE => 4,
			SDL.AudioFormat.AudioF32LE or SDL.AudioFormat.AudioF32BE => 4,
			SDL.AudioFormat.AudioU8 or SDL.AudioFormat.AudioS8 => 1,
			_ => 2
		};
		_bytesPerFrame = bytesPerSample * sourceSpec.Channels;
		
		SDL.GetAudioDeviceFormat(_audioDevice, out var obtainedSpec, out var sampleFrames);
		var formatName = SDL.GetAudioFormatName(obtainedSpec.Format);
		_audioSpec = obtainedSpec;
		GD.Print($"Source spec format is: {sourceSpec.Format}.   Destination audio device format is: {obtainedSpec.Format}");
		GD.Print($"Device preferred settings: name={SDL.GetAudioDeviceName(deviceId)}, " +
		         $"format={formatName}, freq={_audioSpec.Freq}, channels={_audioSpec.Channels}, " +
		         $"samples={sampleFrames}");
		
		// Create SDL3 audio stream
		_audioStream = SDL.CreateAudioStream(sourceSpec, _audioSpec); // devicespec?
		if (_audioStream == System.IntPtr.Zero)
		{
			Console.WriteLine($"Failed to create audio stream: {SDL.GetError()}");
			SDL.CloseAudioDevice(_audioDevice);
			SDL.Quit();
			return;
		}
		
		// Bind audio stream to SDL3 audio device
		var streams = new[] { _audioStream };
		bool bindResult = SDL.BindAudioStreams(_audioDevice, streams, streams.Length);
		if (bindResult == false)
		{
			Console.WriteLine($"Failed to bind audio stream: {SDL.GetError()}");
			SDL.DestroyAudioStream(_audioStream);
			SDL.CloseAudioDevice(_audioDevice);
			SDL.Quit();
			return;
		}
		Console.WriteLine("Audio stream bound successfully.");
		
		
		string vlcFormat = sourceSpec.Format == SDL.AudioFormat.AudioS16LE ? "S16L" :
			sourceSpec.Format == SDL.AudioFormat.AudioS16BE ? "S16N" :
			sourceSpec.Format == SDL.AudioFormat.AudioS32LE ? "S32N" :
			sourceSpec.Format == SDL.AudioFormat.AudioS32BE ? "S32N" :
			sourceSpec.Format == SDL.AudioFormat.AudioF32LE ? "f32n" :
			sourceSpec.Format == SDL.AudioFormat.AudioF32BE ? "f32n" : "S16N";
		GD.Print(vlcFormat);
		_mediaPlayer.SetAudioFormat(vlcFormat, (uint)sourceSpec.Freq, (uint)sourceSpec.Channels);
		//_mediaPlayer.SetAudioFormatCallback(AudioSetup, AudioCleanup);

		_mediaPlayer.SetAudioCallbacks(PlayAudioCB, null, null, null, null);

		GD.Print("hihihihihi");
		// Load media
		_mediaPlayer.Media = _media;

		// Start playback
		if (!_mediaPlayer.Play())
		{
			Console.WriteLine("Failed to start VLC media playback.");
			Cleanup();
			return;
		}


		GD.Print("llalalala");
		_mediaPlayer.Playing += (s, e) => GD.Print("Media player is playing.");
		_mediaPlayer.Stopped += (s, e) => { GD.Print("Media player stopped."); _isPlaying = false; };
		_mediaPlayer.EndReached += (s, e) => { GD.Print("Media playback ended."); Cleanup(); };

		while (SDL.GetAudioStreamQueued(_audioStream) < 4096 && _isPlaying)
		{
			GD.Print("Waiting for initial audio buffer...");
			OS.DelayMsec(100);
		}
		
		SDL.ResumeAudioDevice(_audioDevice);
		_isPlaying = true;
		GD.Print("vivivivivivivivi");

	}
	
	private void PlayAudioCB(nint opaque, nint samples, uint count, long pts)
	{
		if (!_isPlaying) return;
		int byteCount = (int)count * _bytesPerFrame;
		
		if (SDL.PutAudioStreamData(_audioStream, samples, byteCount) == false)
		{
			GD.Print($"Failed to put audio stream data: {SDL.GetError()}");
		}
		else
		{
			GD.Print($"Queued {count} bytes to SDL audio stream.");
		}

		// Check queue status to avoid underflow or overflow
		int queued = SDL.GetAudioStreamQueued(_audioStream);
		if (queued < 4096) // Adjust threshold based on your needs (e.g., 4KB)
		{
			GD.Print("Warning: Audio queue running low, potential underflow!");
		}
		else if (queued > 65536) // Adjust threshold (e.g., 64KB)
		{
			GD.Print("Warning: Audio queue growing large, potential overflow!");
			SDL.ClearAudioStream(_audioStream); // Clear to prevent latency
		}
	}
	
	private int AudioSetup(ref nint opaque, ref nint formatPtr, ref uint rate, ref uint channels)
	{
		GD.Print($"Audio setup: format={formatPtr}, rate={rate}, channels={channels}");
;
		// Optionally adjust _audioSpec or stream if needed
		return 0;
	}

	private void AudioCleanup(nint opaque)
	{
		GD.Print("Audio cleanup called.");
	}
	
	static void Cleanup()
	{
		GD.Print("ohhh no");
		_mediaPlayer?.Stop();
		_mediaPlayer?.Dispose();
		if (_audioStream != null)
		{
			SDL.DestroyAudioStream(_audioStream);
		}
		if (_audioDevice != 0)
		{
			SDL.CloseAudioDevice(_audioDevice);
		}
		SDL.Quit();
	}
    
}