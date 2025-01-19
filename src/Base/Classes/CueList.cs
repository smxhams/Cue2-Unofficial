using System;
using System.Collections.Generic;
using Cue2.Shared;
using Godot;

// This script is attached to the cuelist in main UI
// Originator
namespace Cue2.Base.Classes;


public partial class CueList : Control
{
	private GlobalData _globalData;
	private GlobalSignals _globalSignals;
	
	public static List<ICue> Cuelist { get; private set; }
	public static Dictionary<int, Cue> CueIndex;
	public static int FocusedCueId = -1;
	private static int _index = -1;

	public CueList()
	{
		Cuelist = new List<ICue>();
		CueIndex = new Dictionary<int, Cue>();

	}

	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		_globalData.Cuelist = this;
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
	}

	public void CreateCue(Dictionary<string, string> data)
	{
		GD.Print("Creating Cue from data");
		var newCue = new Cue(data);
		AddCue(newCue);
	}
	public void CreateCue()
	{
		GD.Print("Creating Cue from defaults");
		var newCue = new Cue();
		AddCue(newCue);
	}
	
	public void AddCue(Cue cue)
	{
		CreateNewShell(cue);
		
		Cuelist.Add(cue);
		CueIndex.Add(cue.Id, cue);
		// Will make new cues focused
		FocusCue(cue);
	}
	
	

	public static void RemoveCue(Cue cue)
	{
		cue.ShellBar.Free();
		Cuelist.Remove(cue);
	}

	public static Cue FetchCueFromId(int id)
	{
		try
		{
			CueIndex.TryGetValue(id, out Cue cue);
			return cue;
		}
		catch (KeyNotFoundException)
		{
			return null;
		}
	}

	// ITERATORS
	public static ICue Current()
	{
		return Cuelist[_index];
	}

	public static bool HasNext()
	{
		return _index < Cuelist.Count;
	}
	public ICue Next()
	{
		if (!HasNext()) throw new Exception("No more cues");
		_index++;
		FocusCue(Cuelist[_index]);
		return Cuelist[_index];
	}

	public void FocusCue(ICue cue)
	{
		if (_index != -1) 
		{
			var currentCue = Current();
			if (cue == currentCue) return;
			currentCue.ShellBar.GetNode<Panel>("Panel").RemoveThemeStyleboxOverride("panel");
		}
		cue.ShellBar.GetNode<Panel>("Panel").AddThemeStyleboxOverride("panel", GlobalStyles.FocusedStyle());
		_index = Cuelist.IndexOf(cue);
		FocusedCueId = cue.Id;
		_globalSignals.EmitSignal(nameof(GlobalSignals.ShellFocused), cue.Id);
				
		GD.Print("Focus Cue: " + cue.Name + " - Cuelist index is: " + _index);
	}
	
	public CueListState CreateState()
	{
		return new CueListState(Cuelist, CueIndex);
	}

	public void Restore(CueListState state)
	{
		Cuelist = state.GetCuelist();
		CueIndex = state.GetCueIndex();
	}
		
	// Received Signal Handling
	private void _on_add_shell_pressed()
		// Signal from add shell button
	{
		CreateCue();
	}
	
	
	
	// This instantiates the shell scene which creates the UI elements to represent the cue in the scene
	private void CreateNewShell(Cue newCue)
	{
		// Load in a shell bar
		var shellBarScene = GD.Load<PackedScene>("res://src/Base/shell_bar.tscn");
		var shellBar = shellBarScene.Instantiate();
		var container = GetNode<VBoxContainer>("CueContainer");
		container.CallDeferred("add_child", shellBar);
		shellBar.GetChild(1).GetChild(0).GetChild<LineEdit>(2).Text = newCue.CueNum; // Cue Number
		shellBar.GetChild(1).GetChild(0).GetChild<LineEdit>(3).Text = newCue.Name; // Cue Name
		
		newCue.ShellBar = shellBar; // Adds shellbar scene to the cue object.
		shellBar.Set("CueId", newCue.Id); // Sets shell_bar property CueId
	}
	
	// Resets CueList
	public void ResetCuelist()
	{
		var removalList = Cuelist;
		// Removes shellbars from ui
		foreach (ICue cue in removalList)
		{
			cue.ShellBar.Free();
		}
		// Resets 
		Cuelist = new List<ICue>();
		CueIndex = new Dictionary<int, Cue>();
		_index = -1;
		FocusedCueId = -1;


	}
}

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
