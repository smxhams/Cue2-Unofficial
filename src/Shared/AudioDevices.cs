using System;
using System.Collections.Generic;
using System.Linq;
using Cue2.Base.Classes.Devices;
using Godot;
using SDL3;

namespace Cue2.Shared;


/// <summary>
/// AudioDevices looks after all SDL related tasks.
/// </summary>
public partial class AudioDevices : Node
{
	private GlobalData _globalData;
	private GlobalSignals _globalSignals;
	
	private readonly Dictionary<int, AudioDevice> _openDevices = new Dictionary<int, AudioDevice>();
	private readonly Dictionary<uint, int> _physicalIdToDeviceId = new Dictionary<uint, int>();
	
	private Timer _pollTimer;
	
    public override void _Ready()
    {
	    _globalData = GetNode<GlobalData>("/root/GlobalData");
	    _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
	    
	    if (SDL.Init(SDL.InitFlags.Audio | SDL.InitFlags.Events) == false)
	    {
		    var errorMsg = $"SDL Init failed: {SDL.GetError}";
		    GD.Print("AudioDevices:_Ready - " + errorMsg);
		    _globalSignals.EmitSignal(nameof(GlobalSignals.Log), errorMsg, 3);
	    }
	    GD.Print("AudioDevices:_Ready - SDL initialized successfully.");

	    _pollTimer = new Timer();
	    _pollTimer.WaitTime = 0.5;
	    _pollTimer.Autostart = true;
	    _pollTimer.Timeout += PollSdlEvents;
	    AddChild(_pollTimer);
    }

    private void PollSdlEvents()
    {
	    bool changesDetected = false; 
	    while (SDL.PollEvent(out var ev))
	    {
		    if (ev.Type == (uint)SDL.EventType.AudioDeviceRemoved)
		    {
			    var removedPhysicalId = ev.ADevice.Which;
			    CheckMissingDevices(removedPhysicalId);
			    changesDetected = true;
		    }
		    else if (ev.Type == (uint)SDL.EventType.AudioDeviceAdded)
		    {
			    CheckAddedDevice(ev.ADevice.Which);
			    changesDetected = true;
		    }
	    }

	    if (changesDetected)
	    {
		    _globalSignals.EmitSignal(nameof(GlobalSignals.AudioDevicesChanged));
	    }
    }

    /// <summary>
    /// Checks if any devices in _openDevices are missing from the list of available audio devices.
    /// Logs a warning for each missing device and returns their names.
    /// </summary>
    private void CheckMissingDevices(uint removedPhysicalId)
    {
	    GD.Print("AudioDevices:CheckMissingDevices - Checking for missing audio devices");
	    if (_physicalIdToDeviceId.TryGetValue(removedPhysicalId, out int deviceId))
	    {
		    var device = _openDevices[deviceId];
		    _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Audio device disconnected/lost: {device.Name}", 3);
		    CloseAudioDevice(deviceId);
	    }
	}
    
    /// <summary>
    /// Checks if an audio devices physical ID matches a device used in an audio output patch.
    /// Function will ensure device is opened if it is not already.
    /// </summary>
    /// <param name="addedPhysicalId">The ID of the audio device to check.</param>
    private void CheckAddedDevice(uint addedPhysicalId)
	{
		var name = SDL.GetAudioDeviceName(addedPhysicalId);
		var patches = _globalData.Settings.GetAudioOutputPatches();
		foreach (var patch in patches)
		{
			if (name != null && patch.Value.OutputDevices.ContainsKey(name))
			{
				OpenAudioDevice(name, out var _);
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Needed audio device reconnected: {name}", 0);
				return;
			}
		}
	}
    
