using Cue2.Shared;
using Godot;

namespace Cue2.UI.Scenes.SubWindows;

/// <summary>
/// Shared title-bar drag and edge-resize handles for borderless sub-windows
/// (Settings, Log, About, etc.).
/// </summary>
/// <remarks>
/// Window IDs are resolved at interaction time. Sub-windows often start hidden
/// (to apply size/position without flicker); capturing <see cref="Window.GetWindowId"/>
/// in <c>_Ready</c> can yield an ID that DisplayServer does not yet track, which
/// breaks <see cref="DisplayServer.WindowStartDrag"/> / resize.
/// </remarks>
public partial class SubWindowHandles : Control
{
	private GlobalData _globalData;

	//Handles
	private Control _headerHandle;
	private Control _rightHandle;
	private Control _leftHandle;
	private Control _bottomHandle;
	private Control _bottomRightHandle;
	private Control _bottomLeftHandle;
	private Control _topRightHandle;
	private Control _topLeftHandle;

	/// <summary>
	/// Wires handle input. Does not cache a DisplayServer window id — see class remarks.
	/// </summary>
	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");

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

		GetNode<Button>("%ExitButton").Pressed += OnExitButtonPressed;
	}

	/// <summary>
	/// Returns the host <see cref="Window"/>'s DisplayServer id, or -1 if unavailable.
	/// Prefers the parent Window (this control is always a direct child of the borderless window).
	/// </summary>
	private int ResolveWindowId()
	{
		// Prefer explicit parent Window — more reliable than GetWindow() while a sub-window
		// is still finishing registration with DisplayServer after Show().
		Window window = GetParent() as Window ?? GetWindow();
		if (window == null || !IsInstanceValid(window))
		{
			return -1;
		}

		// Hidden windows may not be registered with DisplayServer yet.
		if (!window.Visible)
		{
			return -1;
		}

		return window.GetWindowId();
	}

	private void OnExitButtonPressed()
	{
		GetParent().QueueFree();
	}

	private void OnHeaderHandleGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseEvent)
		{
			if (mouseEvent.DoubleClick && mouseEvent.ButtonIndex == MouseButton.Left)
			{
				// Toggle maximize on double click
				var window = GetWindow();
				if (window == null) return;

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
				int windowId = ResolveWindowId();
				if (windowId >= 0)
				{
					DisplayServer.WindowStartDrag(windowId);
				}
			}
		}
	}

	private void OnRightHandleGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true })
		{
			StartResize(DisplayServer.WindowResizeEdge.Right);
		}
	}

	private void OnLeftHandleGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true })
		{
			StartResize(DisplayServer.WindowResizeEdge.Left);
		}
	}

	private void OnBottomHandleGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true })
		{
			StartResize(DisplayServer.WindowResizeEdge.Bottom);
		}
	}

	private void OnBottomRightHandleGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true })
		{
			StartResize(DisplayServer.WindowResizeEdge.BottomRight);
		}
	}

	private void OnBottomLeftHandleGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true })
		{
			StartResize(DisplayServer.WindowResizeEdge.BottomLeft);
		}
	}

	private void OnTopRightHandleGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true })
		{
			StartResize(DisplayServer.WindowResizeEdge.TopRight);
		}
	}

	private void OnTopLeftHandleGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true })
		{
			StartResize(DisplayServer.WindowResizeEdge.TopLeft);
		}
	}

	private void StartResize(DisplayServer.WindowResizeEdge edge)
	{
		int windowId = ResolveWindowId();
		if (windowId >= 0)
		{
			DisplayServer.WindowStartResize(edge, windowId);
		}
	}
}
