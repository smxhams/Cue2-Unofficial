using Cue2.Services;
using Cue2.UI.Utilities;
using Godot;

namespace Cue2.UI.Settings;

/// <summary>
/// Cue2 Preferences panel: startup, autosave, backup depth, undo depth, and log session depth.
/// Values are stored in <see cref="UserDataManager"/> (persistent across shows).
/// </summary>
public partial class SettingsCue2Prefs : ScrollContainer
{
	private GlobalData _globalData;
	private GlobalSignals _globalSignals;
	private OptionButton _startupOptionButton;
	private Button _startupResetButton;
	private SpinBox _autosaveInterval;
	private Button _autosaveResetButton;
	private SpinBox _backupDepth;
	private Button _backupResetButton;
	private SpinBox _undoDepth;
	private Button _undoDepthResetButton;
	private SpinBox _logSessionDepth;
	private Button _logSessionDepthResetButton;
	private Button _resetUserDataButton;
	private ConfirmationDialog _resetUserDataDialog;

	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

		_startupOptionButton = GetNode<OptionButton>("%StartupOptionButton");
		_startupOptionButton.ItemSelected += OnStartupItemSelected;

		_startupResetButton = GetNode<Button>("%StartupResetButton");
		_startupResetButton.Pressed += OnStartupResetButtonPressed;
		_startupResetButton.Icon = GetThemeIcon("Refresh", "AtlasIcons");

		_autosaveInterval = GetNode<SpinBox>("%AutosaveInterval");
		_autosaveInterval.ValueChanged += OnAutosaveIntervalChanged;

		_autosaveResetButton = GetNode<Button>("%AutosaveResetButton");
		_autosaveResetButton.Pressed += OnAutosaveResetButtonPressed;
		_autosaveResetButton.Icon = GetThemeIcon("Refresh", "AtlasIcons");

		_backupDepth = GetNode<SpinBox>("%BackupDepth");
		_backupDepth.ValueChanged += OnBackupDepthChanged;

		_backupResetButton = GetNode<Button>("%BackupResetButton");
		_backupResetButton.Pressed += OnBackupResetButtonPressed;
		_backupResetButton.Icon = GetThemeIcon("Refresh", "AtlasIcons");

		_undoDepth = GetNode<SpinBox>("%UndoDepth");
		_undoDepth.MinValue = UserDataManager.MinUndoDepth;
		_undoDepth.MaxValue = UserDataManager.MaxUndoDepth;
		_undoDepth.ValueChanged += OnUndoDepthChanged;

		_undoDepthResetButton = GetNode<Button>("%UndoDepthResetButton");
		_undoDepthResetButton.Pressed += OnUndoDepthResetButtonPressed;
		_undoDepthResetButton.Icon = GetThemeIcon("Refresh", "AtlasIcons");

		_logSessionDepth = GetNode<SpinBox>("%LogSessionDepth");
		_logSessionDepth.MinValue = UserDataManager.MinLogSessionDepth;
		_logSessionDepth.MaxValue = UserDataManager.MaxLogSessionDepth;
		_logSessionDepth.ValueChanged += OnLogSessionDepthChanged;

		_logSessionDepthResetButton = GetNode<Button>("%LogSessionDepthResetButton");
		_logSessionDepthResetButton.Pressed += OnLogSessionDepthResetButtonPressed;
		_logSessionDepthResetButton.Icon = GetThemeIcon("Refresh", "AtlasIcons");

		_resetUserDataButton = GetNode<Button>("%ResetUserDataButton");
		_resetUserDataButton.Pressed += OnResetUserDataButtonPressed;

		_resetUserDataDialog = new ConfirmationDialog
		{
			Title = "Reset User Data",
			OkButtonText = "Reset",
			CancelButtonText = "Cancel",
			DialogText =
				"Reset all Cue2 preferences stored for this user?\n\n" +
				"This will restore defaults for:\n" +
				"• Startup, autosave, backup, undo, and log session settings\n" +
				"• Keyboard shortcuts (Input Map)\n" +
				"• Recent show files list\n" +
				"• Remembered window sizes and positions\n" +
				"• Shell column widths\n\n" +
				"Show files on disk are not deleted.\n" +
				"This cannot be undone."
		};
		_resetUserDataDialog.Confirmed += OnResetUserDataConfirmed;
		AddChild(_resetUserDataDialog);

		// ConfirmationDialog is its own Window and does not inherit Settings content scale.
		ApplyResetDialogUiScale();
		if (_globalSignals != null)
			_globalSignals.UiScaleChanged += OnResetDialogUiScaleChanged;

