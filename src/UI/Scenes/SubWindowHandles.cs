using Godot;
using System;

namespace Cue2.UI.Scenes;

public partial class SubWindowHandles : Control
{
    	//Variables
	private bool _dragging;
	private bool _resizing;
	private Vector2I _initialMouse;
	private Vector2I _initialWindow;

	private int _offsetX;
	private int _offsetY;
	private Control _resizeNode;
	private int _windowNumber;

	private Vector2I _dragOffset;

	private Vector2I _minWindowSize = new Vector2I(600, 370);
	
	//Handles
	private Control _rightHandle;
	private Control _leftHandle;
	private Control _bottomHandle;
	private Control _cornerHandle;
	private Control _dragBar;



	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_windowNumber = GetWindow().GetWindowId();

		_rightHandle = GetNode<Control>("%RightHandle"); 
		_bottomHandle = GetNode<Control>("%BottomHandle"); 
		_cornerHandle = GetNode<Control>("%CornerHandle"); 
		_dragBar = GetNode<Control>("%DragBar");
		
		_rightHandle.GuiInput += _onRightGuiInput; 
		_bottomHandle.GuiInput += _onBottomGuiInput; 
		_cornerHandle.GuiInput += _onCornerGuiInput; 
		_dragBar.GuiInput += _onDragBarGuiInput;
		
		GetNode<Button>("%ExitButton").Pressed += _onExitButtonPressed;
	}
	
	
	
	private void _onExitButtonPressed()
	{
		GetParent().QueueFree();
	}

	private void _onRightGuiInput(InputEvent @event){
		if (@event is InputEventMouseButton)
		{
			_guiInputHandling(@event, _rightHandle);
		}
	}

	private void _onBottomGuiInput(InputEvent @event){
		if (@event is InputEventMouseButton)
		{
			_guiInputHandling(@event, _bottomHandle);
		}
	}

	private void _onCornerGuiInput(InputEvent @event){
		if (@event is InputEventMouseButton)
		{
			_guiInputHandling(@event, _cornerHandle);
		}
	}

	private void _guiInputHandling(InputEvent @event, Control @node){
		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left })
		{
			if (_resizing == false)
			{
				_resizeNode = @node;
			}	
			_resizing = @event.IsPressed();	
			GD.Print(DisplayServer.GetWindowList()[0]);
		}
	}

	private void _onDragBarGuiInput(InputEvent @event){
		if (@event is InputEventMouseButton){
			_windowNumber = GetWindow().GetWindowId();
			if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen) {
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed, _windowNumber);
			}
			_initialMouse = DisplayServer.MouseGetPosition();
			_initialWindow = DisplayServer.WindowGetPosition(_windowNumber);
			_offsetX = _initialWindow[0] - _initialMouse[0];
			_offsetY = _initialWindow[1] - _initialMouse[1];
			_dragging = @event.IsPressed();
			
		}
	}

	public override void _Process(double delta)
	{
		if (_resizing)
		{
			if (_resizeNode == _rightHandle){
				DisplayServer.WindowSetSize(new Vector2I((int)GetLocalMousePosition()[0], DisplayServer.WindowGetSize(_windowNumber)[1]), _windowNumber);
			}
			if (_resizeNode == _bottomHandle){
				DisplayServer.WindowSetSize(new Vector2I(DisplayServer.WindowGetSize(_windowNumber)[0], (int)GetLocalMousePosition()[1]), _windowNumber);
			}
			if (_resizeNode == _cornerHandle){
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
