using Godot;
using System;

public partial class GlobalSignals : Node
{
	[Signal]
	public delegate void CloseSettingsWindowEventHandler();

	[Signal]
	public delegate void ShellSelectedEventHandler(int cueID);

	[Signal]
	public delegate void ErrorLogEventHandler(string errorLog);

	[Signal]
	public delegate void FileSelectedEventHandler(string path);

}
