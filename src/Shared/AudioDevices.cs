using System;
using System.Collections.Generic;
using Godot;
using SDL3;

namespace Cue2.Shared;

public partial class AudioDevices : Node
{
	private GlobalData _globalData;
	private GlobalSignals _globalSignals;
	
	//private static Dictionary<> OpenDevices;
	
	
    public override void _Ready()
    {
	    _globalData = GetNode<GlobalData>("/root/GlobalData");
	    _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
	    
	    if (SDL.Init(SDL.InitFlags.Audio) == false)
	    {
		    GD.Print($"SDL Init failed: {SDL.GetError()}");
		    _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"SDL Init failed: {SDL.GetError()}", 3);
		    return;
	    }
    }
    
    public List<string> GetAudioDeviceNames()
    {
	    try
	    {
		    // Get number of playback devices
		    var devices = SDL.GetAudioPlaybackDevices(out int count);
		    var deviceNames = new List<string>();

		    // Enumerate playback devices
		    if (devices == null) return null;
		    foreach (var deviceId in devices)
		    {
			    var deviceUintId = Convert.ToUInt32(deviceId);
			    string deviceName = SDL.GetAudioDeviceName(deviceUintId);
			    if (deviceName != null)
			    {
				    deviceNames.Add(deviceName);
			    }
			    else
			    {
				    Console.WriteLine($"  Playback Device {deviceId}: [Unknown]");
			    }
		    }
		    return deviceNames;
	    }
	    catch (Exception ex)
	    {
		    Console.WriteLine($"An error occurred: {ex.Message}");
		    return null;
	    }
	    
    }

    public List<string> GetReadableAudioDeviceSpecs(string name)
    {
	    var specs = new List<string>();
	    var device = GetAudioDevicePhysicalIdFromName(name);
	    if (device == 0) return null;
	    SDL.GetAudioDeviceFormat(device, out SDL.AudioSpec spec, out _);
	    var format = spec.Format.ToString().Substring(5);
	    specs.Add($"Bit Depth: {GetBitDepth(spec.Format)} ({format})");
	    specs.Add($"Bit Rate: {spec.Freq}");
	    specs.Add($"Channels: {spec.Channels}");
	    return specs;
    }

    public uint GetAudioDevicePhysicalIdFromName(string name)
    {
	    var devices = SDL.GetAudioPlaybackDevices(out int count);
	    foreach (var deviceId in devices)
	    {
			var deviceName = SDL.GetAudioDeviceName(deviceId);
			if (deviceName == name) return deviceId;
	    }
	    return 0;
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