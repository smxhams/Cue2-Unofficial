using Cue2.Shared;
using Godot;

namespace Cue2.UI.Scenes;
public partial class MainWindowHandles: Control
{
	private GlobalData _globalData;
	
	//Variables
	private bool _dragging;
	private bool _resizing;
	private Vector2I _initialMouse;
	private Vector2I _initialWindow;

	private int _offsetX;
	private int _offsetY;
	private Control _resizeNode;
	private int _windowId;

	private Vector2I _dragOffset;

	private Vector2I _minWindowSize = new Vector2I(600, 370);

	// Debounce timer for window resize saves (only persist final size after mouse release / resize completes)
	private Timer _resizeSaveTimer;
	private Vector2I _pendingWindowSize;
	private Vector2I _pendingWindowPosition;
	private Vector2I _lastKnownPosition;
	
	private GlobalSignals _globalSignals;

	private Color _originalBorderColor;
	private bool _isFading;
	private float _fadeProgress;
	private Color _highlightColor;
	private StyleBoxFlat _boarderStylebox;

	private Control _headerHandle;
	private Control _rightHandle;
	private Control _topHandle;
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
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_windowId = GetWindow().GetWindowId();

		// Setup debounce timer for efficient window size saving (only save after resize completes)
		_resizeSaveTimer = new Timer { OneShot = true, WaitTime = 0.25f };
		_resizeSaveTimer.Timeout += SavePendingWindowSize;
		AddChild(_resizeSaveTimer);

		// Set window size constraints
		var window = GetWindow();
		window.MinSize = _minWindowSize;
		// Calculate total screenspace across all displays
		var screenCount = DisplayServer.GetScreenCount();
		Vector2I minPos = new Vector2I(int.MaxValue, int.MaxValue);
		Vector2I maxPos = new Vector2I(int.MinValue, int.MinValue);
		for (int i = 0; i < screenCount; i++)
		{
			Vector2I pos = DisplayServer.ScreenGetPosition(i);
			Vector2I size = DisplayServer.ScreenGetSize(i);
			minPos = new Vector2I(Mathf.Min(minPos.X, pos.X), Mathf.Min(minPos.Y, pos.Y));
			maxPos = new Vector2I(Mathf.Max(maxPos.X, pos.X + size.X), Mathf.Max(maxPos.Y, pos.Y + size.Y));
		}
		Vector2I totalSize = maxPos - minPos;
		window.MaxSize = totalSize;

		// Restore previous window size / position / maximized state from persistent user data.
		// Position is restored relative to the display that currently contains the mouse cursor.
		var udm = _globalData.UserDataManager;
		if (udm != null)
		{
			var win = GetWindow();
			if (udm.WasMaximized)
			{
				win.Mode = Window.ModeEnum.Maximized;
			}
			else if (udm.LastWindowSize.X >= _minWindowSize.X && udm.LastWindowSize.Y >= _minWindowSize.Y)
			{
				win.Size = udm.LastWindowSize;

				// Place on the display where the mouse currently is, using last relative position
				Vector2I mousePos = DisplayServer.MouseGetPosition();
				int targetScreenIdx = 0;
				Vector2I targetScreenPos = Vector2I.Zero;
				int numScreens = DisplayServer.GetScreenCount();
				for (int i = 0; i < numScreens; i++)
				{
					Vector2I sPos = DisplayServer.ScreenGetPosition(i);
					Vector2I scrSize = DisplayServer.ScreenGetSize(i);
					Rect2I screenRect = new Rect2I(sPos, scrSize);
					if (screenRect.HasPoint(mousePos))
					{
						targetScreenIdx = i;
						targetScreenPos = sPos;
						break;
					}
				}

				Vector2I targetPos = targetScreenPos + udm.LastWindowPosition;

				// Clamp so the window is at least partially visible on the target display
				Vector2I targetMonitorSize = DisplayServer.ScreenGetSize(targetScreenIdx);
				targetPos.X = Mathf.Clamp(targetPos.X, targetScreenPos.X, targetScreenPos.X + targetMonitorSize.X - 200);
				targetPos.Y = Mathf.Clamp(targetPos.Y, targetScreenPos.Y, targetScreenPos.Y + targetMonitorSize.Y - 80);

				win.Position = targetPos;
			}
		}

		// Ensure initial state is persisted (first run or after restore)
		{
			SaveCurrentWindowState();
		}

