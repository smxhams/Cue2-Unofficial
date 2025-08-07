using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata;
using Cue2.Base.Classes.Devices;
using Godot;
using SDL3;

namespace Cue2.Shared;

public partial class AudioDevices : Node
{
	private GlobalData _globalData;
	private GlobalSignals _globalSignals;
	private Dictionary<int, AudioDevice> _openDevices = new Dictionary<int, AudioDevice>(); 
	
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

    public AudioDevice OpenAudioDevice(string name, out string error)
    {
	    // Check if audio device already opened
	    foreach (var dev in _openDevices.Values)
	    {
		    if (dev.Name == name)
		    {
				error = "Device already opened, returned existing device";
				return dev;
			    
		    }
	    }
	    var physicalDeviceId = GetAudioDevicePhysicalIdFromName(name);
	    if (physicalDeviceId == null)
	    {
		    error = "No availible device found with name given";
		    return null; 
	    }
	    //Open audio device
	    var audioDevice = SDL.OpenAudioDevice((uint)physicalDeviceId, (nint)0); // Zero is a null, this means device will open with its own settings
	    if (audioDevice == 0)
	    {
		    _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to open audio device: {SDL.GetError()}", 3);
		    error = "Failed to open audio device: " + SDL.GetError();
		    return null;
	    }
	    
	    //Register Device
	    var device = new AudioDevice(name, audioDevice, out string adError);
	    if (adError != "")
	    {
		    error = adError;
		    return null;
	    }
	    var specs = GetAudioDeviceSpec(name);

	    device.PhysicalId = (uint)physicalDeviceId;
	    device.Channels = specs.Channels;
	    device.Format = specs.Format;
	    device.SampleRate = specs.Freq;
	    device.BitDepth = GetBitDepth(specs.Format);
	    
	    _openDevices.Add(device.DeviceId, device);

	    error = "";
	    return device;
    }
    
    public List<string> GetAvailibleAudioDevicseNames()
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

    public SDL.AudioSpec GetAudioDeviceSpec(string name)
    {
	    var device = GetAudioDevicePhysicalIdFromName(name);
	    Debug.Assert(device != null, nameof(device) + " != null");
	    SDL.GetAudioDeviceFormat((uint)device, out SDL.AudioSpec spec, out _);
	    return spec; // Return SDL_AudioSpec structu
    } 

    public List<string> GetReadableAudioDeviceSpecs(string name)
    {
	    var specs = new List<string>();
	    var device = GetAudioDevicePhysicalIdFromName(name);
	    if (device == null) return null;
	    SDL.GetAudioDeviceFormat((uint)device, out SDL.AudioSpec spec, out _);
	    var format = spec.Format.ToString().Substring(5);
	    specs.Add($"Bit Depth: {GetBitDepth(spec.Format)} ({format})");
	    specs.Add($"Bit Rate: {spec.Freq}");
	    specs.Add($"Channels: {spec.Channels}");
	    return specs;
    }

    public uint GetAudioDevicePhysicalIdFromName(string name)
    {
	    var devices = SDL.GetAudioPlaybackDevices(out int count);

	    if (devices != null)
	    {
		    foreach (var deviceId in devices)
		    {
			    var deviceName = SDL.GetAudioDeviceName(deviceId);
			    if (deviceName == name) return deviceId;
		    }
	    }

	    return 0;
    }

    public int? GetAudioDeviceIdFromName(string name)
    {
	    foreach (var device in _openDevices.Where(device => name == device.Value.Name))
	    {
		    return device.Key;
	    }
		
	    return null;
    }

    public AudioDevice GetAudioDevice(int deviceId)
    {
	    if (_openDevices.ContainsKey(deviceId))
	    {
		    return _openDevices[deviceId];
	    }

	    return null;
    }


// Helper function to map SDL_AudioFormat to bit depth
	public static int GetBitDepth(SDL.AudioFormat format)
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