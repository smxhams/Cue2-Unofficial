using Godot;
using System;
using System.Numerics;

public partial class OutputOverrides : GridContainer
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

	private void _on_expand_pressed(){
		var window_number = GetWindow().GetWindowId();
		//if (DisplayServer.WindowGetSize(window_number) != DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen(window_number))){
			//DisplayServer.WindowSetPosition(DisplayServer.ScreenGetPosition(DisplayServer.WindowGetCurrentScreen(window_number)), window_number);
			//DisplayServer.WindowSetSize(DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen(window_number)), window_number);
		if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Fullscreen){
			GD.Print("Maximise");
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen, window_number);
		}
		else {
			GD.Print("Minimise");
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed, window_number);
			//DisplayServer.WindowSetSize(new Vector2I(600,400), window_number);
		}
	}
}
