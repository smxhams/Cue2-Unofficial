using Cue2.Shared;
using Godot;
using AppSettings = Cue2.Base.Classes.Settings;

namespace Cue2.UI.Scenes.Settings;

/// <summary>
/// General settings panel: UI scale, Go button scale, stop fade-out, media backup, multi-edit, and select-new-cues.
/// Each setting shows a refresh button when not at its system default (same pattern as Cue2 Preferences).
/// Values are stored with the showfile via <see cref="AppSettings"/>.
/// </summary>
public partial class SettingsGeneral : ScrollContainer
{
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;
    private HistoryManager _historyManager;

    private LineEdit _uiScaleNum;
    private HSlider _uiScaleSlider;
    private Button _uiScaleResetButton;

    private OptionButton _goScaleOptionButton;
    private Button _goScaleResetButton;

    private SpinBox _stopFadeSpinBox;
    private Button _stopFadeResetButton;

    private CheckBox _mediaBackupCheckBox;
    private Button _mediaBackupResetButton;

    private CheckBox _multiEditCheckBox;
    private Button _multiEditResetButton;

    private CheckBox _selectNewCuesCheckBox;
    private Button _selectNewCuesResetButton;

    private CheckBox _timelineWaveformsCheckBox;
    private Button _timelineWaveformsResetButton;

    /// <summary>True while pushing model → controls so handlers do not re-record history.</summary>
    private bool _isSyncingUi;

    /// <summary>Go scale option index → scale factor (matches OptionButton order).</summary>
    private static readonly float[] GoScaleValues = { 0.5f, 1.0f, 2.0f, 4.0f, 8.0f, 32.0f };

    public override void _Ready()
    {
        GD.Print("SettingsGeneral:_Ready - Settings General Init");

        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _historyManager = _globalData?.HistoryManager;

        _uiScaleNum = GetNode<LineEdit>("%UiScaleNum");
        _uiScaleSlider = GetNode<HSlider>("%UiScaleSlider");
        _uiScaleResetButton = GetNode<Button>("%UiScaleResetButton");
        _uiScaleResetButton.Icon = GetThemeIcon("Refresh", "AtlasIcons");
        _uiScaleResetButton.Pressed += OnUiScaleResetPressed;

        // Ensure the percentage field can receive focus (scene default was FOCUS_NONE).
        _uiScaleNum.FocusMode = FocusModeEnum.All;
        _uiScaleNum.Editable = true;

        _uiScaleSlider.ValueChanged += OnUiScaleSliderValueChanged;
        _uiScaleSlider.DragEnded += OnUiScaleSliderDragEnded;
        // Commit typed scale on Enter only.
        _uiScaleNum.TextSubmitted += OnUiScaleTextSubmitted;

        _goScaleOptionButton = GetNode<OptionButton>("%GoScaleOptionButton");
        _goScaleResetButton = GetNode<Button>("%GoScaleResetButton");
        _goScaleResetButton.Icon = GetThemeIcon("Refresh", "AtlasIcons");
        _goScaleResetButton.Pressed += OnGoScaleResetPressed;
        _goScaleOptionButton.ItemSelected += OnGoScaleItemSelected;

        _stopFadeSpinBox = GetNode<SpinBox>("%StopFadeSpinBox");
        _stopFadeResetButton = GetNode<Button>("%StopFadeResetButton");
        _stopFadeResetButton.Icon = GetThemeIcon("Refresh", "AtlasIcons");
        _stopFadeResetButton.Pressed += OnStopFadeResetPressed;
        _stopFadeSpinBox.ValueChanged += OnStopFadeChanged;
        _stopFadeSpinBox.Editable = true;
        _stopFadeSpinBox.FocusMode = FocusModeEnum.All;
        var stopFadeEdit = _stopFadeSpinBox.GetLineEdit();
        if (stopFadeEdit != null)
            stopFadeEdit.FocusMode = FocusModeEnum.All;

        _mediaBackupCheckBox = GetNode<CheckBox>("%MediaBackupCheckBox");
        _mediaBackupCheckBox.Toggled += OnMediaBackupToggled;
        _mediaBackupResetButton = GetNode<Button>("%MediaBackupResetButton");
        _mediaBackupResetButton.Icon = GetThemeIcon("Refresh", "AtlasIcons");
        _mediaBackupResetButton.Pressed += OnMediaBackupResetPressed;

        _multiEditCheckBox = GetNodeOrNull<CheckBox>("%MultiEditCheckBox");
        if (_multiEditCheckBox != null)
            _multiEditCheckBox.Toggled += OnMultiEditToggled;
        _multiEditResetButton = GetNodeOrNull<Button>("%MultiEditResetButton");
        if (_multiEditResetButton != null)
        {
            _multiEditResetButton.Icon = GetThemeIcon("Refresh", "AtlasIcons");
            _multiEditResetButton.Pressed += OnMultiEditResetPressed;
        }

        _selectNewCuesCheckBox = GetNodeOrNull<CheckBox>("%SelectNewCuesCheckBox");
        if (_selectNewCuesCheckBox != null)
            _selectNewCuesCheckBox.Toggled += OnSelectNewCuesToggled;
        _selectNewCuesResetButton = GetNodeOrNull<Button>("%SelectNewCuesResetButton");
        if (_selectNewCuesResetButton != null)
        {
            _selectNewCuesResetButton.Icon = GetThemeIcon("Refresh", "AtlasIcons");
            _selectNewCuesResetButton.Pressed += OnSelectNewCuesResetPressed;
        }

        _timelineWaveformsCheckBox = GetNodeOrNull<CheckBox>("%TimelineWaveformsCheckBox");
        if (_timelineWaveformsCheckBox != null)
            _timelineWaveformsCheckBox.Toggled += OnTimelineWaveformsToggled;
        _timelineWaveformsResetButton = GetNodeOrNull<Button>("%TimelineWaveformsResetButton");
        if (_timelineWaveformsResetButton != null)
        {
            _timelineWaveformsResetButton.Icon = GetThemeIcon("Refresh", "AtlasIcons");
            _timelineWaveformsResetButton.Pressed += OnTimelineWaveformsResetPressed;
        }

        // Only re-sync this form when a *settings* history entry was undone/redone —
        // not on cue undos (that re-wrote the spin box and polluted history / looked like
        // fade was jumping while undoing unrelated steps).
        if (_historyManager != null)
            _historyManager.HistoryRestored += OnHistoryRestored;
        if (_globalSignals != null)
            _globalSignals.NewSession += OnNewSession;

        SyncSettings();
    }

