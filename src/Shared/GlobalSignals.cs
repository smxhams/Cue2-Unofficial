using Godot;
using System;

public partial class GlobalSignals : Node
{
	[Signal]
	public delegate void CloseSettingsWindowEventHandler();

	[Signal]
	public delegate void ShellSelectedEventHandler(int cueId);

	[Signal]
	public delegate void ErrorLogEventHandler(string errorLog, int type);

	[Signal]
	public delegate void FileSelectedEventHandler(string path);

	[Signal]
	public delegate void CueGoEventHandler(int cue);

	[Signal]
	public delegate void UpdateShellBarEventHandler(int cue);

	[Signal]
	public delegate void SaveEventHandler();

	[Signal]
	public delegate void SaveFileEventHandler(string url, string showName);

}
