using Godot;
using System;
using System.Collections;
using System.Collections.Generic;

public partial class GlobalData : Node
{
	public Hashtable cuelist = new Hashtable();
	public int cueCount;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
