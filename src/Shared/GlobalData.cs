using System;
using System.Collections;
using System.Collections.Generic;
using Godot;
using LibVLCSharp.Shared;

namespace Cue2.Shared;
// This script manages global data it contains:
// -Data management functions
// -Manages saving and loading of shows
// -Global media manager

public partial class GlobalData : Node
{
	private GlobalSignals _globalSignals;
	private SaveManager _saveManager;

	public Hashtable Cuelist = new Hashtable();
	public int FocusedCue = -1;
	public Dictionary<int, Node> CueShellObj = new Dictionary<int, Node>();
	public ArrayList CueIndex = new ArrayList(); // [CueID, Cue Object]
	public int CueCount;
	public int CueTotal;
	public int CueOrder;
	public int NextCue = -1;

	public int VideoOutputWinNum;
	public int UiOutputWinNum;

	//Create a referencable global class for all media
	public GlobalMediaPlayerManager MediaManager = new GlobalMediaPlayerManager();

	public List<int> LiveCues = new List<int>();

	// Settings
	public bool SelectedIsNext = true; // Whether selecting a cue makes in next to be manualy go'd.
	public bool AutoloadOnStartup = true; // Loads last active show on startup
	public string ActiveShowFile; // URL of current showfile to save to
	public string ShowName;
	public string ShowPath;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// Init MediaManager class so can be referenced everywhere
		MediaManager.Initialize();
		//if (autoloadOnStartup == true){loadShow("Last");}

		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_globalSignals.Save += SaveShow;
		_saveManager = GetNode<SaveManager>("/root/SaveManager");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void SaveShow()
	{
		// First check if this is a first time save
		if (ShowName == null)
		{
			GetNode<FileDialog>("/root/Cue2_Base/SaveDialog").Visible = true;
			_globalSignals.EmitSignal(nameof(GlobalSignals.ErrorLog), "Waiting on save directory and show name to continue save", 0);
			
		}
		else {_saveManager.SaveShow(ShowPath, ShowName);}
	}
	

	public int GetCueCount()
	{
		return CueCount;
	}
}


// Functions



// Media manager
public class GlobalMediaPlayerManager
{
	private Dictionary<int, MediaPlayer> _mediaPlayers = new Dictionary<int, MediaPlayer>();

	public void Initialize()
	{
		Core.Initialize();
	}

	public void PlayAudio(int id, string mediaPath)
	{
		var libVlc = new LibVLC();
		var mediaPlayer = new MediaPlayer(libVlc);
		var media = new Media(libVlc, mediaPath);
		mediaPlayer.Media = media;
		mediaPlayer.Play();

		_mediaPlayers[id] = mediaPlayer;
	}

	public void PlayVideo(int id, string mediaPath, int windowId)
	{
		var libVlc = new LibVLC();
		var mediaPlayer = new MediaPlayer(libVlc);
		var media = new Media(libVlc, mediaPath);
		mediaPlayer.Media = media;

		var windowHandle = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, windowId);
		mediaPlayer.Hwnd = windowHandle;
		mediaPlayer.Play();
		_mediaPlayers[id] = mediaPlayer;
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