		_lastKnownPosition = GetWindow().Position;

		_globalSignals.LogAlert += _alertReceived;
		
		// Border variables for event highlighting
		var border = GetNode<Panel>("%Border");
		_boarderStylebox = border.GetThemeStylebox("panel") as StyleBoxFlat;
		if (_boarderStylebox == null) return;
		_originalBorderColor = _boarderStylebox.BorderColor;
		_highlightColor = GlobalStyles.Danger; //new Color(1,0,0,1);//GlobalStyles.Danger;
		
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

		// Save window state on size changes (when not maximized)
		GetWindow().SizeChanged += OnWindowSizeChanged;
	}

	private async void _alertReceived()
	{
		if (_boarderStylebox == null) return;
		
		_boarderStylebox.BorderColor = _highlightColor;
		await ToSignal(GetTree().CreateTimer(0.5), "timeout");
		_fadeProgress = 0.0f;
		_isFading = true;  
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
					// Save current normal size/position before maximizing
					_resizeSaveTimer.Stop();
					SaveCurrentWindowState();
					window.Mode = Window.ModeEnum.Maximized;
				}

				// Persist the resulting state
				SaveCurrentWindowState();
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
	
	private void OnTopHandleGuiInput(InputEvent @event){
		if (@event is InputEventMouseButton { Pressed: true })
		{
			DisplayServer.WindowStartResize(DisplayServer.WindowResizeEdge.Top, _windowId);
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
	

	public override void _Process(double delta)
	{
		if (_isFading)
		{
			_fadeProgress += (float)delta / 1.0f; // 1-second fade duration
			if (_fadeProgress >= 1.0f)
			{
				_fadeProgress = 1.0f;
				_isFading = false; // Stop fading
			}


			// Interpolate between highlight color and original color
			Color lerpedColor = _highlightColor.Lerp(_originalBorderColor, _fadeProgress);

			_boarderStylebox.BorderColor = lerpedColor;
		}

		// Debounce save for window moves (position changes during drag)
		CheckAndDebounceWindowPosition();
	}

	private void OnWindowSizeChanged()
	{
		var win = GetWindow();
		if (win.Mode != Window.ModeEnum.Maximized)
		{
			_pendingWindowSize = win.Size;
			_pendingWindowPosition = win.Position;
			_resizeSaveTimer.Start();  // restart the debounce timer
		}
	}

	private void SavePendingWindowSize()
	{
		SaveCurrentWindowState();
	}

	/// <summary>
	/// Saves the current window size, relative position (to its display), and maximized state.
	/// Computes relative position so that on restore we can place it at the matching relative spot on the mouse's current display.
	/// </summary>
	private void SaveCurrentWindowState()
	{
		var win = GetWindow();
		bool isMax = win.Mode == Window.ModeEnum.Maximized;
		Vector2I size = win.Size;
		Vector2I globalPos = win.Position;

		Vector2I relPos = globalPos;

		if (!isMax)
		{
			// Find the screen the window's top-left belongs to (fallback to center)
			int screenCount = DisplayServer.GetScreenCount();
			for (int i = 0; i < screenCount; i++)
			{
				Vector2I sPos = DisplayServer.ScreenGetPosition(i);
				Vector2I sSize = DisplayServer.ScreenGetSize(i);
				Rect2I screenRect = new Rect2I(sPos, sSize);

				if (screenRect.HasPoint(globalPos))
				{
					relPos = globalPos - sPos;
					break;
				}

				// Check window center as fallback
				Vector2I center = globalPos + (size / 2);
				if (screenRect.HasPoint(center))
				{
					relPos = globalPos - sPos;
					break;
				}
			}
		}

		_globalData?.UserDataManager?.SetWindowState(size, relPos, isMax);
	}

	private void CheckAndDebounceWindowPosition()
	{
		var win = GetWindow();
		if (win.Mode != Window.ModeEnum.Maximized)
		{
			Vector2I currentPos = win.Position;
			if (currentPos != _lastKnownPosition)
			{
				_lastKnownPosition = currentPos;
				_pendingWindowPosition = currentPos;
				_pendingWindowSize = win.Size;  // capture size too
				_resizeSaveTimer.Start();
			}
		}
	}

	public override void _ExitTree()
	{
		_resizeSaveTimer?.Stop();
		SaveCurrentWindowState();
	}
}
