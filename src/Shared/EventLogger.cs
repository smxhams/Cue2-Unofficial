using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using Cue2.Shared;
using SDL3;

public partial class EventLogger : Node
{

	private GlobalSignals _globalSignals;

	private List<string> _logList = new List<string>();
	private static int _logCount;

	/*
	 * Receives log signals to register in log list. Each logged event has a "type" refering to what it indicates. See LogType enum.
	 * 0 = Information (white text and default)
	 * 1 = Warning (yellow text)
	 * 2 = System error (red text)
	 * 3 = Alert (red text, flash window border red) This is to only be called for issues that may effect playback. Ie Devices disconnecting, network dropout etc.
	 */
	
	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_globalSignals.Log += _logEvent;

		_logCount = 0;
	}
	

	private void _logEvent(String @logString, int @type)
	{
		var typeString = _getLogTypeName(@type);
		var printout = typeString + "  :  " + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss tt") + "  :  " + @logString;
		_logList.Add(printout);
		_logCount++;
		_globalSignals.EmitSignal(nameof(GlobalSignals.LogUpdated), printout, @type);
		GD.Print(printout);
	}

	private string _getLogTypeName(int type)
	{
		if (Enum.IsDefined(typeof(LogType), type))
		{
			return ((LogType)type).ToString();
		}
		return "Unknown";
	}
	
	public static int GetLogCount()
	{
		return _logCount;
	}
}
