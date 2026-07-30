using Cue2.Base.Classes;
using Cue2.Shared;
using Godot;
using AppSettings = Cue2.Base.Classes.Settings;

namespace Cue2.UI.Scenes.Settings;

/// <summary>
/// Video / Image settings panel (Settings tree parent): live disable &amp; blackout,
/// show background colour, and machine performance preferences (quality, HW decode stub,
/// preview quality, vsync). Canvas Editor remains a child page for topology.
/// </summary>
/// <remarks>
/// Disable/blackout are runtime operator controls (not undoable, not saved).
/// Background colour is show-scoped with history. Performance knobs live in
/// <see cref="UserDataManager"/> (persist across shows).
/// </remarks>
public partial class SettingsVideoOutput : ScrollContainer
{
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;
    private HistoryManager _historyManager;
    private DisplaysManager _displaysManager;

    // Live output (runtime)
    private CheckBox _disableOutputCheckBox;
    private CheckBox _blackoutCheckBox;

    // Show-scoped appearance
    private ColorPickerButton _backgroundColorPicker;
    private Button _backgroundColorResetButton;

    // Machine prefs
    private OptionButton _qualityModeOption;
    private Button _qualityModeResetButton;
    private OptionButton _hwDecodeOption;
    private Button _hwDecodeResetButton;
    private OptionButton _previewQualityOption;
    private Button _previewQualityResetButton;
    private OptionButton _vsyncOption;
    private Button _vsyncResetButton;

    /// <summary>True while pushing model → controls so handlers do not re-record history.</summary>
    private bool _isSyncingUi;

    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _historyManager = _globalData?.HistoryManager;
        _displaysManager = GetNodeOrNull<DisplaysManager>("/root/DisplaysManager");

        _disableOutputCheckBox = GetNode<CheckBox>("%DisableOutputCheckBox");
        _blackoutCheckBox = GetNode<CheckBox>("%BlackoutCheckBox");
        _backgroundColorPicker = GetNode<ColorPickerButton>("%BackgroundColorPicker");
        _backgroundColorResetButton = GetNode<Button>("%BackgroundColorResetButton");
        _qualityModeOption = GetNode<OptionButton>("%QualityModeOption");
        _qualityModeResetButton = GetNode<Button>("%QualityModeResetButton");
        _hwDecodeOption = GetNode<OptionButton>("%HwDecodeOption");
        _hwDecodeResetButton = GetNode<Button>("%HwDecodeResetButton");
        _previewQualityOption = GetNode<OptionButton>("%PreviewQualityOption");
        _previewQualityResetButton = GetNode<Button>("%PreviewQualityResetButton");
        _vsyncOption = GetNode<OptionButton>("%VSyncOption");
        _vsyncResetButton = GetNode<Button>("%VSyncResetButton");

        SetupResetButton(_backgroundColorResetButton, OnBackgroundColorResetPressed);
        SetupResetButton(_qualityModeResetButton, OnQualityModeResetPressed);
        SetupResetButton(_hwDecodeResetButton, OnHwDecodeResetPressed);
        SetupResetButton(_previewQualityResetButton, OnPreviewQualityResetPressed);
        SetupResetButton(_vsyncResetButton, OnVSyncResetPressed);

        _disableOutputCheckBox.Toggled += OnDisableOutputToggled;
        _blackoutCheckBox.Toggled += OnBlackoutToggled;
        _backgroundColorPicker.PopupClosed += OnBackgroundColorPopupClosed;
        _qualityModeOption.ItemSelected += OnQualityModeSelected;
        _hwDecodeOption.ItemSelected += OnHwDecodeSelected;
        _previewQualityOption.ItemSelected += OnPreviewQualitySelected;
        _vsyncOption.ItemSelected += OnVSyncSelected;

        if (_historyManager != null)
            _historyManager.HistoryRestored += OnHistoryRestored;
        if (_globalSignals != null)
        {
            _globalSignals.NewSession += OnNewSession;
            _globalSignals.VideoOutputControlChanged += OnVideoOutputControlChanged;
            _globalSignals.VideoPlaybackPrefsChanged += OnVideoPlaybackPrefsChanged;
        }

