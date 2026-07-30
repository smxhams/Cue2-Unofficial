using Cue2.Shared;
using Cue2.UI.Utilities;
using Godot;

namespace Cue2.UI.Scenes;

/// <summary>
/// Borderless main-window title drag, edge/corner resize, maximize toggle, and geometry persistence.
/// </summary>
/// <remarks>
/// Maximize button / title double-click restore the pre-maximize rect.
/// Edge-resize out of maximize only leaves maximized mode and keeps the current size
/// (no snap back to the pre-maximize rect). Drag out of maximize restores the pre-maximize rect
/// under the cursor (standard desktop behaviour).
/// </remarks>
public partial class MainWindowHandles : Control
{
	private GlobalData _globalData;
	private GlobalSignals _globalSignals;

	private static readonly Vector2I MinWindowSize = new Vector2I(600, 370);

	private Timer _resizeSaveTimer;
	private Vector2I _lastKnownPosition;

	/// <summary>Pre-maximize size/position used only by the maximize toggle (and title drag un-max).</summary>
	private Vector2I _restoreSize = Vector2I.Zero;
	private Vector2I _restorePosition = Vector2I.Zero;
	private bool _hasRestoreRect;

	/// <summary>True while chrome-maximized (engine Mode can lag or go hybrid on borderless).</summary>
	private bool _isMaximized;

	/// <summary>Skip persistence for one frame while mode/geometry is being applied.</summary>
	private bool _suppressGeometrySave;

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

