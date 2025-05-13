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
	

	
}
