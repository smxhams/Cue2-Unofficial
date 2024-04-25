using Godot;
using System;
using System.Collections;
using System.Collections.Generic;

public partial class Error_Handler : Node
{

	private GlobalSignals _globalSignals;

	public SortedList<int, string> error_log = new SortedList<int, string>();
	private int errorCount;


	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_globalSignals.ErrorLog += error_event;

		errorCount = 0;

	
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void error_event(String @error)
	{
		var printout = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss tt") + "  :  " + @error;
		GetNode<Label>("/root/Cue2_Base/MarginContainer/BoxContainer/BottomContainer/ErrorPrintout").Text = printout;
		error_log.Add(errorCount, printout);
		errorCount = errorCount + 1;
		GetNode<Label>("/root/Cue2_Base/MarginContainer/BoxContainer/BottomContainer/Log").Text = "Log " + errorCount;


		GD.Print(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss tt") + "  :  " + @error);
	}
}
