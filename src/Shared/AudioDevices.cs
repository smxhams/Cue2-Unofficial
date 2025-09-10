using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cue2.Base.Classes;
using Cue2.Base.Classes.Devices;
using Godot;
using LibVLCSharp.Shared;
using SDL3;

namespace Cue2.Shared;


/// <summary>
/// AudioDevices looks after all SDL related tasks.
/// </summary>
public partial class AudioDevices : Node
{
	private GlobalData _globalData;
	private GlobalSignals _globalSignals;
	private MediaEngine _mediaEngine;
	
	private readonly Dictionary<int, AudioDevice> _openDevices = new Dictionary<int, AudioDevice>();
	private readonly Dictionary<uint, int> _physicalIdToDeviceId = new Dictionary<uint, int>();
	
	private readonly Dictionary<int, List<ActiveAudioPlayback>> _activePlaybacks = new Dictionary<int, List<ActiveAudioPlayback>>();

	private int _bytesPerFrame;
	private SDL.AudioSpec _audioSpec;
	private static IntPtr _audioStream; 
	
	private Timer _pollTimer;
	
    public override void _Ready()
    {
	    _globalData = GetNode<GlobalData>("/root/GlobalData");
	    _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
	    _mediaEngine = GetNode<MediaEngine>("/root/MediaEngine");
	    
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


    public async Task<ActiveAudioPlayback> PlayAudio(AudioComponent audioComponent, int outputChannel, AudioOutputPatch patch)
    {
	    GD.Print(" --- --- Starting audio playback with test implementation of SDL --- --- ");
		var playback = new ActiveAudioPlayback();

		var mediaPath = audioComponent.AudioFile;
		var deviceName = audioComponent.DirectOutput;
		
		GD.Print($"Lets play this track: {mediaPath}");
		var device = OpenAudioDevice(deviceName, out var error);
		if (device == null)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to open audio device: {error}", 2);
			return null;
		}
		
		var media = await _mediaEngine.PreloadMediaAsync(mediaPath);
		if (media == null)
		{
			GD.Print($"AudioDevices:PlayAudio - Failed to preload media: {mediaPath}");
		}
		
		// Get specs of audio track
		SDL.AudioSpec sourceSpec = new SDL.AudioSpec();
		var audioTracks = media.Tracks.Where(t => t.TrackType == TrackType.Audio).ToList();
		if (audioTracks.Count == 0)
		{
			throw new Exception("No audio track found.");
		}
		var primaryAudio = audioTracks.First();
		sourceSpec.Freq = (int)primaryAudio.Data.Audio.Rate;
		sourceSpec.Channels = (byte)primaryAudio.Data.Audio.Channels;
		sourceSpec.Format = GetSdlFormatFromCodec(primaryAudio.Codec);
		
		
		int bytesPerSample = sourceSpec.Format switch
		{
			SDL.AudioFormat.AudioS16LE or SDL.AudioFormat.AudioS16BE => 2,
			SDL.AudioFormat.AudioS32LE or SDL.AudioFormat.AudioS32BE => 4,
			SDL.AudioFormat.AudioF32LE or SDL.AudioFormat.AudioF32BE => 4,
			SDL.AudioFormat.AudioU8 or SDL.AudioFormat.AudioS8 => 1,
			_ => 2
		};
		_bytesPerFrame = bytesPerSample * sourceSpec.Channels;
		
		
		// Get device specs
		SDL.GetAudioDeviceFormat(device.LogicalId, out var obtainedSpec, out var sampleFrames);
		var formatName = SDL.GetAudioFormatName(obtainedSpec.Format);
		_audioSpec = obtainedSpec;
		
		GD.Print($"Source spec format is: {sourceSpec.Format}.   Destination audio device format is: {obtainedSpec.Format}");
		GD.Print($"Device preferred settings: name={SDL.GetAudioDeviceName(device.PhysicalId)}, " +
		         $"format={formatName}, freq={_audioSpec.Freq}, channels={_audioSpec.Channels}, " +
		         $"samples={sampleFrames}");
		
		
		// Create audio stream
		IntPtr audioStream = SDL.CreateAudioStream(sourceSpec, _audioSpec);
		if (audioStream == IntPtr.Zero)
		{
			throw new Exception($"Failed to create SDL audio stream: {SDL.GetError()}");
		}
		
		_audioStream = audioStream;
		// Bind stream to device (specific to output channel? SDL3 may need channel mapping; simplify for now)
		var streams = new[] { audioStream };
		bool bindResult = SDL.BindAudioStreams(device.LogicalId, streams, streams.Length);
		if (bindResult == false)
		{
			Console.WriteLine($"Failed to bind audio stream: {SDL.GetError()}");
			SDL.DestroyAudioStream(audioStream);
		}
		Console.WriteLine("Audio stream bound successfully.");
		string vlcFormat = GetVlcFormat(sourceSpec);
		GD.Print(vlcFormat);

		var mediaPlayer = _mediaEngine.CreateMediaPlayer(media);
		mediaPlayer.SetAudioFormat(vlcFormat, (uint)sourceSpec.Freq, (uint)sourceSpec.Channels);
		mediaPlayer.SetAudioCallbacks(AudioCallback, null, null, null, null);
		
		
		// Start SDL audio
		SDL.ResumeAudioDevice(device.LogicalId);
		
		
		// Create and track ActiveAudioPlayback
		// Each physical deivce Id has a list of AtiveAudioPlaybacks that are associated with it.
		var deviceId = (int)device.PhysicalId;
		playback = new ActiveAudioPlayback(mediaPlayer, device.LogicalId, audioStream, patch, outputChannel, audioComponent);
		if (!_activePlaybacks.ContainsKey(deviceId))
		{
			_activePlaybacks[deviceId] = new List<ActiveAudioPlayback>();
		}
		_activePlaybacks[deviceId].Add(playback);

		GD.Print($"AudioDevices:PlayAudioOnOutput - Started playback on device {device.Name}, output {outputChannel}.");
		return playback;

    }
    
