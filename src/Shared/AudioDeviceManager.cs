using System;
using Godot;
using SDL3;

namespace Cue2.Shared;

public partial class AudioDeviceManager : Node
{
    public override void _Ready()
    {
	    SDL.Init(SDL.InitFlags.Audio);


	    try
	    {
		    // Get number of playback devices
		    var devices = SDL.GetAudioPlaybackDevices(out int count);
		    Console.WriteLine($"Found {count} playback devices:");

		    // Enumerate playback devices
		    foreach (var deviceID in devices)
		    {
			    var deviceUintID = Convert.ToUInt32(deviceID);
			    string deviceName = SDL.GetAudioDeviceName(deviceUintID);
			    if (deviceName != null)
			    {
				    Console.WriteLine($"  Playback Device {deviceID}: {deviceName}");
				    SDL.AudioSpec spec;
				    if (SDL.GetAudioDeviceFormat(deviceUintID, out spec, out int _) == true)
				    {
					    int channels = spec.Channels;
					    int sampleRate = spec.Freq;
					    int bitDepth = GetBitDepth(spec.Format);

					    Console.WriteLine($"  Playback Device {deviceUintID}: {deviceName}");
					    Console.WriteLine($"    Channels: {channels}");
					    Console.WriteLine($"    Sample Rate: {sampleRate} Hz");
					    Console.WriteLine($"    Bit Depth: {bitDepth}-bit");
				    }
				    else
				    {
					    Console.WriteLine($"  Playback Device {deviceUintID}: {deviceName}");
					    Console.WriteLine($"    [Failed to get audio spec: {SDL.GetError()}]");
				    }
				    

			    }
			    else
			    {
				    Console.WriteLine($"  Playback Device {deviceID}: [Unknown]");
			    }
		    }
	    }
	    catch (Exception ex)
	    {
		    Console.WriteLine($"An error occurred: {ex.Message}");
	    }
	    finally
	    {
		    // Clean up SDL
		    SDL.Quit();
	    }
    }

// Helper function to map SDL_AudioFormat to bit depth
	static int GetBitDepth(SDL.AudioFormat format)
	{
		switch (format)
		{
			case SDL.AudioFormat.AudioU8:
			case SDL.AudioFormat.AudioS8:
				return 8;
			case SDL.AudioFormat.AudioS16BE:
			case SDL.AudioFormat.AudioS16LE:
				return 16;
			case SDL.AudioFormat.AudioF32BE:
			case SDL.AudioFormat.AudioF32LE:
			case SDL.AudioFormat.AudioS32BE:
			case SDL.AudioFormat.AudioS32LE:
				return 32;
			default:
				return 0; // Unknown or unsupported format
		}
	}
	
    
}