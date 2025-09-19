using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Cue2.Base;
using Cue2.Base.Classes;
using Cue2.Base.CommandInterpreter;
using Godot;
using LibVLCSharp.Shared;
using SDL3;

namespace Cue2.Shared;
// This script manages global data it contains:
// -Data management functions
// -Manages saving and loading of shows

public partial class GlobalData : Node
{
	public static string Version { get; } = "0.0.1-alpha";

	private GlobalSignals _globalSignals;
	private SaveManager _saveManager;
	
	public CueList Cuelist;
	public ShellSelection ShellSelection;
	public CueCommandInterpreter CueCommandInterpreter;
	public Settings Settings;
	public Devices Devices;
	public CueLightManager CueLightManager;
	//public AudioDevices AudioDevices;
	
	
	public int FocusedCue = -1;
	public Dictionary<int, Node> CueShellObj = new Dictionary<int, Node>();
	public ArrayList CueIndex = new ArrayList(); // [CueID, Cue Object]
	public int CueCount;
	public int CueTotal;
	public int CueOrder;
	public int NextCue = -1;

	public Node VideoCanvas;
	public Window VideoWindow;

	public int VideoOutputWinNum;
	public int UiOutputWinNum;

	public string LaunchLoadPath;

	public static double StopFadeTime = 2.0; // Fade time in seconds

	

	// Settings
	public bool SelectedIsNext = true; // Whether selecting a cue makes in next to be manualy go'd.
	public bool AutoloadOnStartup = true; // Loads last active show on startup
	public string ActiveShowFile; // URL of current show file to save to
	public string SessionName;
	public string SessionPath;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// Init MediaManager class so can be referenced everywhere
		//if (autoloadOnStartup == true){loadShow("Last");}
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_saveManager = GetNode<SaveManager>("/root/SaveManager");
		
		ShellSelection = new ShellSelection();
		AddChild(ShellSelection);
		
		CueCommandInterpreter = new CueCommandInterpreter();
		AddChild(CueCommandInterpreter);
		
		Settings = new Settings();
		AddChild(Settings);

		Devices = new Devices();
		AddChild(Devices);
		
		CueLightManager = new CueLightManager();
		AddChild(CueLightManager);
		
		//AudioDevices = new AudioDevices();
		//AddChild(AudioDevices);
		
		



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
	
	public static string ParseHotkey(string action)
	// Parse Hotkey will retyurn simple text representation of an input action.
	// Currently used to display hotkeys in UI
	{
		// Check if the action exists in the Input Map
		if (InputMap.HasAction(action))
		{
			// Get the list of input events for the action
			var events = InputMap.ActionGetEvents(action);

			foreach (InputEvent @event in events)
			{
				if (@event is InputEventKey keyEvent)
				{
					// Get the key and modifiers
					string keyName = OS.GetKeycodeString(keyEvent.Keycode);
					bool ctrlPressed = keyEvent.CtrlPressed;
					bool shiftPressed = keyEvent.ShiftPressed;
					bool altPressed = keyEvent.AltPressed;
					bool metaPressed = keyEvent.MetaPressed;

					// Build the hotkey string
					string hotkey = "";
					if (ctrlPressed) hotkey += "Ctrl + ";
					if (shiftPressed) hotkey += "Shift + ";
					if (altPressed) hotkey += "Alt + ";
					if (metaPressed) hotkey += "Meta + ";
					hotkey += keyName;

					return hotkey;
				}
			}
		}
		return "";
	}

	/*public string Version()
	{
		return Version();
	}*/


}