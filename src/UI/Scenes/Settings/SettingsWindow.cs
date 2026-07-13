using System;
using Cue2.Shared;
using Cue2.UI.Utilities;
using Godot;

namespace Cue2.UI.Scenes.Settings;

/// <summary>
/// Borderless Settings window. Geometry and last sub-menu are loaded once from user data at
/// the first open of a session, then kept in a process-level session cache for every
/// subsequent open/close. Disk is only updated when <see cref="UserDataManager"/> persists
/// (typically app exit).
/// </summary>
public partial class SettingsWindow : Window
{
	private GlobalSignals _globalSignals;
	private GlobalData _globalData;
	private Godot.Tree _setTree;
	private string _currentDisplay = "";

	private static readonly Vector2I MinWindowSize = new Vector2I(500, 350);
	private const string DefaultMenuKey = "General";

	// Session cache: seeded once from UserDataManager (file load at app start), then authoritative
	// for all later Settings opens until the process ends.
	private static bool _sessionStateInitialized;
	private static Vector2I _sessionSize = Vector2I.Zero;
	private static Vector2I _sessionRelativePosition = Vector2I.Zero;
	private static bool _sessionMaximized;
	/// <summary>Tree item label, e.g. "Canvas Editor".</summary>
	private static string _sessionMenuKey = DefaultMenuKey;

	// Debounce timer for window size/position saves (only update cache after resize/move settles)
	private Timer _resizeSaveTimer;
	private Vector2I _lastKnownPosition;

