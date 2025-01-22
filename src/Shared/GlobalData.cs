using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Cue2.Base.Classes;
using Godot;
using LibVLCSharp.Shared;

namespace Cue2.Shared;
// This script manages global data it contains:
// -Data management functions
// -Manages saving and loading of shows

public partial class GlobalData : Node
{
	private GlobalSignals _globalSignals;
	private SaveManager _saveManager;
	
	public CueList Cuelist;
	public Playback Playback;
	public int FocusedCue = -1;
	public Dictionary<int, Node> CueShellObj = new Dictionary<int, Node>();
	public ArrayList CueIndex = new ArrayList(); // [CueID, Cue Object]
	public int CueCount;
	public int CueTotal;
	public int CueOrder;
	public int NextCue = -1;
	

	public int VideoOutputWinNum;
	public int UiOutputWinNum;

	public string LaunchLoadPath;

	public static double StopFadeTime = 2.0; // Fade time in seconds

	

	// Settings
	public bool SelectedIsNext = true; // Whether selecting a cue makes in next to be manualy go'd.
	public bool AutoloadOnStartup = true; // Loads last active show on startup
	public string ActiveShowFile; // URL of current show file to save to
	public static string SessionName;
	public static string SessionPath;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// Init MediaManager class so can be referenced everywhere
		//if (autoloadOnStartup == true){loadShow("Last");}
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_saveManager = GetNode<SaveManager>("/root/SaveManager");

		Playback = new Playback();
		AddChild(Playback);

		var args = new List<string>(OS.GetCmdlineUserArgs()).Concat(new List<string>(OS.GetCmdlineArgs()));
		foreach (var arg in args)
		{
			GD.Print("Launch argument detected: " + arg);
			if (arg == "--file")
			{
				GD.Print("Opening file: " + args.Last());
				LaunchLoadPath = args.Last(); 
				
			}
		}
		
	}
	
}