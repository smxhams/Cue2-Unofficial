using System;
using System.Collections.Generic;
using Godot;
using LibVLCSharp.Shared;

namespace Cue2.Base.Classes;

public class Playback : LibVLC
{
	private static Dictionary<int, MediaPlayer> _mediaPlayers = new Dictionary<int, MediaPlayer>();

	public void PlayAudio(int id, string mediaPath)
    {
	    var mediaPlayer = new MediaPlayer(this);
	    var media = new Media(this, mediaPath);
	    mediaPlayer.Media = media;
	    mediaPlayer.Play();
	    
		_mediaPlayers.Add(id, mediaPlayer);
    }
    
    public void PlayVideo(int id, string mediaPath, Window window)
    {
	    var mediaPlayer = new MediaPlayer(this);
	    var media = new Media(this, mediaPath);
	    mediaPlayer.Media = media;

	    var windowHandle = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, window.GetWindowId());
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

    public static long GetMediaLength(int id)
    {
	    return _mediaPlayers[id].Length;
    }
    

    public static float GetMediaPosition(int id)
    {
	    return _mediaPlayers[id].Position;
    }
    
    public static void SetMediaPosition(int id, float pos)
    {
	    
	    _mediaPlayers[id].Position = pos;
    }
    
    /*public float GetProgress(int id)
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
	    return false;*/
}
