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
	public HistoryManager HistoryManager;
	public CueLibraryManager CueLibraryManager;
	
	/// <summary>
	/// Id of the cue currently focused for inspectors (-1 if none).
	/// Kept in sync via <see cref="GlobalSignals.ShellFocused"/>.
	/// </summary>
	public int FocusedCue = -1;

	private void OnShellFocused(int cueId)
	{
		FocusedCue = cueId;
	}

	public System.Collections.Generic.Dictionary<int, Node> CueShellObj = new System.Collections.Generic.Dictionary<int, Node>();
	public ArrayList CueIndex = new ArrayList(); // [CueID, Cue Object]
	public int CueCount;

	/// <summary>
	/// Total number of cues in the show (including nested group children). Kept in sync by <see cref="CueList"/>.
	/// </summary>
	/// <value>Non-negative count of cues currently in the cuelist.</value>
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

	/// <summary>
	/// Absolute path to the show session folder (directory containing the .c2 file).
	/// </summary>
	public string SessionDir;

	/// <summary>
	/// Absolute path for show-local audio media (<c>SessionDir/Audio</c>).
	/// </summary>
	public string SessionAudioPath;

	/// <summary>
	/// Absolute path for show-local video media (<c>SessionDir/Video</c>).
	/// </summary>
	public string SessionVideoPath;

	/// <summary>
	/// Absolute path for show-local image media (<c>SessionDir/Images</c>).
	/// </summary>
	public string SessionImagesPath;

	/// <summary>
	/// Absolute path for waveform cache files (<c>SessionDir/Waveforms</c>).
	/// </summary>
	public string SessionWaveformsPath;

	/// <summary>
	/// Resolves a cue media path that may be show-relative (e.g. <c>Audio/song.wav</c>) to an absolute path.
	/// Absolute paths are returned normalized when possible.
	/// </summary>
	/// <param name="storedPath">Path as stored on a cue component.</param>
	/// <returns>Absolute filesystem path for I/O, or the input if it cannot be resolved.</returns>
	public string ResolveMediaPath(string storedPath) => MediaPaths.Resolve(storedPath, SessionDir);

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
	/// List of input actions whose bindings can be customized by the user and persisted in user preferences
	/// (<c>user://user_data.json</c>), not in the showfile.
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
		"ToggleExpandAll",
		"DeleteCue",
		"DuplicateSelectedCues",
		"CutSelectedCues",
		"CopySelectedCues",
		"PasteCues",
		"Undo",
		"Redo"
	};

	/// <summary>
	/// Display categories for the Input Map settings panel (order is UI order).
	/// Actions not listed here still appear under "Other" when discovered at runtime.
	/// </summary>
	public static readonly (string Category, string[] Actions)[] MappableInputActionCategories =
	{
		("Session", new[] { "NewSession", "OpenSession", "SaveSession", "SaveAsSession" }),
		("Playback", new[] { "Go", "StopAll", "PauseAll", "ResumeAll" }),
		("Cue Editing", new[] { "CreateCue", "GroupSelectedCues", "DeleteCue", "DuplicateSelectedCues", "CutSelectedCues", "CopySelectedCues", "PasteCues" }),
		("Navigation", new[] { "SelectNext", "SelectPrevious", "ExpandOneLayer", "CollapseOneLayer", "ToggleExpandAll" }),
		("Windows", new[] { "ToggleSettings", "ToggleLog" }),
		("History", new[] { "Undo", "Redo" }),
	};


	public override void _Ready()
	{
		// Init MediaManager class so can be referenced everywhere
		//if (autoloadOnStartup == true){loadShow("Last");}

		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_saveManager = GetNode<SaveManager>("/root/SaveManager");

		// Track focused cue for history restore and cross-system queries.
		_globalSignals.ShellFocused += OnShellFocused;

		// Print the full resolved path for user:// (Godot's user data directory) as early as possible
		GodotUserDataPath = ProjectSettings.GlobalizePath("user://");
		GD.Print("GlobalData:_Ready - Godot user:// resolves to full path: " + GodotUserDataPath);
		_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Godot user:// full path: {GodotUserDataPath}", 0);

		// Library is pure filesystem I/O — create before SDL so inspectors never race a late null.
		CueLibraryManager = new CueLibraryManager();
		CueLibraryManager.Name = nameof(CueLibraryManager);
		AddChild(CueLibraryManager);

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

		HistoryManager = new HistoryManager();
		AddChild(HistoryManager);

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

		// Capture project.godot factory bindings first, then overlay user-customized shortcuts.
		CaptureDefaultInputBindings();
		UserDataManager?.ApplyInputMapFromUserData();
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
	/// Serializes the current state of all mappable input actions into a Dictionary
	/// suitable for user-preference storage.
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
	/// <param name="bindingsData">Dictionary from user data (under "InputMap" key).</param>
	public void ApplyInputBindings(Dictionary bindingsData)
	{
		if (bindingsData == null || bindingsData.Count == 0) return;

		foreach (var action in MappableInputActions)
		{
			if (!InputMap.HasAction(action)) continue;
			if (!TryGetBindingActionList(bindingsData, action, out var evList)) continue;

			InputMap.ActionEraseEvents(action);

			foreach (var item in evList)
			{
				if (item.VariantType != Variant.Type.Dictionary) continue;
				var evDict = item.AsGodotDictionary();
				if (evDict == null) continue;

				if (!TryGetDictValue(evDict, "type", out var typeVal) || typeVal.AsString() != "InputEventKey")
					continue;

				var keyEvent = new InputEventKey();

				if (TryGetDictValue(evDict, "keycode", out var kc))
					keyEvent.Keycode = (Key)kc.AsInt32();
				if (TryGetDictValue(evDict, "physical_keycode", out var pkc))
					keyEvent.PhysicalKeycode = (Key)pkc.AsInt32();
				if (TryGetDictValue(evDict, "ctrl", out var ctrl))
					keyEvent.CtrlPressed = ctrl.AsBool();
				if (TryGetDictValue(evDict, "shift", out var shift))
					keyEvent.ShiftPressed = shift.AsBool();
				if (TryGetDictValue(evDict, "alt", out var alt))
					keyEvent.AltPressed = alt.AsBool();
				if (TryGetDictValue(evDict, "meta", out var meta))
					keyEvent.MetaPressed = meta.AsBool();

				InputMap.ActionAddEvent(action, keyEvent);
			}
		}
		GD.Print("GlobalData:ApplyInputBindings - Restored custom input bindings from user preferences.");
	}

	/// <summary>
	/// Finds an action's event list in a bindings dictionary (tolerates StringName keys after JSON clone).
	/// </summary>
	private static bool TryGetBindingActionList(Dictionary bindingsData, string action, out Godot.Collections.Array evList)
	{
		evList = null;
		if (bindingsData == null || string.IsNullOrEmpty(action)) return false;

		if (bindingsData.TryGetValue(action, out var raw))
		{
			evList = raw.AsGodotArray();
			return true;
		}

		foreach (var k in bindingsData.Keys)
		{
			if (k.AsString() == action)
			{
				evList = bindingsData[k].AsGodotArray();
				return true;
			}
		}
		return false;
	}

	private static bool TryGetDictValue(Dictionary dict, string key, out Variant value)
	{
		value = default;
		if (dict == null || string.IsNullOrEmpty(key)) return false;
		if (dict.TryGetValue(key, out value)) return true;
		foreach (var k in dict.Keys)
		{
			if (k.AsString() == key)
			{
				value = dict[k];
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Restores every mappable action to the original bindings captured from project.godot at startup.
	/// Used by Input Map "reset all" style flows; does not change user-data storage until
	/// <see cref="UserDataManager.PersistLiveInputMap"/> is called.
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
			return KeyEventsMatch(ka, kb);
		return a.AsText() == b.AsText();
	}

	/// <summary>
	/// Returns true if two key events represent the same hotkey (key + modifiers).
	/// Compares effective keycode (falls back to physical) and Ctrl/Shift/Alt/Meta.
	/// </summary>
	public static bool KeyEventsMatch(InputEventKey a, InputEventKey b)
	{
		if (a == null || b == null) return false;

		Key aKey = a.Keycode != Key.None ? a.Keycode : a.PhysicalKeycode;
		Key bKey = b.Keycode != Key.None ? b.Keycode : b.PhysicalKeycode;
		if (aKey == Key.None || bKey == Key.None || aKey != bKey)
			return false;

		return a.CtrlPressed == b.CtrlPressed &&
		       a.ShiftPressed == b.ShiftPressed &&
		       a.AltPressed == b.AltPressed &&
		       a.MetaPressed == b.MetaPressed;
	}

	/// <summary>
	/// Finds another mappable action that already uses the given key combo.
	/// </summary>
	/// <param name="excludeAction">Action currently being rebound (ignored in the search).</param>
	/// <param name="keyEvent">Proposed key binding.</param>
	/// <returns>Conflicting action name, or null if the combo is free.</returns>
	public static string FindConflictingInputAction(string excludeAction, InputEventKey keyEvent)
	{
		if (keyEvent == null) return null;

		foreach (var action in MappableInputActions)
		{
			if (action == excludeAction) continue;
			if (!InputMap.HasAction(action)) continue;

			foreach (InputEvent ev in InputMap.ActionGetEvents(action))
			{
				if (ev is InputEventKey other && KeyEventsMatch(other, keyEvent))
					return action;
			}
		}

		// Also scan non-ui_ actions not in the curated list (same scope as the settings UI).
		foreach (StringName actionName in InputMap.GetActions())
		{
			string action = actionName.ToString();
			if (string.IsNullOrEmpty(action) || action.StartsWith("ui_")) continue;
			if (action == excludeAction) continue;
			if (System.Array.IndexOf(MappableInputActions, action) >= 0) continue;

			foreach (InputEvent ev in InputMap.ActionGetEvents(action))
			{
				if (ev is InputEventKey other && KeyEventsMatch(other, keyEvent))
					return action;
			}
		}

		return null;
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