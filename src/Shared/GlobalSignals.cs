using Godot;
using System;
using System.Collections.Generic;
using Cue2.Base.Classes;

namespace Cue2.Shared;
public partial class GlobalSignals : Node
{

	[Signal]
	public delegate void CloseSettingsWindowEventHandler();

	[Signal]
	public delegate void ShellFocusedEventHandler(int cueId);

	[Signal]
	public delegate void ErrorLogEventHandler(string errorLog, int type);

	[Signal]
	public delegate void FileSelectedEventHandler(string path);

	[Signal]
	public delegate void CueGoEventHandler(int playbackId, int cueId);

	[Signal]
	public delegate void UpdateShellBarEventHandler(int cue);
	
	
	[Signal]
	public delegate void OpenSelectedSessionEventHandler(string path);

	[Signal]
	public delegate void SaveFileEventHandler(string url, string showName);
	
	
	// Signals Associated with InputActions
	[Signal]
	public delegate void SaveEventHandler();

	[Signal]
	public delegate void OpenSessionEventHandler();
	
	
	[Signal]
	public delegate void StopAllEventHandler();

	[Signal]
	public delegate void GoEventHandler();
	
	[Signal]
	public delegate void CreateCueEventHandler();
	
	
	


}
