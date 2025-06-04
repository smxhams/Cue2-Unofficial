using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Cue2.Shared;
using Godot;
using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;
using SDL3;
using Timer = System.Timers.Timer;

namespace Cue2.Base.Classes;

public partial class Playback : Node
{
	private static readonly Dictionary<int, MediaPlayerState> MediaPlayers = new Dictionary<int, MediaPlayerState>(); // (CueID, State)
	
	private readonly LibVLC _libVLC;

	private Window _window;
	private static  Window _canvasWindow;

	private GlobalData _globalData;
	private GlobalSignals _globalSignals;
	
	private int _playbackIndex = 0;
	
	
	// For test SDL implementation
	private static uint audioDevice;
	private static nint audioStream;
	private static MediaPlayer mediaPlayer;
	private static List<byte> audioBuffer = new List<byte>(); // Buffer for audio data
	private static int bytesPerSampleFrame = 4;
	
	
	
	
	public Playback()
	{
		Core.Initialize();
		_libVLC = new LibVLC();
	}

	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_window = GetWindow();
		_window.CloseRequested += WindowOnCloseRequested;
		_canvasWindow = _globalData.VideoWindow;
		_globalSignals.StopAll += StopAll;

		if (SDL.Init(SDL.InitFlags.Audio) == false)
		{
			Console.WriteLine($"SDL_Init failed: {SDL.GetError()}");
			return;
		}
		GD.Print("INT PASSED");
	}

	
	// See below for test SDL implementation
	/*public async void PlayMedia(Cue cue, Window window = null)
	{
		GD.Print("PLAYTING MEDIA HERE");
		var mediaPlayer = new MediaPlayer(_libVLC);
		var media = new Media(_libVLC, cue.FilePath);
		await media.Parse(); // MediaParseOptions.ParseLocal - this will need to change when refencing online URLS
		while (media.IsParsed != true) { }
		
		mediaPlayer.Media = media;
		
		var (hasVideo, hasAudio) = GetMediaType(media);
		MediaPlayers.Add(_playbackIndex, new MediaPlayerState(mediaPlayer, hasVideo, hasAudio));
		
		
		if (hasVideo && window != null)
		{
			var targetRect = CreateVideoTextureRect();
			MediaPlayers[_playbackIndex].TargetTextureRect = targetRect;

			uint videoheight = 0;
			uint videowidth = 0;
			mediaPlayer.Size(0, ref videowidth, ref videoheight);
			
			targetRect.Set("VideoAlpha", 255);
			targetRect.CallDeferred("InitVideoTexture", _playbackIndex, Convert.ToInt32(videowidth), Convert.ToInt32(videoheight));
		}
		
		MediaPlayers[_playbackIndex].MediaPlayer.Volume = 100;
		MediaPlayers[_playbackIndex].MediaPlayer.Play();
		_globalSignals.EmitSignal(nameof(GlobalSignals.CueGo), _playbackIndex, cue.Id);
		
		MediaPlayers[_playbackIndex].MediaPlayer.EndReached += MediaOnEndReached;
		_playbackIndex++;
		media.Dispose();
	}*/

	// The following is a test implementation with SDL
	public async void PlayMedia(Cue cue, Window window = null)
	{
		GD.Print("Starting audio playback with test implementation of SDL");
		var mediaPlayer = new MediaPlayer(_libVLC);
		var media = new Media(_libVLC, cue.FilePath);
		await media.Parse(); // MediaParseOptions.ParseLocal - this will need to change when refencing online URLS
		while (media.IsParsed != true) { }
		
		mediaPlayer.Media = media;
		
		
		var spec = new SDL.AudioSpec()
		{
			Freq = 41000,
			Format = SDL.AudioFormat.AudioS16LE,
			Channels = 2, // Mono for single channel
		};
		// Open SDL3 audio device
		var devices = SDL.GetAudioPlaybackDevices(out int deviceCount);
		var deviceId = devices[0];
		SDL.GetAudioDeviceFormat(deviceId, out var format, out _);
		var formatName = SDL.GetAudioFormatName(format.Format);
		GD.Print("Chosen device name: " + SDL.GetAudioDeviceName(deviceId) + " and format: " + formatName);
		if (devices != null) audioDevice = SDL.OpenAudioDevice(deviceId, System.IntPtr.Zero); // Zero is a null, this means device will open with its own settings
		if (audioDevice == 0)
		{
			Console.WriteLine($"Failed to open audio device: {SDL.GetError()}");
			SDL.Quit();
			return;
		}
		// Create SDL3 audio stream
		SDL.GetAudioDeviceFormat(deviceId, out var deviceSpec, out int _);
		audioStream = SDL.CreateAudioStream(spec, deviceSpec);
		if (audioStream == System.IntPtr.Zero)
		{
			Console.WriteLine($"Failed to create audio stream: {SDL.GetError()}");
			SDL.CloseAudioDevice(audioDevice);
			SDL.Quit();
			return;
		}
		// Bind audio stream to SDL3 audio device
		GD.Print("Device ID: " + deviceId);
		GD.Print("Device ID: " + audioDevice);
		Task.Delay(1000).Wait();
		var streams = new[] { audioStream };
		bool bindResult = SDL.BindAudioStreams(audioDevice, streams, streams.Length);
		if (bindResult == false)
		{
			Console.WriteLine($"Failed to bind audio stream: {SDL.GetError()}");
			SDL.DestroyAudioStream(audioStream);
			SDL.CloseAudioDevice(audioDevice);
			SDL.Quit();
			return;
		}
		Console.WriteLine("Audio stream bound successfully.");

		//mediaPlayer.SetAudioFormat("S16N", 44100, 2);
		mediaPlayer.SetAudioFormatCallback(AudioSetup, null);
		mediaPlayer.SetAudioCallbacks(AudioPlay, null, null, null, null);

		GD.Print("hihihihihi");
		// Load media
		using var testMedia = new Media(_libVLC, cue.FilePath);
		mediaPlayer.Media = testMedia;

		// Start playback
		if (!mediaPlayer.Play())
		{
			Console.WriteLine("Failed to start VLC media playback.");
			Cleanup();
			return;
		}


		GD.Print("llalalala");

		SDL.ResumeAudioDevice(audioDevice);

		GD.Print("vivivivivivivivi");

	}
	int AudioSetup(ref IntPtr opaque, ref nint format, ref uint rate, ref uint channels)
	{
		GD.Print("In the audio setup");
		GD.Print( FourCCToInt("S16N"));
		//format = FourCCToInt("S16N");
		rate = 44100; // 44.1 kHz
		channels = 2; // Stereo
		//bytesPerSampleFrame = 2 * channels; // 2 bytes per sample * number of channels
		opaque = 0;
		return 0;
	}
	
	static nint FourCCToInt(string fourcc)
	{
		if (fourcc.Length != 4)
			throw new ArgumentException("FourCC code must be 4 characters");
		byte[] bytes = System.Text.Encoding.ASCII.GetBytes(fourcc);
		return (nint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24));
	}
	static void Cleanup()
	{
		GD.Print("ohhh no");
		mediaPlayer?.Stop();
		mediaPlayer?.Dispose();
		if (audioStream != null)
		{
			SDL.DestroyAudioStream(audioStream);
		}
		if (audioDevice != 0)
		{
			SDL.CloseAudioDevice(audioDevice);
		}
		SDL.Quit();
	}
	
	static void AudioPlay(nint opaque, nint samples, uint count, long pts)
	{
		GD.Print("Made it here?");
		GD.Print("Count: " + count);
		GD.Print("Samples: " + samples);
		byte[] tempBuffer = new byte[count];
		Marshal.Copy(samples, tempBuffer, 0, (int)count);
		audioBuffer.AddRange(tempBuffer);
		Console.WriteLine($"AudioPlay: received {count} bytes");
		GD.Print("");
		SubmitAudioBuffer();
	}
	static void SubmitAudioBuffer()
	{
		// Calculate how many complete sample frames we have
		int completeFrameBytes = (audioBuffer.Count / bytesPerSampleFrame) * bytesPerSampleFrame;
		if (completeFrameBytes == 0)
		{
			GD.Print("Not enough data for a comnplete frame");
			return; // Not enough data for a complete frame yet
		}

		// Copy complete frames to a new array
		byte[] frameData = audioBuffer.GetRange(0, completeFrameBytes).ToArray();
		bool result = SDL.PutAudioStreamData(audioStream, frameData, frameData.Length);
		if (result == false)
		{
			Console.WriteLine($"Failed to put audio stream data: {SDL.GetError()}");
		}
		else
		{
			Console.WriteLine($"Submitted {frameData.Length} bytes to SDL audio stream");
		}

		// Remove the submitted data from the buffer
		audioBuffer.RemoveRange(0, completeFrameBytes);
	}	

	
	/*try
	{
		var mediaPlayer = new MediaPlayer(_libVLC);
		var media = new Media(_libVLC, cue.FilePath);
		await media.Parse(); // MediaParseOptions.ParseLocal - this will need to change when refencing online URLS
		while (media.IsParsed != true)
		{
		}

		GD.Print("Media loaded");

		if (!SetupSDLAudioDevices(out LeftDeviceId, out RightDeviceId))
		{
			Console.WriteLine("Failed to set up SDL audio devices.");
			return;
		}

		mediaPlayer.SetAudioFormat("S16N", 44100, 2);
		mediaPlayer.SetAudioCallbacks(
			playCb: (nint data, nint samples, uint count, long pts) =>
			{
				ProcessAudioSamples(samples, count);
			},
			pauseCb: null,
			resumeCb: null,
			flushCb: null,
			drainCb: null);

	}
	catch (Exception e)
	{
		GD.Print($"Error: {e.Message}");
		_globalSignals.EmitSignal(nameof(GlobalSignals.Log), e.Message, 1);
	}
	finally
	{

	}
}



private static bool SetupSDLAudioDevices(out uint LeftDeviceId, out uint RightDeviceId)
{
	LeftDeviceId = Convert.ToUInt32(0);
	RightDeviceId = Convert.ToUInt32(0);

	// Get available playback devices
	var devices = SDL.GetAudioPlaybackDevices(out int deviceCount);//SDL_GetNumAudioDevices(SDL_FALSE);
	if (devices == null) return false;
	if (deviceCount < 2)
	{
		Console.WriteLine("Need at least two audio devices.");
		return false;
	}

	// Open first device for left channel
	SDL.AudioSpec desiredSpec = new SDL.AudioSpec()
	{
		Freq = 44100,
		Format = SDL.AudioFormat.AudioS16LE,
		Channels = 1, // Mono for single channel
	};


	// First device setup
	Console.WriteLine($"Opening device 0: {SDL.GetAudioDeviceName(devices[0])} for left channel");
	LeftDeviceId = SDL.OpenAudioDevice(devices[0], in desiredSpec);
	if (LeftDeviceId == UIntPtr.Zero)
	{
		Console.WriteLine($"Failed to open left audio device: {SDL.GetError()}");
		return false;
	}

	SDL.GetAudioDeviceFormat(LeftDeviceId, out SDL.AudioSpec obtainedSpec, out int sampleFrames);
	LeftAudioStream = SDL.CreateAudioStream(in desiredSpec, in obtainedSpec);
	if (LeftAudioStream == nint.Zero)
	{
		Console.WriteLine($"Failed to create left audio stream: {SDL.GetError()}");
		SDL.CloseAudioDevice(LeftDeviceId);
		return false;
	}
	if (SDL.BindAudioStream(LeftDeviceId, LeftAudioStream) != false)
	{
		Console.WriteLine($"Failed to bind left audio stream: {SDL.GetError()}");
		SDL.DestroyAudioStream(LeftAudioStream);
		SDL.CloseAudioDevice(LeftDeviceId);
		return false;
	}

	SDL.ResumeAudioDevice(LeftDeviceId); // Unpause
	GD.Print("MADE IT HERE");

	// Open second device for right channel
	Console.WriteLine($"Opening device 1: {SDL.GetAudioDeviceName(devices[1])} for right channel");
	RightDeviceId = SDL.OpenAudioDevice(devices[1], in desiredSpec);
	if (RightDeviceId == 0)
	{
		Console.WriteLine($"Failed to open right audio device: {SDL.GetError()}");
		SDL.CloseAudioDevice(LeftDeviceId);
		return false;
	}
	SDL.ResumeAudioDevice(RightDeviceId); // Unpause

	return true;
}

private static unsafe void ProcessAudioSamples(nint samples, uint count)
{
	int sampleCount = (int)count / 4;
	short[] leftSamples = new short[sampleCount];
	short[] rightSamples = new short[sampleCount];

	short* samplePtr = (short*)samples;
	for (int i = 0; i < sampleCount; i++)
	{
		leftSamples[i] = (short)(samplePtr[i * 2] * LeftVolume);     // Left channel
		rightSamples[i] = (short)(samplePtr[i * 2 + 1] * RightVolume); // Right channel
	}

	lock (AudioLock)
	{
	}
}*/
	
	
	//End test SDL implementation

	private static void MediaOnEndReached(object sender, EventArgs e)
	{
		foreach (var m in MediaPlayers)
		{
			if (m.Value.MediaPlayer.State == VLCState.Ended)
			{
				Task.Delay(1).ContinueWith(_ => StopMediaImmediately(m.Key));
				return;
			}
		}
		// In future need to check loop, note cant set time unless media is playing
		// pseudo function:
		/* loop?
		 mediaplayer.media = media
		 mediaplayer.play()
		 await .isplaying()
		 .setTime(long time of restart)
		 */
	}
	
    public static void StopMedia(int id)
    {
	    // Validate playback ID
	    if (!MediaPlayers.TryGetValue(id, out var state)) return;
	    
	    // Checks if fade is already in progress
	    if (state.IsFading)
	    {
		    StopMediaImmediately(id);
		    return;
	    }
	    
	    state.IsFading = true; // True if fading in progress
	    state.CurrentVolume = state.MediaPlayer.Volume;
	    var time = Convert.ToInt32(GlobalData.StopFadeTime * 10); // Stop fade time in second, to ms incremented by 100 (hence x10 (x1000/100))
	    state.FadeOutTimer = new Timer(time);
	    state.FadeOutTimer.Elapsed += (_, _) =>
	    {
		    var shouldStop = true;

		    if (state.HasAudio && state.CurrentVolume > 0)
		    {
			    state.CurrentVolume -= 1;
			    if (state.CurrentVolume < 0) state.CurrentVolume = 0;
			    state.MediaPlayer.Volume = state.CurrentVolume;
			    shouldStop = false;
		    }

		    if (state.HasVideo && state.CurrentAlpha > 0)
		    {
			    state.CurrentAlpha -= 4;
			    if (state.CurrentAlpha < 0) state.CurrentAlpha = 0;
			    state.TargetTextureRect.Set("VideoAlpha", state.CurrentAlpha);
			    shouldStop = false;
		    }

		    if (shouldStop)
		    {
			    StopMediaImmediately(id);
		    }
	    };
	    state.FadeOutTimer.Start();
	    
    }

    private static void StopMediaImmediately(int id)
    {
	    if (!MediaPlayers.TryGetValue(id, out var player)) return;
	    GD.Print("Stopped: " + id + " : " + player.MediaPlayer.Title);
	    ActiveCueContainer.RemoveActiveCue(id);
	    

	    player.FadeOutTimer?.Stop();
	    player.FadeOutTimer?.Dispose();
	    player.MediaPlayer.Stop();
	    Task.Delay(10);
	    player.MediaPlayer.Dispose();
	    if (player.TargetTextureRect != null)
	    {
		    player.TargetTextureRect.GetParent().CallDeferred("remove_child", player.TargetTextureRect);
			player.TargetTextureRect.QueueFree();
	    }

	    MediaPlayers.Remove(id);  // Remove from dictionary
    }



    public static void Pause(int id)
    {
	    MediaPlayers[id].MediaPlayer.Pause();
    }


    private static (bool hasVideo, bool hasAudio) GetMediaType(Media media)
    {
	    var hasVideo = false;
	    var hasAudio = false;
	    
	    media.Parse();
	    while (media.IsParsed != true) { }
	    
	    foreach (var track in media.Tracks)
	    {
		    if (track.TrackType == TrackType.Video)
			    hasVideo = true;
		    if (track.TrackType == TrackType.Audio)
			    hasAudio = true;
	    }
	    return (hasVideo, hasAudio);
    }
    
    public static long GetMediaLength(int id)
    {
	    return MediaPlayers[id].MediaPlayer.Length;
    }
    
    public static float GetMediaPosition(int id)
    {
	    return MediaPlayers[id].MediaPlayer.Position;
    }
    
    public static void SetMediaPosition(int id, float pos)
    {
	    
	    MediaPlayers[id].MediaPlayer.Position = pos;
    }
    
    public MediaPlayerState GetMediaPlayerState(int id)
	{
		return MediaPlayers[id];
	}
    
    private void StopAll()
	{
		foreach (var player in MediaPlayers)
		{
			StopMedia(player.Key);
		}
	}

	public AudioOutputDevice[] GetAvailibleAudioDevices()
	{
		var mediaplayer = new MediaPlayer(_libVLC);
		var devices = mediaplayer.AudioOutputDeviceEnum;
		mediaplayer.Dispose();
		
		return devices;
	}
    
	private TextureRect CreateVideoTextureRect()
	{
		// Get target window and scene for TextureRect
		var canvasScene = _globalData.VideoCanvas;
		var canvasLayer = canvasScene.GetNode<Node>("Layer1");
		
		// Create TextureRect
		var textureRect = new TextureRect();
		canvasLayer.AddChild(textureRect);
		
		// Apply settings to TextureRect
		textureRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		textureRect.StretchMode = TextureRect.StretchModeEnum.KeepAspect;
		textureRect.LayoutMode = 1; // Anchors
		textureRect.AnchorsPreset = 15; // Full Rect
		textureRect.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		textureRect.Material = GD.Load<ShaderMaterial>("res://src/Base/VideoTextureRectMaterial.tres");
		var oldPath = textureRect.GetPath();
		textureRect.SetScript(GD.Load<Script>("res://src/Base/VideoToTextureRect.cs"));
		textureRect = canvasScene.GetNode<TextureRect>(oldPath);
		textureRect.CallDeferred("Initialize");

		return textureRect;
	}
    
    private void WindowOnCloseRequested()
	{
		GD.Print("Window closing");
		foreach (var player in MediaPlayers)
		{
			if (player.Value.MediaPlayer.State == VLCState.Playing)
			{
				StopMediaImmediately(player.Key);
			}
		}
		_libVLC.Dispose();
		
		// Thought on memory issues when quit while media playing - 
		// This is all being disposed fine, however I think other scripts are sneaking in a final reference when these are disposed. 
		// Might need to make a shutdown state
	}
	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest)
		{
			WindowOnCloseRequested();
		}
	}
	

}




