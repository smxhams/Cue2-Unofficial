using Godot;
using LibVLCSharp.Shared;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// This script manages global data it contains:
// -Data management functions
// -Manages saving and loading of shows
// -Global media manager

public partial class GlobalData : Node
{
	private GlobalSignals _globalSignals;
	private SaveManager _saveManager;

	public Hashtable cuelist = new Hashtable();
	public int cueCount;
	public int cueTotal;
	public int cueOrder;
	public int nextCue = -1;

	public int videoOutputWinNum;
	public int uiOutputWinNum;

	//Create a referencable global class for all media
	public GlobalMediaPlayerManager mediaManager = new GlobalMediaPlayerManager();

	public List<int> liveCues = new List<int>();

	// Settings
	public bool selectedIsNext = true; // Whether selecting a cue makes in next to be manualy go'd.
	public bool autoloadOnStartup = true; // Loads last active show on startup
	public string activeShowFile; // URL of current showfile to save to
	public string showName;
	public string showPath;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// Init MediaManager class so can be referenced everywhere
		mediaManager.Initialize();
		//if (autoloadOnStartup == true){loadShow("Last");}

		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_globalSignals.Save += saveShow;
		_saveManager = GetNode<SaveManager>("/root/SaveManager");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void saveShow()
	{
		// First check if this is a first time save
		if (showName == null)
		{
			GetNode<FileDialog>("/root/Cue2_Base/SaveDialog").Visible = true;
			_globalSignals.EmitSignal(nameof(GlobalSignals.ErrorLog), "Waiting on save directory and show name to continue save", 0);
			
		}
		else {_saveManager.saveShow(showPath, showName);}
		
	}

}


//

// Media manager
public class GlobalMediaPlayerManager
{
	private Dictionary<int, MediaPlayer> mediaPlayers = new Dictionary<int, MediaPlayer>();

	public void Initialize()
	{
		Core.Initialize();
	}

	public void PlayAudio(int id, string mediaPath)
	{
		var libVLC = new LibVLC();
		var mediaPlayer = new MediaPlayer(libVLC);
		var media = new Media(libVLC, mediaPath);
		mediaPlayer.Media = media;
		mediaPlayer.Play();

		mediaPlayers[id] = mediaPlayer;
	}

	public void PlayVideo(int id, string mediaPath, int windowID)
	{
		var libVLC = new LibVLC();
		var mediaPlayer = new MediaPlayer(libVLC);
		var media = new Media(libVLC, mediaPath);
		mediaPlayer.Media = media;

		var windowHandle = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, windowID);
		mediaPlayer.Hwnd = windowHandle;
		mediaPlayer.Play();
		mediaPlayers[id] = mediaPlayer;
	}

	
    public void StopMedia(int id)
    {
        if (mediaPlayers.TryGetValue(id, out var mediaPlayer))
        {
            mediaPlayer.Stop();
			mediaPlayer.Dispose();
            mediaPlayers.Remove(id);
        }
    }

    public void PauseMedia(int id)
    {
        if (mediaPlayers.TryGetValue(id, out var mediaPlayer))
        {
            mediaPlayer.Pause();
        }
    }

    public void ResumeMedia(int id)
    {
        if (mediaPlayers.TryGetValue(id, out var mediaPlayer))
        {
            mediaPlayer.Play();
        }
    }

	public float GetProgress(int id)
    {
        if (mediaPlayers.TryGetValue(id, out var mediaPlayer))
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
        if (mediaPlayers.TryGetValue(id, out var mediaPlayer))
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