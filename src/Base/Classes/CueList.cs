using System;
using System.Collections.Generic;
using Cue2.Shared;
using Godot;

// This script is attached to the cuelist in main UI
// Originator
namespace Cue2.Base.Classes;


public partial class CueList : Control
{
	public static List<ICue> Cuelist { get; private set; }
	public static Dictionary<int, Cue> CueIndex;
	public static int FocusedCueId = -1;
	private Shared.GlobalData _globalData;

	public CueList()
	{
		Cuelist = new List<ICue>();
		CueIndex = new Dictionary<int, Cue>();

	}

	public override void _Ready()
	{
		_globalData = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData");
		_globalData.Cuelist = this;
	}

	public void CreateCue(Dictionary<string, string> data)
	{
		var newCue = new Cue(data);
		AddCue(newCue);
	}
	public void CreateCue()
	{
		var newCue = new Cue();
		AddCue(newCue);
	}
	
	
	public void AddCue(Cue cue)
	{
		DisplayCues();
		CreateNewShell(cue);
		Cuelist.Add(cue);
		CueIndex.Add(cue.Id, cue);
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
	
	

	public static void DisplayCues()
	{
		Console.WriteLine("Cue List:");
		foreach (var cue in Cuelist)
		{

			Console.WriteLine($"Cue: {cue.Name} (ID: {cue.Id})");

		}
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
	
	
	
	// Functions
	private void CreateNewShell(Cue newCue)
	{
		// Load in a shell bar
		var shellBarScene = GD.Load<PackedScene>("res://src/Base/shell_bar.tscn");
		var shellBar = shellBarScene.Instantiate();
		var container = GetNode<VBoxContainer>("CueContainer");
		container.AddChild(shellBar);
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
