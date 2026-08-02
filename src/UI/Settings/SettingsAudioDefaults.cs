using System;
using Cue2.Domain.Cuelist;
using Cue2.Domain.Playback;
using Cue2.Domain.Devices;
using Cue2.Domain.ShowSettings;
using Cue2.Domain.Metadata;
using Cue2.Domain.Cues;
using Cue2.Domain.Connections;
using Cue2.Domain.Library;
using Cue2.Domain.Commands;
using Cue2.Services;
using Cue2.UI.Utilities;
using Godot;
using AppSettings = Cue2.Domain.ShowSettings.Settings;

namespace Cue2.UI.Settings;

/// <summary>
/// Settings panel for show-scoped audio component defaults (output, volume, pan, loop, play count, fades).
/// Applied when a new audio component is created on a cue.
/// </summary>
/// <remarks>
/// Each control shows a refresh button when not at its system default (same pattern as Cue Defaults).
/// Stored with the showfile via <see cref="AppSettings"/> under the <c>AudioDefaults</c> key.
/// </remarks>
public partial class SettingsAudioDefaults : ScrollContainer
{
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;
    private HistoryManager _historyManager;
    private AudioDevices _audioDevices;

    private OptionButton _outputOption;
    private Button _outputResetButton;

    private LineEdit _volumeInput;
    private Button _volumeResetButton;

    private HSlider _panSlider;
    private LineEdit _panInput;
    private Button _panResetButton;

    private CheckBox _loopCheckBox;
    private Button _loopResetButton;

    private LineEdit _playCountInput;
    private Button _playCountResetButton;

    private LineEdit _fadeInInput;
    private Button _fadeInResetButton;

    private LineEdit _fadeOutInput;
    private Button _fadeOutResetButton;

    private bool _isSyncingUi;
    private bool _isUpdatingPanUi;

