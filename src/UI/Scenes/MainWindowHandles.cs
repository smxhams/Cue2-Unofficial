using Cue2.Shared;
using Cue2.UI.Utilities;
using Godot;

namespace Cue2.UI.Scenes;

/// <summary>
/// Borderless main-window title drag, edge/corner resize, maximize toggle, and geometry persistence.
/// </summary>
/// <remarks>
/// Interactive resize/drag must leave maximized/fullscreen first. Starting an OS resize while still
/// maximized leaves a hybrid "maximized but not full-screen" state and breaks UI layout.
/// </remarks>
public partial class MainWindowHandles : Control
{
	private GlobalData _globalData;
	private GlobalSignals _globalSignals;

	private static readonly Vector2I MinWindowSize = new Vector2I(600, 370);

	// Debounce timer for window geometry saves (persist after resize/move settles)
	private Timer _resizeSaveTimer;
	private Vector2I _lastKnownPosition;

	private Color _originalBorderColor;
	private bool _isFading;
	private float _fadeProgress;
	private Color _highlightColor;
	private StyleBoxFlat _borderStylebox;

	private Control _headerHandle;
	private Control _rightHandle;
	private Control _topHandle;
	private Control _leftHandle;
	private Control _bottomHandle;
	private Control _bottomRightHandle;
	private Control _bottomLeftHandle;
	private Control _topRightHandle;
	private Control _topLeftHandle;

	/// <summary>
	/// Wires handles, restores last geometry/maximized state, and starts persistence listeners.
	/// </summary>
	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

		_resizeSaveTimer = new Timer { OneShot = true, WaitTime = 0.25f };
		_resizeSaveTimer.Timeout += SaveCurrentWindowState;
		AddChild(_resizeSaveTimer);

		var window = GetWindow();
		window.MinSize = MinWindowSize;
		window.MaxSize = ComputeVirtualDesktopSize();

		RestoreWindowFromUserData(window);
		SaveCurrentWindowState();
		_lastKnownPosition = window.Position;

		_globalSignals.LogAlert += OnAlertReceived;

		var border = GetNode<Panel>("%Border");
		_borderStylebox = border.GetThemeStylebox("panel") as StyleBoxFlat;
		if (_borderStylebox != null)
		{
			_originalBorderColor = _borderStylebox.BorderColor;
			_highlightColor = GlobalStyles.Danger;
		}

		_headerHandle = GetNode<Control>("%HeaderHandle");
		_rightHandle = GetNode<Control>("%RightHandle");
		_topHandle = GetNode<Control>("%TopHandle");
		_leftHandle = GetNode<Control>("%LeftHandle");
		_bottomHandle = GetNode<Control>("%BottomHandle");
		_bottomRightHandle = GetNode<Control>("%BottomRightCornerHandle");
		_bottomLeftHandle = GetNode<Control>("%BottomLeftCornerHandle");
		_topRightHandle = GetNode<Control>("%TopRightCornerHandle");
		_topLeftHandle = GetNode<Control>("%TopLeftCornerHandle");

		_headerHandle.GuiInput += OnHeaderHandleGuiInput;
		_rightHandle.GuiInput += OnRightHandleGuiInput;
		_topHandle.GuiInput += OnTopHandleGuiInput;
		_leftHandle.GuiInput += OnLeftHandleGuiInput;
		_bottomHandle.GuiInput += OnBottomHandleGuiInput;
		_topRightHandle.GuiInput += OnTopRightHandleGuiInput;
		_bottomLeftHandle.GuiInput += OnBottomLeftHandleGuiInput;
		_topLeftHandle.GuiInput += OnTopLeftHandleGuiInput;
		_bottomRightHandle.GuiInput += OnBottomRightHandleGuiInput;