    private void AudioCallback(nint opaque, nint samples, uint count, long pts)
    {
	    //if (!_isPlaying) return;
	    int byteCount = (int)count * _bytesPerFrame;
		
	    if (SDL.PutAudioStreamData(_audioStream, samples, byteCount) == false)
	    {
		    GD.Print($"Failed to put audio stream data: {SDL.GetError()}");
	    }
	    else
	    {
		    //GD.Print($"Queued {count} bytes to SDL audio stream.");
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


	/// <summary>
	/// Map VLC codec to SDL.AudioFormat
	/// </summary>
	private SDL.AudioFormat GetSdlFormatFromCodec(uint codec)
	{
		string codecName = System.Text.Encoding.ASCII.GetString(BitConverter.GetBytes(codec)).ToLower();
		GD.Print($"AudioDevices:GetSdlFormatFromCodec - Mapping codec {codecName} to SDL format");
		switch (codecName)
		{
			case "s16l": return SDL.AudioFormat.AudioS16LE;
			case "s16b": return SDL.AudioFormat.AudioS16BE;
			case "s32l": return SDL.AudioFormat.AudioS32LE;
			case "s32b": return SDL.AudioFormat.AudioS32BE;
			case "f32l": return SDL.AudioFormat.AudioF32LE;
			case "f32b": return SDL.AudioFormat.AudioF32BE;
			case "mp3": return SDL.AudioFormat.AudioS16LE;
			case "mpeg": return SDL.AudioFormat.AudioS16LE; // Common for MP3
			case "flac": return SDL.AudioFormat.AudioS32LE;
			case "pcm": return SDL.AudioFormat.AudioS32LE; // Common for FLAC, high-quality PCM
			default: return SDL.AudioFormat.AudioS16LE;
		}
	}

	private string GetVlcFormat(SDL.AudioSpec sourceSpec)
	{
		switch (sourceSpec.Format)
		{
			case SDL.AudioFormat.AudioS16LE : return "S16L";
			case SDL.AudioFormat.AudioS16BE : return "S16N";
			case SDL.AudioFormat.AudioS32LE : return "S32N";
			case SDL.AudioFormat.AudioS32BE : return "S32N";
			case SDL.AudioFormat.AudioF32LE : return "f32n";
			case SDL.AudioFormat.AudioF32BE : return "f32n"; 
			default: return "S16N";
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