    /// <inheritdoc />
    public override void _Ready()
    {
        GD.Print("SettingsAudioDefaults:_Ready - Audio Defaults panel init");

        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _historyManager = _globalData?.HistoryManager;
        _audioDevices = GetNodeOrNull<AudioDevices>("/root/AudioDevices");

        _outputOption = GetNodeOrNull<OptionButton>("%OutputOption");
        _outputResetButton = GetNodeOrNull<Button>("%OutputResetButton");
        _volumeInput = GetNode<LineEdit>("%VolumeInput");
        _volumeResetButton = GetNode<Button>("%VolumeResetButton");
        _panSlider = GetNode<HSlider>("%PanSlider");
        _panInput = GetNode<LineEdit>("%PanInput");
        _panResetButton = GetNode<Button>("%PanResetButton");
        _loopCheckBox = GetNode<CheckBox>("%LoopCheckBox");
        _loopResetButton = GetNode<Button>("%LoopResetButton");
        _playCountInput = GetNode<LineEdit>("%PlayCountInput");
        _playCountResetButton = GetNode<Button>("%PlayCountResetButton");
        _fadeInInput = GetNode<LineEdit>("%FadeInInput");
        _fadeInResetButton = GetNode<Button>("%FadeInResetButton");
        _fadeOutInput = GetNode<LineEdit>("%FadeOutInput");
        _fadeOutResetButton = GetNode<Button>("%FadeOutResetButton");

        SetupResetButton(_outputResetButton, OnOutputResetPressed);
        SetupResetButton(_volumeResetButton, OnVolumeResetPressed);
        SetupResetButton(_panResetButton, OnPanResetPressed);
        SetupResetButton(_loopResetButton, OnLoopResetPressed);
        SetupResetButton(_playCountResetButton, OnPlayCountResetPressed);
        SetupResetButton(_fadeInResetButton, OnFadeInResetPressed);
        SetupResetButton(_fadeOutResetButton, OnFadeOutResetPressed);

        if (_outputOption != null)
            _outputOption.ItemSelected += OnOutputSelected;
        _volumeInput.TextSubmitted += OnVolumeSubmitted;
        _volumeInput.FocusExited += OnVolumeFocusExited;
        _panSlider.ValueChanged += OnPanSliderChanged;
        _panSlider.DragEnded += OnPanDragEnded;
        _panInput.TextSubmitted += OnPanSubmitted;
        _panInput.FocusExited += OnPanFocusExited;
        _loopCheckBox.Toggled += OnLoopToggled;
        _playCountInput.TextSubmitted += OnPlayCountSubmitted;
        _playCountInput.FocusExited += OnPlayCountFocusExited;
        _fadeInInput.TextSubmitted += OnFadeInSubmitted;
        _fadeInInput.FocusExited += OnFadeInFocusExited;
        _fadeOutInput.TextSubmitted += OnFadeOutSubmitted;
        _fadeOutInput.FocusExited += OnFadeOutFocusExited;

        if (_historyManager != null)
            _historyManager.HistoryRestored += OnHistoryRestored;
        if (_globalSignals != null)
            _globalSignals.NewSession += OnNewSession;

        SyncSettings();
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_historyManager != null)
            _historyManager.HistoryRestored -= OnHistoryRestored;
        if (_globalSignals != null)
            _globalSignals.NewSession -= OnNewSession;
        base._ExitTree();
    }

    private void SetupResetButton(Button button, Action pressed)
    {
        if (button == null) return;
        try
        {
            button.Icon = GetThemeIcon("Refresh", "AtlasIcons");
        }
        catch
        {
            // Icon optional
        }
        button.Pressed += pressed;
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
        if (!GodotObject.IsInstanceValid(this) || _globalData?.Settings == null)
            return;
        SyncSettings();
    }

    /// <summary>
    /// Pulls current audio defaults into the form without re-firing edit handlers.
    /// </summary>
    private void SyncSettings()
    {
        if (_globalData?.Settings == null) return;

        _isSyncingUi = true;
        try
        {
            var s = _globalData.Settings;
            ComponentDefaultsUi.PopulateAudioOutputOption(
                _outputOption, s, _audioDevices,
                s.AudioDefaultOutputMode, s.AudioDefaultPatchId, s.AudioDefaultDirectOutput);

            if (_volumeInput != null)
                _volumeInput.Text = $"{UiUtilities.LinearToDb((float)s.AudioDefaultVolume)}dB";

            _isUpdatingPanUi = true;
            try
            {
                float pan = Mathf.Clamp(s.AudioDefaultPan, -1f, 1f);
                _panSlider?.SetValueNoSignal(Mathf.Round(pan * 100f));
                if (_panInput != null)
                    _panInput.Text = UiUtilities.FormatPan(pan);
            }
            finally
            {
                _isUpdatingPanUi = false;
            }

            _loopCheckBox?.SetPressedNoSignal(s.AudioDefaultLoop);
            if (_playCountInput != null)
                _playCountInput.Text = Math.Max(1, s.AudioDefaultPlayCount).ToString();
            if (_fadeInInput != null)
                _fadeInInput.Text = UiUtilities.FormatTime(s.AudioDefaultFadeIn);
            if (_fadeOutInput != null)
                _fadeOutInput.Text = UiUtilities.FormatTime(s.AudioDefaultFadeOut);

            UpdateAllResetButtons();
        }
        finally
        {
            _isSyncingUi = false;
        }
    }

    private void UpdateAllResetButtons()
    {
        UpdateOutputResetButton();
        UpdateVolumeResetButton();
        UpdatePanResetButton();
        UpdateLoopResetButton();
        UpdatePlayCountResetButton();
        UpdateFadeInResetButton();
        UpdateFadeOutResetButton();
    }

    private void RecordHistory(string description)
    {
        _historyManager?.RecordSettingsChange(description, null, "AudioDefaults");
    }

    // ── Output ─────────────────────────────────────────────────────────────

    private void OnOutputSelected(long index)
    {
        if (_isSyncingUi || _globalData?.Settings == null || _outputOption == null) return;
        if (_historyManager?.IsRestoring == true) return;

        ComponentDefaultsUi.ReadAudioOutputSelection(
            _outputOption, out var mode, out int patchId, out string direct);
        var s = _globalData.Settings;
        if (s.AudioDefaultOutputMode == mode
            && s.AudioDefaultPatchId == patchId
            && string.Equals(s.AudioDefaultDirectOutput ?? string.Empty, direct ?? string.Empty,
                StringComparison.Ordinal))
        {
            UpdateOutputResetButton();
            return;
        }

        RecordHistory("Change default audio output");
        s.AudioDefaultOutputMode = mode;
        s.AudioDefaultPatchId = patchId;
        s.AudioDefaultDirectOutput = direct ?? string.Empty;
        UpdateOutputResetButton();
    }

    private void OnOutputResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        var s = _globalData.Settings;
        if (ComponentDefaultsUi.IsAudioOutputAtSystem(
                s.AudioDefaultOutputMode, s.AudioDefaultPatchId, s.AudioDefaultDirectOutput))
        {
            SyncSettings();
            return;
        }

        RecordHistory("Reset default audio output");
        s.AudioDefaultOutputMode = AppSettings.SystemDefaultAudioOutputMode;
        s.AudioDefaultPatchId = -1;
        s.AudioDefaultDirectOutput = string.Empty;
        SyncSettings();
    }

    private void UpdateOutputResetButton()
    {
        if (_outputResetButton == null || _globalData?.Settings == null) return;
        var s = _globalData.Settings;
        bool atDefault = ComponentDefaultsUi.IsAudioOutputAtSystem(
            s.AudioDefaultOutputMode, s.AudioDefaultPatchId, s.AudioDefaultDirectOutput);
        _outputResetButton.Visible = !atDefault;
        if (!atDefault)
            _outputResetButton.TooltipText = "Reset to default: Preferred (Default Patch)";
    }

    // ── Volume ─────────────────────────────────────────────────────────────

    private void OnVolumeSubmitted(string text) => CommitVolume(text);

    private void OnVolumeFocusExited()
    {
        if (_isSyncingUi || _volumeInput == null) return;
        CommitVolume(_volumeInput.Text);
    }

    private void CommitVolume(string text)
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;

        try
        {
            if (!float.TryParse(text.Replace("dB", "").Trim(), out var dbValue))
            {
                _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                    $"Invalid default volume: {text}", 1);
                _volumeInput.Text =
                    $"{UiUtilities.LinearToDb((float)_globalData.Settings.AudioDefaultVolume)}dB";
                return;
            }

            if (dbValue > 0)
                dbValue = -dbValue;

            float volume = UiUtilities.DbToLinear(dbValue);
            _volumeInput.Text = $"{UiUtilities.LinearToDb(volume)}dB";

            if (Math.Abs(_globalData.Settings.AudioDefaultVolume - volume) < 1e-6)
            {
                UpdateVolumeResetButton();
                return;
            }

            RecordHistory("Change default audio volume");
            _globalData.Settings.AudioDefaultVolume = volume;
            UpdateVolumeResetButton();
        }
        catch (Exception ex)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"Error parsing default volume: {ex.Message}", 2);
        }
    }

    private void OnVolumeResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (Math.Abs(_globalData.Settings.AudioDefaultVolume - AppSettings.SystemDefaultAudioVolume) < 1e-6)
        {
            SyncSettings();
            return;
        }

        RecordHistory("Reset default audio volume");
        _globalData.Settings.AudioDefaultVolume = AppSettings.SystemDefaultAudioVolume;
        SyncSettings();
    }

    private void UpdateVolumeResetButton()
    {
        if (_volumeResetButton == null || _globalData?.Settings == null) return;
        bool atDefault = Math.Abs(_globalData.Settings.AudioDefaultVolume
                                  - AppSettings.SystemDefaultAudioVolume) < 1e-6;
        _volumeResetButton.Visible = !atDefault;
        if (!atDefault)
            _volumeResetButton.TooltipText =
                $"Reset to default: {UiUtilities.LinearToDb((float)AppSettings.SystemDefaultAudioVolume)}dB";
    }

    // ── Pan ────────────────────────────────────────────────────────────────

    private void OnPanSliderChanged(double value)
    {
        if (_isSyncingUi || _isUpdatingPanUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;

        float pan = Mathf.Clamp((float)value / 100f, -1f, 1f);
        if (Math.Abs(_globalData.Settings.AudioDefaultPan - pan) < 1e-6f)
            return;

        _historyManager?.RecordSettingsChange(
            "Change default audio pan", "settings:audio-defaults:pan", "AudioDefaults");
        _globalData.Settings.AudioDefaultPan = pan;

        _isUpdatingPanUi = true;
        try
        {
            if (_panInput != null)
                _panInput.Text = UiUtilities.FormatPan(pan);
        }
        finally
        {
            _isUpdatingPanUi = false;
        }

        UpdatePanResetButton();
    }

    private void OnPanDragEnded(bool valueChanged)
    {
        if (!valueChanged) return;
        _historyManager?.EndCoalesceSession("settings:audio-defaults:pan");
    }

    private void OnPanSubmitted(string text) => CommitPan(text);

    private void OnPanFocusExited()
    {
        if (_isSyncingUi || _panInput == null) return;
        CommitPan(_panInput.Text);
    }

    private void CommitPan(string text)
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;

        if (!UiUtilities.TryParsePan(text, out float pan))
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"Invalid default pan: {text}", 1);
            _panInput.Text = UiUtilities.FormatPan(_globalData.Settings.AudioDefaultPan);
            return;
        }

        pan = Mathf.Clamp(pan, -1f, 1f);
        _panInput.Text = UiUtilities.FormatPan(pan);
        _isUpdatingPanUi = true;
        try
        {
            _panSlider?.SetValueNoSignal(Mathf.Round(pan * 100f));
        }
        finally
        {
            _isUpdatingPanUi = false;
        }

        if (Math.Abs(_globalData.Settings.AudioDefaultPan - pan) < 1e-6f)
        {
            UpdatePanResetButton();
            return;
        }

        RecordHistory("Change default audio pan");
        _globalData.Settings.AudioDefaultPan = pan;
        UpdatePanResetButton();
    }

    private void OnPanResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (Math.Abs(_globalData.Settings.AudioDefaultPan - AppSettings.SystemDefaultAudioPan) < 1e-6f)
        {
            SyncSettings();
            return;
        }

        RecordHistory("Reset default audio pan");
        _globalData.Settings.AudioDefaultPan = AppSettings.SystemDefaultAudioPan;
        SyncSettings();
    }

    private void UpdatePanResetButton()
    {
        if (_panResetButton == null || _globalData?.Settings == null) return;
        bool atDefault = Math.Abs(_globalData.Settings.AudioDefaultPan
                                  - AppSettings.SystemDefaultAudioPan) < 1e-6f;
        _panResetButton.Visible = !atDefault;
        if (!atDefault)
            _panResetButton.TooltipText =
                $"Reset to default: {UiUtilities.FormatPan(AppSettings.SystemDefaultAudioPan)}";
    }

    // ── Loop ───────────────────────────────────────────────────────────────

    private void OnLoopToggled(bool pressed)
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;

        if (_globalData.Settings.AudioDefaultLoop == pressed)
        {
            UpdateLoopResetButton();
            return;
        }

        RecordHistory(pressed ? "Enable default audio loop" : "Disable default audio loop");
        _globalData.Settings.AudioDefaultLoop = pressed;
        UpdateLoopResetButton();
    }

    private void OnLoopResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_globalData.Settings.AudioDefaultLoop == AppSettings.SystemDefaultAudioLoop)
        {
            SyncSettings();
            return;
        }

        RecordHistory("Reset default audio loop");
        _globalData.Settings.AudioDefaultLoop = AppSettings.SystemDefaultAudioLoop;
        SyncSettings();
    }

    private void UpdateLoopResetButton()
    {
        if (_loopResetButton == null || _globalData?.Settings == null) return;
        bool atDefault = _globalData.Settings.AudioDefaultLoop == AppSettings.SystemDefaultAudioLoop;
        _loopResetButton.Visible = !atDefault;
        if (!atDefault)
            _loopResetButton.TooltipText =
                $"Reset to default: {(AppSettings.SystemDefaultAudioLoop ? "On" : "Off")}";
    }

    // ── Play count ─────────────────────────────────────────────────────────

    private void OnPlayCountSubmitted(string text) => CommitPlayCount(text);

    private void OnPlayCountFocusExited()
    {
        if (_isSyncingUi || _playCountInput == null) return;
        CommitPlayCount(_playCountInput.Text);
    }

    private void CommitPlayCount(string text)
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;

        if (!int.TryParse(text.Trim(), out int count) || count < 1)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"Invalid default play count: {text}", 1);
            _playCountInput.Text = Math.Max(1, _globalData.Settings.AudioDefaultPlayCount).ToString();
            return;
        }

        _playCountInput.Text = count.ToString();
        if (_globalData.Settings.AudioDefaultPlayCount == count)
        {
            UpdatePlayCountResetButton();
            return;
        }

        RecordHistory("Change default audio play count");
        _globalData.Settings.AudioDefaultPlayCount = count;
        UpdatePlayCountResetButton();
    }

    private void OnPlayCountResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_globalData.Settings.AudioDefaultPlayCount == AppSettings.SystemDefaultAudioPlayCount)
        {
            SyncSettings();
            return;
        }

        RecordHistory("Reset default audio play count");
        _globalData.Settings.AudioDefaultPlayCount = AppSettings.SystemDefaultAudioPlayCount;
        SyncSettings();
    }

    private void UpdatePlayCountResetButton()
    {
        if (_playCountResetButton == null || _globalData?.Settings == null) return;
        bool atDefault = _globalData.Settings.AudioDefaultPlayCount
                         == AppSettings.SystemDefaultAudioPlayCount;
        _playCountResetButton.Visible = !atDefault;
        if (!atDefault)
            _playCountResetButton.TooltipText =
                $"Reset to default: {AppSettings.SystemDefaultAudioPlayCount}";
    }

    // ── Fade in / out ──────────────────────────────────────────────────────

    private void OnFadeInSubmitted(string text) => CommitFade(text, isIn: true);

    private void OnFadeInFocusExited()
    {
        if (_isSyncingUi || _fadeInInput == null) return;
        CommitFade(_fadeInInput.Text, isIn: true);
    }

    private void OnFadeOutSubmitted(string text) => CommitFade(text, isIn: false);

    private void OnFadeOutFocusExited()
    {
        if (_isSyncingUi || _fadeOutInput == null) return;
        CommitFade(_fadeOutInput.Text, isIn: false);
    }

    private void CommitFade(string text, bool isIn)
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;

        var field = isIn ? _fadeInInput : _fadeOutInput;
        var formatted = UiUtilities.ParseAndFormatTime(text, out var seconds, out string labeled);
        if (string.IsNullOrEmpty(formatted))
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"Invalid default fade time: {text}", 1);
            double current = isIn
                ? _globalData.Settings.AudioDefaultFadeIn
                : _globalData.Settings.AudioDefaultFadeOut;
            field.Text = UiUtilities.FormatTime(current);
            return;
        }

        field.Text = formatted;
        field.TooltipText = labeled;
        seconds = Math.Max(0.0, seconds);

        double existing = isIn
            ? _globalData.Settings.AudioDefaultFadeIn
            : _globalData.Settings.AudioDefaultFadeOut;
        if (Mathf.IsEqualApprox((float)existing, (float)seconds))
        {
            if (isIn) UpdateFadeInResetButton();
            else UpdateFadeOutResetButton();
            return;
        }

        RecordHistory(isIn ? "Change default audio fade-in" : "Change default audio fade-out");
        if (isIn)
            _globalData.Settings.AudioDefaultFadeIn = seconds;
        else
            _globalData.Settings.AudioDefaultFadeOut = seconds;

        if (isIn) UpdateFadeInResetButton();
        else UpdateFadeOutResetButton();
    }

    private void OnFadeInResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (Mathf.IsEqualApprox((float)_globalData.Settings.AudioDefaultFadeIn,
                (float)AppSettings.SystemDefaultAudioFadeIn))
        {
            SyncSettings();
            return;
        }

        RecordHistory("Reset default audio fade-in");
        _globalData.Settings.AudioDefaultFadeIn = AppSettings.SystemDefaultAudioFadeIn;
        SyncSettings();
    }

    private void OnFadeOutResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (Mathf.IsEqualApprox((float)_globalData.Settings.AudioDefaultFadeOut,
                (float)AppSettings.SystemDefaultAudioFadeOut))
        {
            SyncSettings();
            return;
        }

        RecordHistory("Reset default audio fade-out");
        _globalData.Settings.AudioDefaultFadeOut = AppSettings.SystemDefaultAudioFadeOut;
        SyncSettings();
    }

    private void UpdateFadeInResetButton()
    {
        if (_fadeInResetButton == null || _globalData?.Settings == null) return;
        bool atDefault = Mathf.IsEqualApprox((float)_globalData.Settings.AudioDefaultFadeIn,
            (float)AppSettings.SystemDefaultAudioFadeIn);
        _fadeInResetButton.Visible = !atDefault;
        if (!atDefault)
            _fadeInResetButton.TooltipText =
                $"Reset to default: {UiUtilities.FormatTime(AppSettings.SystemDefaultAudioFadeIn)}";
    }

    private void UpdateFadeOutResetButton()
    {
        if (_fadeOutResetButton == null || _globalData?.Settings == null) return;
        bool atDefault = Mathf.IsEqualApprox((float)_globalData.Settings.AudioDefaultFadeOut,
            (float)AppSettings.SystemDefaultAudioFadeOut);
        _fadeOutResetButton.Visible = !atDefault;
        if (!atDefault)
            _fadeOutResetButton.TooltipText =
                $"Reset to default: {UiUtilities.FormatTime(AppSettings.SystemDefaultAudioFadeOut)}";
    }
}
