using System;
using System.Collections.Generic;
using Godot;
using LibVLCSharp.Shared;

namespace Cue2.Base.Classes;

public class Playback : LibVLC
{
	private readonly Dictionary<int, MediaPlayer> _mediaPlayers;
	private int _mediaPlayerId;
    public Playback()
    {
		_mediaPlayers = new Dictionary<int, MediaPlayer>();
		_mediaPlayerId = 0;

    }
    
    public void PlayAudio(int id, string mediaPath)
    {
	    var mediaPlayer = new MediaPlayer(this);
	    var media = new Media(this, mediaPath);
	    mediaPlayer.Media = media;
	    mediaPlayer.Play();
	    
		_mediaPlayers.Add(id, mediaPlayer);
    }
    
    public void PlayVideo(int id, string mediaPath, int windowId)
    {
	    var mediaPlayer = new MediaPlayer(this);
	    var media = new Media(this, mediaPath);
	    mediaPlayer.Media = media;

	    var windowHandle = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, windowId);
	    mediaPlayer.Hwnd = windowHandle;
	    mediaPlayer.Play();
	    _mediaPlayers.Add(id, mediaPlayer);
    }
    
    public void StopMedia(int id)
    {
	    if (_mediaPlayers.TryGetValue(id, out var mediaPlayer))
	    {
		    mediaPlayer.Stop();
		    mediaPlayer.Dispose();
		    _mediaPlayers.Remove(id);
	    }
    }
    
    public float GetProgress(int id)
    {
	    if (_mediaPlayers.TryGetValue(id, out var mediaPlayer))
	    {
		    var totalTime = mediaPlayer.Length;
		    var currentTime = mediaPlayer.Time;
		    if (currentTime == (long)0)
		    {
			    return (float)0.0;
		    }
		    float progress = ((float)currentTime / (float)totalTime) * 100;
		    return (float)progress;
	    }
	    return (float)0.0;
    }
    public bool SetProgress(int id, float value)
    {
	    if (_mediaPlayers.TryGetValue(id, out var mediaPlayer))
	    {
		    var totalTime = mediaPlayer.Length;
		    var currentTime = mediaPlayer.Time;
		    if (value == 0)
		    {
			    mediaPlayer.Time = 0;
			    return true;
		    }
		    float progress = ((float)totalTime / 100) * value;
		    mediaPlayer.Time = (long)progress;
		    return true;
	    }
	    return false;
    }
    
    
}

/*// Media manager
public class GlobalMediaPlayerManager
{
	private Dictionary<int, MediaPlayer> _mediaPlayers = new Dictionary<int, MediaPlayer>();

	public void Initialize()
	{
		Core.Initialize();
	}



	


	public void PauseMedia(int id)
	{
		if (_mediaPlayers.TryGetValue(id, out var mediaPlayer))
		{
			mediaPlayer.Pause();
		}
	}

	public void ResumeMedia(int id)
	{
		if (_mediaPlayers.TryGetValue(id, out var mediaPlayer))
		{
			mediaPlayer.Play();
		}
	}
	*/