	/// <summary>
	/// Initializes the settings UI, restores last window geometry and sub-menu, and wires signals.
	/// Stays hidden until geometry is applied so the default scene size never flashes on screen.
	/// </summary>
	public override void _Ready()
	{
		// Scene starts visible=false; keep hidden until final size/position are set.
		Visible = false;

		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_globalData = GetNode<GlobalData>("/root/GlobalData");

		GD.Print($"SettingsWindow:_Ready - UI Scale: " + _globalData.Settings.UiScale);

		MinSize = MinWindowSize;

		GetNode<Button>("%SaveWithShow").Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.SettingsSaveUserDir), _getFilters());

		// Seed session state first so we can apply the correct size in one step (no intermediate resize).
		EnsureSessionStateSeeded();

		// Content scale only — does not change outer window pixel size.
		UiUtilities.RescaleUi(this, _globalData.Settings.UiScale, _globalData.BaseDisplayScale);

		// Prefer session/cached geometry. Only scale the scene default on a true first run.
		if (!TryRestoreWindowState())
		{
			UiUtilities.RescaleWindow(this, _globalData.BaseDisplayScale);
		}

		_resizeSaveTimer = new Timer { OneShot = true, WaitTime = 0.25f };
		_resizeSaveTimer.Timeout += SaveCurrentWindowState;
		AddChild(_resizeSaveTimer);

		_lastKnownPosition = Position;
		SizeChanged += OnWindowSizeChanged;

		_generateTree();
		RestoreSelectedMenu();
		_connectSignals();

		// Reveal only after size, position, scale, and menu are ready — avoids open-time flicker.
		Show();
	}

	/// <summary>
	/// On the first Settings open of the process, copies geometry and last menu from
	/// UserDataManager (already loaded from user:// at app startup). Later opens skip file access.
	/// </summary>
	private void EnsureSessionStateSeeded()
	{
		if (_sessionStateInitialized)
		{
			return;
		}

		var udm = _globalData?.UserDataManager;
		if (udm != null)
		{
			_sessionSize = udm.LastSettingsWindowSize;
			_sessionRelativePosition = udm.LastSettingsWindowPosition;
			_sessionMaximized = udm.SettingsWasMaximized;
			if (!string.IsNullOrWhiteSpace(udm.LastSettingsMenu))
			{
				_sessionMenuKey = udm.LastSettingsMenu;
			}
		}

		_sessionStateInitialized = true;
	}

	/// <summary>
	/// Restores size, relative position, and maximized state from the session cache.
	/// Position is applied relative to the display that currently contains the mouse cursor.
	/// </summary>
	/// <returns>True when a valid session size (or maximized state) was applied.</returns>
	private bool TryRestoreWindowState()
	{
		if (_sessionMaximized)
		{
			// Apply last normal size first so un-maximize has a sensible restore rect.
			if (_sessionSize.X >= MinWindowSize.X && _sessionSize.Y >= MinWindowSize.Y)
			{
				Size = _sessionSize;
				ApplySessionRelativePosition();
			}
			Mode = ModeEnum.Maximized;
			return true;
		}

		if (_sessionSize.X < MinWindowSize.X || _sessionSize.Y < MinWindowSize.Y)
		{
			return false;
		}

		Size = _sessionSize;
		ApplySessionRelativePosition();
		return true;
	}

	/// <summary>
	/// Places the window on the display under the mouse using the cached relative position.
	/// </summary>
	private void ApplySessionRelativePosition()
	{
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

		Vector2I targetPos = targetScreenPos + _sessionRelativePosition;

		// Clamp so the window is at least partially visible on the target display
		Vector2I targetMonitorSize = DisplayServer.ScreenGetSize(targetScreenIdx);
		targetPos.X = Mathf.Clamp(targetPos.X, targetScreenPos.X, targetScreenPos.X + targetMonitorSize.X - 200);
		targetPos.Y = Mathf.Clamp(targetPos.Y, targetScreenPos.Y, targetScreenPos.Y + targetMonitorSize.Y - 80);

		Position = targetPos;
	}

	private void OnWindowSizeChanged()
	{
		if (Mode != ModeEnum.Maximized)
		{
			_resizeSaveTimer?.Start();
		}
	}

	/// <summary>
	/// Debounces position changes during drag and updates the session cache when settled.
	/// </summary>
	public override void _Process(double delta)
	{
		if (Mode != ModeEnum.Maximized)
		{
			Vector2I currentPos = Position;
			if (currentPos != _lastKnownPosition)
			{
				_lastKnownPosition = currentPos;
				_resizeSaveTimer?.Start();
			}
		}
	}

	/// <summary>
	/// Updates the session cache (and UserDataManager in-memory fields) with current geometry.
	/// Does not write the external user data file.
	/// </summary>
	private void SaveCurrentWindowState()
	{
		bool isMax = Mode == ModeEnum.Maximized;
		Vector2I size = Size;
		Vector2I globalPos = Position;
		Vector2I relPos = globalPos;

		if (!isMax)
		{
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

		// Session cache is the source of truth for re-opens within this process.
		_sessionMaximized = isMax;
		if (!isMax)
		{
			if (size.X > 0 && size.Y > 0)
				_sessionSize = size;
			_sessionRelativePosition = relPos;
		}
		_sessionStateInitialized = true;

		// Mirror into UserDataManager so app-exit can persist to disk once.
		_globalData?.UserDataManager?.SetSettingsWindowState(size, relPos, isMax);
	}

	/// <summary>
	/// Remembers the active Settings sub-menu in the session cache and UserDataManager memory.
	/// </summary>
	/// <param name="menuKey">Tree item label (e.g. "Canvas Editor").</param>
	private void SaveSelectedMenu(string menuKey)
	{
		if (string.IsNullOrWhiteSpace(menuKey) || !TryGetMenuNode(menuKey, out _))
		{
			return;
		}

		_sessionMenuKey = menuKey;
		_sessionStateInitialized = true;
		_globalData?.UserDataManager?.SetSettingsMenu(menuKey);
	}

	/// <summary>
	/// Selects the tree item for the session-cached menu and shows its panel.
	/// Falls back to General if the key is unknown or the item is missing.
	/// </summary>
	private void RestoreSelectedMenu()
	{
		if (_setTree == null)
		{
			return;
		}

		string menuKey = _sessionMenuKey;
		if (string.IsNullOrWhiteSpace(menuKey) || !TryGetMenuNode(menuKey, out _))
		{
			menuKey = DefaultMenuKey;
		}

		TreeItem root = _setTree.GetRoot();
		TreeItem item = FindTreeItemByText(root, menuKey);
		if (item == null && menuKey != DefaultMenuKey)
		{
			menuKey = DefaultMenuKey;
			item = FindTreeItemByText(root, menuKey);
		}

		if (item == null)
		{
			return;
		}

		// Expand ancestors so the selection is visible
		TreeItem parent = item.GetParent();
		while (parent != null && parent != root)
		{
			parent.Collapsed = false;
			parent = parent.GetParent();
		}

		item.Select(0);
		// Select does not always emit item_selected when set programmatically — show panel explicitly.
		ShowMenuForTreeLabel(menuKey);
		_setTree.ScrollToItem(item);
	}

	/// <summary>
	/// Depth-first search for a tree item whose column-0 text matches <paramref name="text"/>.
	/// </summary>
	private static TreeItem FindTreeItemByText(TreeItem item, string text)
	{
		if (item == null || string.IsNullOrEmpty(text))
		{
			return null;
		}

		if (item.GetText(0) == text)
		{
			return item;
		}

		TreeItem child = item.GetFirstChild();
		while (child != null)
		{
			TreeItem found = FindTreeItemByText(child, text);
			if (found != null)
			{
				return found;
			}

			child = child.GetNext();
		}

		return null;
	}

	private void _connectSignals()
	{
		_globalSignals.UiScaleChanged += ScaleUi;
		GetNode<Button>("%SaveFilterOptionButton").Pressed += () =>
		{
			GetNode<PanelContainer>("%DropMenuFilter").Visible = true;
			GetNode<Button>("%SaveFilterOptionButton").Disabled = true;
		};
		GetNode<PanelContainer>("%DropMenuFilter").MouseExited += () =>
		{
			GetNode<PanelContainer>("%DropMenuFilter").Visible = false;
			GetNode<Button>("%SaveFilterOptionButton").Disabled = false;
		};
	}

	private string _getFilters()
	{
		return "";
	}

	private void ScaleUi(float value)
	{
		UiUtilities.RescaleUi(this, _globalData.Settings.UiScale, _globalData.BaseDisplayScale);
	}

	// On tree item pressed display each settings menu.
	private void _on_tree_item_selected()
	{
		var selected = _setTree?.GetSelected();
		if (selected == null)
		{
			return;
		}

		string action = selected.GetText(0);
		if (!TryGetMenuNode(action, out string menuNode))
		{
			// Category headers / unimplemented pages — keep previous panel.
			return;
		}

		ShowMenuPanel(menuNode);
		SaveSelectedMenu(action);
	}

	/// <summary>
	/// Shows the settings panel for a tree label and hides the previous one.
	/// </summary>
	private void ShowMenuForTreeLabel(string treeLabel)
	{
		if (!TryGetMenuNode(treeLabel, out string menuNode))
		{
			return;
		}

		ShowMenuPanel(menuNode);
		_sessionMenuKey = treeLabel;
	}

	private void ShowMenuPanel(string menuNode)
	{
		if (_currentDisplay != "")
		{
			var previous = GetNodeOrNull<Control>("%" + _currentDisplay);
			previous?.Hide();
		}
		else
		{
			// Checks all settings displays in case one is already open
			foreach (var node in GetNode<MarginContainer>("%RightSide").GetChildren())
			{
				if (node is Control child && child.IsVisible())
					child.Hide();
			}
		}

		var panel = GetNodeOrNull<Control>("%" + menuNode);
		if (panel == null)
		{
			GD.PrintErr($"SettingsWindow:ShowMenuPanel - Panel %{menuNode} not found.");
			return;
		}

		panel.Show();
		_currentDisplay = menuNode;
	}

	/// <summary>
	/// Maps a Settings tree item label to the unique-name of its content panel.
	/// </summary>
	/// <returns>True when the label has an implemented settings page.</returns>
	private static bool TryGetMenuNode(string action, out string menuNode)
	{
		menuNode = action switch
		{
			"General" => "SettingsGeneral",
			"Input Map" => "SettingsInputMap",
			"Audio" => "SettingsAudio",
			"Audio Output Patch" => "AudioOutputPatch",
			"Canvas Editor" => "CanvasEditor",
			"Cue Lights" => "CueLights",
			"OSC Connections" => "SettingsOscConnections",
			"OSC Listener" => "SettingsOscListen",
			"Cue2 Preferences" => "SettingsCue2Prefs",
			_ => null
		};
		return menuNode != null;
	}

	private void _generateTree()
	{
		// Settings Tree
		_setTree = GetNode<Godot.Tree>("%SettingsTree");
		TreeItem root = _setTree.CreateItem();
		_setTree.HideRoot = true;

		//General
		TreeItem tiGeneral = _setTree.CreateItem(root);
		tiGeneral.SetText(0, "General");
		TreeItem tiInputMap = _setTree.CreateItem(tiGeneral);
		tiInputMap.SetText(0, "Input Map");

		// Audio
		TreeItem tiAudio = _setTree.CreateItem(root);
		tiAudio.SetText(0, "Audio");
		TreeItem tiAudioOutputPatch = _setTree.CreateItem(tiAudio);
		tiAudioOutputPatch.SetText(0, "Audio Output Patch");


		// Output Devices
		TreeItem tiOutputDevices = _setTree.CreateItem(root);
		tiOutputDevices.SetText(0, "Video/Image");
		TreeItem tiVideoDevice = _setTree.CreateItem(tiOutputDevices);
		tiVideoDevice.SetText(0, "Canvas Editor");

		// Connections
		TreeItem tiConnections = _setTree.CreateItem(root);
		tiConnections.SetText(0, "Connections");
		TreeItem tiCueLights = _setTree.CreateItem(tiConnections);
		tiCueLights.SetText(0, "Cue Lights");
		TreeItem tiOscConnections = _setTree.CreateItem(tiConnections);
		tiOscConnections.SetText(0, "OSC Connections");
		TreeItem tiOscListener = _setTree.CreateItem(tiConnections);
		tiOscListener.SetText(0, "OSC Listener");
		tiOscListener.SetTooltipText(0, "Settings for received OSC messages");
		TreeItem tiNetworkConnection = _setTree.CreateItem(tiConnections);
		tiNetworkConnection.SetText(0, "Network Connection");
		TreeItem tiArtNet = _setTree.CreateItem(tiConnections);
		tiArtNet.SetText(0, "Art-Net");

		// Cue defaults
		TreeItem tiDefaults = _setTree.CreateItem(root);
		tiDefaults.SetText(0, "Defaults");
		tiDefaults.SetTooltipText(0, "Set default behaviors and paramaters across shells and cues universaly.");
		TreeItem tiAudioCueDafaults = _setTree.CreateItem(tiDefaults);
		tiAudioCueDafaults.SetText(0, "Audio Cues");
		tiAudioCueDafaults.SetTooltipText(0, "Set defaults for audio cues.");
		TreeItem tiVideoCueDefaults = _setTree.CreateItem(tiDefaults);
		tiVideoCueDefaults.SetText(0, "Video Defaults");
		tiVideoCueDefaults.SetTooltipText(0, "Set defaults for video cues.");

		TreeItem tiCue2Preferences = _setTree.CreateItem(root);
		tiCue2Preferences.SetText(0, "Cue2 Preferences");
		tiCue2Preferences.SetTooltipText(0, "Set showfile independant preferences");
	}

	/// <summary>
	/// Disconnects signals and persists final window geometry and menu before the node is freed.
	/// </summary>
	public override void _ExitTree()
	{
		SizeChanged -= OnWindowSizeChanged;
		_resizeSaveTimer?.Stop();
		SaveCurrentWindowState();

		// Ensure last visible page is cached even if selection signal didn't fire again.
		if (!string.IsNullOrEmpty(_sessionMenuKey))
		{
			SaveSelectedMenu(_sessionMenuKey);
		}

		if (_globalSignals != null)
		{
			_globalSignals.UiScaleChanged -= ScaleUi;
		}
	}
}