	/// <summary>
	/// Closes an open audio device by its ID, removes it from internal tracking, and logs the result.
	/// </summary>
	/// <param name="deviceId">The ID of the audio device to close (key in _openDevices).</param>
	/// <returns>True if successfully closed and removed; false on error.</returns>
    private bool CloseAudioDevice(int deviceId)
    {
	    var device = _openDevices[deviceId];
	    try
	    {
		    SDL.CloseAudioDevice(device.LogicalId);
		    _openDevices.Remove(deviceId);
		    _physicalIdToDeviceId.Remove(device.PhysicalId);
		    
		    GD.Print("AudioDevices:CloseAudioDevice - Successfully closed device ID: " + deviceId);
		    return true;
	    }
	    catch (Exception ex)
	    {
		    _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
			    $"Error closing device '{device.Name}' (ID: {deviceId}): {ex.Message}", 2);
		    GD.PrintErr("AudioDevices:CloseAudioDevice - Error closing device ID " + deviceId + ": " + ex.Message);
		    return false;
	    }
	    
    }

    /// <summary>
    /// Opens an audio device by name if not already open, registers it, and retrieves its specs.
    /// </summary>
    /// <param name="name">The name of the audio device to open.</param>
    /// <param name="error">Output parameter for any error message; empty string on success.</param>
    /// <returns>The opened AudioDevice instance, or null on failure.</returns>
    /// <remarks>
    /// If the device is already open, returns the existing instance.
    /// </remarks>
    public AudioDevice OpenAudioDevice(string name, out string error)
    {
	    // Check if audio device already opened
	    GD.Print($"OpenAudioDevice called for: {name}");
	    foreach (var dev in _openDevices.Values)
	    {
		    if (dev.Name == name)
		    {
			    GD.Print("    ^^ Device already opened");
			    error = "";
				return dev;
			    
		    }
	    }
	    var physicalDeviceId = GetAudioDevicePhysicalIdFromName(name);
	    //Open audio device
	    var audioDevice = SDL.OpenAudioDevice(physicalDeviceId, 0); // Zero is a null, this means device will open with its own settings
	    if (audioDevice == 0)
	    {
		    _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to find and open audio device of name: {name} ", 3);
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

	    device.PhysicalId = physicalDeviceId;
	    device.Channels = specs.Channels;
	    device.Format = specs.Format;
	    device.SampleRate = specs.Freq;
	    device.BitDepth = GetBitDepth(specs.Format);
	    
	    _openDevices.Add(device.DeviceId, device);
	    _physicalIdToDeviceId[device.PhysicalId] = device.DeviceId;

	    error = "";
	    return device;
    }
    
    /// <summary>
    /// Retrieves the names of all available audio playback devices using SDL.
    /// </summary>
    /// <returns>A list of device names, or null if an error occurs during enumeration.</returns>
    /// <remarks>
    /// Catches exceptions and logs them internally. Does not include already opened devices' status.
    /// </remarks>
    public List<string> GetAvailableAudioDeviceNames()
    {
	    try
	    {
		    GD.Print("AudioDevices:GetAvailableAudioDeviceNames - Enumerating playback devices");
		    // Get number of playback devices
		    var devices = SDL.GetAudioPlaybackDevices(out int _);
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
		    _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Error enumerating audio devices: {ex.Message}", 2);
		    GD.PrintErr("AudioDevices:GetAvailableAudioDeviceNames - " + ex.Message);
		    return null;
	    }
	    
	    
    }

    public Godot.Collections.Array<string> GetOpenAudioDevicesNames()
    {
	    var deviceNames = new Godot.Collections.Array<string>();
	    foreach (var device in _openDevices.Values)
	    {
		    deviceNames.Add(device.Name);
	    }

	    return deviceNames;
    }

    /// <summary>
    /// Retrieves the audio specifications for a device by name.
    /// </summary>
    /// <param name="name">The name of the audio device.</param>
    /// <returns>The SDL.AudioSpec struct for the device.</returns>
    /// <remarks>
    /// Assumes the device exists; no error checking for invalid names.
    /// </remarks>
    private SDL.AudioSpec GetAudioDeviceSpec(string name)
    {
	    var device = GetAudioDevicePhysicalIdFromName(name);
	    SDL.GetAudioDeviceFormat(device, out SDL.AudioSpec spec, out _);
	    return spec; // Return SDL_AudioSpec structu
    } 

    
    /// <summary>
    /// Converts audio device specs into a human-readable list of strings.
    /// </summary>
    /// <param name="name">The name of the audio device.</param>
    /// <returns>A list of formatted spec strings (e.g., "Bit Depth: 16 (S16LE)").</returns>
    public List<string> GetReadableAudioDeviceSpecs(string name)
    {
	    var specs = new List<string>();
	    var device = GetAudioDevicePhysicalIdFromName(name);
	    SDL.GetAudioDeviceFormat(device, out SDL.AudioSpec spec, out _);
	    var format = spec.Format.ToString().Substring(5);
	    specs.Add($"Bit Depth: {GetBitDepth(spec.Format)} ({format})");
	    specs.Add($"Bit Rate: {spec.Freq}");
	    specs.Add($"Channels: {spec.Channels}");
	    return specs;
    }

    private uint GetAudioDevicePhysicalIdFromName(string name)
    {
	    var devices = SDL.GetAudioPlaybackDevices(out int _);

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
	    return _openDevices.GetValueOrDefault(deviceId);
    }


    /// <summary>
    /// Maps an SDL.AudioFormat to its bit depth.
    /// </summary>
    /// <param name="format">The SDL audio format.</param>
    /// <returns>The bit depth (e.g., 16), or 0 for unsupported formats.</returns>
    /// <remarks>
    /// Logs a warning for unknown formats. Consider throwing an exception in strict modes.
    /// </remarks>
	private static int GetBitDepth(SDL.AudioFormat format)
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

	public override void _ExitTree()
	{
		foreach (var device in _openDevices.Values.ToList())
		{
			CloseAudioDevice(device.DeviceId);
		}
		if (SDL.WasInit(SDL.InitFlags.Audio) != 0) SDL.Quit();
		GD.Print("AudioDevices:_ExitTree - Cleaned up SDL and devices.");
	}
	
	
    
}