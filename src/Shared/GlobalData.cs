//==================================================================================//
// GlobalData.cs																	//
// This file is part of Cue2														//
// http://cue2.live/																//
//==================================================================================//
// MIT License																		//
//																					//
// Copyright © 2025 Samuel Moxham													//
//																					//
// Permission is hereby granted, free of charge, to any person obtaining a copy		//
// 	of this software and associated documentation files (the ""Software""), to deal	//
// 	in the Software without restriction, including without limitation the rights	//
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell		//
// copies of the Software, and to permit persons to whom the Software is			//
// 	furnished to do so, subject to the following conditions:						//
//																					//
// The above copyright notice and this permission notice shall be included in all	//
// 	copies or substantial portions of the Software.									//
//																					//
// 	THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR	//
// 	IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,		//
// 	FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE		//
// 	AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER			//
// 	LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,	//
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE	//
// SOFTWARE.																		//
//==================================================================================//

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cue2.Base;
using Cue2.Base.Classes;
using Cue2.Base.Classes.Connections;
using Cue2.Base.CommandInterpreter;
using Cue2.Base.Minor;
using Godot;
using Godot.Collections;
using SDL3;

namespace Cue2.Shared;
// This script manages global data it contains:
// -Data management functions
// -Manages saving and loading of shows

public partial class GlobalData : Node
{
	private GlobalSignals _globalSignals;
	private SaveManager _saveManager;
	
	public CueList Cuelist;
	public ShellSelection ShellSelection;
	public CueCommandExectutor CueCommandExectutor;
	public Settings Settings;
	public Devices Devices;
	public CueLightManager CueLightManager;
	public Canvas VideoCanvas;
	public DisplaysManager DisplaysManager;

	public FileDropper FileDropper;
	
	public int FocusedCue = -1;
	public System.Collections.Generic.Dictionary<int, Node> CueShellObj = new System.Collections.Generic.Dictionary<int, Node>();
	public ArrayList CueIndex = new ArrayList(); // [CueID, Cue Object]
	public int CueCount;
	public int CueTotal;
	public int CueOrder;
	public int NextCue = -1;
	
	public int VideoOutputWinNum;
	public int UiOutputWinNum;

	public string LaunchLoadPath;

	public static double StopFadeTime = 2.0; // Fade time in seconds
	
	public float BaseDisplayScale { get; private set; } = 1.0f;

	// Settings
	public bool SelectedIsNext = true; // Whether selecting a cue makes in next to be manualy go'd.
	public bool AutoloadOnStartup = true; // Loads last active show on startup
	public string ActiveShowFile; // URL of current show file to save to
	public string SessionName;
	public string SessionPath;
	public string SessionMediaPath;
	public string SessionWaveformsPath;

	/// <summary>
	/// The absolute filesystem path that Godot resolves "user://" to.
	/// Useful for debugging or storing app-specific data in the user data directory.
	/// </summary>
	public string GodotUserDataPath { get; private set; } //!!!
	
	// File filters for media files (FFmpeg compatible)
	public static readonly List<string> VideoFileFilters = new List<string> {
		"*.mp4", "*.avi", "*.mkv", "*.mov", "*.flv", "*.webm", "*.m4v", "*.3gp", "*.asf",
		"*.wmv", "*.mpg", "*.mpeg", "*.ts", "*.mts", "*.vob", "*.ogv", "*.rm", "*.rmvb",
		"*.divx", "*.xvid"
	};
	public static readonly List<string> ImageFileFilters = new List<string> {
		"*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tiff", "*.tif", "*.gif", "*.webp", "*.tga",
		"*.dds", "*.exr", "*.hdr", "*.svg"
	};
	public static readonly List<string> AudioFileFilters = new List<string> {
		"*.mp3", "*.wav", "*.flac", "*.aac", "*.ogg", "*.m4a", "*.wma", "*.aiff", "*.au", "*.ra",
		"*.ape", "*.ac3", "*.dts", "*.pcm"
	};
	public static readonly string AllSupportedFileFilters = string.Join(",", VideoFileFilters.Concat(ImageFileFilters).Concat(AudioFileFilters));


	public override void _Ready()
	{
		// Init MediaManager class so can be referenced everywhere
		//if (autoloadOnStartup == true){loadShow("Last");}

		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_saveManager = GetNode<SaveManager>("/root/SaveManager");

		// Print the full resolved path for user:// (Godot's user data directory) as early as possible
		GodotUserDataPath = ProjectSettings.GlobalizePath("user://");
		GD.Print("GlobalData:_Ready - Godot user:// resolves to full path: " + GodotUserDataPath);
		_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Godot user:// full path: {GodotUserDataPath}", 0);

		// Initialize SDL with audio, events, and video
		if (SDL.Init(SDL.InitFlags.Audio | SDL.InitFlags.Events | SDL.InitFlags.Video) == false)
		{
			var errorMsg = $"SDL Init failed: {SDL.GetError()}";
			GD.Print("GlobalData:_Ready - " + errorMsg);
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), errorMsg, 3);
			return;
		}
		GD.Print("GlobalData:_Ready - SDL initialized successfully.");

		ShellSelection = new ShellSelection();
		AddChild(ShellSelection);
		
		CueCommandExectutor = new CueCommandExectutor();
		AddChild(CueCommandExectutor);
		
		Settings = new Settings();
		AddChild(Settings);

		Devices = new Devices();
		AddChild(Devices);
		
		CueLightManager = new CueLightManager();
		AddChild(CueLightManager);
		
		FileDropper = new FileDropper();
		AddChild(FileDropper);

		DisplaysManager = GetNode<DisplaysManager>("/root/DisplaysManager");
		
		int currentScreen = DisplayServer.WindowGetCurrentScreen(GetWindow().GetWindowId());
		BaseDisplayScale = DisplayServer.ScreenGetScale(currentScreen);
		if (BaseDisplayScale <= 0f) {
			BaseDisplayScale = 1.0f; // Fallback if invalid 
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Failed to fetch display scale; using fallback 1.0", 1);
		}
		
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

	public override void _ExitTree()
	{
		if (SDL.WasInit(SDL.InitFlags.Audio) != 0) SDL.Quit();
		GD.Print("GlobalData:_ExitTree - Cleaned up SDL.");
	}

	public static string ParseHotkey(string action)
	// Parse Hotkey will return simple text representation of an input action.
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


	public Dictionary GetAvailableConnections()
	{
		var dict = new Dictionary();
		var cueLights = CueLightManager.GetCueLights();
		foreach (var cueLight in cueLights)
		{
			dict.Add(cueLight,"Cue Light");
		}

		var oscConnections = OscConnections.Connections;
		foreach (var oscCon in oscConnections)
		{
			dict.Add(oscCon,"Osc Connection");
		}
		return dict;
	}

}