		window.SizeChanged += OnWindowSizeChanged;
	}

	/// <summary>
	/// Union of all monitor rectangles — upper bound for the main window size.
	/// </summary>
	private static Vector2I ComputeVirtualDesktopSize()
	{
		var screenCount = DisplayServer.GetScreenCount();
		if (screenCount <= 0)
			return new Vector2I(4096, 2160);

		Vector2I minPos = new Vector2I(int.MaxValue, int.MaxValue);
		Vector2I maxPos = new Vector2I(int.MinValue, int.MinValue);
		for (int i = 0; i < screenCount; i++)
		{
			Vector2I pos = DisplayServer.ScreenGetPosition(i);
			Vector2I size = DisplayServer.ScreenGetSize(i);
			minPos = new Vector2I(Mathf.Min(minPos.X, pos.X), Mathf.Min(minPos.Y, pos.Y));
			maxPos = new Vector2I(Mathf.Max(maxPos.X, pos.X + size.X), Mathf.Max(maxPos.Y, pos.Y + size.Y));
		}

		return maxPos - minPos;
	}

	/// <summary>
	/// Restores previous size / position / maximized state from persistent user data.
	/// Position is relative to the display that currently contains the mouse cursor.
	/// </summary>
	private void RestoreWindowFromUserData(Window window)
	{
		var udm = _globalData?.UserDataManager;
		if (udm == null)
			return;

		bool hasNormalSize = udm.LastWindowSize.X >= MinWindowSize.X
			&& udm.LastWindowSize.Y >= MinWindowSize.Y;

		// Apply normal geometry first so un-maximize (or engine restore rect) is sensible.
		if (hasNormalSize)
		{
			window.Size = udm.LastWindowSize;

			int targetScreen = UiUtilities.FindScreenAtPoint(DisplayServer.MouseGetPosition());
			window.Position = UiUtilities.ClampWindowPositionToScreen(
				targetScreen, udm.LastWindowPosition);
		}

		if (udm.WasMaximized)
			window.Mode = Window.ModeEnum.Maximized;
	}

	private async void OnAlertReceived()
	{
		if (_borderStylebox == null)
			return;

		_borderStylebox.BorderColor = _highlightColor;
		await ToSignal(GetTree().CreateTimer(0.5), "timeout");
		_fadeProgress = 0.0f;
		_isFading = true;
	}

	private void OnHeaderHandleGuiInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mouseEvent)
			return;

		if (mouseEvent.DoubleClick && mouseEvent.ButtonIndex == MouseButton.Left)
		{
			var window = GetWindow();
			if (window == null)
				return;

			// Save normal size before maximizing so restore-on-resize has a real rect.
			if (!UiUtilities.IsWindowFillScreen(window))
			{
				_resizeSaveTimer.Stop();
				SaveCurrentWindowState();
			}

			UiUtilities.ToggleMaximize(window);
			SaveCurrentWindowState();
			return;
		}

		if (mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
			StartDrag();
	}

	private void OnRightHandleGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true })
			StartResize(DisplayServer.WindowResizeEdge.Right);
	}

	private void OnTopHandleGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true })
			StartResize(DisplayServer.WindowResizeEdge.Top);
	}

	private void OnLeftHandleGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true })
			StartResize(DisplayServer.WindowResizeEdge.Left);
	}

	private void OnBottomHandleGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true })
			StartResize(DisplayServer.WindowResizeEdge.Bottom);
	}

	private void OnBottomRightHandleGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true })
			StartResize(DisplayServer.WindowResizeEdge.BottomRight);
	}

	private void OnBottomLeftHandleGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true })
			StartResize(DisplayServer.WindowResizeEdge.BottomLeft);
	}

	private void OnTopRightHandleGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true })
			StartResize(DisplayServer.WindowResizeEdge.TopRight);
	}

	private void OnTopLeftHandleGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true })
			StartResize(DisplayServer.WindowResizeEdge.TopLeft);
	}

	/// <summary>
	/// Begins an OS edge resize after ensuring the window is not maximized/fullscreen.
	/// </summary>
	private void StartResize(DisplayServer.WindowResizeEdge edge)
	{
		var window = GetWindow();
		if (window == null || !IsInstanceValid(window))
			return;

		RestoreWindowedIfNeeded(window);

		int windowId = window.GetWindowId();
		if (windowId >= 0)
			DisplayServer.WindowStartResize(edge, windowId);
	}

	/// <summary>
	/// Begins an OS window drag after ensuring the window is not maximized/fullscreen.
	/// </summary>
	private void StartDrag()
	{
		var window = GetWindow();
		if (window == null || !IsInstanceValid(window))
			return;

		RestoreWindowedIfNeeded(window);

		int windowId = window.GetWindowId();
		if (windowId >= 0)
			DisplayServer.WindowStartDrag(windowId);
	}

	/// <summary>
	/// Toggle maximize from chrome buttons. Saves normal geometry before maximizing so
	/// later edge-resize can restore a real windowed rect.
	/// </summary>
	public void ToggleMaximizeFromChrome()
	{
		var window = GetWindow();
		if (window == null || !IsInstanceValid(window))
			return;

		if (!UiUtilities.IsWindowFillScreen(window))
		{
			_resizeSaveTimer.Stop();
			SaveCurrentWindowState();
		}

		UiUtilities.ToggleMaximize(window);
		SaveCurrentWindowState();
	}

	/// <summary>
	/// If the window is maximized or fullscreen, restore last normal geometry and go windowed
	/// before interactive drag/resize. Prevents hybrid maximized layout glitches.
	/// </summary>
	private void RestoreWindowedIfNeeded(Window window)
	{
		if (!UiUtilities.IsWindowFillScreen(window))
			return;

		Vector2I? restoreSize = null;
		Vector2I? restorePos = null;
		var udm = _globalData?.UserDataManager;
		if (udm != null
			&& udm.LastWindowSize.X >= MinWindowSize.X
			&& udm.LastWindowSize.Y >= MinWindowSize.Y)
		{
			restoreSize = udm.LastWindowSize;
			// Prefer the screen the maximized window currently occupies.
			int screen = window.CurrentScreen;
			if (screen < 0)
				screen = UiUtilities.FindScreenAtPoint(DisplayServer.MouseGetPosition());
			restorePos = UiUtilities.ClampWindowPositionToScreen(screen, udm.LastWindowPosition);
		}

		UiUtilities.EnsureWindowedForInteraction(window, restoreSize, restorePos);
		// Mode is no longer maximized — persist immediately so later saves stay consistent.
		SaveCurrentWindowState();
		_lastKnownPosition = window.Position;
	}

	public override void _Process(double delta)
	{
		if (_isFading && _borderStylebox != null)
		{
			_fadeProgress += (float)delta / 1.0f; // 1-second fade duration
			if (_fadeProgress >= 1.0f)
			{
				_fadeProgress = 1.0f;
				_isFading = false;
			}

			_borderStylebox.BorderColor = _highlightColor.Lerp(_originalBorderColor, _fadeProgress);
		}

		// Debounce save for window moves (position changes during drag)
		CheckAndDebounceWindowPosition();
	}

	private void OnWindowSizeChanged()
	{
		var win = GetWindow();
		if (UiUtilities.IsWindowFillScreen(win))
		{
			// Persist maximized/fullscreen flag only (SetWindowState skips size while maximized).
			SaveCurrentWindowState();
			return;
		}

		_resizeSaveTimer.Start();
	}

	/// <summary>
	/// Saves the current window size, relative position (to its display), and maximized state.
	/// </summary>
	private void SaveCurrentWindowState()
	{
		var win = GetWindow();
		if (win == null || !IsInstanceValid(win))
			return;

		bool isMax = UiUtilities.IsWindowFillScreen(win);
		Vector2I size = win.Size;
		Vector2I globalPos = win.Position;
		Vector2I relPos = isMax
			? globalPos
			: UiUtilities.ToScreenRelativePosition(globalPos, size);

		_globalData?.UserDataManager?.SetWindowState(size, relPos, isMax);
	}

	private void CheckAndDebounceWindowPosition()
	{
		var win = GetWindow();
		if (UiUtilities.IsWindowFillScreen(win))
			return;

		Vector2I currentPos = win.Position;
		if (currentPos != _lastKnownPosition)
		{
			_lastKnownPosition = currentPos;
			_resizeSaveTimer.Start();
		}
	}

	public override void _ExitTree()
	{
		var window = GetWindow();
		if (window != null && IsInstanceValid(window))
			window.SizeChanged -= OnWindowSizeChanged;

		if (_headerHandle != null) _headerHandle.GuiInput -= OnHeaderHandleGuiInput;
		if (_rightHandle != null) _rightHandle.GuiInput -= OnRightHandleGuiInput;
		if (_topHandle != null) _topHandle.GuiInput -= OnTopHandleGuiInput;
		if (_leftHandle != null) _leftHandle.GuiInput -= OnLeftHandleGuiInput;
		if (_bottomHandle != null) _bottomHandle.GuiInput -= OnBottomHandleGuiInput;
		if (_bottomRightHandle != null) _bottomRightHandle.GuiInput -= OnBottomRightHandleGuiInput;
		if (_bottomLeftHandle != null) _bottomLeftHandle.GuiInput -= OnBottomLeftHandleGuiInput;
		if (_topRightHandle != null) _topRightHandle.GuiInput -= OnTopRightHandleGuiInput;
		if (_topLeftHandle != null) _topLeftHandle.GuiInput -= OnTopLeftHandleGuiInput;

		if (_globalSignals != null)
			_globalSignals.LogAlert -= OnAlertReceived;

		_resizeSaveTimer?.Stop();
		SaveCurrentWindowState();
	}
}