	/// <inheritdoc />
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
	/// </summary>
	private void RestoreWindowFromUserData(Window window)
	{
		var udm = _globalData?.UserDataManager;
		if (udm == null)
			return;

		if (udm.LastWindowSize.X >= MinWindowSize.X && udm.LastWindowSize.Y >= MinWindowSize.Y)
		{
			RememberRestoreRect(udm.LastWindowSize,
				UiUtilities.ClampWindowPositionToScreen(
					UiUtilities.FindScreenAtPoint(DisplayServer.MouseGetPosition()),
					udm.LastWindowPosition));

			window.Size = _restoreSize;
			window.Position = _restorePosition;
		}

		if (udm.WasMaximized)
		{
			_isMaximized = true;
			SetWindowMode(window, DisplayServer.WindowMode.Maximized);
		}
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
			ToggleMaximizeFromChrome();
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
	/// Begins an OS edge resize. Leaves maximized without snapping to the pre-maximize rect.
	/// </summary>
	private void StartResize(DisplayServer.WindowResizeEdge edge)
	{
		var window = GetWindow();
		if (window == null || !IsInstanceValid(window))
			return;

		// Keep current (full) size so the edge drag continues naturally.
		LeaveMaximized(window, applyRestoreRect: false);

		int windowId = window.GetWindowId();
		if (windowId >= 0)
			DisplayServer.WindowStartResize(edge, windowId);
	}

	/// <summary>
	/// Begins an OS window drag. Leaves maximized and restores the pre-maximize rect (desktop convention).
	/// </summary>
	private void StartDrag()
	{
		var window = GetWindow();
		if (window == null || !IsInstanceValid(window))
			return;

		LeaveMaximized(window, applyRestoreRect: true);

		int windowId = window.GetWindowId();
		if (windowId >= 0)
			DisplayServer.WindowStartDrag(windowId);
	}

	/// <summary>
	/// Toggle maximize from chrome buttons or title double-click.
	/// </summary>
	public void ToggleMaximizeFromChrome()
	{
		var window = GetWindow();
		if (window == null || !IsInstanceValid(window))
			return;

		if (IsEffectivelyMaximized(window))
		{
			LeaveMaximized(window, applyRestoreRect: true);
			return;
		}

		// Snapshot before mode change — SizeChanged during maximize must not overwrite this.
		_resizeSaveTimer.Stop();
		RememberRestoreRect(window.Size, window.Position);
		PersistWindowState(maximized: false);

		_suppressGeometrySave = true;
		_isMaximized = true;
		SetWindowMode(window, DisplayServer.WindowMode.Maximized);
		PersistWindowState(maximized: true);
		CallDeferred(nameof(EndGeometrySaveSuppression));
	}

	/// <summary>
	/// Leaves maximized/fullscreen. Optionally restores the pre-maximize rect (button / drag),
	/// or keeps the current size (edge-resize).
	/// </summary>
	/// <param name="window">Target window.</param>
	/// <param name="applyRestoreRect">True to apply remembered pre-maximize size/position.</param>
	private void LeaveMaximized(Window window, bool applyRestoreRect)
	{
		if (!IsEffectivelyMaximized(window))
			return;

		_resizeSaveTimer.Stop();
		_suppressGeometrySave = true;
		_isMaximized = false;

		SetWindowMode(window, DisplayServer.WindowMode.Windowed);

		if (applyRestoreRect && _hasRestoreRect)
		{
			ApplyGeometry(window, _restoreSize, _restorePosition);
			_lastKnownPosition = _restorePosition;
			// Borderless: engine often overwrites size on the next frame.
			CallDeferred(nameof(ReapplyRestoreRectDeferred));
		}
		else
		{
			// Free-size path (edge resize): current dimensions stay; next save captures them.
			_lastKnownPosition = window.Position;
			PersistWindowState(maximized: false);
			CallDeferred(nameof(EndGeometrySaveSuppression));
		}
	}

	/// <summary>
	/// Re-applies the restore rect after the display server finishes leaving maximized mode.
	/// </summary>
	private void ReapplyRestoreRectDeferred()
	{
		var window = GetWindow();
		if (window != null && IsInstanceValid(window) && _hasRestoreRect)
		{
			if (UiUtilities.IsWindowFillScreen(window))
				SetWindowMode(window, DisplayServer.WindowMode.Windowed);

			ApplyGeometry(window, _restoreSize, _restorePosition);
			_lastKnownPosition = _restorePosition;
			PersistWindowState(maximized: false);
		}

		_suppressGeometrySave = false;
	}

	private void EndGeometrySaveSuppression()
	{
		_suppressGeometrySave = false;
	}

	private bool IsEffectivelyMaximized(Window window) =>
		_isMaximized || UiUtilities.IsWindowFillScreen(window);

	private void RememberRestoreRect(Vector2I size, Vector2I globalPosition)
	{
		if (size.X < MinWindowSize.X || size.Y < MinWindowSize.Y)
			return;

		_restoreSize = size;
		_restorePosition = globalPosition;
		_hasRestoreRect = true;
	}

	private static void SetWindowMode(Window window, DisplayServer.WindowMode mode)
	{
		int id = window.GetWindowId();
		if (id >= 0)
			DisplayServer.WindowSetMode(mode, id);
		else
			window.Mode = mode switch
			{
				DisplayServer.WindowMode.Maximized => Window.ModeEnum.Maximized,
				DisplayServer.WindowMode.Fullscreen => Window.ModeEnum.Fullscreen,
				DisplayServer.WindowMode.ExclusiveFullscreen => Window.ModeEnum.ExclusiveFullscreen,
				DisplayServer.WindowMode.Minimized => Window.ModeEnum.Minimized,
				_ => Window.ModeEnum.Windowed
			};
	}

	private static void ApplyGeometry(Window window, Vector2I size, Vector2I globalPosition)
	{
		int id = window.GetWindowId();
		if (id >= 0)
		{
			if (size.X > 0 && size.Y > 0)
				DisplayServer.WindowSetSize(size, id);
			DisplayServer.WindowSetPosition(globalPosition, id);
			return;
		}

		if (size.X > 0 && size.Y > 0)
			window.Size = size;
		window.Position = globalPosition;
	}

	/// <summary>
	/// Persists restore rect (when known) and maximized flag. Uses session restore fields while
	/// maximized so transitional full-screen sizes never overwrite the pre-maximize rect.
	/// </summary>
	private void PersistWindowState(bool maximized)
	{
		Vector2I size;
		Vector2I relPos;

		if (maximized && _hasRestoreRect)
		{
			size = _restoreSize;
			relPos = UiUtilities.ToScreenRelativePosition(_restorePosition, size);
		}
		else
		{
			var win = GetWindow();
			if (win == null || !IsInstanceValid(win))
				return;

			size = win.Size;
			relPos = UiUtilities.ToScreenRelativePosition(win.Position, size);

			if (!maximized && size.X >= MinWindowSize.X && size.Y >= MinWindowSize.Y)
				RememberRestoreRect(size, win.Position);
		}

		// SetWindowState only writes size/position when not maximized.
		_globalData?.UserDataManager?.SetWindowState(size, relPos, maximized);
	}

	/// <inheritdoc />
	public override void _Process(double delta)
	{
		if (_isFading && _borderStylebox != null)
		{
			_fadeProgress += (float)delta / 1.0f;
			if (_fadeProgress >= 1.0f)
			{
				_fadeProgress = 1.0f;
				_isFading = false;
			}

			_borderStylebox.BorderColor = _highlightColor.Lerp(_originalBorderColor, _fadeProgress);
		}

		CheckAndDebounceWindowPosition();
	}

	private void OnWindowSizeChanged()
	{
		if (_suppressGeometrySave)
			return;

		var win = GetWindow();
		if (IsEffectivelyMaximized(win))
		{
			PersistWindowState(maximized: true);
			return;
		}

		_resizeSaveTimer.Start();
	}

	/// <summary>
	/// Saves the current window size, relative position, and maximized state.
	/// </summary>
	private void SaveCurrentWindowState()
	{
		if (_suppressGeometrySave)
			return;

		var win = GetWindow();
		if (win == null || !IsInstanceValid(win))
			return;

		PersistWindowState(IsEffectivelyMaximized(win));
	}

	private void CheckAndDebounceWindowPosition()
	{
		if (_suppressGeometrySave)
			return;

		var win = GetWindow();
		if (IsEffectivelyMaximized(win))
			return;

		Vector2I currentPos = win.Position;
		if (currentPos != _lastKnownPosition)
		{
			_lastKnownPosition = currentPos;
			_resizeSaveTimer.Start();
		}
	}

	/// <inheritdoc />
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
