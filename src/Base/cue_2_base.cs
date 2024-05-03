using Godot;
using Godot.NativeInterop;
using System;
using System.Collections;
using System.ComponentModel;
using System.Linq;
using LibVLCSharp.Shared;

// This script handles:
// -Activation of cues
// -Main window UI handling
// 


public partial class cue_2_base : Control
{
	private GlobalSignals _globalSignals;
	public GlobalData _gd;
	private Connections _connections;

	private Node setWin;


	private Media _media;
	private LibVLC libVLC;
	private MediaPlayer mediaPlayer;
	private IntPtr windowHandle;
	private Window newWindow;

	public GlobalMediaPlayerManager mediaManager;


	private AudioStreamPlayer streamPlayer = new AudioStreamPlayer();


	public override void _Ready()
	{
		//Connect global signals
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_globalSignals.CloseSettingsWindow += close_settings_window;
		_globalSignals.ShellSelected += shell_selected;
		_gd = GetNode<GlobalData>("/root/GlobalData");
		_connections = GetNode<Connections>("/root/Connections");

		//mediaManager = new GlobalMediaPlayerManager();
		//mediaManager.Initialize();

	}



	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void _on_settings_toggled(Boolean @toggle){
		if (@toggle == true){
			if (setWin == null){
				var settings = GD.Load<PackedScene>("res://src/Base/settings.tscn");
				setWin = settings.Instantiate();
				AddChild(setWin);
				}
			else {
				setWin.GetWindow().Show();
			}
			
		}
		if (@toggle == false){
			setWin.GetWindow().Hide();
		}
	}
	private void close_settings_window(){ //From global signal, emitted by close button of settings window.
		GetNode<Button>("MarginContainer/BoxContainer/HeaderContainer/Settings").ButtonPressed = false;
		
	}
	private void shell_selected(int @cueID)
	{ //From global signal, emitted by shell_bar
		GD.Print("Shell Selected: " + @cueID);
		
	}

	private void _on_go_pressed()
	{
		go();
	}

	private void _on_stop_pressed()
	{
		stop();
	}

	public void go()
	{
		// Check for next cue
		if (_gd.nextCue != -1)
		{
			var cueNumToGo = _gd.nextCue;
			var shellData = (Hashtable)_gd.cuelist[cueNumToGo];
			var cueType = shellData["type"];

			// Check cue type to determine how to play
			if ((string)cueType == "")
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.ErrorLog), "Nothing in the Cue.");
			}

			// Play audio file
			else if ((string)cueType == "Audio")
			{
				var path = (string)shellData["filepath"];
				mediaManager = _gd.mediaManager;
				mediaManager.PlayMedia(cueNumToGo, path);
				_gd.liveCues.Add(cueNumToGo);
			}

			// Play video
			else if ((string)cueType == "Video")
			{
				var path = (string)shellData["filepath"];

				newWindow = new Window();
				AddChild(newWindow);
				newWindow.Name = "Video Player";
				newWindow.CloseRequested += NewWindowOnCloseRequested;
				newWindow.PopupCentered(new Vector2I(700, 500));
				windowHandle = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, 1);
				libVLC = new LibVLC();
				mediaPlayer = new MediaPlayer(libVLC);
				_media = new Media(libVLC, new Uri(path));
				mediaPlayer.Hwnd = windowHandle;
				mediaPlayer.Play(_media);


			}

			foreach (var item in _gd.liveCues)
			{
				GD.Print(item);
			}

			

		}
		else {_globalSignals.EmitSignal(nameof(GlobalSignals.ErrorLog), "Couldn't find a Cue to GO");}

		// Send cue to live
		GD.Print("GOOOOO");
	}


	public void stop()
	{
		foreach (int cue in _gd.liveCues)
		{
			GD.Print("Cue num stopping: " + cue);
			mediaManager = _gd.mediaManager;
			mediaManager.StopMedia(cue);
			
		}
		_gd.liveCues.Clear();
	}
	private void NewWindowOnCloseRequested()
	{
		// Clean up
		newWindow.QueueFree();
		mediaPlayer.Dispose();
		libVLC.Dispose();
	}

}
