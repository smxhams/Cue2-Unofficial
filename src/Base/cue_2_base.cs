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

	private Window newWindow;
	private Window uiWindow;

	//public GlobalMediaPlayerManager mediaManager;

	public override void _Ready()
	{
		//Connect global signals
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_globalSignals.CloseSettingsWindow += close_settings_window;
		_globalSignals.ShellSelected += shell_selected;
		_gd = GetNode<GlobalData>("/root/GlobalData");
		_connections = GetNode<Connections>("/root/Connections");

		// Test video output window
		newWindow = new Window();
		AddChild(newWindow);
		newWindow.Name = "Test Video Output";
		_gd.videoOutputWinNum = newWindow.GetWindowId();
		DisplayServer.WindowSetCurrentScreen(1, _gd.videoOutputWinNum);
		DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen, _gd.videoOutputWinNum);	

		// Test UI overlay
		// I reckon in future video outputs set else where, ui should be a viewport set up as .tscn and loaded into window above video
		uiWindow = new Window();
		AddChild(uiWindow);
		uiWindow.Name = "Top Layer";
		_gd.uiOutputWinNum = uiWindow.GetWindowId();
		uiWindow.AlwaysOnTop = true;
		DisplayServer.WindowSetCurrentScreen(1, _gd.uiOutputWinNum);
		DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen, _gd.uiOutputWinNum);	
		Label testLabel = new Label();
		uiWindow.AddChild(testLabel);
		testLabel.Text = "AHHHHHH";


		//Set both transparents to true for invisible window
		uiWindow.Transparent = true;
		uiWindow.TransparentBg = true;



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
				_gd.mediaManager.PlayAudio(cueNumToGo, path);
				_gd.liveCues.Add(cueNumToGo);
			}

			// Play video
			else if ((string)cueType == "Video")
			{
				var path = (string)shellData["filepath"];
				_gd.mediaManager.PlayVideo(cueNumToGo, path, _gd.videoOutputWinNum);
				_gd.liveCues.Add(cueNumToGo);
				_globalSignals.EmitSignal(nameof(GlobalSignals.CueGo), cueNumToGo);
				Label testLabel2 = new Label();
				testLabel2.Text = "AHHHHHH2222";
				newWindow.AddChild(testLabel2);
			}

			foreach (var item in _gd.liveCues)
			{
				GD.Print(item);
			}

			

		}
		else {_globalSignals.EmitSignal(nameof(GlobalSignals.ErrorLog), "Couldn't find a Cue to GO");}

	}


	public void stop()
	{
		foreach (int cue in _gd.liveCues)
		{
			GD.Print("Cue num stopping: " + cue);
			_gd.mediaManager.StopMedia(cue);
			
		}
		_gd.liveCues.Clear();
	}
}
