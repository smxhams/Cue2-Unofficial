using Godot;
using System;
using System.Collections.Generic;
using Cue2.Base.Classes;

namespace Cue2.Shared;
public partial class GlobalSignals : Node
{
	[Signal]  public delegate void CloseSettingsWindowEventHandler();

	[Signal]  public delegate void ShellFocusedEventHandler(int cueId);

	[Signal]  public delegate void LogEventHandler(string log, int type);
	[Signal]  public delegate void LogUpdatedEventHandler(string printout, int type);
	
	[Signal] public delegate void LogAlertEventHandler();

	[Signal]  public delegate void FileSelectedEventHandler(string path);

	[Signal]  public delegate void CueGoEventHandler(int playbackId, int cueId);

	[Signal]  public delegate void UpdateShellBarEventHandler(int cue);
	
	
	[Signal]  public delegate void OpenSelectedSessionEventHandler(string path);

	[Signal]  public delegate void SaveFileEventHandler(string url, string showName);
	
	
	// Signals Associated with InputActions
	[Signal] public delegate void SaveEventHandler();
	[Signal] public delegate void SaveAsEventHandler();

	[Signal] public delegate void OpenSessionEventHandler();

	[Signal] public delegate void GoEventHandler();
	
	[Signal] public delegate void ResumeAllEventHandler();
	[Signal] public delegate void PauseAllEventHandler();
	[Signal] public delegate void StopAllEventHandler();
	
	[Signal] public delegate void CreateCueEventHandler();
	
	[Signal] public delegate void CreateGroupEventHandler();
	
	
	
	// Text edit signal connector
	[Signal]  public delegate void TextEditFocusEnteredEventHandler();
	[Signal]  public delegate void TextEditFocusExitedEventHandler();
	
	
	// Singals assaciated with settings
	[Signal] public delegate void UiScaleChangedEventHandler(float value);
	[Signal] public delegate void GoScaleChangedEventHandler(float value);
	[Signal] public delegate void SettingsSaveAsEventHandler(string filters, string url);
	[Signal] public delegate void SettingsSaveWithShowEventHandler(string filters);
	[Signal] public delegate void SettingsSaveUserDirEventHandler(string filters);
	

	// The below checks all nodes for text edits and connects the signals for is they are focused. This is primarily to toggle input actions that clash with typing
	public override void _Ready()
	{
		// Scan for existing text fields at startup
		ScanForTextFields(GetTree().Root);

		// Listen for new nodes added dynamically
		GetTree().NodeAdded += OnNodeAdded;
	}

	private void ScanForTextFields(Node node)
	{
		if (node is LineEdit || node is TextEdit)
		{
			ConnectFocusSignals(node);
		}

		foreach (Node child in node.GetChildren())
		{
			ScanForTextFields(child);
		}
	}

	private void OnNodeAdded(Node node)
	{
		if (node is LineEdit || node is TextEdit)
		{
			ConnectFocusSignals(node);
		}
	}

	private void ConnectFocusSignals(Node node)
	{
		if (node is Control textField)
		{
			textField.FocusEntered += () => EmitSignal(SignalName.TextEditFocusEntered);
			textField.FocusExited += () => EmitSignal(SignalName.TextEditFocusExited);
		}
	}
}
