using Godot;

using System.IO;
using Cue2.Base.Classes;

// This script is attached to shell context tab

namespace Cue2.Base;
public partial class ShellContext : MarginContainer
{
	// Called when the node enters the scene tree for the first time.
	private GlobalSignals _globalSignals;
	private Shared.GlobalData _globalData;
	
	private int _focusedCueId;

	private Cue _focusedCue;

	
	public override void _Ready()
	{
		_globalData = GetNode<Shared.GlobalData>("/root/GlobalData");
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
		// Display shell options in PanelContainer
		GetNode<ScrollContainer>("ShellScroll").Visible = true;
		_focusedCue = CueList.FetchCueFromId(cueId);
		// Init shell inspector and load relevant data
		_focusedCueId = cueId;
		
		GetNode<LineEdit>("ShellScroll/ShellVBox/ShellRow2/fileURL").Text = _focusedCue.FilePath;
		GetNode<LineEdit>("ShellScroll/ShellVBox/ShellRow1/CueNum").Text = _focusedCue.CueNum;
		GetNode<LineEdit>("ShellScroll/ShellVBox/ShellRow1/ShellName").Text = _focusedCue.Name;

	}

	private void _on_button_select_file_pressed()
	{
		GetNode<FileDialog>("/root/Cue2_Base/FileDialog").Visible = true;
	}

	private void file_selected(string @path) // On Signal from file selection window
	{
		var newPath = Path.Combine("res://Files/", Path.GetFileName(@path));
		GD.Print(@path + "    :    " + newPath);
		GetNode<LineEdit>("ShellScroll/ShellVBox/ShellRow2/fileURL").Text = @path;
		
		_focusedCue.FilePath = @path;
		var fileExtension = Path.GetExtension(newPath);
		_focusedCue.Type = fileExtension switch // Sets type based on extension
		{
			".wav" => "Audio",
			".mp4" or ".mov" or ".avi" or ".mpg" => "Video",
			_ => _focusedCue.Type
		};

		GD.Print(_focusedCue.FilePath);
	}
	
	// Handling the updating of fields
	private void _onCueNumTextChanged(string data)
	{
		_focusedCue.CueNum = data; // Updates Cue with user input
		var shellObj = _focusedCue.ShellBar;
		//Directly update shell bar (This might be a terrible way of doing it)
		shellObj.GetChild(1).GetChild(0).GetChild<LineEdit>(2).Text = data;
	}
	private void _onShellNameTextChanged(string data)
	{
		// Update GD
		_focusedCue.Name = data;
	
		//Directly update shell bar (This might be a terrible way of doing it)
		var shellObj = _focusedCue.ShellBar;
		shellObj.GetChild(1).GetChild(0).GetChild<LineEdit>(3).Text = data;
	}
}
