using Cue2.Shared;
using Godot;
using AppSettings = Cue2.Base.Classes.Settings;

namespace Cue2.UI.Scenes.Settings;

/// <summary>
/// General settings panel: UI scale, Go button scale, stop fade-out, and media backup.
/// Each setting shows a refresh button when not at its system default (same pattern as Cue2 Preferences).
/// Values are stored with the showfile via <see cref="AppSettings"/>.
/// </summary>
public partial class SettingsGeneral : ScrollContainer
{
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;

    private LineEdit _uiScaleNum;
    private HSlider _uiScaleSlider;
    private Button _uiScaleResetButton;

    private OptionButton _goScaleOptionButton;
    private Button _goScaleResetButton;

    private SpinBox _stopFadeSpinBox;
    private Button _stopFadeResetButton;

    private CheckBox _mediaBackupCheckBox;
    private Button _mediaBackupResetButton;

    /// <summary>Go scale option index → scale factor (matches OptionButton order).</summary>
    private static readonly float[] GoScaleValues = { 0.5f, 1.0f, 2.0f, 4.0f, 8.0f, 32.0f };

    public override void _Ready()
    {
        GD.Print("SettingsGeneral:_Ready - Settings General Init");

        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNode<GlobalData>("/root/GlobalData");

        _uiScaleNum = GetNode<LineEdit>("%UiScaleNum");
        _uiScaleSlider = GetNode<HSlider>("%UiScaleSlider");
        _uiScaleResetButton = GetNode<Button>("%UiScaleResetButton");
        _uiScaleResetButton.Icon = GetThemeIcon("Refresh", "AtlasIcons");
        _uiScaleResetButton.Pressed += OnUiScaleResetPressed;

        _uiScaleSlider.ValueChanged += OnUiScaleSliderValueChanged;
        _uiScaleSlider.DragEnded += OnUiScaleSliderDragEnded;
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

        _mediaBackupCheckBox = GetNode<CheckBox>("%MediaBackupCheckBox");
        _mediaBackupCheckBox.Toggled += OnMediaBackupToggled;
        _mediaBackupResetButton = GetNode<Button>("%MediaBackupResetButton");
        _mediaBackupResetButton.Icon = GetThemeIcon("Refresh", "AtlasIcons");
        _mediaBackupResetButton.Pressed += OnMediaBackupResetPressed;

        SyncSettings();
    }

    private void SyncSettings()
    {
        float uiPct = _globalData.Settings.UiScale * 100f;
        _uiScaleNum.Text = uiPct + "%";
        _uiScaleSlider.SetValueNoSignal(uiPct);

        _goScaleOptionButton.Selected = GoScaleToIndex(_globalData.Settings.GoScale);

        _stopFadeSpinBox.SetValueNoSignal(_globalData.Settings.StopFadeDuration);

        _mediaBackupCheckBox.SetPressedNoSignal(_globalData.Settings.MediaBackupEnabled);

        UpdateAllResetButtons();
    }

    private void UpdateAllResetButtons()
    {
        UpdateUiScaleResetButton();
        UpdateGoScaleResetButton();
        UpdateStopFadeResetButton();
        UpdateMediaBackupResetButton();
    }

    // ── UI Scale ──────────────────────────────────────────────────────────

    private void OnUiScaleSliderValueChanged(double value)
    {
        _uiScaleNum.Text = value + "%";
    }

    private void OnUiScaleSliderDragEnded(bool _)
    {
        ApplyUiScale((float)(_uiScaleSlider.Value / 100.0));
    }

    private void OnUiScaleTextSubmitted(string input)
    {
        string cleaned = input.Replace("%", "").Trim();
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
    }

    private void ApplyUiScale(float scaleFactor)
    {
        scaleFactor = Mathf.Clamp(scaleFactor, 0.5f, 2.0f);
        _globalData.Settings.UiScale = scaleFactor;
        _globalSignals.EmitSignal(nameof(GlobalSignals.UiScaleChanged), scaleFactor);
        UpdateUiScaleResetButton();
    }

    private void OnUiScaleResetPressed()
    {
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
        int i = (int)index;
        if (i < 0 || i >= GoScaleValues.Length)
            i = GoScaleToIndex(AppSettings.DefaultGoScale);

        _globalData.Settings.GoScale = GoScaleValues[i];
        _globalSignals.EmitSignal(nameof(GlobalSignals.GoScaleChanged), _globalData.Settings.GoScale);
        UpdateGoScaleResetButton();
    }

    private void OnGoScaleResetPressed()
    {
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
        // Fall back to default (Base Scale Go = index 1)
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
        _globalData.Settings.StopFadeDuration = (float)Mathf.Clamp(value, 0.0, 30.0);
        UpdateStopFadeResetButton();
    }

    private void OnStopFadeResetPressed()
    {
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
        _globalData.Settings.MediaBackupEnabled = enabled;
        UpdateMediaBackupResetButton();
    }

    private void OnMediaBackupResetPressed()
    {
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
}
