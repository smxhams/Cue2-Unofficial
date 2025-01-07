using Godot;
using System;
using System.Collections;
using System.Collections.Generic;

public partial class ErrorHandler : Node
{

	private GlobalSignals _globalSignals;

	public SortedList<int, string> ErrorLog = new SortedList<int, string>();
	private int _errorCount;


	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_globalSignals.ErrorLog += error_event;

		_errorCount = 0;

	
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void error_event(String @error, int @type)
	{
		var printout = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss tt") + "  :  " + @error;
		GetNode<Label>("/root/Cue2_Base/MarginContainer/BoxContainer/BottomContainer/ErrorPrintout").Text = printout;
		ErrorLog.Add(_errorCount, printout);
		_errorCount = _errorCount + 1;
		GetNode<Label>("/root/Cue2_Base/MarginContainer/BoxContainer/BottomContainer/Log").Text = "Log " + _errorCount;

		/// TODO Type casts colour and urgency

		GD.Print(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss tt") + "  :  " + @error);
	}
}
