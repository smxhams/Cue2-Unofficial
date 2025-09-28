using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
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
	private MediaEngine _mediaEngine;
	
	private readonly Dictionary<int, AudioDevice> _openDevices = new Dictionary<int, AudioDevice>();
	private readonly Dictionary<uint, int> _physicalIdToDeviceId = new Dictionary<uint, int>();
	
	private readonly Dictionary<uint, List<ActiveAudioPlayback>> _activePlaybacks = new Dictionary<uint, List<ActiveAudioPlayback>>();
	
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
		    return;
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
		    _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioDevices:OpenAudioDevice - Failed to find and open audio device of name: {name} ", 3);
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
    
    
    public async Task StartAudioPlayback(ActiveAudioPlayback playback, AudioComponent audioComponent)
    {
	    if (playback == null) 
	    {
		    GD.PrintErr("AudioDevices:StartAudioPlayback - Playback is null.");
		    return;
	    }
	    
	    if (playback.Patch == null)
	    {
		    _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "AudioDevices:StartAudioPlayback - No patch assigned to playback.", 2);
		    return;
	    }
	    
	    // Ensure DeviceStreams is initialized
	    if (playback.DeviceStreams == null)
	    {
		    playback.DeviceStreams = new Dictionary<uint, IntPtr>();
		    GD.Print("AudioDevices:StartAudioPlayback - Initialized DeviceStreams dictionary.");
	    }
	    
	    // Open required devices based on routing
	    var devicesToOpen = _openDevices.Values.Where(d => ShouldRouteToDevice(d, playback)).ToList();
	    
	    // Define source spec from FFmpeg decoder
	    var sourceSpec = new SDL.AudioSpec
	    {
		    Freq = playback.SourceSampleRate,
		    Format = playback.SourceFormat,
		    Channels = (byte)playback.SourceChannels
	    };
	    
	    foreach (var device in devicesToOpen)
	    {
		    if (!_openDevices.ContainsKey((int)device.LogicalId))
		    {
			    OpenAudioDevice(device.Name, out _);
		    }
		    SDL.GetAudioDeviceFormat(device.LogicalId, out var deviceSpec, out var _);

		    // Create and bind audio stream with conversion
		    var stream = SDL.CreateAudioStream(sourceSpec, deviceSpec);
		    if (stream == IntPtr.Zero)
		    {
			    _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioDevices:StartAudioPlayback - Failed to create stream for {device.Name}: {SDL.GetError()}", 2);
			    continue;
		    }

		    if (!SDL.BindAudioStream(device.LogicalId, stream))
		    {
			    _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioDevices:StartAudioPlayback - Failed to bind stream for {device.Name}: {SDL.GetError()}", 2);
			    SDL.DestroyAudioStream(stream);
			    continue;
		    }
		    
		    // Use LogicalId as key
		    if (device.LogicalId == 0)
		    {
			    GD.PrintErr($"AudioDevices:StartAudioPlayback - Invalid LogicalId for device {device.Name}.");
			    SDL.DestroyAudioStream(stream);
			    continue;
		    }

		    playback.DeviceStreams[device.LogicalId] = stream;

		    lock (_activePlaybacks)
		    {
			    if (!_activePlaybacks.ContainsKey(device.LogicalId))
				    _activePlaybacks[device.LogicalId] = new List<ActiveAudioPlayback>();
			    _activePlaybacks[device.LogicalId].Add(playback);
		    }

		    if (SDL.AudioDevicePaused(device.LogicalId) == true)
		    {
			    SDL.ResumeAudioDevice(device.LogicalId);
			    GD.Print($"AudioDevices:StartAudioPlayback - Resumed device {device.Name}");
		    }
	    }
	    
	    GD.Print("AudioDevices:StartAudioPlayback - Audio playback started with FFmpeg.");
    }
    
    
    
    private bool ShouldRouteToDevice(AudioDevice device, ActiveAudioPlayback playback)
    {
	    return playback.Patch.OutputDevices.ContainsKey(device.Name); // Simplified; expand if needed for channel routing
    }
    

    /*/// <summary>
    /// Initiates audio playback for a cue, applying routing via CuePatch and AudioOutputPatch.
    /// Supports multiple devices and concurrent playbacks, creating per-device SDL audio streams.
    /// </summary>
    /// <param name="audioComponent">The audio component containing the file path, routing, and patch details.</param>
    /// <param name="outputChannel">The output channel index (used only for direct output; ignored if patch is provided).</param>
    /// <param name="patch">The AudioOutputPatch defining device channel routing, or null for direct output.</param>
    /// <returns>An ActiveAudioPlayback instance tracking the playback, or null on failure.</returns>
    /// <remarks>
    /// The method:
    /// 1. Validates inputs (media path, devices).
    /// 2. Preloads media via MediaEngine to minimize latency.
    /// 3. Retrieves source audio specs (format, channels, sample rate) from VLC.
    /// 4. Validates CuePatch input channels against source channels.
    /// 5. For each target device (from patch or direct output):
    ///    - Opens the device and retrieves its specs.
    ///    - Creates an SDL audio stream from source format (post-CuePatch channels, float32) to device format.
    ///    - Binds the stream to the device for SDL's native mixing of concurrent playbacks.
    /// 6. Sets up a LibVLC MediaPlayer with an audio callback that processes samples through CuePatch (file-to-patch channels) and AudioOutputPatch (patch-to-device channels).
    /// 7. Tracks playback in _activePlaybacks for each device, resuming devices as needed.
    /// Errors are logged via globalSignals.Log, and playback is aborted if no valid streams are created.
    /// </remarks>
    public async Task<ActiveAudioPlayback> PlayAudio(AudioComponent audioComponent)
    {
	    GD.Print("AudioDevices:PlayAudio --- --- Starting audio playback with test implementation of SDL --- --- ");
		var mediaPath = audioComponent.AudioFile;
		var cuePatch = audioComponent.Routing;
		var patch = audioComponent.Patch;
		var deviceNames = patch != null ? patch.OutputDevices.Keys.ToList() : new List<string> { audioComponent.DirectOutput };
		foreach (var deviceName in deviceNames)
		{
			GD.Print($"Deivce list names: {deviceName}");
		}
		
		// Validate inputs
		if (string.IsNullOrEmpty(mediaPath))
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), "AudioDevices:PlayAudio - Invalid media path", 2);
			return null;
		}
		
		if (deviceNames.Count == 0)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), "AudioDevices:PlayAudio - No output devices specified", 2);
			return null;
		}
		
		// Preload media
		var media = await _mediaEngine.PreloadMediaAsync(mediaPath);
		if (media == null)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioDevices:PlayAudio - Failed to preload media: {mediaPath}", 2);
			GD.Print("AudioDevices:PlayAudio - Failed to preload media: " + mediaPath);
			return null;
		}
		
		
		// Get specs of audio track
		var audioTracks = media.Tracks.Where(t => t.TrackType == TrackType.Audio).ToList();
		if (audioTracks.Count == 0)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioDevices:PlayAudio - No audio tracks in {mediaPath}", 2);
			return null;
		}
		var primaryAudio = audioTracks.First();
		SDL.AudioSpec sourceSpec = new SDL.AudioSpec
		{
			Freq = (int)primaryAudio.Data.Audio.Rate,
			Channels = (int)primaryAudio.Data.Audio.Channels,
			Format = GetSdlFormatFromCodec(primaryAudio.Codec)
		};
		
		int bytesPerSample = sourceSpec.Format switch
		{
			SDL.AudioFormat.AudioS16LE or SDL.AudioFormat.AudioS16BE => 2,
			SDL.AudioFormat.AudioS32LE or SDL.AudioFormat.AudioS32BE => 4,
			SDL.AudioFormat.AudioF32LE or SDL.AudioFormat.AudioF32BE => 4,
			SDL.AudioFormat.AudioU8 or SDL.AudioFormat.AudioS8 => 1,
			_ => 2
		};
		int sourceBytesPerFrame = bytesPerSample * sourceSpec.Channels;
		// _bytesPerFrame = bytesPerSample * sourceSpec.Channels; BOVE VAR IS NEW REPLACING THIS
		
		
		// Validate CuePatch
		if (cuePatch != null && cuePatch.InputChannels != sourceSpec.Channels)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
				$"AudioDevices:PlayAudio - CuePatch input channels ({cuePatch.InputChannels}) " +
				$"do not match source channels ({sourceSpec.Channels})", 2);
			return null;
		}
		int patchChannels = cuePatch?.OutputChannels ?? sourceSpec.Channels;
		
		// Create media player
		var mediaPlayer = _mediaEngine.CreateMediaPlayer(media);
		
		var playback = new ActiveAudioPlayback(mediaPlayer, audioComponent);
		string vlcFormat = GetVlcFormat(sourceSpec);
		mediaPlayer.SetAudioFormat(vlcFormat, (uint)sourceSpec.Freq, (uint)sourceSpec.Channels);
		mediaPlayer.SetAudioCallbacks((opaque, samples, count, pts) => 
			AudioCallback(opaque, samples, count, pts, playback), null, null, null, null);
		
		// Initialise playback with per-device streams
		playback.DeviceStreams = new Dictionary<uint, IntPtr>();
		foreach (var deviceName in deviceNames)
		{
			var device = OpenAudioDevice(deviceName, out var error);
			if (device == null)
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioDevices:PlayAudio - Failed to open device {deviceName}: {error}", 2);
				continue;
			}

			// Validate AudioOutputPatch
			var outputs = new List<OutputChannel>();
			if (patch != null && patch.OutputDevices.TryGetValue(deviceName, out outputs) && outputs.Count != device.Channels)
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
					$"AudioDevices:PlayAudio - Patch outputs ({outputs.Count}) do not match device channels ({device.Channels}) for {deviceName}", 2);
				continue;
			}
			
			// Create audio stream: sourceSpec to deviceSpec
			SDL.GetAudioDeviceFormat(device.LogicalId, out var deviceSpec, out var _);
			var streamSrcSpec = sourceSpec;
			streamSrcSpec.Channels = (byte)(patch != null ? outputs.Count : device.Channels); // Post-patch channels
			streamSrcSpec.Format = SDL.AudioFormat.AudioF32LE; // Process in float32
			var audioStream = SDL.CreateAudioStream(streamSrcSpec, deviceSpec);
			if (audioStream == IntPtr.Zero)
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
					$"AudioDevices:PlayAudio - Failed to create SDL audio stream for {deviceName}: {SDL.GetError()}", 2);
				continue;
			}
			
			// Bind stream
			var streams = new[] { audioStream };
			if (!SDL.BindAudioStreams(device.LogicalId, streams, streams.Length))
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
					$"AudioDevices:PlayAudio - Failed to bind audio stream for {deviceName}: {SDL.GetError()}", 2);
				SDL.DestroyAudioStream(audioStream);
				continue;
			}

			playback.DeviceStreams[device.PhysicalId] = audioStream;
			if (!_activePlaybacks.ContainsKey(device.PhysicalId))
			{
				_activePlaybacks[device.PhysicalId] = new List<ActiveAudioPlayback>();
			}
			_activePlaybacks[device.PhysicalId].Add(playback);

			// Resume device if first playback
			if (_activePlaybacks[device.PhysicalId].Count == 1)
			{
				SDL.ResumeAudioDevice(device.LogicalId);
			}
		}
		
		if (playback.DeviceStreams.Count == 0)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), "AudioDevices:PlayAudio - No valid devices/streams initialized", 2);
			mediaPlayer.Dispose();
			return null;
		}

		// Set up playback
		playback.MediaPlayer = mediaPlayer;
		playback.Patch = patch;
		playback.CuePatch = cuePatch;
		playback.SourceChannels = sourceSpec.Channels;
		playback.SourceBytesPerFrame = sourceBytesPerFrame;
		playback.SourceFormat = sourceSpec.Format;
		playback.Completed += () => OnPlaybackCompleted(playback);
        
		GD.Print($"AudioDevices:PlayAudio - Started playback on {playback.DeviceStreams.Count} devices for {mediaPath}");
		return playback;

    }
    
    
    /// <summary>
    /// Processes audio samples from VLC, applying CuePatch and AudioOutputPatch routing, and queues to per-device SDL streams.
    /// </summary>
    /// <param name="opaque">Unused pointer from VLC callback.</param>
    /// <param name="samples">Pointer to interleaved audio samples from VLC.</param>
    /// <param name="count">Number of frames (samples per channel).</param>
    /// <param name="pts">Presentation timestamp (ignored).</param>
    /// <param name="playback">The ActiveAudioPlayback instance containing routing and stream info.</param>
    /// <remarks>
    /// The processing pipeline:
    /// 1. Deinterleaves samples into per-channel arrays, normalizing to float32 [-1,1] based on source format (S16 or F32).
    /// 2. Applies CuePatch volume matrix to mix source channels to patch channels (if CuePatch is present).
    /// 3. For each device:
    ///    - Applies AudioOutputPatch to sum patch channels to device physical channels (or uses 1:1 mapping for direct output).
    ///    - Clamps samples to [-1,1] to prevent clipping.
    ///    - Interleaves samples to float32 and queues to the device's SDL audio stream.
    /// 4. Monitors queue size to prevent underflow (low latency) or overflow (high latency).
    /// SDL handles format conversion (from float32 to device format) and mixing of multiple streams per device.
    /// Errors are logged via globalSignals.Log, and memory is safely managed with try-finally blocks.
    /// </remarks>
    private unsafe void AudioCallback(nint opaque, nint samples, uint count, long pts, ActiveAudioPlayback playback)
    {
	    int frameCount = (int)count;
	    int sourceChannels = playback.SourceChannels;
	    int sourceBytesPerFrame = playback.SourceBytesPerFrame;

	    // Deinterleave samples based on source format
	    float[][] patchSamples;
	    try
	    {
		    patchSamples = DeinterleaveSamples(samples, sourceChannels, frameCount, playback.SourceFormat);
	    }
	    catch (Exception ex)
	    {
		    _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
			    $"AudioDevices:AudioCallback - Failed to deinterleave samples: {ex.Message}", 2);
		    GD.PrintErr($"AudioDevices:AudioCallback - Deinterleave error: {ex.Message}");
		    return;
	    }
	    
	    // Apply CuePatch (file channels to patch channels)
	    int patchChannels = playback.CuePatch?.OutputChannels ?? sourceChannels;
	    if (playback.CuePatch != null)
	    {
		    var temp = new float[patchChannels][];
		    for (int ch = 0; ch < patchChannels; ch++)
			    temp[ch] = new float[frameCount];
            
		    for (int f = 0; f < frameCount; f++)
		    {
			    for (int outCh = 0; outCh < patchChannels; outCh++)
			    {
				    float sum = 0f;
				    for (int inCh = 0; inCh < sourceChannels; inCh++)
				    {
					    sum += patchSamples[inCh][f] * playback.CuePatch.GetVolume(inCh, outCh);
				    }
				    temp[outCh][f] = Math.Clamp(sum, -1f, 1f);
			    }
		    }
		    patchSamples = temp;
	    }
	    
	    // Process per device
        foreach (var kv in playback.DeviceStreams)
        {
            uint physicalId = kv.Key;
            IntPtr stream = kv.Value;
            if (!_physicalIdToDeviceId.TryGetValue(physicalId, out int deviceId))
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                    $"AudioDevices:AudioCallback - Device not found for physical ID {physicalId}", 2);
                continue;
            }
            var device = _openDevices[deviceId];
            var deviceName = device.Name;
            int deviceChannels = device.Channels;

            // Apply AudioOutputPatch (patch channels to device channels)
            float[][] deviceSamples = new float[deviceChannels][];
            for (int ch = 0; ch < deviceChannels; ch++)
                deviceSamples[ch] = new float[frameCount];

            if (playback.Patch != null && playback.Patch.OutputDevices.TryGetValue(deviceName, out var outputs))
            {
                for (int physicalCh = 0; physicalCh < deviceChannels; physicalCh++)
                {
                    var routed = outputs[physicalCh].RoutedChannels;
                    if (routed.Count == 0)
                    {
                        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                            $"AudioDevices:AudioCallback - No routes for {deviceName} channel {physicalCh}", 1);
                        continue;
                    }
                    for (int f = 0; f < frameCount; f++)
                    {
                        float sum = 0f;
                        foreach (int patchCh in routed)
                        {
                            if (patchCh < patchChannels)
                                sum += patchSamples[patchCh][f];
                        }
                        deviceSamples[physicalCh][f] = Math.Clamp(sum, -1f, 1f);
                    }
                }
            }
            else
            {
                // Direct output: assume 1:1 mapping
                int minCh = Math.Min(patchChannels, deviceChannels);
                for (int ch = 0; ch < minCh; ch++)
                {
                    Array.Copy(patchSamples[ch], deviceSamples[ch], frameCount);
                }
            }

            // Interleave and queue to SDL stream
            int byteCount = frameCount * 4 * deviceChannels;
            nint tempPtr = Marshal.AllocHGlobal(byteCount);
            try
            {
                InterleaveFloat(deviceSamples, deviceChannels, frameCount, tempPtr);
                if (!SDL.PutAudioStreamData(stream, tempPtr, byteCount))
                {
                    _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                        $"AudioDevices:AudioCallback - Failed to queue data for {deviceName}: {SDL.GetError()}", 2);
                }

                // Monitor queue to prevent underflow/overflow
                int queued = SDL.GetAudioStreamQueued(stream);
                if (queued < 4096)
                {
                    GD.Print($"AudioDevices:AudioCallback - Warning: Queue low for {deviceName} ({queued} bytes)");
                }
                else if (queued > 65536)
                {
                    GD.Print($"AudioDevices:AudioCallback - Warning: Queue overflow for {deviceName} ({queued} bytes)");
                    SDL.ClearAudioStream(stream);
                }
            }
            catch (Exception ex)
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                    $"AudioDevices:AudioCallback - Error processing for {deviceName}: {ex.Message}", 2);
            }
            finally
            {
                Marshal.FreeHGlobal(tempPtr);
            }
        }
    }*/

    /// <summary>
    /// Deinterleaves audio samples from VLC into per-channel float arrays, normalizing to [-1,1].
    /// </summary>
    /// <param name="samplesPtr">Pointer to interleaved samples from VLC.</param>
    /// <param name="channels">Number of source channels.</param>
    /// <param name="frameCount">Number of frames (samples per channel).</param>
    /// <param name="format">SDL audio format (S16 or F32 supported).</param>
    /// <returns>Array of per-channel float arrays, normalized to [-1,1].</returns>
    /// <exception cref="NotSupportedException">Thrown for unsupported audio formats.</exception>
    /// <remarks>
    /// Converts interleaved samples (e.g., LRLR for stereo) into separate arrays per channel.
    /// For S16, normalizes 16-bit integers to [-1,1] by dividing by 32768.
    /// For F32, uses samples directly (already [-1,1]).
    /// Uses unsafe code for performance; ensure project settings allow unsafe code.
    /// This step prepares samples for CuePatch processing (volume matrix application).
    /// </remarks>
    private unsafe float[][] DeinterleaveSamples(nint samplesPtr, int channels, int frameCount, SDL.AudioFormat format)
    {
	    float[][] deinterleaved = new float[channels][];
	    for (int ch = 0; ch < channels; ch++)
		    deinterleaved[ch] = new float[frameCount];

	    if (format == SDL.AudioFormat.AudioF32LE || format == SDL.AudioFormat.AudioF32BE)
	    {
		    float* samples = (float*)samplesPtr;
		    for (int f = 0; f < frameCount; f++)
		    {
			    for (int ch = 0; ch < channels; ch++)
			    {
				    deinterleaved[ch][f] = samples[f * channels + ch];
			    }
		    }
	    }
	    else if (format == SDL.AudioFormat.AudioS16LE || format == SDL.AudioFormat.AudioS16BE)
	    {
		    short* samples = (short*)samplesPtr;
		    for (int f = 0; f < frameCount; f++)
		    {
			    for (int ch = 0; ch < channels; ch++)
			    {
				    deinterleaved[ch][f] = samples[f * channels + ch] / 32768f; // Normalize to [-1,1]
			    }
		    }
	    }
	    else
	    {
		    throw new NotSupportedException($"Audio format {format} not supported for processing");
	    }
	    return deinterleaved;
    }
    
    
    /// <summary>
    /// Interleaves per-channel float arrays into a single float32 buffer for SDL audio stream.
    /// </summary>
    /// <param name="deinterleaved">Array of per-channel float arrays (post-routing).</param>
    /// <param name="channels">Number of device channels.</param>
    /// <param name="frameCount">Number of frames per channel.</param>
    /// <param name="outputPtr">Pointer to output buffer for interleaved samples.</param>
    /// <remarks>
    /// Combines per-channel float arrays into interleaved format (e.g., LRLR for stereo).
    /// Samples are assumed to be [-1,1] floats, suitable for SDL audio stream (float32).
    /// Uses unsafe code for performance; caller must free outputPtr.
    /// This step finalizes samples for queuing to a device's SDL stream, which handles conversion to the device's native format.
    /// </remarks>
    private unsafe void InterleaveFloat(float[][] deinterleaved, int channels, int frameCount, nint outputPtr)
    {
	    float* output = (float*)outputPtr;
	    for (int f = 0; f < frameCount; f++)
	    {
		    for (int ch = 0; ch < channels; ch++)
		    {
			    output[f * channels + ch] = deinterleaved[ch][f];
		    }
	    }
    }
    
    
    /// <summary>
    /// Handles cleanup when an audio playback completes, pausing devices with no active playbacks.
    /// </summary>
    /// <param name="playback">The completed ActiveAudioPlayback instance.</param>
    /// <remarks>
    /// For each device in the playback:
    /// 1. Removes the playback from _activePlaybacks.
    /// 2. Destroys the associated SDL audio stream.
    /// 3. Pauses the device if no playbacks remain.
    /// Ensures resources are freed and devices are paused to save CPU when idle.
    /// Logs cleanup actions via GD.Print for debugging.
    /// </remarks>
    private void OnPlaybackCompleted(ActiveAudioPlayback playback) 
    {
	    foreach (var physicalId in playback.DeviceStreams.Keys.ToList())
	    {
		    if (_activePlaybacks.TryGetValue(physicalId, out var list))
		    {
			    GD.Print($"AudioDevices:OnPlaybackCompleted - Cleaning up for device {physicalId}");
			    list.Remove(playback);
			    if (list.Count == 0)
			    {
				    var deviceId = _physicalIdToDeviceId[physicalId];
				    var device = _openDevices[deviceId];
				    SDL.PauseAudioDevice(device.LogicalId);
				    GD.Print($"AudioDevices:OnPlaybackCompleted - Paused device {device.Name} as no active playbacks.");
			    }
		    }
	    }
	    playback.DeviceStreams.Clear(); // Ensure cleared even if not in _activePlaybacks
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
		foreach (var playbacks in _activePlaybacks.Values)
		{
			foreach (var playback in playbacks.ToList())
			{
				playback.Stop(0).Wait();
			}
		}
		_activePlaybacks.Clear();
		
		foreach (var device in _openDevices.Values.ToList())
		{
			CloseAudioDevice(device.DeviceId);
		}
		if (SDL.WasInit(SDL.InitFlags.Audio) != 0) SDL.Quit();
		GD.Print("AudioDevices:_ExitTree - Cleaned up SDL and devices.");
	}
	
	
    
}