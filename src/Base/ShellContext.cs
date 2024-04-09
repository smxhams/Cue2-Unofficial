using Godot;
using System;

using System.Collections;
using System.Collections.Generic;

public partial class ShellContext : MarginContainer
{
	// Called when the node enters the scene tree for the first time.
	private GlobalSignals _globalSignals;
	private GlobalData _globalData;

	private Hashtable selectedData;
	private int selectedCueID;

	
	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		//Connect global signals
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_globalSignals.ShellSelected += shell_selected;
		_globalSignals.FileSelected += file_selected;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void shell_selected(int @cueID)
	{
		GetNode<GridContainer>("GridContainer").Visible = true;

		selectedCueID = @cueID;
		var shellData = (Hashtable)_globalData.cuelist[cueID];
		selectedData = shellData;
		var shellObj = (Node)shellData["shellObj"];
		GD.Print(shellObj.GetChildren());
		GetNode<Label>("GridContainer/Label").Text = "Shell ID: " + shellData["id"];
		GetNode<Label>("GridContainer/fileURL").Text = (string)selectedData["filepath"];

	}

	private void _on_button_select_file_pressed()
	{
		GetNode<FileDialog>("/root/Cue2_Base/FileDialog").Visible = true;
	}

	private void file_selected(string @path)
	{
		GetNode<Label>("GridContainer/fileURL").Text = @path;
		((Hashtable)_globalData.cuelist[selectedCueID])["filepath"] = @path;
		GD.Print(((Hashtable)_globalData.cuelist[selectedCueID])["filepath"]);

	}
}
