using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cue2.Shared;
using Godot;
using LibVLCSharp.Shared;
using Timer = System.Timers.Timer;

namespace Cue2.Base.Classes;

public class Playback : LibVLC
{
	private static Dictionary<int, MediaPlayerState> _mediaPlayers = new Dictionary<int, MediaPlayerState>();
	
	public Playback()
	{
		Core.Initialize();
	}
	
	public void PlayMedia(int id, string mediaPath, Window window = null)
	{
		var mediaPlayer = new MediaPlayer(this);
		var media = new Media(this, mediaPath);
		media.Parse(MediaParseOptions.ParseLocal);
		while (media.IsParsed != true) { }
		
		mediaPlayer.Media = media;
		
		(bool hasVideo, bool hasAudio) = GetMediaType(media);

		if (hasVideo && window != null)
		{
			var windowHandle = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, window.GetWindowId());
			mediaPlayer.Hwnd = windowHandle;
			
		}
		mediaPlayer.Volume = 100;
		mediaPlayer.Play();
		_mediaPlayers.Add(id, new MediaPlayerState(mediaPlayer, hasVideo, hasAudio));
		
		mediaPlayer.EndReached += MediaOnEndReached;
		
		media.Dispose();
	}
	
	private void MediaOnEndReached(object? sender, EventArgs e)
	{
		foreach (var m in _mediaPlayers)
		{
			if (m.Value.MediaPlayer.State == VLCState.Ended)
			{
				Task.Delay(1).ContinueWith(action => StopMediaImmediately(m.Key));
				return;
			}
		}
		// In future need to check loop, note cant set time unless media is playing
		// pseudo function:
		/* loop?
		 mediaplayer.media = media // I don't know yet if need to reassing media
		 mediaplayer.play()
		 await .isplaying()
		 .setTime(long time of restart)
		 */
	}
	
    public static void StopMedia(int id)
    {
	    // Validate playback ID
	    if (!_mediaPlayers.TryGetValue(id, out var state)) return;
	    
	    // Checks if fade is already in progress
	    if (state.IsFading == true)
	    {
		    StopMediaImmediately(id);
		    return;
	    }
	    
	    state.IsFading = true; // True if fading in progress
	    state.CurrentVolume = state.MediaPlayer.Volume;
	    state.CurrentBrightness = state.MediaPlayer.AdjustFloat(VideoAdjustOption.Brightness);
	    var time = Convert.ToInt32(GlobalData.StopFadeTime * 10); // Stop fade time in second, to ms incremented by 100 (hence x10 (x1000/100))
	    state.FadeOutTimer = new Timer(time);
	    state.FadeOutTimer.Elapsed += (sender, e) =>
	    {
		    bool shouldStop = true;

		    if (state.HasAudio && state.CurrentVolume > 0)
		    {
			    state.CurrentVolume -= 1;
			    if (state.CurrentVolume < 0) state.CurrentVolume = 0;
			    state.MediaPlayer.Volume = state.CurrentVolume;
			    shouldStop = false;
		    }

		    if (state.HasVideo && state.CurrentBrightness > 0)
		    {
			    state.MediaPlayer.SetAdjustFloat(VideoAdjustOption.Enable, 1);
			    state.CurrentBrightness -= 0.01f;
			    if (state.CurrentBrightness < 0) state.CurrentBrightness = 0;
			    state.MediaPlayer.SetAdjustFloat(VideoAdjustOption.Brightness, state.CurrentBrightness);
			    
			    // TODO Fade to black
			    
			    /*//
			    state.MediaPlayer.SetLogoInt(VideoLogoOption.Enable, 1);
			    //state.MediaPlayer.SetLogoString(VideoLogoOption.File, "C:\\MyFiles\\Cue2_Home\\Cue2\\src\\UI\\BlackSquare.jpg");
			    state.MediaPlayer.SetLogoInt(VideoLogoOption.X, 0);
			    state.MediaPlayer.SetLogoInt(VideoLogoOption.Y, 0);
			    //state.MediaPlayer.SetLogoInt(VideoLogoOption., 100);
			    state.MediaPlayer.SetLogoInt(VideoLogoOption.Repeat, 1);
			    state.MediaPlayer.SetLogoInt(VideoLogoOption.Opacity, Convert.ToInt32(state.CurrentBrightness * 255));*/
			    
		    }

		    if (shouldStop)
		    {
			    StopMediaImmediately(id);
		    }
	    };
	    state.FadeOutTimer.Start();
	    
    }

    public static void StopMediaImmediately(int id)
    {
	    if (!_mediaPlayers.TryGetValue(id, out var player)) return;
	    GD.Print("Stopped: " + id + " : " + player.MediaPlayer.Title);
	    ActiveCuelist.RemoveActiveCue(id);
	    

	    player.FadeOutTimer?.Stop();
	    player.FadeOutTimer?.Dispose();  // Dispose the timer properly
	    player.MediaPlayer.SetAdjustFloat(VideoAdjustOption.Enable, 0);
	    player.MediaPlayer.Stop();
	    Task.Delay(10);
	    //player.MediaPlayer.Dispose(); // Free the MediaPlayer safely
	    player.MediaPlayer.Dispose();
	    _mediaPlayers.Remove(id);  // Remove from dictionary
    }



    public static void Pause(int id)
    {
	    _mediaPlayers[id].MediaPlayer.Pause();
    }

    
    static (bool hasVideo, bool hasAudio) GetMediaType(Media media)
    {
	    bool hasVideo = false;
	    bool hasAudio = false;
	    
	    media.Parse(MediaParseOptions.ParseLocal);
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
	    return _mediaPlayers[id].MediaPlayer.Length;
    }
    
    public static float GetMediaPosition(int id)
    {
	    return _mediaPlayers[id].MediaPlayer.Position;
    }
    
    public static void SetMediaPosition(int id, float pos)
    {
	    
	    _mediaPlayers[id].MediaPlayer.Position = pos;
    }

}

class MediaPlayerState
{
	public MediaPlayer MediaPlayer { get; }
	public Timer FadeOutTimer { get; set; }
	public int CurrentVolume { get; set; } = 100;
	public float CurrentBrightness { get; set; } = 1.0f;
	public bool HasVideo { get; }
	public bool HasAudio { get; }
	public bool IsFading { get; set; } = false;

	public MediaPlayerState(MediaPlayer mediaPlayer, bool hasVideo, bool hasAudio)
	{
		MediaPlayer = mediaPlayer;
		HasVideo = hasVideo;
		HasAudio = hasAudio;
	}
}


