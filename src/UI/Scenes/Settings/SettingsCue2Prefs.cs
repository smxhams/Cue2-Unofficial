using Cue2.Shared;
using Godot;

namespace Cue2.UI.Scenes.Settings;

/// <summary>
/// Cue2 Preferences panel: startup, autosave, and backup depth settings.
/// Values are stored in <see cref="UserDataManager"/> (persistent across shows).
/// </summary>
public partial class SettingsCue2Prefs : ScrollContainer
{
	private GlobalData _globalData;
	private OptionButton _startupOptionButton;
	private Button _startupResetButton;
	private SpinBox _autosaveInterval;
	private Button _autosaveResetButton;
	private SpinBox _backupDepth;
	private Button _backupResetButton;

	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");

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

		SyncSettings();
	}

	private void SyncSettings()
	{
		if (_globalData?.UserDataManager != null)
		{
			var udm = _globalData.UserDataManager;
			_startupOptionButton.Selected = (int)udm.Startup;
			_autosaveInterval.Value = udm.AutosaveInterval;
			_backupDepth.Value = udm.BackupDepth;
			UpdateStartupResetButton();
			UpdateAutosaveResetButton();
			UpdateBackupResetButton();
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
}
