using Godot;
using System;

public partial class LaunchManager : Control
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{

	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}


	private void _on_exit_pressed(){
		GetTree().Quit();
	}

	private void _on_new_pressed(){
		GD.Print("New Show");
	}
}
