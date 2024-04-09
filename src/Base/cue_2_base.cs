using Godot;
using System;
using System.Collections;

public partial class cue_2_base : Control
{
	private Node setWin;
	private GlobalSignals _globalSignals;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		//Connect global signals
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_globalSignals.CloseSettingsWindow += close_settings_window;
		_globalSignals.ShellSelected += shell_selected;
	}



	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void _on_settings_toggled(Boolean @toggle){
		if (@toggle == true){
			if (setWin == null){
				var settings = GD.Load<PackedScene>("res://src/Base/settings.tscn");
				setWin = settings.Instantiate();
				AddChild(setWin);
				}
			else {
				setWin.GetWindow().Show();
			}
			
		}
		if (@toggle == false){
			setWin.GetWindow().Hide();
		}
	}
	private void close_settings_window(){ //From global signal, emitted by close button of settings window.
		GetNode<Button>("MarginContainer/BoxContainer/HeaderContainer/Settings").ButtonPressed = false;
		
	}
	private void shell_selected(int @cueID)
	{ //From global signal, emitted by close button of settings window.
		GD.Print("Shell Selected: " + @cueID);
		
	}

	private void _on_go_pressed()
	{
		go();
	}

	public void go()
	{
		GD.Print("GOOOOO");
	}


}
