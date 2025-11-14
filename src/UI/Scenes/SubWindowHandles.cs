using Godot;
using System;
using Cue2.Shared;

namespace Cue2.UI.Scenes;

public partial class SubWindowHandles : Control
{
	private GlobalData _globalData;
	
	private int _windowId;
	
	//Handles
	private Control _headerHandle;
	private Control _rightHandle;
	private Control _leftHandle;
	private Control _bottomHandle;
	private Control _bottomRightHandle;
	private Control _bottomLeftHandle;
	private Control _topRightHandle;
	private Control _topLeftHandle;



	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		
		_windowId = GetWindow().GetWindowId();

		_headerHandle = GetNode<Control>("%HeaderHandle");
		_rightHandle = GetNode<Control>("%RightHandle");
		_leftHandle = GetNode<Control>("%LeftHandle");
		_bottomHandle = GetNode<Control>("%BottomHandle");
		_bottomRightHandle = GetNode<Control>("%BottomRightCornerHandle");
		_bottomLeftHandle = GetNode<Control>("%BottomLeftCornerHandle");
		_topRightHandle = GetNode<Control>("%TopRightCornerHandle");
		_topLeftHandle = GetNode<Control>("%TopLeftCornerHandle");

		_headerHandle.GuiInput += OnHeaderHandleGuiInput;
		_rightHandle.GuiInput += OnRightHandleGuiInput;
		_leftHandle.GuiInput += OnLeftHandleGuiInput;
		_bottomHandle.GuiInput += OnBottomHandleGuiInput;
		_topRightHandle.GuiInput += OnTopRightHandleGuiInput;
		_bottomLeftHandle.GuiInput += OnBottomLeftHandleGuiInput;
		_topLeftHandle.GuiInput += OnTopLeftHandleGuiInput;
		_bottomRightHandle.GuiInput += OnBottomRightHandleGuiInput;
		
		GetNode<Button>("%ExitButton").Pressed += _onExitButtonPressed;
	}
	
	
	
	private void _onExitButtonPressed()
	{
		GetParent().GetParent().QueueFree();
	}

	private void OnHeaderHandleGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseEvent)
		{
			if (mouseEvent.DoubleClick && mouseEvent.ButtonIndex == MouseButton.Left)
			{
				// Toggle maximize on double click
				var window = GetWindow();
				if (window.Mode == Window.ModeEnum.Maximized)
				{
					window.Mode = Window.ModeEnum.Windowed;
				}
				else
				{
					window.Mode = Window.ModeEnum.Maximized;
				}
			}
			else if (mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
			{
				DisplayServer.WindowStartDrag(_windowId);
			}
		}
	}

	private void OnRightHandleGuiInput(InputEvent @event){
		if (@event is InputEventMouseButton { Pressed: true })
		{
			DisplayServer.WindowStartResize(DisplayServer.WindowResizeEdge.Right, _windowId);
		}
	}

	private void OnLeftHandleGuiInput(InputEvent @event){
		if (@event is InputEventMouseButton { Pressed: true })
		{
			DisplayServer.WindowStartResize(DisplayServer.WindowResizeEdge.Left, _windowId);
			
		}
	}
	
	private void OnBottomHandleGuiInput(InputEvent @event){
		if (@event is InputEventMouseButton { Pressed: true })
		{
			DisplayServer.WindowStartResize(DisplayServer.WindowResizeEdge.Bottom, _windowId);
		}
	}
	
	private void OnBottomRightHandleGuiInput(InputEvent @event){
		if (@event is InputEventMouseButton { Pressed: true })
		{
			DisplayServer.WindowStartResize(DisplayServer.WindowResizeEdge.BottomRight, _windowId);
		}
	}
	
	private void OnBottomLeftHandleGuiInput(InputEvent @event){
		if (@event is InputEventMouseButton { Pressed: true })
		{
			DisplayServer.WindowStartResize(DisplayServer.WindowResizeEdge.BottomLeft, _windowId);
		}
	}
	
	private void OnTopRightHandleGuiInput(InputEvent @event){
		if (@event is InputEventMouseButton { Pressed: true })
		{
			DisplayServer.WindowStartResize(DisplayServer.WindowResizeEdge.TopRight, _windowId);
		}
	}
	
	private void OnTopLeftHandleGuiInput(InputEvent @event){
		if (@event is InputEventMouseButton { Pressed: true })
		{
			DisplayServer.WindowStartResize(DisplayServer.WindowResizeEdge.TopLeft, _windowId);
		}
	}
}