		SyncSettings();
	}

	public override void _ExitTree()
	{
		if (_globalSignals != null)
			_globalSignals.UiScaleChanged -= OnResetDialogUiScaleChanged;
		base._ExitTree();
	}

	private void SyncSettings()
	{
		if (_globalData?.UserDataManager != null)
		{
			var udm = _globalData.UserDataManager;
			_startupOptionButton.Selected = (int)udm.Startup;
			_autosaveInterval.Value = udm.AutosaveInterval;
			_backupDepth.Value = udm.BackupDepth;
			_undoDepth.Value = udm.UndoDepth;
			_logSessionDepth.Value = udm.LogSessionDepth;
			UpdateStartupResetButton();
			UpdateAutosaveResetButton();
			UpdateBackupResetButton();
			UpdateUndoDepthResetButton();
			UpdateLogSessionDepthResetButton();
		}
	}

	private void OnStartupItemSelected(long index)
	{
		if (_globalData?.UserDataManager != null)
		{
			_globalData.UserDataManager.Startup = (UserDataManager.StartupBehavior)index;
			UpdateStartupResetButton();
		}
	}

	private void OnAutosaveIntervalChanged(double value)
	{
		if (_globalData?.UserDataManager != null)
		{
			_globalData.UserDataManager.AutosaveInterval = (int)value;
			// Reconfigure running autosave timer
			GetNode<SaveManager>("/root/SaveManager").ConfigureAutosave();
			UpdateAutosaveResetButton();
		}
	}

	private void OnAutosaveResetButtonPressed()
	{
		if (_globalData?.UserDataManager != null)
		{
			_globalData.UserDataManager.AutosaveInterval = UserDataManager.DefaultAutosaveInterval;
			SyncSettings();
			// Reconfigure running autosave timer
			GetNode<SaveManager>("/root/SaveManager").ConfigureAutosave();
		}
	}

	private void OnBackupDepthChanged(double value)
	{
		if (_globalData?.UserDataManager != null)
		{
			_globalData.UserDataManager.BackupDepth = (int)value;
			UpdateBackupResetButton();
		}
	}

	private void OnStartupResetButtonPressed()
	{
		if (_globalData?.UserDataManager != null)
		{
			_globalData.UserDataManager.Startup = UserDataManager.DefaultStartupBehavior;
			SyncSettings();
		}
	}

	private void UpdateStartupResetButton()
	{
		if (_startupResetButton == null || _globalData?.UserDataManager == null) return;

		bool atDefault = _globalData.UserDataManager.Startup == UserDataManager.DefaultStartupBehavior;
		_startupResetButton.Visible = !atDefault;

		if (!atDefault)
		{
			string defaultText = UserDataManager.DefaultStartupBehavior == UserDataManager.StartupBehavior.OpenLastShowfile 
				? "Open last showfile" 
				: "New showfile";
			_startupResetButton.TooltipText = $"Reset to default: {defaultText}";
		}
	}

	private void UpdateAutosaveResetButton()
	{
		if (_autosaveResetButton == null || _globalData?.UserDataManager == null) return;

		bool atDefault = _globalData.UserDataManager.AutosaveInterval == UserDataManager.DefaultAutosaveInterval;
		_autosaveResetButton.Visible = !atDefault;

		if (!atDefault)
		{
			_autosaveResetButton.TooltipText = $"Reset to default: {UserDataManager.DefaultAutosaveInterval}";
		}
	}

	private void OnBackupResetButtonPressed()
	{
		if (_globalData?.UserDataManager != null)
		{
			_globalData.UserDataManager.BackupDepth = UserDataManager.DefaultBackupDepth;
			SyncSettings();
		}
	}

	private void UpdateBackupResetButton()
	{
		if (_backupResetButton == null || _globalData?.UserDataManager == null) return;

		bool atDefault = _globalData.UserDataManager.BackupDepth == UserDataManager.DefaultBackupDepth;
		_backupResetButton.Visible = !atDefault;

		if (!atDefault)
		{
			_backupResetButton.TooltipText = $"Reset to default: {UserDataManager.DefaultBackupDepth}";
		}
	}

	private void OnUndoDepthChanged(double value)
	{
		if (_globalData?.UserDataManager != null)
		{
			_globalData.UserDataManager.UndoDepth = (int)value;
			_globalData.HistoryManager?.TrimToMaxDepth();
			UpdateUndoDepthResetButton();
		}
	}

	private void OnUndoDepthResetButtonPressed()
	{
		if (_globalData?.UserDataManager != null)
		{
			_globalData.UserDataManager.UndoDepth = UserDataManager.DefaultUndoDepth;
			_globalData.HistoryManager?.TrimToMaxDepth();
			SyncSettings();
		}
	}

	private void UpdateUndoDepthResetButton()
	{
		if (_undoDepthResetButton == null || _globalData?.UserDataManager == null) return;

		bool atDefault = _globalData.UserDataManager.UndoDepth == UserDataManager.DefaultUndoDepth;
		_undoDepthResetButton.Visible = !atDefault;

		if (!atDefault)
		{
			_undoDepthResetButton.TooltipText = $"Reset to default: {UserDataManager.DefaultUndoDepth}";
		}
	}

	private void OnLogSessionDepthChanged(double value)
	{
		if (_globalData?.UserDataManager != null)
		{
			_globalData.UserDataManager.LogSessionDepth = (int)value;
			GetNodeOrNull<EventLogger>("/root/EventLogger")
				?.ApplyLogSessionDepth(_globalData.UserDataManager.LogSessionDepth);
			UpdateLogSessionDepthResetButton();
		}
	}

	private void OnLogSessionDepthResetButtonPressed()
	{
		if (_globalData?.UserDataManager != null)
		{
			_globalData.UserDataManager.LogSessionDepth = UserDataManager.DefaultLogSessionDepth;
			GetNodeOrNull<EventLogger>("/root/EventLogger")
				?.ApplyLogSessionDepth(_globalData.UserDataManager.LogSessionDepth);
			SyncSettings();
		}
	}

	private void UpdateLogSessionDepthResetButton()
	{
		if (_logSessionDepthResetButton == null || _globalData?.UserDataManager == null) return;

		bool atDefault = _globalData.UserDataManager.LogSessionDepth == UserDataManager.DefaultLogSessionDepth;
		_logSessionDepthResetButton.Visible = !atDefault;

		if (!atDefault)
		{
			_logSessionDepthResetButton.TooltipText =
				$"Reset to default: {UserDataManager.DefaultLogSessionDepth}";
		}
	}

	/// <summary>
	/// Shows a confirmation dialog before wiping persistent user preferences.
	/// </summary>
	private void OnResetUserDataButtonPressed()
	{
		if (_resetUserDataDialog == null || !GodotObject.IsInstanceValid(_resetUserDataDialog))
			return;

		// Re-apply scale in case UiScale/BaseDisplayScale changed while the dialog was hidden.
		ApplyResetDialogUiScale();
		_resetUserDataDialog.PopupCentered();
	}

	/// <summary>
	/// Applies the same content scale used by other Cue2 windows to the reset confirmation dialog.
	/// </summary>
	private void ApplyResetDialogUiScale()
	{
		if (_resetUserDataDialog == null || !GodotObject.IsInstanceValid(_resetUserDataDialog))
			return;
		if (_globalData?.Settings == null)
			return;

		// Match FileDropPopup / AboutWindow / SettingsWindow scaling path.
		_resetUserDataDialog.WrapControls = true;
		UiUtilities.RescaleUi(
			_resetUserDataDialog,
			_globalData.Settings.UiScale,
			_globalData.BaseDisplayScale);
	}

	/// <summary>
	/// Live UI-scale updates while the confirmation dialog is open.
	/// </summary>
	/// <param name="value">New user UI scale (ignored; read from Settings for consistency).</param>
	private void OnResetDialogUiScaleChanged(float value)
	{
		ApplyResetDialogUiScale();
	}

	/// <summary>
	/// Applies factory defaults via <see cref="UserDataManager.ResetToDefaults"/> and
	/// refreshes dependent systems (this panel, autosave, history depth, log pruning).
	/// </summary>
	private void OnResetUserDataConfirmed()
	{
		if (_globalData?.UserDataManager == null)
		{
			GD.PrintErr("SettingsCue2Prefs:OnResetUserDataConfirmed - UserDataManager unavailable.");
			return;
		}

		_globalData.UserDataManager.ResetToDefaults();

		// Side effects for systems that cache user prefs at runtime.
		_globalData.HistoryManager?.TrimToMaxDepth();
		GetNodeOrNull<SaveManager>("/root/SaveManager")?.ConfigureAutosave();
		GetNodeOrNull<EventLogger>("/root/EventLogger")
			?.ApplyLogSessionDepth(_globalData.UserDataManager.LogSessionDepth);

		SyncSettings();
		GD.Print("SettingsCue2Prefs:OnResetUserDataConfirmed - User data reset and UI resynced.");
	}
}
