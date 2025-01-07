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

	private Hashtable _selectedData;
	private int _selectedCueId;

	
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

	private void shell_selected(int cueId)
	{
		// Display shell insector
		GetNode<ScrollContainer>("ShellScroll").Visible = true;

		// Init shell inspector and load relevant data
		_selectedCueId = cueId;
		Hashtable shellData = (Hashtable)_globalData.Cuelist[cueId];
		_selectedData = shellData;
		Node shellObj = (Node)_globalData.CueShellObj[cueId];
		GD.Print(shellObj.GetChildren());
		GetNode<LineEdit>("ShellScroll/ShellVBox/ShellRow2/fileURL").Text = (string)_selectedData["filepath"];
		GetNode<LineEdit>("ShellScroll/ShellVBox/ShellRow1/CueNum").Text = (string)_selectedData["cueNum"];
		GetNode<LineEdit>("ShellScroll/ShellVBox/ShellRow1/ShellName").Text = (string)_selectedData["name"];

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
		

		((Hashtable)_globalData.Cuelist[_selectedCueId])["filepath"] = @path;
		var extention = Path.GetExtension(newPath);
		if (extention == ".wav")
		{
			((Hashtable)_globalData.Cuelist[_selectedCueId])["type"] = "Audio";
		}
		if (extention == ".mov")
		{
			((Hashtable)_globalData.Cuelist[_selectedCueId])["type"] = "Video";
		}
		if (extention == ".mp4")
		{
			((Hashtable)_globalData.Cuelist[_selectedCueId])["type"] = "Video";
		}

		GD.Print(((Hashtable)_globalData.Cuelist[_selectedCueId])["filepath"]);
		//GetNode<Label>("ScrollContainer/VBoxContainer/HBoxContainer/CurrentType").Text = (string)((Hashtable)_globalData.cuelist[selectedCueID])["type"];

	}

	// Handling the updating of feilds
	private void _onCueNumTextChanged(String data)
	{
		// Update GD
		((Hashtable)_globalData.Cuelist[_selectedCueId])["cueNum"] = data;
		//_globalSignals.EmitSignal(nameof(GlobalSignals.UpdateShellBar), selectedCueID);

		//Directly update shell bar (This might be a terrible way of doing it)
		Hashtable shellData = (Hashtable)_globalData.Cuelist[_selectedCueId];
		_selectedData = shellData;
		Node shellObj = (Node)_globalData.CueShellObj[_selectedCueId];
		shellObj.GetChild(1).GetChild(0).GetChild<LineEdit>(2).Text = data;

	}
	private void _onShellNameTextChanged(String data)
	{
		// Update GD
		((Hashtable)_globalData.Cuelist[_selectedCueId])["name"] = data;
		//_globalSignals.EmitSignal(nameof(GlobalSignals.UpdateShellBar), selectedCueID);

		//Directly update shell bar (This might be a terrible way of doing it)
		Hashtable shellData = (Hashtable)_globalData.Cuelist[_selectedCueId];
		_selectedData = shellData;
		Node shellObj = (Node)_globalData.CueShellObj[_selectedCueId];
		shellObj.GetChild(1).GetChild(0).GetChild<LineEdit>(3).Text = data;

	}
}
