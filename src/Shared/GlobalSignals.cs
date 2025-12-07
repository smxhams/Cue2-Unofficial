//==================================================================================//
// GlobalSignals.cs																	//
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

using Godot;
using System;
using System.Collections.Generic;
using Cue2.Base.Classes;

namespace Cue2.Shared;
public partial class GlobalSignals : Node
{
	[Signal]  public delegate void ShellFocusedEventHandler(int cueId);
	[Signal]  public delegate void LogEventHandler(string log, int type);
	// 0 = info, 1 = warning, 2 = error, 3 = alert
	[Signal]  public delegate void LogUpdatedEventHandler(string printout, int type);
	[Signal] public delegate void LogAlertEventHandler();
	[Signal]  public delegate void FileSelectedEventHandler(string path);
	[Signal]  public delegate void CueGoEventHandler(int playbackId, int cueId);
	[Signal]  public delegate void UpdateShellBarEventHandler(int cue);
	[Signal]  public delegate void OpenSelectedSessionEventHandler(string path);
	[Signal]  public delegate void SaveFileEventHandler(string url, string showName);
	[Signal] public delegate void SyncShellInspectorEventHandler();
	
	// Sub-window events
	//[Signal]  public delegate void CloseSettingsWindowEventHandler();
	//[Signal] public delegate void AboutWindowClosedEventHandler();
	
	// Signals Associated with InputActions
	[Signal] public delegate void NewSessionEventHandler();
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
	
	
	// Signals associated with devices
	[Signal] public delegate void AudioDevicesChangedEventHandler();
	[Signal] public delegate void DisplaysChangedEventHandler();
	[Signal] public delegate void CanvasSizeChangedEventHandler(Vector2I newSize);

	public static event Action<string, int> Logger;

	private HashSet<Node> _connectedTextFields = new HashSet<Node>();

	// The below checks all nodes for text edits and connects the signals for is they are focused. This is primarily to toggle input actions that clash with typing
	public override void _Ready()
	{
		// Scan for existing text fields at startup
		ScanForTextFields(GetTree().Root);

		// Listen for new nodes added dynamically
		GetTree().NodeAdded += OnNodeAdded;
		GetTree().NodeRemoved += OnNodeRemoved;
	}

	public static void StaticLog(string s, int i)
	{
		// TODO: This can be made static in the future, will require changing all calls everywhere though!
		Logger?.Invoke("hi", 1);
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
		if (node is LineEdit or TextEdit)
		{
			ConnectFocusSignals(node);
		}
	}
	
	private void OnNodeRemoved(Node node)
	{
		if (node is LineEdit or TextEdit)
		{
			DisonnectFocusSignals(node);
		}
	}

	private void ConnectFocusSignals(Node node)
	{
		if (node is Control textField && !_connectedTextFields.Contains(node))
		{
			textField.FocusEntered += FocusEntered;
			textField.FocusExited += FocusExited;
			_connectedTextFields.Add(node);
		}
	}
	
	private void DisonnectFocusSignals(Node node)
	{
		if (_connectedTextFields.Contains(node) && node is Control textField)
		{
			textField.FocusEntered -= FocusEntered;
			textField.FocusExited -= FocusExited;
			_connectedTextFields.Remove(node);
		}
	}

	private void FocusEntered()
	{
		EmitSignal(SignalName.TextEditFocusEntered);
		GD.Print($"Not Listening");
	}

	private void FocusExited()
	{
		EmitSignal(SignalName.TextEditFocusExited);
		GD.Print($"Listening");
	}
	
}
