using System;
using System.Collections;
using System.Collections.Generic;
using Godot;
using LibVLCSharp.Shared;

// This script is attached to the cuelist in main UI

namespace Cue2.Base.Classes;

public partial class CueList : Control
{
	private List<ICue> Cuelist { get; set; }
	private Dictionary<int, Cue> _cueIndex = new Dictionary<int, Cue>();
	
	private Cue2.Shared.GlobalData _globalData;
	private StyleBoxFlat _nextStyle = new StyleBoxFlat();
	public CueList()
	{
		Cuelist = new List<ICue>();
	}

	public void AddCue(Cue cue)
	{
		Cuelist.Add(cue);
		_cueIndex.Add(cue.Id, cue);
	}

	public void RemoveCue(Cue cue)
	{
		Cuelist.Remove(cue);
	}

	public void DisplayCues()
	{
		Console.WriteLine("Cue List:");
		foreach (var cue in Cuelist)
		{

			Console.WriteLine($"Cue: {cue.Name} (ID: {cue.Id})");

		}
	}

	// Received Signal Handling
	private void _on_add_shell_pressed()
		// Signal from add shell button
	{
		var newCue = new Cue();
		AddCue(newCue);
		DisplayCues();
		CreateNewShell(newCue);
		
	}
	
	// Functions
	private void CreateNewShell(Cue newCue)
	{
		// Load in a shell bar
		var shellBarScene = GD.Load<PackedScene>("res://src/Base/shell_bar.tscn");
		var shellBar = shellBarScene.Instantiate();
		var container = GetNode<VBoxContainer>("CueContainer");
		container.AddChild(shellBar);
		shellBar.GetChild(1).GetChild(0).GetChild<LineEdit>(2).Text = newCue.CueNum.ToString(); // Cue Number
		shellBar.GetChild(1).GetChild(0).GetChild<LineEdit>(3).Text = newCue.Name.ToString(); // Cue Name
		
		newCue.ShellBar = shellBar; // Adds shellbar scene to the cue object.
		shellBar.Set("CueId", newCue.Id); // Sets shell_bar property CueId
	}
}

//
// public partial class CueList : Control
// {
//
// 	private Cue2.Shared.GlobalData _globalData;
// 	private GlobalStyles _globalStyles;
//
// 	private Variant _cueCount;
// 	private MediaPlayer _mediaPlayer;
//
// 	private StyleBoxFlat _nextStyle = new StyleBoxFlat();
// 	
//
//
//
// 	public override void _Ready()
// 	{
// 		_globalData = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData");
// 		_cueCount = _globalData.cueCount;
//
// 		_globalStyles = GetNode<GlobalStyles>("/root/GlobalStyles");
// 		_nextStyle = _globalStyles.nextStyle;
// 	}
//
// 	// Received Signal Handling
// 	private void _on_add_shell_pressed()
// 		// Signal from add shell button
// 	{
// 		CreateNewShell();
// 	}
// 	
// 	// Maybe hve int array (int[] cueOrder {cueID, cueID})
// 	
// 	// Functions
// 	private void CreateNewShell()
// 	{
// 		// Create New Shell Data
// 		var newShell = new Hashtable()
// 		{
// 			{"id", _globalData.cueCount},
// 			{"name", (String)""},
// 			{"cueNum", (String)""},
// 			{"type", ""},
// 			//{"shellObj", shellBar},
// 			{"filepath", ""},
// 			{"player", null},
// 			{"media", null}
// 		};
// 		
// 		// Load in a shell bar
// 		var shellBarScene = GD.Load<PackedScene>("res://src/Base/shell_bar.tscn");
// 		var shellBar = shellBarScene.Instantiate();
// 		var container = GetNode<VBoxContainer>("CueContainer");
// 		container.AddChild(shellBar);
// 		shellBar.GetChild(1).GetChild(0).GetChild<LineEdit>(2).Text = _globalData.getCueCount().ToString();
// 		
//
// 		_globalData.cuelist[_globalData.cueCount] = (Hashtable)newShell;
// 		_globalData.cueShellObj[(int)_globalData.cueCount] = (Node)shellBar;
// 		//Shift Add bar to bottom of cue list
// 		//container.MoveChild(shellBar, cueCount);
//
// 		//Check if added cue is next cue
// 		NextCueCheck(_globalData.cueCount);
//
// 		_globalData.cueCount = _globalData.cueCount + 1;
//
// 	}
//
// 	private void NextCueCheck(int cueId)
// 	{
// 		if (_globalData.nextCue == -1)
// 		{
// 			GD.Print("No Next Cue");
// 			_globalData.nextCue = cueId;
// 		}
// 		var shellData = (Hashtable)_globalData.cuelist[_globalData.nextCue];
// 		var shellObj = (Node)_globalData.cueShellObj[_globalData.nextCue];
// 		shellObj.GetChild<Panel>(0).AddThemeStyleboxOverride("panel", _nextStyle);
//
// 	}
// }
