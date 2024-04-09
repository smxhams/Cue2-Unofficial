using Godot;
using System;
using System.Collections;
using System.Collections.Generic;

public partial class CueList : Control
{

	private GlobalData _globalData;
	public Hashtable cuelist = new Hashtable();
	private Variant cueCount;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		cueCount = _globalData.cueCount;	
	}

	private void _on_add_shell_pressed()
	{
		addShell();
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void addShell()
	{
		

		var shellBarScene = GD.Load<PackedScene>("res://src/Base/shell_bar.tscn");
		var shellBar = shellBarScene.Instantiate();
		var container = GetNode<VBoxContainer>("CueContainer");
		container.AddChild(shellBar);
		shellBar.GetChild(1).GetChild<LineEdit>(2).Text = _globalData.cueCount.ToString();
		
		var newShell = new Hashtable()
		{
			{"id", _globalData.cueCount},
			{"name", "New Shell"},
			{"type", null},
			{"shellObj", shellBar},
			{"filepath", ""}
		};
		_globalData.cuelist[_globalData.cueCount] = (Hashtable)newShell;
		//Shift Add bar to bottom of cue list
		//container.MoveChild(shellBar, cueCount);
		_globalData.cueCount = _globalData.cueCount + 1;
	}
}
