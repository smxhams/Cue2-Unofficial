using Godot;
using System;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;

public partial class launcherHandles : Control
{
	//Variables

	private bool dragging = false;
	private bool resizing = false;
	private Vector2I intitialMouse;
	private Vector2I initialWindow;

	private int offsetX;
	private int offsetY;
	private Control resize_node;
	private int window_number;

	private Vector2I dragOffset;

	private Vector2I minWindowSize = new Vector2I(600, 370);



	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	private void _on_right_gui_input(InputEvent @event){
		if (@event is InputEventMouseButton)
		{
			_gui_input_handling(@event, GetNode<Control>("Right"));
		}
	}

	private void _on_bottom_gui_input(InputEvent @event){
		if (@event is InputEventMouseButton)
		{
			_gui_input_handling(@event, GetNode<Control>("Bottom"));
		}
	}

	private void _on_corner_gui_input(InputEvent @event){
		if (@event is InputEventMouseButton)
		{
			_gui_input_handling(@event, GetNode<Control>("Corner"));
		}
	}

	private void _gui_input_handling(InputEvent @event, Control @node){
		if (@event is InputEventMouseButton mouseEvent && mouseEvent.ButtonIndex == MouseButton.Left)
		{
			if (resizing == false)
			{
				resize_node = @node;
			}	
			resizing = @event.IsPressed();	
			GD.Print(DisplayServer.GetWindowList()[0]);
		}
	}

	private void _on_drag_bar_gui_input(InputEvent @event){
		if (@event is InputEventMouseButton){
			window_number = GetWindow().GetWindowId();
			if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen) {
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed, window_number);
			}
			intitialMouse = DisplayServer.MouseGetPosition();
			initialWindow = DisplayServer.WindowGetPosition(window_number);
			offsetX = (int)initialWindow[0] - (int)intitialMouse[0];
			offsetY = (int)initialWindow[1] - (int)intitialMouse[1];
			dragging = @event.IsPressed();
			
		}
	}

	public override void _Process(double delta)
	{
		if (resizing)
		{
			if (resize_node == GetNode<Node>("Right")){
				DisplayServer.WindowSetSize(new Vector2I((int)GetLocalMousePosition()[0], DisplayServer.WindowGetSize(window_number)[1]), window_number);
			}
			if (resize_node == GetNode<Node>("Bottom")){
				DisplayServer.WindowSetSize(new Vector2I(DisplayServer.WindowGetSize(window_number)[0], (int)GetLocalMousePosition()[1]), window_number);
			}
			if (resize_node == GetNode<Node>("Corner")){
				DisplayServer.WindowSetSize(new Vector2I((int)GetLocalMousePosition()[0], (int)GetLocalMousePosition()[1]), window_number);
			}
			if (DisplayServer.WindowGetSize()[0] < minWindowSize[0]){
				DisplayServer.WindowSetSize(new Vector2I(minWindowSize[0], DisplayServer.WindowGetSize(window_number)[1]), window_number);
			}
			if (DisplayServer.WindowGetSize()[1] < minWindowSize[1]){
				DisplayServer.WindowSetSize(new Vector2I(DisplayServer.WindowGetSize(window_number)[0], minWindowSize[1]), window_number);
			}
		}

		if (dragging)
		{
			DisplayServer.WindowSetPosition(new Vector2I(DisplayServer.MouseGetPosition()[0] + offsetX, DisplayServer.MouseGetPosition()[1] + offsetY), window_number);
		}
	}
}
