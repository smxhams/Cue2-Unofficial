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

	/// <summary>
	/// Captured at startup from the project.godot [input] definitions. Used to restore "factory" bindings on New Session.
	/// </summary>
	private System.Collections.Generic.Dictionary<string, Godot.Collections.Array<InputEvent>> _defaultInputBindings = new();
	
	public CueList Cuelist;
	public ShellSelection ShellSelection;
	public CueCommandExectutor CueCommandExectutor;
	public Settings Settings;
	public Devices Devices;
	public CueLightManager CueLightManager;
	public Canvas VideoCanvas;
	public DisplaysManager DisplaysManager;

	public FileDropper FileDropper;
	public UserDataManager UserDataManager;
	
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

	// Prefer Settings.StopFadeDuration (session-persisted, editable in General settings).
	
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

	/// <summary>
	/// List of input actions whose bindings can be customized by the user and persisted with sessions.
	/// </summary>
	public static readonly string[] MappableInputActions =
	{
		"NewSession",
		"OpenSession",
		"SaveSession",
		"SaveAsSession",
		"Go",
		"StopAll",
		"CreateCue",
		"GroupSelectedCues",
		"SelectNext",
		"SelectPrevious",
		"PauseAll",
		"ResumeAll",
		"ToggleSettings",
		"ToggleLog",
		"ExpandOneLayer",
		"CollapseOneLayer",
		"ToggleExpandAll"
	};


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

		UserDataManager = new UserDataManager();
		AddChild(UserDataManager);

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

		// Apply startup preference only if no explicit file was provided via command line.
		if (LaunchLoadPath == null && UserDataManager != null)
		{
			if (UserDataManager.Startup == UserDataManager.StartupBehavior.OpenLastShowfile)
			{
				var recents = UserDataManager.GetRecentShowFiles();
				if (recents.Count > 0)
				{
					LaunchLoadPath = recents[0];
					GD.Print("GlobalData:_Ready - Startup preference: opening last showfile: " + LaunchLoadPath);
				}
				else
				{
					GD.Print("GlobalData:_Ready - Startup preference set to open last, but no recent showfiles found.");
				}
			}
			else
			{
				GD.Print("GlobalData:_Ready - Startup preference: new showfile.");
			}
		}

		CaptureDefaultInputBindings();
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
					string keyName = GetKeyDisplayName(keyEvent.Keycode);
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

	/// <summary>
	/// Captures the original input bindings as defined in project.godot for all mappable actions.
	/// Must be called once early in startup before any user rebinding.
	/// </summary>
	private void CaptureDefaultInputBindings()
	{
		_defaultInputBindings.Clear();
		foreach (var action in MappableInputActions)
		{
			if (InputMap.HasAction(action))
			{
				var events = InputMap.ActionGetEvents(action);
				var clone = new Godot.Collections.Array<InputEvent>();
				foreach (InputEvent e in events)
				{
					clone.Add((InputEvent)e.Duplicate());
				}
				_defaultInputBindings[action] = clone;
			}
		}
		GD.Print($"GlobalData:CaptureDefaultInputBindings - Captured defaults for {_defaultInputBindings.Count} actions.");
	}

	/// <summary>
	/// Serializes the current state of all mappable input actions into a Dictionary suitable for saving with the session.
	/// Empty lists are included so that "cleared" bindings are preserved.
	/// </summary>
	public Dictionary GetCustomInputBindings()
	{
		var data = new Dictionary();
		foreach (var action in MappableInputActions)
		{
			if (!InputMap.HasAction(action)) continue;

			var events = InputMap.ActionGetEvents(action);
			var eventList = new Array();
			foreach (InputEvent ev in events)
			{
				if (ev is InputEventKey key)
				{
					var evData = new Dictionary();
					evData["type"] = "InputEventKey";
					evData["keycode"] = (int)key.Keycode;
					evData["physical_keycode"] = (int)key.PhysicalKeycode;
					evData["ctrl"] = key.CtrlPressed;
					evData["shift"] = key.ShiftPressed;
					evData["alt"] = key.AltPressed;
					evData["meta"] = key.MetaPressed;
					eventList.Add(evData);
				}
			}
			// Always include the key so cleared actions (0 events) are saved explicitly.
			data[action] = eventList;
		}
		return data;
	}

	/// <summary>
	/// Applies a previously saved set of input bindings to the live InputMap.
	/// Existing events for the action are erased first.
	/// </summary>
	/// <param name="bindingsData">Dictionary from save file (under "InputMap" key).</param>
	public void ApplyInputBindings(Dictionary bindingsData)
	{
		if (bindingsData == null || bindingsData.Count == 0) return;

		foreach (var action in MappableInputActions)
		{
			if (!InputMap.HasAction(action)) continue;
			if (!bindingsData.ContainsKey(action)) continue;

			InputMap.ActionEraseEvents(action);

			var evList = bindingsData[action].AsGodotArray();
			foreach (var item in evList)
			{
				var evDict = item.AsGodotDictionary();
				if (evDict == null) continue;

				if (!evDict.TryGetValue("type", out var typeVal) || typeVal.AsString() != "InputEventKey")
					continue;

				var keyEvent = new InputEventKey();

				if (evDict.TryGetValue("keycode", out var kc))
					keyEvent.Keycode = (Key)kc.AsInt32();
				if (evDict.TryGetValue("physical_keycode", out var pkc))
					keyEvent.PhysicalKeycode = (Key)pkc.AsInt32();
				if (evDict.TryGetValue("ctrl", out var ctrl))
					keyEvent.CtrlPressed = ctrl.AsBool();
				if (evDict.TryGetValue("shift", out var shift))
					keyEvent.ShiftPressed = shift.AsBool();
				if (evDict.TryGetValue("alt", out var alt))
					keyEvent.AltPressed = alt.AsBool();
				if (evDict.TryGetValue("meta", out var meta))
					keyEvent.MetaPressed = meta.AsBool();

				InputMap.ActionAddEvent(action, keyEvent);
			}
		}
		GD.Print("GlobalData:ApplyInputBindings - Restored custom input bindings from session data.");
	}

	/// <summary>
	/// Restores every mappable action to the original bindings captured from project.godot at startup.
	/// Called on New Session.
	/// </summary>
	public void ResetInputBindingsToDefaults()
	{
		foreach (var kvp in _defaultInputBindings)
		{
			string action = kvp.Key;
			if (!InputMap.HasAction(action)) continue;

			InputMap.ActionEraseEvents(action);
			foreach (InputEvent ev in kvp.Value)
			{
				InputMap.ActionAddEvent(action, (InputEvent)ev.Duplicate());
			}
		}
		GD.Print("GlobalData:ResetInputBindingsToDefaults - Input bindings restored to project defaults.");
	}

	/// <summary>
	/// Returns a copy of the default events captured for the given action, or empty array if none.
	/// </summary>
	public Godot.Collections.Array<InputEvent> GetDefaultInputEvents(string action)
	{
		if (string.IsNullOrEmpty(action) || !_defaultInputBindings.TryGetValue(action, out var events))
			return new Godot.Collections.Array<InputEvent>();
		// Return a shallow copy of the stored array (events are already duplicated at capture time)
		var copy = new Godot.Collections.Array<InputEvent>();
		foreach (var e in events)
			copy.Add(e);
		return copy;
	}

	/// <summary>
	/// Returns true if the current binding for the action exactly matches the captured default.
	/// </summary>
	public bool IsInputActionAtDefault(string action)
	{
		if (string.IsNullOrEmpty(action) || !InputMap.HasAction(action))
			return true;

		if (!_defaultInputBindings.TryGetValue(action, out var defaults) || defaults == null)
			return true;

		var current = InputMap.ActionGetEvents(action);
		if (current.Count != defaults.Count)
			return false;

		for (int i = 0; i < current.Count; i++)
		{
			if (!InputEventsEqual(current[i], defaults[i]))
				return false;
		}
		return true;
	}

	/// <summary>
	/// Restores only the specified action to its captured default binding.
	/// </summary>
	public void ResetInputActionToDefault(string action)
	{
		if (string.IsNullOrEmpty(action) || !InputMap.HasAction(action))
			return;

		if (!_defaultInputBindings.TryGetValue(action, out var defaults) || defaults == null)
			return;

		InputMap.ActionEraseEvents(action);
		foreach (InputEvent ev in defaults)
		{
			InputMap.ActionAddEvent(action, (InputEvent)ev.Duplicate());
		}
		GD.Print($"GlobalData:ResetInputActionToDefault - Restored '{action}' to default binding.");
	}

	/// <summary>
	/// Returns a human readable string for the default binding of an action (e.g. "Ctrl+G" or "Space").
	/// </summary>
	public string GetDefaultBindingDisplay(string action)
	{
		var defaults = GetDefaultInputEvents(action);
		if (defaults.Count == 0)
			return "Unbound";

		var parts = new System.Collections.Generic.List<string>();
		int shown = 0;
		foreach (InputEvent ev in defaults)
		{
			if (shown >= 2) break;
			string s = FormatInputEvent(ev);
			if (!string.IsNullOrEmpty(s))
			{
				parts.Add(s);
				shown++;
			}
		}
		string result = string.Join(" / ", parts);
		if (defaults.Count > 2)
			result += " …";
		return result;
	}

	/// <summary>
	/// Returns a user-friendly display name for a keycode.
	/// Uses symbols for punctuation keys instead of "BracketLeft", "QuoteLeft", etc.
	/// </summary>
	public static string GetKeyDisplayName(Key keycode)
	{
		string name = OS.GetKeycodeString(keycode);
		if (string.IsNullOrEmpty(name) || name == "None")
		{
			name = keycode.ToString();
			if (name.StartsWith("Key"))
				name = name.Substring(3);
		}

		// Map ugly key names to nice symbols / short names
		switch (name)
		{
			case "BracketLeft": return "[";
			case "BracketRight": return "]";
			case "QuoteLeft": return "`";
			case "Apostrophe": return "'";
			case "Semicolon": return ";";
			case "Comma": return ",";
			case "Period": return ".";
			case "Slash": return "/";
			case "Backslash": return "\\";
			case "Minus": return "-";
			case "Equal": return "=";
			case "Escape": return "Esc";
			default:
				return name;
		}
	}

	/// <summary>
	/// Formats a single input event for display (matches card formatting style).
	/// </summary>
	public static string FormatInputEvent(InputEvent ev)
	{
		if (ev is InputEventKey key)
		{
			string keyName = GetKeyDisplayName(key.Keycode);
			if (string.IsNullOrEmpty(keyName) || keyName == "None") keyName = key.PhysicalKeycode.ToString();

			string mods = "";
			if (key.CtrlPressed) mods += "Ctrl+";
			if (key.ShiftPressed) mods += "Shift+";
			if (key.AltPressed) mods += "Alt+";
			if (key.MetaPressed) mods += "Meta+";
			return mods + keyName;
		}
		return ev.AsText();
	}

	private bool InputEventsEqual(InputEvent a, InputEvent b)
	{
		if (a is InputEventKey ka && b is InputEventKey kb)
		{
			return ka.Keycode == kb.Keycode &&
			       ka.PhysicalKeycode == kb.PhysicalKeycode &&
			       ka.CtrlPressed == kb.CtrlPressed &&
			       ka.ShiftPressed == kb.ShiftPressed &&
			       ka.AltPressed == kb.AltPressed &&
			       ka.MetaPressed == kb.MetaPressed;
		}
		return a.AsText() == b.AsText();
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