using System;
using Cue2.Base.Classes;
using Cue2.Shared;
using Cue2.UI.Utilities;
using Godot;
using AppSettings = Cue2.Base.Classes.Settings;

namespace Cue2.UI.Scenes.Settings;

/// <summary>
/// Settings panel for show-scoped video component defaults (target layer, embedded audio output,
/// layout, opacity, loop, still-image hold, fades). Applied when a new video component is created.
/// </summary>
/// <remarks>
/// Each control shows a refresh button when not at its system default.
/// Stored with the showfile via <see cref="AppSettings"/> under the <c>VideoDefaults</c> key.
/// </remarks>
public partial class SettingsVideoDefaults : ScrollContainer
{
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;
    private HistoryManager _historyManager;
    private AudioDevices _audioDevices;

    private OptionButton _targetLayerOption;
    private Button _targetLayerResetButton;

    private OptionButton _audioOutputOption;
    private Button _audioOutputResetButton;

    private OptionButton _expandOption;
    private Button _expandResetButton;

    private OptionButton _stretchOption;
    private Button _stretchResetButton;

    private LineEdit _opacityInput;
    private Button _opacityResetButton;

    private CheckBox _loopCheckBox;
    private Button _loopResetButton;

    private LineEdit _playCountInput;
    private Button _playCountResetButton;

    private CheckBox _useAudioCheckBox;
    private Button _useAudioResetButton;

    private LineEdit _audioVolumeInput;
    private Button _audioVolumeResetButton;

    private HSlider _panSlider;
    private LineEdit _panInput;
    private Button _panResetButton;

    private LineEdit _imageDurationInput;
    private Button _imageDurationResetButton;

    private LineEdit _fadeInInput;
    private Button _fadeInResetButton;

    private LineEdit _fadeOutInput;
    private Button _fadeOutResetButton;

    private bool _isSyncingUi;
    private bool _isUpdatingPanUi;

