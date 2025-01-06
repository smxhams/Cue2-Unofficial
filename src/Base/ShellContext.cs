using Godot;
using System;

using System.Collections;
using System.Collections.Generic;
using System.IO;

// This script is attached to shell context tab


public partial class ShellContext : MarginContainer
{
	// Called when the node enters the scene tree for the first time.
	private GlobalSignals _globalSignals;
	private Cue2.Shared.GlobalData _globalData;

	private Hashtable selectedData;
	private int selectedCueID;

	
	public override void _Ready()
	{
		_globalData = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData");
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
		// Display shell insector
		GetNode<ScrollContainer>("ShellScroll").Visible = true;

		// Init shell inspector and load relevant data
		selectedCueID = @cueID;
		Hashtable shellData = (Hashtable)_globalData.cuelist[cueID];
		selectedData = shellData;
		Node shellObj = (Node)_globalData.cueShellObj[cueID];
		GD.Print(shellObj.GetChildren());
		GetNode<LineEdit>("ShellScroll/ShellVBox/ShellRow2/fileURL").Text = (string)selectedData["filepath"];
		GetNode<LineEdit>("ShellScroll/ShellVBox/ShellRow1/CueNum").Text = (string)selectedData["cueNum"];
		GetNode<LineEdit>("ShellScroll/ShellVBox/ShellRow1/ShellName").Text = (string)selectedData["name"];

	}

	private void _on_button_select_file_pressed()
	{
		GetNode<FileDialog>("/root/Cue2_Base/FileDialog").Visible = true;
	}

	private void file_selected(string @path)
	{
		String newPath = Path.Combine("res://Files/", Path.GetFileName(@path));
		GD.Print(@path + "    :    " + newPath);
		//DirAccess.CopyAbsolute((string)@path, (string)newPath);
		GetNode<LineEdit>("ShellScroll/ShellVBox/ShellRow2/fileURL").Text = @path;
		

		((Hashtable)_globalData.cuelist[selectedCueID])["filepath"] = @path;
		var extention = Path.GetExtension(newPath);
		if (extention == ".wav")
		{
			((Hashtable)_globalData.cuelist[selectedCueID])["type"] = "Audio";
		}
		if (extention == ".mov")
		{
			((Hashtable)_globalData.cuelist[selectedCueID])["type"] = "Video";
		}
		if (extention == ".mp4")
		{
			((Hashtable)_globalData.cuelist[selectedCueID])["type"] = "Video";
		}

		GD.Print(((Hashtable)_globalData.cuelist[selectedCueID])["filepath"]);
		//GetNode<Label>("ScrollContainer/VBoxContainer/HBoxContainer/CurrentType").Text = (string)((Hashtable)_globalData.cuelist[selectedCueID])["type"];

	}

	// Handling the updating of feilds
	private void _onCueNumTextChanged(String data)
	{
		// Update GD
		((Hashtable)_globalData.cuelist[selectedCueID])["cueNum"] = data;
		//_globalSignals.EmitSignal(nameof(GlobalSignals.UpdateShellBar), selectedCueID);

		//Directly update shell bar (This might be a terrible way of doing it)
		Hashtable shellData = (Hashtable)_globalData.cuelist[selectedCueID];
		selectedData = shellData;
		Node shellObj = (Node)_globalData.cueShellObj[selectedCueID];
		shellObj.GetChild(1).GetChild(0).GetChild<LineEdit>(2).Text = data;

	}
	private void _onShellNameTextChanged(String data)
	{
		// Update GD
		((Hashtable)_globalData.cuelist[selectedCueID])["name"] = data;
		//_globalSignals.EmitSignal(nameof(GlobalSignals.UpdateShellBar), selectedCueID);

		//Directly update shell bar (This might be a terrible way of doing it)
		Hashtable shellData = (Hashtable)_globalData.cuelist[selectedCueID];
		selectedData = shellData;
		Node shellObj = (Node)_globalData.cueShellObj[selectedCueID];
		shellObj.GetChild(1).GetChild(0).GetChild<LineEdit>(3).Text = data;

	}
}
