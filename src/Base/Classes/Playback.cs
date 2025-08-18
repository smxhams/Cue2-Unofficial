using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Cue2.Shared;
using Godot;
using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;
using SDL3;
using Timer = System.Timers.Timer;

namespace Cue2.Base.Classes;

/// <summary>
///  !!!! THIS HAS BEEN MADE REDUNDANT AND TRANSITIONING TO MediaEngine.cs !!!!
/// </summary>




public partial class Playback : Node
{
	private static readonly Dictionary<int, MediaPlayerState> MediaPlayers = new Dictionary<int, MediaPlayerState>(); // (CueID, State)
	
	private readonly LibVLC _libVLC;

	private Window _window;
	private static  Window _canvasWindow;

	private GlobalData _globalData;
	private GlobalSignals _globalSignals;
	
	private int _playbackIndex = 0;
	
	private Dictionary<string, MediaPlayer> _activePlayers = new Dictionary<string, MediaPlayer>();
	
	/// <summary>
	/// This is 
	/// </summary>
	public Playback()
	{
		//Core.Initialize();
		//_libVLC = new LibVLC();
	}

	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_window = GetWindow();
		_window.CloseRequested += WindowOnCloseRequested;
		_canvasWindow = _globalData.VideoWindow;
		_globalSignals.StopAll += StopAll;
	}

	public async void PlayMedia(Cue cue, Window window = null)
	{
		//TODO: THIS RUNS SDL TEST, CURRENTLY WORKING ON FULL IMPLEMENTATION
		//var test = new SdlTest();
		//test.PlayAudio(cue.FilePath, _libVLC);
		return;
		
		/*if (!File.Exists(cue.FilePath))
		{
			GD.PrintErr($"Error: File not found at {cue.FilePath}");
			return;
		}
		
		
		GD.Print("PLAYING MEDIA HERE");
		var mediaPlayer = new MediaPlayer(_libVLC);
		var media = new Media(_libVLC, cue.FilePath);
		media.Parse().Wait(); 
		
		mediaPlayer.SetAudioFormat("f32le", 48000, 2);
		//mediaPlayer.SetAudioFormatCallback();
		mediaPlayer.SetAudioCallbacks(playAudio, null, null, null, null);
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
		media.Dispose();*/

	}
	/*static void playAudio(nint opaque, nint samples, uint count, long pts)
	{
		GD.Print("Count: " + count);
		GD.Print("Samples: " + samples);
		float[] tempBuffer = new float[count*2];
		Marshal.Copy(samples, tempBuffer, 0, (int)count);
		//_audioBuffer.AddRange(tempBuffer);
		Console.WriteLine($"AudioPlay: received {count} bytes");
	}*/
	

	
	//End test SDL implementation
/*public async void PlayMedia(Cue cue, Window window = null)
	{
		GD.Print("PLAYTING MEDIA HERE");
		var mediaPlayer = new MediaPlayer(_libVLC);
		var media = new Media(_libVLC, cue.FilePath);
		media.Parse().Wait(); // MediaParseOptions.ParseLocal - this will need to change when refencing online URLS
		GD.Print("Before");
		
		GD.Print("After");

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