    public override void _ExitTree()
    {
        if (_historyManager != null)
            _historyManager.HistoryRestored -= OnHistoryRestored;
        if (_globalSignals != null)
            _globalSignals.NewSession -= OnNewSession;
        base._ExitTree();
    }

    /// <summary>
    /// After undo/redo of a settings-scoped entry, re-read show settings into this panel.
    /// </summary>
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
        if (!GodotObject.IsInstanceValid(this) || _globalData?.Settings == null)
            return;
        SyncSettings();
    }

    /// <summary>
    /// Pulls current <see cref="AppSettings"/> values into the form without re-firing edit handlers.
    /// </summary>
    private void SyncSettings()
    {
        if (_globalData?.Settings == null) return;

        _isSyncingUi = true;
        try
        {
            float uiPct = _globalData.Settings.UiScale * 100f;
            if (_uiScaleNum != null)
                _uiScaleNum.Text = uiPct + "%";
            _uiScaleSlider?.SetValueNoSignal(uiPct);

            if (_goScaleOptionButton != null)
            {
                _goScaleOptionButton.SetBlockSignals(true);
                _goScaleOptionButton.Selected = GoScaleToIndex(_globalData.Settings.GoScale);
                _goScaleOptionButton.SetBlockSignals(false);
            }

            _stopFadeSpinBox?.SetValueNoSignal(_globalData.Settings.StopFadeDuration);
            _mediaBackupCheckBox?.SetPressedNoSignal(_globalData.Settings.MediaBackupEnabled);
            _multiEditCheckBox?.SetPressedNoSignal(_globalData.Settings.MultiEditEnabled);
            _selectNewCuesCheckBox?.SetPressedNoSignal(_globalData.Settings.SelectNewCues);
            _timelineWaveformsCheckBox?.SetPressedNoSignal(_globalData.Settings.ShowTimelineWaveforms);

            UpdateAllResetButtons();
        }
        finally
        {
            _isSyncingUi = false;
        }
    }

    private void UpdateAllResetButtons()
    {
        UpdateUiScaleResetButton();
        UpdateGoScaleResetButton();
        UpdateStopFadeResetButton();
        UpdateMediaBackupResetButton();
        UpdateMultiEditResetButton();
        UpdateSelectNewCuesResetButton();
        UpdateTimelineWaveformsResetButton();
    }

    // ── UI Scale ──────────────────────────────────────────────────────────

    private void OnUiScaleSliderValueChanged(double value)
    {
        if (_isSyncingUi) return;
        _uiScaleNum.Text = value + "%";
    }

    private void OnUiScaleSliderDragEnded(bool _)
    {
        if (_isSyncingUi) return;
        ApplyUiScale((float)(_uiScaleSlider.Value / 100.0));
    }

    private void OnUiScaleTextSubmitted(string input)
    {
        if (_isSyncingUi) return;
        CommitUiScaleFromText(input);
    }

    private void CommitUiScaleFromText(string input)
    {
        string cleaned = (input ?? string.Empty).Replace("%", "").Trim();
        if (!float.TryParse(cleaned, out float value))
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Invalid value for UI Scale entered", 1);
            _uiScaleNum.Text = _globalData.Settings.UiScale * 100f + "%";
            return;
        }

        value = Mathf.Clamp(value, 50f, 200f);
        _uiScaleNum.Text = value + "%";
        _uiScaleSlider.SetValueNoSignal(value);
        ApplyUiScale(value / 100f);
        if (_uiScaleNum.HasFocus())
            _uiScaleNum.ReleaseFocus();
    }

    private void ApplyUiScale(float scaleFactor)
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;

        scaleFactor = Mathf.Clamp(scaleFactor, 0.5f, 2.0f);
        if (Mathf.IsEqualApprox(_globalData.Settings.UiScale, scaleFactor))
        {
            UpdateUiScaleResetButton();
            return;
        }

        _historyManager?.RecordSettingsChange("Change UI scale", null, "UiScale");
        _globalData.Settings.UiScale = scaleFactor;
        _globalSignals.EmitSignal(nameof(GlobalSignals.UiScaleChanged), scaleFactor);
        UpdateUiScaleResetButton();
    }

    private void OnUiScaleResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (Mathf.IsEqualApprox(_globalData.Settings.UiScale, AppSettings.DefaultUiScale))
        {
            SyncSettings();
            return;
        }

        _historyManager?.RecordSettingsChange("Reset UI scale", null, "UiScale");
        _globalData.Settings.UiScale = AppSettings.DefaultUiScale;
        SyncSettings();
        _globalSignals.EmitSignal(nameof(GlobalSignals.UiScaleChanged), AppSettings.DefaultUiScale);
    }

    private void UpdateUiScaleResetButton()
    {
        if (_uiScaleResetButton == null || _globalData?.Settings == null) return;

        bool atDefault = Mathf.IsEqualApprox(_globalData.Settings.UiScale, AppSettings.DefaultUiScale);
        _uiScaleResetButton.Visible = !atDefault;
        if (!atDefault)
            _uiScaleResetButton.TooltipText = $"Reset to default: {AppSettings.DefaultUiScale * 100f:0}%";
    }

    // ── Go Button Scale ───────────────────────────────────────────────────

    private void OnGoScaleItemSelected(long index)
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;

        int i = (int)index;
        if (i < 0 || i >= GoScaleValues.Length)
            i = GoScaleToIndex(AppSettings.DefaultGoScale);

        float scale = GoScaleValues[i];
        if (Mathf.IsEqualApprox(_globalData.Settings.GoScale, scale))
        {
            UpdateGoScaleResetButton();
            return;
        }

        _historyManager?.RecordSettingsChange("Change Go scale", null, "GoScale");
        _globalData.Settings.GoScale = scale;
        _globalSignals.EmitSignal(nameof(GlobalSignals.GoScaleChanged), _globalData.Settings.GoScale);
        UpdateGoScaleResetButton();
    }

    private void OnGoScaleResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (Mathf.IsEqualApprox(_globalData.Settings.GoScale, AppSettings.DefaultGoScale))
        {
            SyncSettings();
            return;
        }

        _historyManager?.RecordSettingsChange("Reset Go scale", null, "GoScale");
        _globalData.Settings.GoScale = AppSettings.DefaultGoScale;
        SyncSettings();
        _globalSignals.EmitSignal(nameof(GlobalSignals.GoScaleChanged), AppSettings.DefaultGoScale);
    }

    private void UpdateGoScaleResetButton()
    {
        if (_goScaleResetButton == null || _globalData?.Settings == null) return;

        bool atDefault = Mathf.IsEqualApprox(_globalData.Settings.GoScale, AppSettings.DefaultGoScale);
        _goScaleResetButton.Visible = !atDefault;
        if (!atDefault)
            _goScaleResetButton.TooltipText = $"Reset to default: {GoScaleLabel(AppSettings.DefaultGoScale)}";
    }

    private static int GoScaleToIndex(float scale)
    {
        for (int i = 0; i < GoScaleValues.Length; i++)
        {
            if (Mathf.IsEqualApprox(GoScaleValues[i], scale))
                return i;
        }
        for (int i = 0; i < GoScaleValues.Length; i++)
        {
            if (Mathf.IsEqualApprox(GoScaleValues[i], AppSettings.DefaultGoScale))
                return i;
        }
        return 1;
    }

    private static string GoScaleLabel(float scale)
    {
        return scale switch
        {
            0.5f => "Half Go",
            1.0f => "Base Scale Go",
            2.0f => "Big Go",
            4.0f => "Very Big Go",
            8.0f => "Wow, that's a big go",
            32.0f => "Nothing but Go",
            _ => scale.ToString("0.##")
        };
    }

    // ── Stop Fade Out ─────────────────────────────────────────────────────

    private void OnStopFadeChanged(double value)
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;

        float fade = (float)Mathf.Clamp(value, 0.0, 30.0);
        if (Mathf.IsEqualApprox(_globalData.Settings.StopFadeDuration, fade))
        {
            UpdateStopFadeResetButton();
            return;
        }

        // Simple discrete step: each committed value change is one undo entry (no coalesce).
        _historyManager?.RecordSettingsChange("Change stop fade", null, "StopFadeDuration");
        _globalData.Settings.StopFadeDuration = fade;
        UpdateStopFadeResetButton();
    }

    private void OnStopFadeResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (Mathf.IsEqualApprox(_globalData.Settings.StopFadeDuration, AppSettings.DefaultStopFadeDuration))
        {
            SyncSettings();
            return;
        }

        _historyManager?.RecordSettingsChange("Reset stop fade", null, "StopFadeDuration");
        _globalData.Settings.StopFadeDuration = AppSettings.DefaultStopFadeDuration;
        SyncSettings();
    }

    private void UpdateStopFadeResetButton()
    {
        if (_stopFadeResetButton == null || _globalData?.Settings == null) return;

        bool atDefault = Mathf.IsEqualApprox(_globalData.Settings.StopFadeDuration, AppSettings.DefaultStopFadeDuration);
        _stopFadeResetButton.Visible = !atDefault;
        if (!atDefault)
            _stopFadeResetButton.TooltipText = $"Reset to default: {AppSettings.DefaultStopFadeDuration:0.#}s";
    }

    // ── Media Backup ──────────────────────────────────────────────────────

    private void OnMediaBackupToggled(bool enabled)
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;

        if (_globalData.Settings.MediaBackupEnabled == enabled)
        {
            UpdateMediaBackupResetButton();
            return;
        }

        _historyManager?.RecordSettingsChange("Change media backup setting", null, "MediaBackupEnabled");
        _globalData.Settings.MediaBackupEnabled = enabled;
        UpdateMediaBackupResetButton();
    }

    private void OnMediaBackupResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_globalData.Settings.MediaBackupEnabled == AppSettings.DefaultMediaBackupEnabled)
        {
            SyncSettings();
            return;
        }

        _historyManager?.RecordSettingsChange("Reset media backup setting", null, "MediaBackupEnabled");
        _globalData.Settings.MediaBackupEnabled = AppSettings.DefaultMediaBackupEnabled;
        SyncSettings();
    }

    private void UpdateMediaBackupResetButton()
    {
        if (_mediaBackupResetButton == null || _globalData?.Settings == null) return;

        bool atDefault = _globalData.Settings.MediaBackupEnabled == AppSettings.DefaultMediaBackupEnabled;
        _mediaBackupResetButton.Visible = !atDefault;
        if (!atDefault)
        {
            string defaultText = AppSettings.DefaultMediaBackupEnabled ? "Enabled" : "Disabled";
            _mediaBackupResetButton.TooltipText = $"Reset to default: {defaultText}";
        }
    }

    // ── Multi-edit ────────────────────────────────────────────────────────

    private void OnMultiEditToggled(bool enabled)
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;

        if (_globalData.Settings.MultiEditEnabled == enabled)
        {
            UpdateMultiEditResetButton();
            return;
        }

        _historyManager?.RecordSettingsChange("Change multi-edit setting", null, "MultiEditEnabled");
        _globalData.Settings.MultiEditEnabled = enabled;
        // Refresh shell inspector if open so multi/single mode tracks the toggle immediately.
        _globalSignals?.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
        UpdateMultiEditResetButton();
    }

    private void OnMultiEditResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_globalData.Settings.MultiEditEnabled == AppSettings.DefaultMultiEditEnabled)
        {
            SyncSettings();
            return;
        }

        _historyManager?.RecordSettingsChange("Reset multi-edit setting", null, "MultiEditEnabled");
        _globalData.Settings.MultiEditEnabled = AppSettings.DefaultMultiEditEnabled;
        SyncSettings();
        _globalSignals?.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
    }

    private void UpdateMultiEditResetButton()
    {
        if (_multiEditResetButton == null || _globalData?.Settings == null) return;

        bool atDefault = _globalData.Settings.MultiEditEnabled == AppSettings.DefaultMultiEditEnabled;
        _multiEditResetButton.Visible = !atDefault;
        if (!atDefault)
        {
            string defaultText = AppSettings.DefaultMultiEditEnabled ? "Enabled" : "Disabled";
            _multiEditResetButton.TooltipText = $"Reset to default: {defaultText}";
        }
    }

    // ── Select new cues ───────────────────────────────────────────────────

    private void OnSelectNewCuesToggled(bool enabled)
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;

        if (_globalData.Settings.SelectNewCues == enabled)
        {
            UpdateSelectNewCuesResetButton();
            return;
        }

        _historyManager?.RecordSettingsChange("Change select new cues setting", null, "SelectNewCues");
        _globalData.Settings.SelectNewCues = enabled;
        UpdateSelectNewCuesResetButton();
    }

    private void OnSelectNewCuesResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_globalData.Settings.SelectNewCues == AppSettings.DefaultSelectNewCues)
        {
            SyncSettings();
            return;
        }

        _historyManager?.RecordSettingsChange("Reset select new cues setting", null, "SelectNewCues");
        _globalData.Settings.SelectNewCues = AppSettings.DefaultSelectNewCues;
        SyncSettings();
    }

    private void UpdateSelectNewCuesResetButton()
    {
        if (_selectNewCuesResetButton == null || _globalData?.Settings == null) return;

        bool atDefault = _globalData.Settings.SelectNewCues == AppSettings.DefaultSelectNewCues;
        _selectNewCuesResetButton.Visible = !atDefault;
        if (!atDefault)
        {
            string defaultText = AppSettings.DefaultSelectNewCues ? "Enabled" : "Disabled";
            _selectNewCuesResetButton.TooltipText = $"Reset to default: {defaultText}";
        }
    }

    // ── Timeline waveforms ────────────────────────────────────────────────

    private void OnTimelineWaveformsToggled(bool enabled)
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;

        if (_globalData.Settings.ShowTimelineWaveforms == enabled)
        {
            UpdateTimelineWaveformsResetButton();
            return;
        }

        _historyManager?.RecordSettingsChange("Change timeline waveforms setting", null, "ShowTimelineWaveforms");
        _globalData.Settings.ShowTimelineWaveforms = enabled;
        _globalSignals?.EmitSignal(nameof(GlobalSignals.ShowTimelineWaveformsChanged), enabled);
        UpdateTimelineWaveformsResetButton();
    }

    private void OnTimelineWaveformsResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_globalData.Settings.ShowTimelineWaveforms == AppSettings.DefaultShowTimelineWaveforms)
        {
            SyncSettings();
            return;
        }

        _historyManager?.RecordSettingsChange("Reset timeline waveforms setting", null, "ShowTimelineWaveforms");
        _globalData.Settings.ShowTimelineWaveforms = AppSettings.DefaultShowTimelineWaveforms;
        SyncSettings();
        _globalSignals?.EmitSignal(nameof(GlobalSignals.ShowTimelineWaveformsChanged),
            AppSettings.DefaultShowTimelineWaveforms);
    }

    private void UpdateTimelineWaveformsResetButton()
    {
        if (_timelineWaveformsResetButton == null || _globalData?.Settings == null) return;

        bool atDefault = _globalData.Settings.ShowTimelineWaveforms == AppSettings.DefaultShowTimelineWaveforms;
        _timelineWaveformsResetButton.Visible = !atDefault;
        if (!atDefault)
        {
            string defaultText = AppSettings.DefaultShowTimelineWaveforms ? "Enabled" : "Disabled";
            _timelineWaveformsResetButton.TooltipText = $"Reset to default: {defaultText}";
        }
    }
}
