using Godot;
using Godot.NativeInterop;
using System;
using System.Collections;
using System.ComponentModel;
using System.Linq;
using Cue2.Base.Classes;
using Cue2.Base.CommandInterpreter;
using Cue2.Shared;
using LibVLCSharp.Shared;

// This script handles:
// -Activation of cues
// -Main window UI handling
//

namespace Cue2.Base;

public partial class Cue2Base : Control
{
	private GlobalSignals _globalSignals;
	private GlobalData _globalData;
	private Connections _connections;

	private Node _settingsWindow;
	
	//private Window _uiWindow;
	private Window VideoWindow;
	private int _playbackIndex;
	
	public WorkspaceStates State { get; set; }

	//public GlobalMediaPlayerManager mediaManager;

	public override void _Ready()
	{
		//Connect global signals
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_globalSignals.CloseSettingsWindow += close_settings_window;
		_globalData = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData");
		_connections = GetNode<Connections>("/root/Connections");

		// Creation and assignment of test video output window - in future this will be created in settings
		/*VideoWindow = new Window();
		AddChild(VideoWindow);
		VideoWindow.Name = "Test Video Output";
		_globalData.VideoOutputWinNum = VideoWindow.GetWindowId();
		DisplayServer.WindowSetCurrentScreen(1, _globalData.VideoOutputWinNum);
		DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen, _globalData.VideoOutputWinNum);*/
		
		// Load in a test canvas - In future this will be created in settings
		/*var videoCanvas = GD.Load<PackedScene>("res://src/Base/VideoCanvas.tscn").Instantiate();
		VideoWindow.AddChild(videoCanvas);
		_globalData.VideoCanvas = videoCanvas;
		_globalData.VideoWindow = VideoWindow;*/
		
	}


	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
	
	
	private void _on_settings_toggled(Boolean @toggle){
		if (@toggle == true){
			if (_settingsWindow == null)
			{
				var settings = GD.Load<PackedScene>("res://src/Base/settings.tscn");
				_settingsWindow = settings.Instantiate();
				AddChild(_settingsWindow);
			}
			else {
				_settingsWindow.GetWindow().Show();
			}
			
		}
		if (@toggle == false){
			_settingsWindow.GetWindow().Hide();
		}
	}
	private void close_settings_window(){ //From global signal, emitted by close button of settings window.
		GetNode<Button>("MarginContainer/BoxContainer/HeaderContainer/Settings").ButtonPressed = false;
		
	}

	private void _on_go_pressed()
	{
		Go();
	}
	
	private void _on_stop_pressed()
	{
		_globalSignals.EmitSignal(nameof(GlobalSignals.StopAll));
	}
	

	private void Go()
	{
		// In Future this should only pass the selected cue's command to interpreter which will instruct relevant workers what to do
		
		// Check for next cue
		foreach (var cue1 in _globalData.ShellSelection.SelectedShells)
		{
			var cue = (Cue)cue1;
			_globalData.CueCommandInterpreter.CueCommandExectutor.ExecuteCommand(cue);
		}
	}
	
}