        SyncSettings();
    }

    public override void _ExitTree()
    {
        if (_historyManager != null)
            _historyManager.HistoryRestored -= OnHistoryRestored;
        if (_globalSignals != null)
        {
            _globalSignals.NewSession -= OnNewSession;
            _globalSignals.VideoOutputControlChanged -= OnVideoOutputControlChanged;
            _globalSignals.VideoPlaybackPrefsChanged -= OnVideoPlaybackPrefsChanged;
        }
        base._ExitTree();
    }

    private static void SetupResetButton(Button button, System.Action handler)
    {
        if (button == null)
            return;
        button.Icon = button.GetThemeIcon("Refresh", "AtlasIcons");
        button.Pressed += handler;
    }

    private void OnHistoryRestored(int scope)
    {
        if (!GodotObject.IsInstanceValid(this) || _globalData?.Settings == null)
            return;
        if (scope != (int)HistoryManager.HistoryScope.Settings)
            return;
        SyncSettings();
    }

    private void OnNewSession()
    {
        if (!GodotObject.IsInstanceValid(this))
            return;
        SyncSettings();
    }

    private void OnVideoOutputControlChanged(bool disabled, bool blackout)
    {
        if (!GodotObject.IsInstanceValid(this) || _isSyncingUi)
            return;
        _isSyncingUi = true;
        try
        {
            _disableOutputCheckBox?.SetPressedNoSignal(disabled);
            _blackoutCheckBox?.SetPressedNoSignal(blackout);
            // Blackout is meaningless while displays are closed.
            if (_blackoutCheckBox != null)
                _blackoutCheckBox.Disabled = disabled;
        }
        finally
        {
            _isSyncingUi = false;
        }
    }

    private void OnVideoPlaybackPrefsChanged()
    {
        if (!GodotObject.IsInstanceValid(this) || _isSyncingUi)
            return;
        SyncMachinePrefsOnly();
    }

    /// <summary>
    /// Pulls current model values into the form without re-firing edit handlers.
    /// </summary>
    private void SyncSettings()
    {
        if (_globalData == null)
            return;

        _isSyncingUi = true;
        try
        {
            bool disabled = DisplaysManager.OutputDisabled;
            bool blackout = DisplaysManager.OutputBlackout;
            _disableOutputCheckBox?.SetPressedNoSignal(disabled);
            _blackoutCheckBox?.SetPressedNoSignal(blackout);
            if (_blackoutCheckBox != null)
                _blackoutCheckBox.Disabled = disabled;

            if (_globalData.Settings != null && _backgroundColorPicker != null)
                _backgroundColorPicker.Color = _globalData.Settings.OutputBackgroundColor;

            SyncMachinePrefsOnly(blockSignals: true);
            UpdateAllResetButtons();
        }
        finally
        {
            _isSyncingUi = false;
        }
    }

    private void SyncMachinePrefsOnly(bool blockSignals = false)
    {
        var udm = _globalData?.UserDataManager;
        if (udm == null)
            return;

        if (_qualityModeOption != null)
        {
            if (blockSignals) _qualityModeOption.SetBlockSignals(true);
            _qualityModeOption.Selected = (int)udm.VideoQualityMode;
            if (blockSignals) _qualityModeOption.SetBlockSignals(false);
        }
        if (_hwDecodeOption != null)
        {
            if (blockSignals) _hwDecodeOption.SetBlockSignals(true);
            _hwDecodeOption.Selected = (int)udm.HardwareDecodePreference;
            if (blockSignals) _hwDecodeOption.SetBlockSignals(false);
        }
        if (_previewQualityOption != null)
        {
            if (blockSignals) _previewQualityOption.SetBlockSignals(true);
            _previewQualityOption.Selected = (int)udm.VideoPreviewQuality;
            if (blockSignals) _previewQualityOption.SetBlockSignals(false);
        }
        if (_vsyncOption != null)
        {
            if (blockSignals) _vsyncOption.SetBlockSignals(true);
            _vsyncOption.Selected = (int)udm.OutputVSyncMode;
            if (blockSignals) _vsyncOption.SetBlockSignals(false);
        }

        UpdateMachineResetButtons();
    }

    private void UpdateAllResetButtons()
    {
        UpdateBackgroundColorResetButton();
        UpdateMachineResetButtons();
    }

    private void UpdateMachineResetButtons()
    {
        UpdateQualityModeResetButton();
        UpdateHwDecodeResetButton();
        UpdatePreviewQualityResetButton();
        UpdateVSyncResetButton();
    }

    // ── Live output controls ────────────────────────────────────────────────

    private void OnDisableOutputToggled(bool pressed)
    {
        if (_isSyncingUi || _displaysManager == null)
            return;
        _displaysManager.SetOutputDisabled(pressed);
        if (_blackoutCheckBox != null)
            _blackoutCheckBox.Disabled = pressed;
    }

    private void OnBlackoutToggled(bool pressed)
    {
        if (_isSyncingUi || _displaysManager == null)
            return;
        if (DisplaysManager.OutputDisabled)
            return;
        _displaysManager.SetOutputBlackout(pressed);
    }

    // ── Show background colour ──────────────────────────────────────────────

    private void OnBackgroundColorPopupClosed()
    {
        if (_isSyncingUi || _globalData?.Settings == null || _historyManager?.IsRestoring == true)
            return;
        if (_backgroundColorPicker == null)
            return;

        Color next = _backgroundColorPicker.Color;
        if (_globalData.Settings.OutputBackgroundColor.IsEqualApprox(next))
        {
            UpdateBackgroundColorResetButton();
            return;
        }

        _historyManager?.RecordSettingsChange("Output background colour", null, "OutputBackgroundColor");
        _globalData.Settings.OutputBackgroundColor = next;
        _displaysManager?.ApplyOutputBackgroundColor(next);
        UpdateBackgroundColorResetButton();
    }

    private void OnBackgroundColorResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null || _historyManager?.IsRestoring == true)
            return;
        if (_globalData.Settings.OutputBackgroundColor.IsEqualApprox(AppSettings.DefaultOutputBackgroundColor))
            return;

        _historyManager?.RecordSettingsChange("Reset output background colour", null, "OutputBackgroundColor");
        _globalData.Settings.OutputBackgroundColor = AppSettings.DefaultOutputBackgroundColor;
        if (_backgroundColorPicker != null)
            _backgroundColorPicker.Color = AppSettings.DefaultOutputBackgroundColor;
        _displaysManager?.ApplyOutputBackgroundColor(AppSettings.DefaultOutputBackgroundColor);
        UpdateBackgroundColorResetButton();
    }

    private void UpdateBackgroundColorResetButton()
    {
        if (_backgroundColorResetButton == null || _globalData?.Settings == null)
            return;
        bool atDefault = _globalData.Settings.OutputBackgroundColor
            .IsEqualApprox(AppSettings.DefaultOutputBackgroundColor);
        _backgroundColorResetButton.Visible = !atDefault;
        if (!atDefault)
            _backgroundColorResetButton.TooltipText = "Reset to default: black";
    }

    // ── Machine performance prefs ───────────────────────────────────────────

    private void OnQualityModeSelected(long index)
    {
        if (_isSyncingUi || _globalData?.UserDataManager == null)
            return;
        _globalData.UserDataManager.VideoQualityMode = (VideoQualityMode)(int)index;
        UpdateQualityModeResetButton();
    }

    private void OnQualityModeResetPressed()
    {
        if (_globalData?.UserDataManager == null)
            return;
        _globalData.UserDataManager.VideoQualityMode = UserDataManager.DefaultVideoQualityMode;
        SyncMachinePrefsOnly(blockSignals: true);
    }

    private void UpdateQualityModeResetButton()
    {
        if (_qualityModeResetButton == null || _globalData?.UserDataManager == null)
            return;
        bool atDefault = _globalData.UserDataManager.VideoQualityMode
            == UserDataManager.DefaultVideoQualityMode;
        _qualityModeResetButton.Visible = !atDefault;
        if (!atDefault)
            _qualityModeResetButton.TooltipText = "Reset to default: Balanced";
    }

    private void OnHwDecodeSelected(long index)
    {
        if (_isSyncingUi || _globalData?.UserDataManager == null)
            return;
        _globalData.UserDataManager.HardwareDecodePreference = (HardwareDecodePreference)(int)index;
        UpdateHwDecodeResetButton();
    }

    private void OnHwDecodeResetPressed()
    {
        if (_globalData?.UserDataManager == null)
            return;
        _globalData.UserDataManager.HardwareDecodePreference =
            UserDataManager.DefaultHardwareDecodePreference;
        SyncMachinePrefsOnly(blockSignals: true);
    }

    private void UpdateHwDecodeResetButton()
    {
        if (_hwDecodeResetButton == null || _globalData?.UserDataManager == null)
            return;
        bool atDefault = _globalData.UserDataManager.HardwareDecodePreference
            == UserDataManager.DefaultHardwareDecodePreference;
        _hwDecodeResetButton.Visible = !atDefault;
        if (!atDefault)
            _hwDecodeResetButton.TooltipText = "Reset to default: Auto";
    }

    private void OnPreviewQualitySelected(long index)
    {
        if (_isSyncingUi || _globalData?.UserDataManager == null)
            return;
        _globalData.UserDataManager.VideoPreviewQuality = (VideoPreviewQuality)(int)index;
        UpdatePreviewQualityResetButton();
    }

    private void OnPreviewQualityResetPressed()
    {
        if (_globalData?.UserDataManager == null)
            return;
        _globalData.UserDataManager.VideoPreviewQuality = UserDataManager.DefaultVideoPreviewQuality;
        SyncMachinePrefsOnly(blockSignals: true);
    }

    private void UpdatePreviewQualityResetButton()
    {
        if (_previewQualityResetButton == null || _globalData?.UserDataManager == null)
            return;
        bool atDefault = _globalData.UserDataManager.VideoPreviewQuality
            == UserDataManager.DefaultVideoPreviewQuality;
        _previewQualityResetButton.Visible = !atDefault;
        if (!atDefault)
            _previewQualityResetButton.TooltipText = "Reset to default: Full";
    }

    private void OnVSyncSelected(long index)
    {
        if (_isSyncingUi || _globalData?.UserDataManager == null)
            return;
        _globalData.UserDataManager.OutputVSyncMode = (OutputVSyncMode)(int)index;
        UpdateVSyncResetButton();
    }

    private void OnVSyncResetPressed()
    {
        if (_globalData?.UserDataManager == null)
            return;
        _globalData.UserDataManager.OutputVSyncMode = UserDataManager.DefaultOutputVSyncMode;
        SyncMachinePrefsOnly(blockSignals: true);
    }

    private void UpdateVSyncResetButton()
    {
        if (_vsyncResetButton == null || _globalData?.UserDataManager == null)
            return;
        bool atDefault = _globalData.UserDataManager.OutputVSyncMode
            == UserDataManager.DefaultOutputVSyncMode;
        _vsyncResetButton.Visible = !atDefault;
        if (!atDefault)
            _vsyncResetButton.TooltipText = "Reset to default: Prefer VSync";
    }
}
