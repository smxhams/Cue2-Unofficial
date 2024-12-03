using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using LibVLCSharp.Shared;

// This script is attached to the cuelist in main UI

public partial class CueList : Control
{

	private GlobalData _globalData;
	private GlobalStyles _globalStyles;

	private Variant _cueCount;
	private MediaPlayer _mediaPlayer;

	private StyleBoxFlat _nextStyle = new StyleBoxFlat();


	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		_cueCount = _globalData.cueCount;

		_globalStyles = GetNode<GlobalStyles>("/root/GlobalStyles");
		_nextStyle = _globalStyles.nextStyle;
	}

	private void _on_add_shell_pressed()
	{
		CreateNewShell();
	}

	public override void _Process(double delta)
	{
	}

	private void CreateNewShell()
	{
		var shellBarScene = GD.Load<PackedScene>("res://src/Base/shell_bar.tscn");
		var shellBar = shellBarScene.Instantiate();
		var container = GetNode<VBoxContainer>("CueContainer");
		container.AddChild(shellBar);
		shellBar.GetChild(1).GetChild(0).GetChild<LineEdit>(2).Text = _globalData.cueCount.ToString();
		
		var newShell = new Hashtable()
		{
			{"id", _globalData.cueCount},
			{"name", (String)""},
			{"cueNum", (String)""},
			{"type", ""},
			//{"shellObj", shellBar},
			{"filepath", ""},
			{"player", null},
			{"media", null}
		};
		_globalData.cuelist[_globalData.cueCount] = (Hashtable)newShell;
		_globalData.cueShellObj[(int)_globalData.cueCount] = (Node)shellBar;
		//Shift Add bar to bottom of cue list
		//container.MoveChild(shellBar, cueCount);

		//Check if added cue is next cue
		NextCueCheck(_globalData.cueCount);

		_globalData.cueCount = _globalData.cueCount + 1;

	}

	private void NextCueCheck(int cueId)
	{
		if (_globalData.nextCue == -1)
		{
			GD.Print("No Next Cue");
			_globalData.nextCue = cueId;
		}
		var shellData = (Hashtable)_globalData.cuelist[_globalData.nextCue];
		var shellObj = (Node)_globalData.cueShellObj[_globalData.nextCue];
		shellObj.GetChild<Panel>(0).AddThemeStyleboxOverride("panel", _nextStyle);

	}
}
