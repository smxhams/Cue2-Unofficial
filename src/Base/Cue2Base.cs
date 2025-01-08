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

public partial class Cue2Base : Control
{
	private GlobalSignals _globalSignals;
	public Cue2.Shared.GlobalData Gd;
	private Connections _connections;

	private Node _setWin;

	private Window _newWindow;
	private Window _uiWindow;

	//public GlobalMediaPlayerManager mediaManager;

	public override void _Ready()
	{
		//Connect global signals
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_globalSignals.CloseSettingsWindow += close_settings_window;
		_globalSignals.ShellSelected += shell_selected;
		Gd = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData");
		_connections = GetNode<Connections>("/root/Connections");

		// Test video output window
		_newWindow = new Window();
		AddChild(_newWindow);
		_newWindow.Name = "Test Video Output";
		Gd.VideoOutputWinNum = _newWindow.GetWindowId();
		DisplayServer.WindowSetCurrentScreen(1, Gd.VideoOutputWinNum);
		DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen, Gd.VideoOutputWinNum);	

		// Test UI overlay
		// I reckon in future video outputs set else where, ui should be a viewport set up as .tscn and loaded into window above video
		_uiWindow = new Window();
		AddChild(_uiWindow);
		_uiWindow.Name = "Top Layer";
		Gd.UiOutputWinNum = _uiWindow.GetWindowId();
		_uiWindow.AlwaysOnTop = true;
		DisplayServer.WindowSetCurrentScreen(1, Gd.UiOutputWinNum);
		DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen, Gd.UiOutputWinNum);	
		Label testLabel = new Label();
		_uiWindow.AddChild(testLabel);
		testLabel.Text = "AHHHHHH";


		//Set both transparents to true for invisible window
		_uiWindow.Transparent = true;
		_uiWindow.TransparentBg = true;



	}


	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void _OnTreeExiting()
	{
		GD.Print("OnTreeExiting");
		PrintOrphanNodes();
	}
	private void _on_settings_toggled(Boolean @toggle){
		if (@toggle == true){
			if (_setWin == null){
				var settings = GD.Load<PackedScene>("res://src/Base/settings.tscn");
				_setWin = settings.Instantiate();
				AddChild(_setWin);
				}
			else {
				_setWin.GetWindow().Show();
			}
			
		}
		if (@toggle == false){
			_setWin.GetWindow().Hide();
		}
	}
	private void close_settings_window(){ //From global signal, emitted by close button of settings window.
		GetNode<Button>("MarginContainer/BoxContainer/HeaderContainer/Settings").ButtonPressed = false;
		
	}
	private void shell_selected(int cueId)
	{ //From global signal, emitted by shell_bar
		GD.Print("Shell Selected: " + cueId);
		
	}

	private void _on_go_pressed()
	{
		Go();
	}

	private void _on_stop_pressed()
	{
		Stop();
	}

	public void Go()
	{
		// Check for next cue
		if (Gd.NextCue != -1)
		{
			var cueNumToGo = Gd.NextCue;
			var shellData = (Hashtable)Gd.Cuelist[cueNumToGo];
			var cueType = shellData["type"];

			// Check cue type to determine how to play
			if ((string)cueType == "")
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.ErrorLog), "Nothing in the Cue.", 1);
			}

			// Play audio file
			else if ((string)cueType == "Audio")
			{
				var path = (string)shellData["filepath"];
				Gd.MediaManager.PlayAudio(cueNumToGo, path);
				Gd.LiveCues.Add(cueNumToGo);
			}

			// Play video
			else if ((string)cueType == "Video")
			{
				var path = (string)shellData["filepath"];
				Gd.MediaManager.PlayVideo(cueNumToGo, path, Gd.VideoOutputWinNum);
				Gd.LiveCues.Add(cueNumToGo);
				_globalSignals.EmitSignal(nameof(GlobalSignals.CueGo), cueNumToGo);
				Label testLabel2 = new Label();
				testLabel2.Text = "AHHHHHH2222";
				_newWindow.AddChild(testLabel2);
			}

			foreach (var item in Gd.LiveCues)
			{
				GD.Print(item);
			}

			

		}
		else {_globalSignals.EmitSignal(nameof(GlobalSignals.ErrorLog), "Couldn't find a Cue to GO");}

	}


	public void Stop()
	{
		foreach (int cue in Gd.LiveCues)
		{
			GD.Print("Cue num stopping: " + cue);
			Gd.MediaManager.StopMedia(cue);
			
		}
		Gd.LiveCues.Clear();
	}
}
