using Godot;
using Godot.NativeInterop;
using System;
using System.Collections;
using System.ComponentModel;
using System.Linq;
using Cue2.Base.Classes;
using Cue2.Base.CommandInterpreter;
using Cue2.Shared;
using Cue2.UI.Utilities;
using LibVLCSharp.Shared;
// DOES THIS UPDATE?
// This script handles:
// -Activation of cues
// -Main window UI handling
//

namespace Cue2.Base;

public partial class Cue2Base : Control
{
	
	private GlobalSignals _globalSignals;
	private GlobalData _globalData;

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

		_globalSignals.UiScaleChanged += ScaleUI;
		
		GD.Print("Main Window ID is: " + GetWindow().GetWindowId());

		UiUtilities.RescaleWindow(GetWindow(), _globalData.BaseDisplayScale);
		UiUtilities.RescaleUi(GetWindow(), _globalData.Settings.UiScale, _globalData.BaseDisplayScale);
		var uiScale = _globalData.BaseDisplayScale;

		var windowDimensions = GetWindow().Size;
	}

	private void ScaleUI(float uiScale)
	{
		UiUtilities.RescaleUi(GetWindow(), _globalData.Settings.UiScale, _globalData.BaseDisplayScale);
	}

	public override void _ExitTree()
	{
		_globalSignals.UiScaleChanged -= ScaleUI;
	}
	

	
}