    /// <inheritdoc />
    public override void _Ready()
    {
        GD.Print("SettingsVideoDefaults:_Ready - Video Defaults panel init");

        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _historyManager = _globalData?.HistoryManager;
        _audioDevices = GetNodeOrNull<AudioDevices>("/root/AudioDevices");

        _targetLayerOption = GetNodeOrNull<OptionButton>("%TargetLayerOption");
        _targetLayerResetButton = GetNodeOrNull<Button>("%TargetLayerResetButton");
        _audioOutputOption = GetNodeOrNull<OptionButton>("%AudioOutputOption");
        _audioOutputResetButton = GetNodeOrNull<Button>("%AudioOutputResetButton");
        _expandOption = GetNode<OptionButton>("%ExpandOption");
        _expandResetButton = GetNode<Button>("%ExpandResetButton");
        _stretchOption = GetNode<OptionButton>("%StretchOption");
        _stretchResetButton = GetNode<Button>("%StretchResetButton");
        _opacityInput = GetNode<LineEdit>("%OpacityInput");
        _opacityResetButton = GetNode<Button>("%OpacityResetButton");
        _loopCheckBox = GetNode<CheckBox>("%LoopCheckBox");
        _loopResetButton = GetNode<Button>("%LoopResetButton");
        _playCountInput = GetNode<LineEdit>("%PlayCountInput");
        _playCountResetButton = GetNode<Button>("%PlayCountResetButton");
        _useAudioCheckBox = GetNode<CheckBox>("%UseAudioCheckBox");
        _useAudioResetButton = GetNode<Button>("%UseAudioResetButton");
        _audioVolumeInput = GetNode<LineEdit>("%AudioVolumeInput");
        _audioVolumeResetButton = GetNode<Button>("%AudioVolumeResetButton");
        _panSlider = GetNode<HSlider>("%PanSlider");
        _panInput = GetNode<LineEdit>("%PanInput");
        _panResetButton = GetNode<Button>("%PanResetButton");
        _imageDurationInput = GetNode<LineEdit>("%ImageDurationInput");
        _imageDurationResetButton = GetNode<Button>("%ImageDurationResetButton");
        _fadeInInput = GetNode<LineEdit>("%FadeInInput");
        _fadeInResetButton = GetNode<Button>("%FadeInResetButton");
        _fadeOutInput = GetNode<LineEdit>("%FadeOutInput");
        _fadeOutResetButton = GetNode<Button>("%FadeOutResetButton");

        SetupResetButton(_targetLayerResetButton, OnTargetLayerResetPressed);
        SetupResetButton(_audioOutputResetButton, OnAudioOutputResetPressed);
        SetupResetButton(_expandResetButton, OnExpandResetPressed);
        SetupResetButton(_stretchResetButton, OnStretchResetPressed);
        SetupResetButton(_opacityResetButton, OnOpacityResetPressed);
        SetupResetButton(_loopResetButton, OnLoopResetPressed);
        SetupResetButton(_playCountResetButton, OnPlayCountResetPressed);
        SetupResetButton(_useAudioResetButton, OnUseAudioResetPressed);
        SetupResetButton(_audioVolumeResetButton, OnAudioVolumeResetPressed);
        SetupResetButton(_panResetButton, OnPanResetPressed);
        SetupResetButton(_imageDurationResetButton, OnImageDurationResetPressed);
        SetupResetButton(_fadeInResetButton, OnFadeInResetPressed);
        SetupResetButton(_fadeOutResetButton, OnFadeOutResetPressed);

        EnsureTextureOptions();

        if (_targetLayerOption != null)
            _targetLayerOption.ItemSelected += OnTargetLayerSelected;
        if (_audioOutputOption != null)
            _audioOutputOption.ItemSelected += OnAudioOutputSelected;
        _expandOption.ItemSelected += OnExpandSelected;
        _stretchOption.ItemSelected += OnStretchSelected;
        _opacityInput.TextSubmitted += OnOpacitySubmitted;
        _opacityInput.FocusExited += OnOpacityFocusExited;
        _loopCheckBox.Toggled += OnLoopToggled;
        _playCountInput.TextSubmitted += OnPlayCountSubmitted;
        _playCountInput.FocusExited += OnPlayCountFocusExited;
        _useAudioCheckBox.Toggled += OnUseAudioToggled;
        _audioVolumeInput.TextSubmitted += OnAudioVolumeSubmitted;
        _audioVolumeInput.FocusExited += OnAudioVolumeFocusExited;
        _panSlider.ValueChanged += OnPanSliderChanged;
        _panSlider.DragEnded += OnPanDragEnded;
        _panInput.TextSubmitted += OnPanSubmitted;
        _panInput.FocusExited += OnPanFocusExited;
        _imageDurationInput.TextSubmitted += OnImageDurationSubmitted;
        _imageDurationInput.FocusExited += OnImageDurationFocusExited;
        _fadeInInput.TextSubmitted += t => CommitFade(t, isIn: true);
        _fadeInInput.FocusExited += () =>
        {
            if (!_isSyncingUi && _fadeInInput != null)
                CommitFade(_fadeInInput.Text, isIn: true);
        };
        _fadeOutInput.TextSubmitted += t => CommitFade(t, isIn: false);
        _fadeOutInput.FocusExited += () =>
        {
            if (!_isSyncingUi && _fadeOutInput != null)
                CommitFade(_fadeOutInput.Text, isIn: false);
        };

        if (_historyManager != null)
            _historyManager.HistoryRestored += OnHistoryRestored;
        if (_globalSignals != null)
        {
            _globalSignals.NewSession += OnNewSession;
            _globalSignals.DisplaysChanged += OnDisplaysChanged;
        }

        SyncSettings();
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_historyManager != null)
            _historyManager.HistoryRestored -= OnHistoryRestored;
        if (_globalSignals != null)
        {
            _globalSignals.NewSession -= OnNewSession;
            _globalSignals.DisplaysChanged -= OnDisplaysChanged;
        }
        base._ExitTree();
    }

    private void OnDisplaysChanged()
    {
        if (!GodotObject.IsInstanceValid(this) || _globalData?.Settings == null)
            return;
        // Rebuild layer list only (keep other fields via SyncSettings).
        SyncSettings();
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

    private void EnsureTextureOptions()
    {
        if (_expandOption != null && _expandOption.ItemCount == 0)
        {
            AddOption(_expandOption, "Keep Size", (int)TextureRect.ExpandModeEnum.KeepSize);
            AddOption(_expandOption, "Ignore Size", (int)TextureRect.ExpandModeEnum.IgnoreSize);
            AddOption(_expandOption, "Fit Width Proportional",
                (int)TextureRect.ExpandModeEnum.FitWidthProportional);
            AddOption(_expandOption, "Fit Height Proportional",
                (int)TextureRect.ExpandModeEnum.FitHeightProportional);
        }

        if (_stretchOption != null && _stretchOption.ItemCount == 0)
        {
            AddOption(_stretchOption, "Scale", (int)TextureRect.StretchModeEnum.Scale);
            AddOption(_stretchOption, "Tile", (int)TextureRect.StretchModeEnum.Tile);
            AddOption(_stretchOption, "Keep", (int)TextureRect.StretchModeEnum.Keep);
            AddOption(_stretchOption, "Keep Centered", (int)TextureRect.StretchModeEnum.KeepCentered);
            AddOption(_stretchOption, "Keep Aspect", (int)TextureRect.StretchModeEnum.KeepAspect);
            AddOption(_stretchOption, "Keep Aspect Centered",
                (int)TextureRect.StretchModeEnum.KeepAspectCentered);
            AddOption(_stretchOption, "Keep Aspect Covered",
                (int)TextureRect.StretchModeEnum.KeepAspectCovered);
        }
    }

    private static void AddOption(OptionButton button, string label, int id)
    {
        int index = button.ItemCount;
        button.AddItem(label);
        button.SetItemMetadata(index, id);
    }

    private static void SelectByMetadata(OptionButton button, int metadata)
    {
        if (button == null) return;
        button.SetBlockSignals(true);
        for (int i = 0; i < button.ItemCount; i++)
        {
            if (button.GetItemMetadata(i).AsInt32() == metadata)
            {
                button.Selected = i;
                button.SetBlockSignals(false);
                return;
            }
        }
        button.Selected = 0;
        button.SetBlockSignals(false);
    }

    private void SyncSettings()
    {
        if (_globalData?.Settings == null) return;

        _isSyncingUi = true;
        try
        {
            var s = _globalData.Settings;
            ComponentDefaultsUi.PopulateTargetLayerOption(
                _targetLayerOption, s.VideoDefaultTargetLayerMode, s.VideoDefaultTargetLayerId);
            ComponentDefaultsUi.PopulateAudioOutputOption(
                _audioOutputOption, s, _audioDevices,
                s.VideoDefaultOutputMode, s.VideoDefaultPatchId, s.VideoDefaultDirectOutput);

            EnsureTextureOptions();
            SelectByMetadata(_expandOption, (int)s.VideoDefaultExpandMode);
            SelectByMetadata(_stretchOption, (int)s.VideoDefaultStretchMode);

            if (_opacityInput != null)
            {
                float pct = Mathf.Clamp(s.VideoDefaultOpacity, 0f, 1f) * 100f;
                _opacityInput.Text = pct.ToString("0.#");
            }

            _loopCheckBox?.SetPressedNoSignal(s.VideoDefaultLoop);
            if (_playCountInput != null)
                _playCountInput.Text = Math.Max(1, s.VideoDefaultPlayCount).ToString();
            _useAudioCheckBox?.SetPressedNoSignal(s.VideoDefaultUseAudio);

            if (_audioVolumeInput != null)
                _audioVolumeInput.Text = $"{UiUtilities.LinearToDb(s.VideoDefaultAudioVolume)}dB";

            _isUpdatingPanUi = true;
            try
            {
                float pan = Mathf.Clamp(s.VideoDefaultPan, -1f, 1f);
                _panSlider?.SetValueNoSignal(Mathf.Round(pan * 100f));
                if (_panInput != null)
                    _panInput.Text = UiUtilities.FormatPan(pan);
            }
            finally
            {
                _isUpdatingPanUi = false;
            }

            if (_imageDurationInput != null)
            {
                if (s.VideoDefaultImageDuration <= 0)
                    _imageDurationInput.Text = "Until stopped";
                else
                    _imageDurationInput.Text = UiUtilities.FormatTime(s.VideoDefaultImageDuration);
            }

            if (_fadeInInput != null)
                _fadeInInput.Text = UiUtilities.FormatTime(s.VideoDefaultFadeIn);
            if (_fadeOutInput != null)
                _fadeOutInput.Text = UiUtilities.FormatTime(s.VideoDefaultFadeOut);

            UpdateAllResetButtons();
        }
        finally
        {
            _isSyncingUi = false;
        }
    }

    private void UpdateAllResetButtons()
    {
        UpdateTargetLayerResetButton();
        UpdateAudioOutputResetButton();
        UpdateExpandResetButton();
        UpdateStretchResetButton();
        UpdateOpacityResetButton();
        UpdateLoopResetButton();
        UpdatePlayCountResetButton();
        UpdateUseAudioResetButton();
        UpdateAudioVolumeResetButton();
        UpdatePanResetButton();
        UpdateImageDurationResetButton();
        UpdateFadeInResetButton();
        UpdateFadeOutResetButton();
    }

    private void RecordHistory(string description)
    {
        _historyManager?.RecordSettingsChange(description, null, "VideoDefaults");
    }

    // ── Target layer / audio output ────────────────────────────────────────

    private void OnTargetLayerSelected(long index)
    {
        if (_isSyncingUi || _globalData?.Settings == null || _targetLayerOption == null) return;
        if (_historyManager?.IsRestoring == true) return;

        ComponentDefaultsUi.ReadTargetLayerSelection(
            _targetLayerOption, out var mode, out int layerId);
        var s = _globalData.Settings;
        if (s.VideoDefaultTargetLayerMode == mode && s.VideoDefaultTargetLayerId == layerId)
        {
            UpdateTargetLayerResetButton();
            return;
        }

        RecordHistory("Change default video target layer");
        s.VideoDefaultTargetLayerMode = mode;
        s.VideoDefaultTargetLayerId = layerId;
        UpdateTargetLayerResetButton();
    }

    private void OnTargetLayerResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        var s = _globalData.Settings;
        if (ComponentDefaultsUi.IsTargetLayerAtSystem(
                s.VideoDefaultTargetLayerMode, s.VideoDefaultTargetLayerId))
        {
            SyncSettings();
            return;
        }

        RecordHistory("Reset default video target layer");
        s.VideoDefaultTargetLayerMode = AppSettings.SystemDefaultVideoTargetLayerMode;
        s.VideoDefaultTargetLayerId = -1;
        SyncSettings();
    }

    private void UpdateTargetLayerResetButton()
    {
        if (_targetLayerResetButton == null || _globalData?.Settings == null) return;
        var s = _globalData.Settings;
        bool atDefault = ComponentDefaultsUi.IsTargetLayerAtSystem(
            s.VideoDefaultTargetLayerMode, s.VideoDefaultTargetLayerId);
        _targetLayerResetButton.Visible = !atDefault;
        if (!atDefault)
            _targetLayerResetButton.TooltipText = "Reset to default: First available layer";
    }

    private void OnAudioOutputSelected(long index)
    {
        if (_isSyncingUi || _globalData?.Settings == null || _audioOutputOption == null) return;
        if (_historyManager?.IsRestoring == true) return;

        ComponentDefaultsUi.ReadAudioOutputSelection(
            _audioOutputOption, out var mode, out int patchId, out string direct);
        var s = _globalData.Settings;
        if (s.VideoDefaultOutputMode == mode
            && s.VideoDefaultPatchId == patchId
            && string.Equals(s.VideoDefaultDirectOutput ?? string.Empty, direct ?? string.Empty,
                StringComparison.Ordinal))
        {
            UpdateAudioOutputResetButton();
            return;
        }

        RecordHistory("Change default video audio output");
        s.VideoDefaultOutputMode = mode;
        s.VideoDefaultPatchId = patchId;
        s.VideoDefaultDirectOutput = direct ?? string.Empty;
        UpdateAudioOutputResetButton();
    }

    private void OnAudioOutputResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        var s = _globalData.Settings;
        if (ComponentDefaultsUi.IsAudioOutputAtSystem(
                s.VideoDefaultOutputMode, s.VideoDefaultPatchId, s.VideoDefaultDirectOutput))
        {
            SyncSettings();
            return;
        }

        RecordHistory("Reset default video audio output");
        s.VideoDefaultOutputMode = AppSettings.SystemDefaultVideoOutputMode;
        s.VideoDefaultPatchId = -1;
        s.VideoDefaultDirectOutput = string.Empty;
        SyncSettings();
    }

    private void UpdateAudioOutputResetButton()
    {
        if (_audioOutputResetButton == null || _globalData?.Settings == null) return;
        var s = _globalData.Settings;
        bool atDefault = ComponentDefaultsUi.IsAudioOutputAtSystem(
            s.VideoDefaultOutputMode, s.VideoDefaultPatchId, s.VideoDefaultDirectOutput);
        _audioOutputResetButton.Visible = !atDefault;
        if (!atDefault)
            _audioOutputResetButton.TooltipText = "Reset to default: Preferred (Default Patch)";
    }

    // ── Expand / Stretch ───────────────────────────────────────────────────

    private void OnExpandSelected(long index)
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;

        int mode = _expandOption.GetItemMetadata((int)index).AsInt32();
        var expand = (TextureRect.ExpandModeEnum)mode;
        if (_globalData.Settings.VideoDefaultExpandMode == expand)
        {
            UpdateExpandResetButton();
            return;
        }

        RecordHistory("Change default video expand mode");
        _globalData.Settings.VideoDefaultExpandMode = expand;
        UpdateExpandResetButton();
    }

    private void OnExpandResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_globalData.Settings.VideoDefaultExpandMode == AppSettings.SystemDefaultVideoExpandMode)
        {
            SyncSettings();
            return;
        }

        RecordHistory("Reset default video expand mode");
        _globalData.Settings.VideoDefaultExpandMode = AppSettings.SystemDefaultVideoExpandMode;
        SyncSettings();
    }

    private void UpdateExpandResetButton()
    {
        if (_expandResetButton == null || _globalData?.Settings == null) return;
        bool atDefault = _globalData.Settings.VideoDefaultExpandMode
                         == AppSettings.SystemDefaultVideoExpandMode;
        _expandResetButton.Visible = !atDefault;
        if (!atDefault)
            _expandResetButton.TooltipText = "Reset to default: Ignore Size";
    }

    private void OnStretchSelected(long index)
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;

        int mode = _stretchOption.GetItemMetadata((int)index).AsInt32();
        var stretch = (TextureRect.StretchModeEnum)mode;
        if (_globalData.Settings.VideoDefaultStretchMode == stretch)
        {
            UpdateStretchResetButton();
            return;
        }

        RecordHistory("Change default video stretch mode");
        _globalData.Settings.VideoDefaultStretchMode = stretch;
        UpdateStretchResetButton();
    }

    private void OnStretchResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_globalData.Settings.VideoDefaultStretchMode == AppSettings.SystemDefaultVideoStretchMode)
        {
            SyncSettings();
            return;
        }

        RecordHistory("Reset default video stretch mode");
        _globalData.Settings.VideoDefaultStretchMode = AppSettings.SystemDefaultVideoStretchMode;
        SyncSettings();
    }

    private void UpdateStretchResetButton()
    {
        if (_stretchResetButton == null || _globalData?.Settings == null) return;
        bool atDefault = _globalData.Settings.VideoDefaultStretchMode
                         == AppSettings.SystemDefaultVideoStretchMode;
        _stretchResetButton.Visible = !atDefault;
        if (!atDefault)
            _stretchResetButton.TooltipText = "Reset to default: Keep Aspect Centered";
    }

    // ── Opacity ────────────────────────────────────────────────────────────

    private void OnOpacitySubmitted(string text) => CommitOpacity(text);

    private void OnOpacityFocusExited()
    {
        if (_isSyncingUi || _opacityInput == null) return;
        CommitOpacity(_opacityInput.Text);
    }

    private void CommitOpacity(string text)
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;

        string cleaned = (text ?? string.Empty).Replace("%", "").Trim();
        if (!float.TryParse(cleaned, out float pct))
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"Invalid default opacity: {text}", 1);
            float cur = Mathf.Clamp(_globalData.Settings.VideoDefaultOpacity, 0f, 1f) * 100f;
            _opacityInput.Text = cur.ToString("0.#");
            return;
        }

        pct = Mathf.Clamp(pct, 0f, 100f);
        float opacity = pct / 100f;
        _opacityInput.Text = pct.ToString("0.#");

        if (Math.Abs(_globalData.Settings.VideoDefaultOpacity - opacity) < 1e-4f)
        {
            UpdateOpacityResetButton();
            return;
        }

        RecordHistory("Change default video opacity");
        _globalData.Settings.VideoDefaultOpacity = opacity;
        UpdateOpacityResetButton();
    }

    private void OnOpacityResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (Math.Abs(_globalData.Settings.VideoDefaultOpacity
                      - AppSettings.SystemDefaultVideoOpacity) < 1e-4f)
        {
            SyncSettings();
            return;
        }

        RecordHistory("Reset default video opacity");
        _globalData.Settings.VideoDefaultOpacity = AppSettings.SystemDefaultVideoOpacity;
        SyncSettings();
    }

    private void UpdateOpacityResetButton()
    {
        if (_opacityResetButton == null || _globalData?.Settings == null) return;
        bool atDefault = Math.Abs(_globalData.Settings.VideoDefaultOpacity
                                  - AppSettings.SystemDefaultVideoOpacity) < 1e-4f;
        _opacityResetButton.Visible = !atDefault;
        if (!atDefault)
            _opacityResetButton.TooltipText = "Reset to default: 100%";
    }

    // ── Loop / play count ──────────────────────────────────────────────────

    private void OnLoopToggled(bool pressed)
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;
        if (_globalData.Settings.VideoDefaultLoop == pressed)
        {
            UpdateLoopResetButton();
            return;
        }

        RecordHistory(pressed ? "Enable default video loop" : "Disable default video loop");
        _globalData.Settings.VideoDefaultLoop = pressed;
        UpdateLoopResetButton();
    }

    private void OnLoopResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_globalData.Settings.VideoDefaultLoop == AppSettings.SystemDefaultVideoLoop)
        {
            SyncSettings();
            return;
        }

        RecordHistory("Reset default video loop");
        _globalData.Settings.VideoDefaultLoop = AppSettings.SystemDefaultVideoLoop;
        SyncSettings();
    }

    private void UpdateLoopResetButton()
    {
        if (_loopResetButton == null || _globalData?.Settings == null) return;
        bool atDefault = _globalData.Settings.VideoDefaultLoop == AppSettings.SystemDefaultVideoLoop;
        _loopResetButton.Visible = !atDefault;
        if (!atDefault)
            _loopResetButton.TooltipText =
                $"Reset to default: {(AppSettings.SystemDefaultVideoLoop ? "On" : "Off")}";
    }

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
            _playCountInput.Text = Math.Max(1, _globalData.Settings.VideoDefaultPlayCount).ToString();
            return;
        }

        _playCountInput.Text = count.ToString();
        if (_globalData.Settings.VideoDefaultPlayCount == count)
        {
            UpdatePlayCountResetButton();
            return;
        }

        RecordHistory("Change default video play count");
        _globalData.Settings.VideoDefaultPlayCount = count;
        UpdatePlayCountResetButton();
    }

    private void OnPlayCountResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_globalData.Settings.VideoDefaultPlayCount == AppSettings.SystemDefaultVideoPlayCount)
        {
            SyncSettings();
            return;
        }

        RecordHistory("Reset default video play count");
        _globalData.Settings.VideoDefaultPlayCount = AppSettings.SystemDefaultVideoPlayCount;
        SyncSettings();
    }

    private void UpdatePlayCountResetButton()
    {
        if (_playCountResetButton == null || _globalData?.Settings == null) return;
        bool atDefault = _globalData.Settings.VideoDefaultPlayCount
                         == AppSettings.SystemDefaultVideoPlayCount;
        _playCountResetButton.Visible = !atDefault;
        if (!atDefault)
            _playCountResetButton.TooltipText =
                $"Reset to default: {AppSettings.SystemDefaultVideoPlayCount}";
    }

    // ── Use audio / volume / pan ───────────────────────────────────────────

    private void OnUseAudioToggled(bool pressed)
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;
        if (_globalData.Settings.VideoDefaultUseAudio == pressed)
        {
            UpdateUseAudioResetButton();
            return;
        }

        RecordHistory(pressed ? "Enable default video audio" : "Disable default video audio");
        _globalData.Settings.VideoDefaultUseAudio = pressed;
        UpdateUseAudioResetButton();
    }

    private void OnUseAudioResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_globalData.Settings.VideoDefaultUseAudio == AppSettings.SystemDefaultVideoUseAudio)
        {
            SyncSettings();
            return;
        }

        RecordHistory("Reset default video use-audio");
        _globalData.Settings.VideoDefaultUseAudio = AppSettings.SystemDefaultVideoUseAudio;
        SyncSettings();
    }

    private void UpdateUseAudioResetButton()
    {
        if (_useAudioResetButton == null || _globalData?.Settings == null) return;
        bool atDefault = _globalData.Settings.VideoDefaultUseAudio
                         == AppSettings.SystemDefaultVideoUseAudio;
        _useAudioResetButton.Visible = !atDefault;
        if (!atDefault)
            _useAudioResetButton.TooltipText =
                $"Reset to default: {(AppSettings.SystemDefaultVideoUseAudio ? "On" : "Off")}";
    }

    private void OnAudioVolumeSubmitted(string text) => CommitAudioVolume(text);

    private void OnAudioVolumeFocusExited()
    {
        if (_isSyncingUi || _audioVolumeInput == null) return;
        CommitAudioVolume(_audioVolumeInput.Text);
    }

    private void CommitAudioVolume(string text)
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;

        try
        {
            if (!float.TryParse(text.Replace("dB", "").Trim(), out var dbValue))
            {
                _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                    $"Invalid default audio volume: {text}", 1);
                _audioVolumeInput.Text =
                    $"{UiUtilities.LinearToDb(_globalData.Settings.VideoDefaultAudioVolume)}dB";
                return;
            }

            if (dbValue > 0)
                dbValue = -dbValue;

            float volume = UiUtilities.DbToLinear(dbValue);
            _audioVolumeInput.Text = $"{UiUtilities.LinearToDb(volume)}dB";

            if (Math.Abs(_globalData.Settings.VideoDefaultAudioVolume - volume) < 1e-6f)
            {
                UpdateAudioVolumeResetButton();
                return;
            }

            RecordHistory("Change default video audio volume");
            _globalData.Settings.VideoDefaultAudioVolume = volume;
            UpdateAudioVolumeResetButton();
        }
        catch (Exception ex)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"Error parsing default audio volume: {ex.Message}", 2);
        }
    }

    private void OnAudioVolumeResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (Math.Abs(_globalData.Settings.VideoDefaultAudioVolume
                      - AppSettings.SystemDefaultVideoAudioVolume) < 1e-6f)
        {
            SyncSettings();
            return;
        }

        RecordHistory("Reset default video audio volume");
        _globalData.Settings.VideoDefaultAudioVolume = AppSettings.SystemDefaultVideoAudioVolume;
        SyncSettings();
    }

    private void UpdateAudioVolumeResetButton()
    {
        if (_audioVolumeResetButton == null || _globalData?.Settings == null) return;
        bool atDefault = Math.Abs(_globalData.Settings.VideoDefaultAudioVolume
                                  - AppSettings.SystemDefaultVideoAudioVolume) < 1e-6f;
        _audioVolumeResetButton.Visible = !atDefault;
        if (!atDefault)
            _audioVolumeResetButton.TooltipText =
                $"Reset to default: {UiUtilities.LinearToDb(AppSettings.SystemDefaultVideoAudioVolume)}dB";
    }

    private void OnPanSliderChanged(double value)
    {
        if (_isSyncingUi || _isUpdatingPanUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;

        float pan = Mathf.Clamp((float)value / 100f, -1f, 1f);
        if (Math.Abs(_globalData.Settings.VideoDefaultPan - pan) < 1e-6f)
            return;

        _historyManager?.RecordSettingsChange(
            "Change default video pan", "settings:video-defaults:pan", "VideoDefaults");
        _globalData.Settings.VideoDefaultPan = pan;

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
        _historyManager?.EndCoalesceSession("settings:video-defaults:pan");
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
            _panInput.Text = UiUtilities.FormatPan(_globalData.Settings.VideoDefaultPan);
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

        if (Math.Abs(_globalData.Settings.VideoDefaultPan - pan) < 1e-6f)
        {
            UpdatePanResetButton();
            return;
        }

        RecordHistory("Change default video pan");
        _globalData.Settings.VideoDefaultPan = pan;
        UpdatePanResetButton();
    }

    private void OnPanResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (Math.Abs(_globalData.Settings.VideoDefaultPan - AppSettings.SystemDefaultVideoPan) < 1e-6f)
        {
            SyncSettings();
            return;
        }

        RecordHistory("Reset default video pan");
        _globalData.Settings.VideoDefaultPan = AppSettings.SystemDefaultVideoPan;
        SyncSettings();
    }

    private void UpdatePanResetButton()
    {
        if (_panResetButton == null || _globalData?.Settings == null) return;
        bool atDefault = Math.Abs(_globalData.Settings.VideoDefaultPan
                                  - AppSettings.SystemDefaultVideoPan) < 1e-6f;
        _panResetButton.Visible = !atDefault;
        if (!atDefault)
            _panResetButton.TooltipText =
                $"Reset to default: {UiUtilities.FormatPan(AppSettings.SystemDefaultVideoPan)}";
    }

    // ── Image duration ─────────────────────────────────────────────────────

    private void OnImageDurationSubmitted(string text) => CommitImageDuration(text);

    private void OnImageDurationFocusExited()
    {
        if (_isSyncingUi || _imageDurationInput == null) return;
        CommitImageDuration(_imageDurationInput.Text);
    }

    private void CommitImageDuration(string text)
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (_historyManager?.IsRestoring == true) return;

        string trimmed = (text ?? string.Empty).Trim();
        double seconds;
        if (string.IsNullOrEmpty(trimmed)
            || trimmed.Equals("until stopped", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("0", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("inf", StringComparison.OrdinalIgnoreCase))
        {
            seconds = 0;
            _imageDurationInput.Text = "Until stopped";
        }
        else
        {
            var formatted = UiUtilities.ParseAndFormatTime(trimmed, out seconds, out string labeled);
            if (string.IsNullOrEmpty(formatted))
            {
                _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                    $"Invalid default image duration: {text}", 1);
                double cur = _globalData.Settings.VideoDefaultImageDuration;
                _imageDurationInput.Text = cur <= 0
                    ? "Until stopped"
                    : UiUtilities.FormatTime(cur);
                return;
            }

            seconds = Math.Max(0.0, seconds);
            if (seconds <= 0)
                _imageDurationInput.Text = "Until stopped";
            else
            {
                _imageDurationInput.Text = formatted;
                _imageDurationInput.TooltipText = labeled;
            }
        }

        if (Mathf.IsEqualApprox((float)_globalData.Settings.VideoDefaultImageDuration, (float)seconds))
        {
            UpdateImageDurationResetButton();
            return;
        }

        RecordHistory("Change default image duration");
        _globalData.Settings.VideoDefaultImageDuration = seconds;
        UpdateImageDurationResetButton();
    }

    private void OnImageDurationResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (Mathf.IsEqualApprox((float)_globalData.Settings.VideoDefaultImageDuration,
                (float)AppSettings.SystemDefaultVideoImageDuration))
        {
            SyncSettings();
            return;
        }

        RecordHistory("Reset default image duration");
        _globalData.Settings.VideoDefaultImageDuration = AppSettings.SystemDefaultVideoImageDuration;
        SyncSettings();
    }

    private void UpdateImageDurationResetButton()
    {
        if (_imageDurationResetButton == null || _globalData?.Settings == null) return;
        bool atDefault = Mathf.IsEqualApprox((float)_globalData.Settings.VideoDefaultImageDuration,
            (float)AppSettings.SystemDefaultVideoImageDuration);
        _imageDurationResetButton.Visible = !atDefault;
        if (!atDefault)
            _imageDurationResetButton.TooltipText = "Reset to default: Until stopped (images only)";
    }

    // ── Fades ──────────────────────────────────────────────────────────────

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
                ? _globalData.Settings.VideoDefaultFadeIn
                : _globalData.Settings.VideoDefaultFadeOut;
            field.Text = UiUtilities.FormatTime(current);
            return;
        }

        field.Text = formatted;
        field.TooltipText = labeled;
        seconds = Math.Max(0.0, seconds);

        double existing = isIn
            ? _globalData.Settings.VideoDefaultFadeIn
            : _globalData.Settings.VideoDefaultFadeOut;
        if (Mathf.IsEqualApprox((float)existing, (float)seconds))
        {
            if (isIn) UpdateFadeInResetButton();
            else UpdateFadeOutResetButton();
            return;
        }

        RecordHistory(isIn ? "Change default video fade-in" : "Change default video fade-out");
        if (isIn)
            _globalData.Settings.VideoDefaultFadeIn = seconds;
        else
            _globalData.Settings.VideoDefaultFadeOut = seconds;

        if (isIn) UpdateFadeInResetButton();
        else UpdateFadeOutResetButton();
    }

    private void OnFadeInResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (Mathf.IsEqualApprox((float)_globalData.Settings.VideoDefaultFadeIn,
                (float)AppSettings.SystemDefaultVideoFadeIn))
        {
            SyncSettings();
            return;
        }

        RecordHistory("Reset default video fade-in");
        _globalData.Settings.VideoDefaultFadeIn = AppSettings.SystemDefaultVideoFadeIn;
        SyncSettings();
    }

    private void OnFadeOutResetPressed()
    {
        if (_isSyncingUi || _globalData?.Settings == null) return;
        if (Mathf.IsEqualApprox((float)_globalData.Settings.VideoDefaultFadeOut,
                (float)AppSettings.SystemDefaultVideoFadeOut))
        {
            SyncSettings();
            return;
        }

        RecordHistory("Reset default video fade-out");
        _globalData.Settings.VideoDefaultFadeOut = AppSettings.SystemDefaultVideoFadeOut;
        SyncSettings();
    }

    private void UpdateFadeInResetButton()
    {
        if (_fadeInResetButton == null || _globalData?.Settings == null) return;
        bool atDefault = Mathf.IsEqualApprox((float)_globalData.Settings.VideoDefaultFadeIn,
            (float)AppSettings.SystemDefaultVideoFadeIn);
        _fadeInResetButton.Visible = !atDefault;
        if (!atDefault)
            _fadeInResetButton.TooltipText =
                $"Reset to default: {UiUtilities.FormatTime(AppSettings.SystemDefaultVideoFadeIn)}";
    }

    private void UpdateFadeOutResetButton()
    {
        if (_fadeOutResetButton == null || _globalData?.Settings == null) return;
        bool atDefault = Mathf.IsEqualApprox((float)_globalData.Settings.VideoDefaultFadeOut,
            (float)AppSettings.SystemDefaultVideoFadeOut);
        _fadeOutResetButton.Visible = !atDefault;
        if (!atDefault)
            _fadeOutResetButton.TooltipText =
                $"Reset to default: {UiUtilities.FormatTime(AppSettings.SystemDefaultVideoFadeOut)}";
    }
}
