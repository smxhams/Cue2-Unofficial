using Godot;
using System;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;

public partial class LauncherHandles : Control
{
	//Variables

	private bool _dragging = false;
	private bool _resizing = false;
	private Vector2I _intitialMouse;
	private Vector2I _initialWindow;

	private int _offsetX;
	private int _offsetY;
	private Control _resizeNode;
	private int _windowNumber;

	private Vector2I _dragOffset;

	private Vector2I _minWindowSize = new Vector2I(600, 370);



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
			if (_resizing == false)
			{
				_resizeNode = @node;
			}	
			_resizing = @event.IsPressed();	
			GD.Print(DisplayServer.GetWindowList()[0]);
		}
	}

	private void _on_drag_bar_gui_input(InputEvent @event){
		if (@event is InputEventMouseButton){
			_windowNumber = GetWindow().GetWindowId();
			if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen) {
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed, _windowNumber);
			}
			_intitialMouse = DisplayServer.MouseGetPosition();
			_initialWindow = DisplayServer.WindowGetPosition(_windowNumber);
			_offsetX = (int)_initialWindow[0] - (int)_intitialMouse[0];
			_offsetY = (int)_initialWindow[1] - (int)_intitialMouse[1];
			_dragging = @event.IsPressed();
			
		}
	}

	public override void _Process(double delta)
	{
		if (_resizing)
		{
			if (_resizeNode == GetNode<Node>("Right")){
				DisplayServer.WindowSetSize(new Vector2I((int)GetLocalMousePosition()[0], DisplayServer.WindowGetSize(_windowNumber)[1]), _windowNumber);
			}
			if (_resizeNode == GetNode<Node>("Bottom")){
				DisplayServer.WindowSetSize(new Vector2I(DisplayServer.WindowGetSize(_windowNumber)[0], (int)GetLocalMousePosition()[1]), _windowNumber);
			}
			if (_resizeNode == GetNode<Node>("Corner")){
				DisplayServer.WindowSetSize(new Vector2I((int)GetLocalMousePosition()[0], (int)GetLocalMousePosition()[1]), _windowNumber);
			}
			if (DisplayServer.WindowGetSize()[0] < _minWindowSize[0]){
				DisplayServer.WindowSetSize(new Vector2I(_minWindowSize[0], DisplayServer.WindowGetSize(_windowNumber)[1]), _windowNumber);
			}
			if (DisplayServer.WindowGetSize()[1] < _minWindowSize[1]){
				DisplayServer.WindowSetSize(new Vector2I(DisplayServer.WindowGetSize(_windowNumber)[0], _minWindowSize[1]), _windowNumber);
			}
		}

		if (_dragging)
		{
			DisplayServer.WindowSetPosition(new Vector2I(DisplayServer.MouseGetPosition()[0] + _offsetX, DisplayServer.MouseGetPosition()[1] + _offsetY), _windowNumber);
		}
	}
}
