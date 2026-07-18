using System;
using Cue2.Base;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Cue2.UI.Utilities;
using Godot;
using System.Linq;

namespace Cue2.UI.Scenes.Inspectors;

/// <summary>
/// Inspector card for a single <see cref="ControlComponent"/> (GO / Pause / Stop target).
/// </summary>
/// <remarks>
/// User may edit target by id or cue number; the other field and name label update when a match is found.
/// Hold the pick-target button and release over a shell to assign the target from the cuelist.
/// Stop/GO cards expose fade times with per-field reset buttons.
/// Multiple controls on one cue run in list order (reorder with up/down).
/// </remarks>
public partial class ControlComponentCard : PanelContainer
{
    private Label _orderLabel;
    private Label _actionLabel;
    private Button _moveUpButton;
    private Button _moveDownButton;
    private Button _pickTargetButton;
    private Button _deleteButton;
    private LineEdit _idLineEdit;
    private LineEdit _numberLineEdit;
    private Label _nameLabel;
    private Button _targetResetButton;
    private Label _fadeCaption;
    private Control _fadeRow;
    private LineEdit _fadeLineEdit;
    private Button _fadeResetButton;

    private Label _modeCaption;
    private OptionButton _fadeModeOption;
    private Label _noFadableLabel;
    private Control _noFadableSpacer;
    private Label _audioFadeCaption;
    private Control _audioFadeRow;
    private CheckBox _audioFadeEnable;
    private LineEdit _audioFadeLineEdit;
    private Label _opacityFadeCaption;
    private Control _opacityFadeRow;
    private CheckBox _opacityFadeEnable;
    private LineEdit _opacityFadeLineEdit;
    private Label _seekTimeCaption;
    private LineEdit _seekTimeLineEdit;
    private Label _layerCaption;
    private OptionButton _targetLayerOption;
    private Label _sizeEnableCaption;
    private Control _sizeEnableRow;
    private CheckBox _sizeEnable;
    private LineEdit _sizeXLineEdit;
    private LineEdit _sizeYLineEdit;
    private Label _posEnableCaption;
    private Control _posEnableRow;
    private CheckBox _posEnable;
    private LineEdit _posXLineEdit;
    private LineEdit _posYLineEdit;

    private ControlComponent _component;
    private ControlInspector _inspector;
    private GlobalData _globalData;
    private GlobalSignals _globalSignals;

    private int _orderIndex;
    private int _orderCount = 1;

    private bool _isSyncingUi;
    private bool _idEditing;
    private bool _numberEditing;
    private bool _fadeEditing;
    private bool _audioFadeEditing;
    private bool _opacityFadeEditing;
    private bool _seekTimeEditing;
    private bool _sizeEditing;
    private bool _posEditing;

    // --- Interactive pick-target tool ---
    private bool _pickActive;
    private CanvasLayer _pickLayer;
    private PanelContainer _pickBadge;
    private Label _pickBadgeLabel;
    private TextureRect _pickBadgeIcon;
    private ShellBar _pickHoverShell;
    private Color _pickHoverRestoreModulate = Colors.White;

    /// <inheritdoc />
    public override void _Ready()
    {
        _globalData = GetNodeOrNull<GlobalData>("/root/GlobalData");
        _globalSignals = GetNodeOrNull<GlobalSignals>("/root/GlobalSignals");

        _orderLabel = GetNodeOrNull<Label>("%OrderLabel");
        _actionLabel = GetNode<Label>("%ActionLabel");
        _moveUpButton = GetNodeOrNull<Button>("%MoveUpButton");
        _moveDownButton = GetNodeOrNull<Button>("%MoveDownButton");
        _pickTargetButton = GetNode<Button>("%PickTargetButton");
        _deleteButton = GetNode<Button>("%DeleteButton");
        _idLineEdit = GetNode<LineEdit>("%IdLineEdit");
        _numberLineEdit = GetNode<LineEdit>("%NumberLineEdit");
        _nameLabel = GetNode<Label>("%NameLabel");
        _targetResetButton = GetNode<Button>("%TargetResetButton");
        _fadeCaption = GetNode<Label>("%FadeCaption");
        _fadeRow = GetNode<Control>("%FadeRow");
        _fadeLineEdit = GetNode<LineEdit>("%FadeLineEdit");
        _fadeResetButton = GetNode<Button>("%FadeResetButton");

        _modeCaption = GetNodeOrNull<Label>("%ModeCaption");
        _fadeModeOption = GetNodeOrNull<OptionButton>("%FadeModeOption");
        _noFadableLabel = GetNodeOrNull<Label>("%NoFadableLabel");
        _noFadableSpacer = GetNodeOrNull<Control>("%NoFadableSpacer");
        _audioFadeCaption = GetNodeOrNull<Label>("%AudioFadeCaption");
        _audioFadeRow = GetNodeOrNull<Control>("%AudioFadeRow");
        _audioFadeEnable = GetNodeOrNull<CheckBox>("%AudioFadeEnable");
        _audioFadeLineEdit = GetNodeOrNull<LineEdit>("%AudioFadeLineEdit");
        _opacityFadeCaption = GetNodeOrNull<Label>("%OpacityFadeCaption");
        _opacityFadeRow = GetNodeOrNull<Control>("%OpacityFadeRow");
        _opacityFadeEnable = GetNodeOrNull<CheckBox>("%OpacityFadeEnable");
        _opacityFadeLineEdit = GetNodeOrNull<LineEdit>("%OpacityFadeLineEdit");

        if (_fadeModeOption != null)
        {
            _fadeModeOption.Clear();
            _fadeModeOption.AddItem("Absolute", (int)ControlFadeMode.Absolute);
            _fadeModeOption.AddItem("Relative", (int)ControlFadeMode.Relative);
            _fadeModeOption.ItemSelected += OnFadeModeSelected;
        }

        if (_audioFadeEnable != null)
            _audioFadeEnable.Toggled += OnAudioFadeEnableToggled;
        if (_opacityFadeEnable != null)
            _opacityFadeEnable.Toggled += OnOpacityFadeEnableToggled;

        if (_audioFadeLineEdit != null)
        {
            _audioFadeLineEdit.TextSubmitted += OnAudioFadeSubmitted;
            _audioFadeLineEdit.FocusExited += OnAudioFadeFocusExited;
            _audioFadeLineEdit.TextChanged += _ => _audioFadeEditing = true;
        }

        if (_opacityFadeLineEdit != null)
        {
            _opacityFadeLineEdit.TextSubmitted += OnOpacityFadeSubmitted;
            _opacityFadeLineEdit.FocusExited += OnOpacityFadeFocusExited;
            _opacityFadeLineEdit.TextChanged += _ => _opacityFadeEditing = true;
        }

        _seekTimeCaption = GetNodeOrNull<Label>("%SeekTimeCaption");
        _seekTimeLineEdit = GetNodeOrNull<LineEdit>("%SeekTimeLineEdit");
        if (_seekTimeLineEdit != null)
        {
            _seekTimeLineEdit.TextSubmitted += OnSeekTimeSubmitted;
            _seekTimeLineEdit.FocusExited += OnSeekTimeFocusExited;
            _seekTimeLineEdit.TextChanged += _ => _seekTimeEditing = true;
        }

        _layerCaption = GetNodeOrNull<Label>("%LayerCaption");
        _targetLayerOption = GetNodeOrNull<OptionButton>("%TargetLayerOption");
        if (_targetLayerOption != null)
            _targetLayerOption.ItemSelected += OnTargetLayerSelected;

        _sizeEnableCaption = GetNodeOrNull<Label>("%SizeEnableCaption");
        _sizeEnableRow = GetNodeOrNull<Control>("%SizeEnableRow");
        _sizeEnable = GetNodeOrNull<CheckBox>("%SizeEnable");
        _sizeXLineEdit = GetNodeOrNull<LineEdit>("%SizeXLineEdit");
        _sizeYLineEdit = GetNodeOrNull<LineEdit>("%SizeYLineEdit");
        if (_sizeEnable != null)
            _sizeEnable.Toggled += OnSizeEnableToggled;
        if (_sizeXLineEdit != null)
        {
            _sizeXLineEdit.TextSubmitted += _ => CommitSizeFields();
            _sizeXLineEdit.FocusExited += () => { if (_sizeEditing) CommitSizeFields(); };
            _sizeXLineEdit.TextChanged += _ => _sizeEditing = true;
        }
        if (_sizeYLineEdit != null)
        {
            _sizeYLineEdit.TextSubmitted += _ => CommitSizeFields();
            _sizeYLineEdit.FocusExited += () => { if (_sizeEditing) CommitSizeFields(); };
            _sizeYLineEdit.TextChanged += _ => _sizeEditing = true;
        }

        _posEnableCaption = GetNodeOrNull<Label>("%PosEnableCaption");
        _posEnableRow = GetNodeOrNull<Control>("%PosEnableRow");
        _posEnable = GetNodeOrNull<CheckBox>("%PosEnable");
        _posXLineEdit = GetNodeOrNull<LineEdit>("%PosXLineEdit");
        _posYLineEdit = GetNodeOrNull<LineEdit>("%PosYLineEdit");
        if (_posEnable != null)
            _posEnable.Toggled += OnPosEnableToggled;
        if (_posXLineEdit != null)
        {
            _posXLineEdit.TextSubmitted += _ => CommitPosFields();
            _posXLineEdit.FocusExited += () => { if (_posEditing) CommitPosFields(); };
            _posXLineEdit.TextChanged += _ => _posEditing = true;
        }
        if (_posYLineEdit != null)
        {
            _posYLineEdit.TextSubmitted += _ => CommitPosFields();
            _posYLineEdit.FocusExited += () => { if (_posEditing) CommitPosFields(); };
            _posYLineEdit.TextChanged += _ => _posEditing = true;
        }

        if (_moveUpButton != null)
        {
            _moveUpButton.Icon = GetThemeIcon("Up", "AtlasIcons");
            _moveUpButton.Pressed += () => _inspector?.MoveControlComponent(_component, -1);
        }

        if (_moveDownButton != null)
        {
            _moveDownButton.Icon = GetThemeIcon("Down", "AtlasIcons");
            _moveDownButton.Pressed += () => _inspector?.MoveControlComponent(_component, +1);
        }

        // Pointing / crosshair feel for the pick tool.
        // GuiInput + AcceptEvent (not ButtonDown): mouse-up is consumed globally in _Input while
        // picking, which would leave BaseButton latched and require a second click — same fix as
        // ShellBar reorder grabber (OnDragBarGuiInput / ReleaseDragGrabber).
        _pickTargetButton.Icon = GetThemeIcon("Right", "AtlasIcons");
        _pickTargetButton.TooltipText = "Hold and release over a cue in the list to set target";
        _pickTargetButton.KeepPressedOutside = false;
        _pickTargetButton.GuiInput += OnPickTargetButtonGuiInput;

        _deleteButton.Icon = GetThemeIcon("DeleteBin", "AtlasIcons");
        _deleteButton.Pressed += OnDeletePressed;

        _targetResetButton.Icon = GetThemeIcon("Refresh", "AtlasIcons");
        _targetResetButton.Pressed += OnTargetResetPressed;

        _fadeResetButton.Icon = GetThemeIcon("Refresh", "AtlasIcons");
        _fadeResetButton.Pressed += OnFadeResetPressed;

        _idLineEdit.TextSubmitted += OnIdSubmitted;
        _idLineEdit.FocusExited += OnIdFocusExited;
        _idLineEdit.TextChanged += _ => _idEditing = true;

        _numberLineEdit.TextSubmitted += OnNumberSubmitted;
        _numberLineEdit.FocusExited += OnNumberFocusExited;
        _numberLineEdit.TextChanged += _ => _numberEditing = true;

        _fadeLineEdit.TextSubmitted += OnFadeSubmitted;
        _fadeLineEdit.FocusExited += OnFadeFocusExited;
        _fadeLineEdit.TextChanged += _ => _fadeEditing = true;
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        EndPickMode(applyTarget: false);
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (!_pickActive)
            return;

        // Escape cancels without changing target.
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            EndPickMode(applyTarget: false);
            GetViewport()?.SetInputAsHandled();
            return;
        }

        // Release left button: assign shell under cursor (if any).
        if (@event is InputEventMouseButton mb &&
            mb.ButtonIndex == MouseButton.Left &&
            !mb.Pressed)
        {
            EndPickMode(applyTarget: true);
            GetViewport()?.SetInputAsHandled();
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (!_pickActive)
            return;

        UpdatePickCursorAndBadge();
    }

    /// <summary>
    /// Binds this card to a component and parent inspector.
    /// </summary>
    /// <param name="component">Control component model.</param>
    /// <param name="inspector">Owning inspector (for remove / reorder).</param>
    /// <param name="orderIndex">0-based execution order among control components.</param>
    /// <param name="orderCount">Total control components on the cue.</param>
    public void SetComponent(
        ControlComponent component,
        ControlInspector inspector,
        int orderIndex = 0,
        int orderCount = 1)
    {
        _component = component;
        _inspector = inspector;
        _orderIndex = orderIndex;
        _orderCount = Math.Max(1, orderCount);
        RefreshFromComponent();
    }

    /// <summary>
    /// Pushes model values into the UI fields.
    /// </summary>
    public void RefreshFromComponent()
    {
        if (_component == null) return;

        _isSyncingUi = true;
        try
        {
            if (_orderLabel != null)
                _orderLabel.Text = $"#{_orderIndex + 1}";

            if (_moveUpButton != null)
                _moveUpButton.Disabled = _orderIndex <= 0;
            if (_moveDownButton != null)
                _moveDownButton.Disabled = _orderIndex >= _orderCount - 1;

            _actionLabel.Text = ControlComponent.GetActionDisplayName(_component.Action).ToUpperInvariant();

            bool isTranslate = _component.Action == ControlAction.TranslateLayer;
            bool showCueTarget = !isTranslate;

            // Cue-target fields (hidden for Translate Layer).
            SetCueTargetFieldsVisible(showCueTarget);
            if (showCueTarget)
            {
                _component.ResolveTargetIfNeeded();

                _idLineEdit.Text = _component.TargetCueId >= 0
                    ? _component.TargetCueId.ToString()
                    : string.Empty;
                _numberLineEdit.Text = _component.TargetCueNum ?? string.Empty;

                string name = _component.GetTargetCueName();
                _nameLabel.Text = string.IsNullOrEmpty(name) ? "(none)" : name;
            }

            if (_pickTargetButton != null)
                _pickTargetButton.Visible = showCueTarget;

            bool showGoStopFade = _component.Action is ControlAction.Stop or ControlAction.Go;
            bool isPropertyFade = _component.Action == ControlAction.Fade;
            bool isSeek = _component.Action == ControlAction.Seek;
            // Translate uses the same Time: row as property fade duration.
            bool showTimeFade = showGoStopFade || isPropertyFade || isTranslate;

            if (_fadeCaption != null)
            {
                _fadeCaption.Visible = showTimeFade;
                _fadeCaption.Text = _component.Action switch
                {
                    ControlAction.Go => "Fade In:",
                    ControlAction.Stop => "Fade Out:",
                    ControlAction.Fade => "Time:",
                    ControlAction.TranslateLayer => "Time:",
                    _ => "Fade:"
                };
            }
            if (_fadeRow != null)
                _fadeRow.Visible = showTimeFade;

            if (_component.Action == ControlAction.Stop)
            {
                float sessionDefault = _globalData?.Settings?.StopFadeDuration ?? 0f;
                double displayFade = _component.ResolveStopFadeDuration(sessionDefault);
                _fadeLineEdit.Text = UiUtilities.FormatTime(displayFade);
                _fadeLineEdit.PlaceholderText = $"session ({UiUtilities.FormatTime(sessionDefault)})";
                _fadeLineEdit.TooltipText =
                    "Stop fade-out time. 0 = immediate. Default follows session Stop Fade.";
            }
            else if (_component.Action == ControlAction.Go)
            {
                _fadeLineEdit.Text = UiUtilities.FormatTime(_component.GoFadeInDuration);
                _fadeLineEdit.PlaceholderText = "0 (no fade-in)";
                _fadeLineEdit.TooltipText =
                    "GO fade-in time for the target cue. 0 = no control fade-in (default).";
            }
            else if (isPropertyFade)
            {
                _fadeLineEdit.Text = UiUtilities.FormatTime(_component.PropertyFadeDuration);
                _fadeLineEdit.PlaceholderText = "1.000";
                _fadeLineEdit.TooltipText =
                    "Property fade duration. 0 = snap immediately. Default 1s.";
            }
            else if (isTranslate)
            {
                _fadeLineEdit.Text = UiUtilities.FormatTime(_component.TranslateDuration);
                _fadeLineEdit.PlaceholderText = "0 (instant)";
                _fadeLineEdit.TooltipText =
                    "Layer translate duration. 0 = instant change (default).";
            }

            RefreshPropertyFadeUi(isPropertyFade);
            RefreshSeekUi(isSeek);
            RefreshTranslateLayerUi(isTranslate);

            UpdateResetButtons();
        }
        finally
        {
            _isSyncingUi = false;
            _idEditing = false;
            _numberEditing = false;
            _fadeEditing = false;
            _audioFadeEditing = false;
            _opacityFadeEditing = false;
            _seekTimeEditing = false;
            _sizeEditing = false;
            _posEditing = false;
        }
    }

    private void SetCueTargetFieldsVisible(bool visible)
    {
        // ID / No / Name captions are not unique-named; hide via line edits' parents where needed.
        if (_idLineEdit != null)
        {
            _idLineEdit.Visible = visible;
            if (_idLineEdit.GetParent() is Control idRow)
                idRow.Visible = visible;
        }
        if (_numberLineEdit != null)
            _numberLineEdit.Visible = visible;
        if (_nameLabel != null)
            _nameLabel.Visible = visible;

        // Caption labels are previous siblings in the 2-col grid — hide by walking grid children is fragile;
        // leave captions if fields empty is messy. Hide the whole first columns by finding grid.
        var grid = GetNodeOrNull<GridContainer>("MarginContainer/VBoxContainer/TargetGrid");
        if (grid == null) return;
        foreach (var child in grid.GetChildren())
        {
            if (child is not Control c) continue;
            // Hide static captions for cue targeting when not needed.
            string n = c.Name;
            if (n == "IdCaption" || n == "NumberCaption" || n == "NameCaption")
                c.Visible = visible;
        }
    }

    /// <summary>
    /// Shows Absolute/Relative mode and audio/opacity rows for Fade controls (contextual to target).
    /// </summary>
    private void RefreshPropertyFadeUi(bool isPropertyFade)
    {
        // Mode option is shared with Seek / Translate Layer — only configure Fade-specific rows here.
        if (!isPropertyFade)
        {
            bool keepMode = _component?.Action is ControlAction.Seek or ControlAction.TranslateLayer;
            if (!keepMode)
            {
                if (_modeCaption != null) _modeCaption.Visible = false;
                if (_fadeModeOption != null) _fadeModeOption.Visible = false;
            }

            if (_audioFadeCaption != null) _audioFadeCaption.Visible = false;
            if (_audioFadeRow != null) _audioFadeRow.Visible = false;
            if (_opacityFadeCaption != null) _opacityFadeCaption.Visible = false;
            if (_opacityFadeRow != null) _opacityFadeRow.Visible = false;
            if (_component?.Action is not (ControlAction.Seek or ControlAction.TranslateLayer))
            {
                if (_noFadableLabel != null) _noFadableLabel.Visible = false;
                if (_noFadableSpacer != null) _noFadableSpacer.Visible = false;
            }
            return;
        }

        if (_modeCaption != null)
            _modeCaption.Visible = true;
        if (_fadeModeOption != null)
        {
            _fadeModeOption.Visible = true;
            _fadeModeOption.Select((int)_component.FadeMode);
        }

        // Volume / opacity rows only appear once a real target is set and has that media.
        // Volume: dedicated AudioComponent and/or video with embedded audio (HasAudio + UseAudio).
        bool targetHasAudio = false;
        bool targetHasOpacity = false;
        if (_component.TargetCueId >= 0)
        {
            var target = CueList.FetchCueFromId(_component.TargetCueId);
            if (target != null)
            {
                targetHasAudio = ControlComponent.CueHasFadableAudio(target);
                targetHasOpacity = ControlComponent.CueHasFadableOpacity(target);
            }
        }

        bool showAudio = targetHasAudio;
        bool showOpacity = targetHasOpacity;
        bool showNoFadable = _component.TargetCueId >= 0 && !targetHasAudio && !targetHasOpacity;

        if (_noFadableLabel != null)
        {
            _noFadableLabel.Visible = showNoFadable;
            if (showNoFadable)
                _noFadableLabel.Text = "No valid fadable properties";
        }
        if (_noFadableSpacer != null)
            _noFadableSpacer.Visible = showNoFadable;

        if (_audioFadeCaption != null)
            _audioFadeCaption.Visible = showAudio;
        if (_audioFadeRow != null)
            _audioFadeRow.Visible = showAudio;
        if (_opacityFadeCaption != null)
            _opacityFadeCaption.Visible = showOpacity;
        if (_opacityFadeRow != null)
            _opacityFadeRow.Visible = showOpacity;

        if (_audioFadeEnable != null)
            _audioFadeEnable.SetPressedNoSignal(_component.FadeAudioVolumeEnabled);
        if (_opacityFadeEnable != null)
            _opacityFadeEnable.SetPressedNoSignal(_component.FadeVideoOpacityEnabled);

        bool relative = _component.FadeMode == ControlFadeMode.Relative;
        if (_audioFadeLineEdit != null)
        {
            string dbText = $"{_component.FadeAudioDb:0.#}dB";
            if (relative && _component.FadeAudioDb > 0)
                dbText = $"+{_component.FadeAudioDb:0.#}dB";
            _audioFadeLineEdit.Text = dbText;
            _audioFadeLineEdit.PlaceholderText = relative ? "±dB" : "0dB";
            _audioFadeLineEdit.TooltipText = relative
                ? "Relative change in dB (clamped to −60…0)."
                : "Absolute target level in dB (−60…0).";
            _audioFadeLineEdit.Editable = _component.FadeAudioVolumeEnabled;
        }

        if (_opacityFadeLineEdit != null)
        {
            string pctText = relative && _component.FadeOpacityPercent > 0
                ? $"+{_component.FadeOpacityPercent:0.#}%"
                : $"{_component.FadeOpacityPercent:0.#}%";
            _opacityFadeLineEdit.Text = pctText;
            _opacityFadeLineEdit.PlaceholderText = relative ? "±%" : "100%";
            _opacityFadeLineEdit.TooltipText = relative
                ? "Relative change in opacity % (clamped to 0…100)."
                : "Absolute opacity % (0…100).";
            _opacityFadeLineEdit.Editable = _component.FadeVideoOpacityEnabled;
        }
    }

    /// <summary>
    /// Shows Absolute/Relative mode and seek time for Seek controls (contextual to target).
    /// </summary>
    private void RefreshSeekUi(bool isSeek)
    {
        if (_seekTimeCaption != null)
            _seekTimeCaption.Visible = isSeek;
        if (_seekTimeLineEdit != null)
            _seekTimeLineEdit.Visible = isSeek;

        if (!isSeek)
            return;

        if (_modeCaption != null)
            _modeCaption.Visible = true;
        if (_fadeModeOption != null)
        {
            _fadeModeOption.Visible = true;
            _fadeModeOption.Select((int)_component.SeekMode);
        }

        bool hasSeekable = false;
        if (_component.TargetCueId >= 0)
        {
            var target = CueList.FetchCueFromId(_component.TargetCueId);
            hasSeekable = ControlComponent.CueHasSeekableMedia(target);
        }

        bool showNoSeekable = _component.TargetCueId >= 0 && !hasSeekable;
        if (_noFadableLabel != null)
        {
            // Reuse message label for seek-with-no-media (hidden when Fade also active — mutually exclusive).
            _noFadableLabel.Visible = showNoSeekable;
            if (showNoSeekable)
                _noFadableLabel.Text = "No seekable media on target";
        }
        if (_noFadableSpacer != null)
            _noFadableSpacer.Visible = showNoSeekable;

        bool relative = _component.SeekMode == ControlFadeMode.Relative;
        if (_seekTimeLineEdit != null)
        {
            string formatted = UiUtilities.FormatTime(Math.Abs(_component.SeekTimeSeconds));
            if (relative && _component.SeekTimeSeconds < 0)
                _seekTimeLineEdit.Text = $"-{formatted}";
            else if (relative && _component.SeekTimeSeconds > 0)
                _seekTimeLineEdit.Text = $"+{formatted}";
            else
                _seekTimeLineEdit.Text = formatted;

            _seekTimeLineEdit.PlaceholderText = relative ? "±0:00.000" : "0:00.000";
            _seekTimeLineEdit.TooltipText = relative
                ? "Relative seek offset from current playhead (±)."
                : "Absolute media time to seek to.";
            _seekTimeLineEdit.Visible = _component.TargetCueId < 0 || hasSeekable;
        }
        if (_seekTimeCaption != null)
            _seekTimeCaption.Visible = _component.TargetCueId < 0 || hasSeekable;
    }

    private void UpdateResetButtons()
    {
        if (_component == null) return;

        if (_targetResetButton != null)
        {
            bool showTargetReset = _component.Action != ControlAction.TranslateLayer &&
                                   !_component.IsTargetAtDefault;
            _targetResetButton.Visible = showTargetReset;
            if (showTargetReset)
                _targetResetButton.TooltipText = "Clear target";
        }

        if (_fadeResetButton != null)
        {
            bool showFadeReset = !_component.IsFadeAtDefault;
            _fadeResetButton.Visible = showFadeReset;
            if (showFadeReset)
            {
                if (_component.Action == ControlAction.Stop)
                {
                    float sessionDefault = _globalData?.Settings?.StopFadeDuration ?? 0f;
                    _fadeResetButton.TooltipText =
                        $"Reset fade to session Stop Fade ({UiUtilities.FormatTime(sessionDefault)})";
                }
                else if (_component.Action == ControlAction.Go)
                {
                    _fadeResetButton.TooltipText = "Reset fade-in to 0";
                }
                else if (_component.Action == ControlAction.Fade)
                {
                    _fadeResetButton.TooltipText = "Reset fade time to 1s";
                }
                else if (_component.Action == ControlAction.TranslateLayer)
                {
                    _fadeResetButton.TooltipText = "Reset translate time to 0 (instant)";
                }
            }
        }
    }

    private void OnTargetResetPressed()
    {
        if (_component == null || _component.IsTargetAtDefault) return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;

        RecordHistory("Reset control target");
        _component.ClearTarget();
        RefreshFromComponent();
    }

    private void OnFadeResetPressed()
    {
        if (_component == null || _component.IsFadeAtDefault) return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;

        if (_component.Action == ControlAction.Stop)
        {
            RecordHistory("Reset control stop fade");
            _component.ResetStopFadeToSessionDefault();
        }
        else if (_component.Action == ControlAction.Go)
        {
            RecordHistory("Reset control go fade-in");
            _component.ResetGoFadeInToDefault();
        }
        else if (_component.Action == ControlAction.Fade)
        {
            RecordHistory("Reset control property fade time");
            _component.ResetPropertyFadeDurationToDefault();
        }
        else if (_component.Action == ControlAction.TranslateLayer)
        {
            RecordHistory("Reset control translate duration");
            _component.ResetTranslateDurationToDefault();
        }
        else
        {
            return;
        }

        RefreshFromComponent();
    }

    private void OnFadeSubmitted(string text)
    {
        CommitFade(text);
        _fadeLineEdit.ReleaseFocus();
    }

    private void OnFadeFocusExited()
    {
        if (_fadeEditing)
            CommitFade(_fadeLineEdit.Text);
    }

    private void CommitFade(string text)
    {
        if (_isSyncingUi || _component == null) return;
        if (_component.Action is not (ControlAction.Stop or ControlAction.Go or ControlAction.Fade
                or ControlAction.TranslateLayer))
            return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;

        if (_component.Action == ControlAction.Go)
        {
            CommitGoFade(text);
            return;
        }

        if (_component.Action == ControlAction.Fade)
        {
            CommitPropertyFadeDuration(text);
            return;
        }

        if (_component.Action == ControlAction.TranslateLayer)
        {
            CommitTranslateDuration(text);
            return;
        }

        CommitStopFade(text);
    }

    private void CommitTranslateDuration(string text)
    {
        text = (text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
        {
            if (_component.IsTranslateDurationAtDefault)
            {
                _fadeEditing = false;
                RefreshFromComponent();
                return;
            }

            RecordHistory("Edit control translate duration");
            _component.ResetTranslateDurationToDefault();
            _fadeEditing = false;
            RefreshFromComponent();
            return;
        }

        string formatted = UiUtilities.ParseAndFormatTime(text, out double seconds, out bool isValid);
        if (!isValid || seconds < 0)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "Control card: invalid translate duration", (int)LogType.Warning);
            RefreshFromComponent();
            return;
        }

        if (Math.Abs(_component.TranslateDuration - seconds) < 1e-9)
        {
            _fadeEditing = false;
            _fadeLineEdit.Text = formatted;
            UpdateResetButtons();
            return;
        }

        RecordHistory("Edit control translate duration");
        _component.TranslateDuration = seconds;
        _fadeEditing = false;
        RefreshFromComponent();
    }

    private void CommitPropertyFadeDuration(string text)
    {
        text = (text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
        {
            if (_component.IsPropertyFadeDurationAtDefault)
            {
                _fadeEditing = false;
                RefreshFromComponent();
                return;
            }

            RecordHistory("Edit control property fade time");
            _component.ResetPropertyFadeDurationToDefault();
            _fadeEditing = false;
            RefreshFromComponent();
            return;
        }

        string formatted = UiUtilities.ParseAndFormatTime(text, out double seconds, out bool isValid);
        if (!isValid || seconds < 0)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "Control card: invalid property fade time", (int)LogType.Warning);
            RefreshFromComponent();
            return;
        }

        if (Math.Abs(_component.PropertyFadeDuration - seconds) < 1e-9)
        {
            _fadeEditing = false;
            _fadeLineEdit.Text = formatted;
            UpdateResetButtons();
            return;
        }

        RecordHistory("Edit control property fade time");
        _component.PropertyFadeDuration = seconds;
        _fadeEditing = false;
        RefreshFromComponent();
    }

    private void OnFadeModeSelected(long index)
    {
        if (_isSyncingUi || _component == null) return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;

        var mode = (ControlFadeMode)(int)index;

        if (_component.Action == ControlAction.Fade)
        {
            if (_component.FadeMode == mode) return;

            RecordHistory("Edit control fade mode");
            _component.FadeMode = mode;
            // Relative default delta 0 is clearer when switching modes.
            if (mode == ControlFadeMode.Relative)
            {
                _component.FadeAudioDb = 0f;
                _component.FadeOpacityPercent = 0f;
            }
            else
            {
                _component.FadeAudioDb = 0f;
                _component.FadeOpacityPercent = 100f;
            }

            RefreshFromComponent();
            return;
        }

        if (_component.Action == ControlAction.Seek)
        {
            if (_component.SeekMode == mode) return;

            RecordHistory("Edit control seek mode");
            _component.SeekMode = mode;
            _component.SeekTimeSeconds = 0;
            RefreshFromComponent();
            return;
        }

        if (_component.Action == ControlAction.TranslateLayer)
        {
            if (_component.TranslateMode == mode) return;

            RecordHistory("Edit control translate mode");
            _component.TranslateMode = mode;
            // Relative defaults to zero deltas; absolute seeds from current layer geometry.
            if (mode == ControlFadeMode.Relative)
            {
                _component.TranslateSizeX = 0;
                _component.TranslateSizeY = 0;
                _component.TranslatePosX = 0;
                _component.TranslatePosY = 0;
            }
            else
            {
                SeedTranslateFromLayer();
            }

            RefreshFromComponent();
        }
    }

    /// <summary>
    /// Absolute/Relative + size/position fields for Translate Layer.
    /// </summary>
    private void RefreshTranslateLayerUi(bool isTranslate)
    {
        if (_layerCaption != null)
            _layerCaption.Visible = isTranslate;
        if (_targetLayerOption != null)
            _targetLayerOption.Visible = isTranslate;

        if (_sizeEnableCaption != null)
            _sizeEnableCaption.Visible = isTranslate;
        if (_sizeEnableRow != null)
            _sizeEnableRow.Visible = isTranslate;
        if (_posEnableCaption != null)
            _posEnableCaption.Visible = isTranslate;
        if (_posEnableRow != null)
            _posEnableRow.Visible = isTranslate;

        if (!isTranslate)
            return;

        if (_modeCaption != null)
            _modeCaption.Visible = true;
        if (_fadeModeOption != null)
        {
            _fadeModeOption.Visible = true;
            _fadeModeOption.Select((int)_component.TranslateMode);
        }

        PopulateTargetLayerOptions();

        if (_sizeEnable != null)
            _sizeEnable.SetPressedNoSignal(_component.TranslateSizeEnabled);
        if (_posEnable != null)
            _posEnable.SetPressedNoSignal(_component.TranslatePositionEnabled);

        bool relative = _component.TranslateMode == ControlFadeMode.Relative;
        if (_sizeXLineEdit != null)
        {
            _sizeXLineEdit.Text = FormatSignedInt(_component.TranslateSizeX, relative);
            _sizeXLineEdit.Editable = _component.TranslateSizeEnabled;
            _sizeXLineEdit.PlaceholderText = relative ? "±W" : "W";
        }
        if (_sizeYLineEdit != null)
        {
            _sizeYLineEdit.Text = FormatSignedInt(_component.TranslateSizeY, relative);
            _sizeYLineEdit.Editable = _component.TranslateSizeEnabled;
            _sizeYLineEdit.PlaceholderText = relative ? "±H" : "H";
        }
        if (_posXLineEdit != null)
        {
            _posXLineEdit.Text = FormatSignedInt(_component.TranslatePosX, relative);
            _posXLineEdit.Editable = _component.TranslatePositionEnabled;
            _posXLineEdit.PlaceholderText = relative ? "±X" : "X";
        }
        if (_posYLineEdit != null)
        {
            _posYLineEdit.Text = FormatSignedInt(_component.TranslatePosY, relative);
            _posYLineEdit.Editable = _component.TranslatePositionEnabled;
            _posYLineEdit.PlaceholderText = relative ? "±Y" : "Y";
        }
    }

    private static string FormatSignedInt(int value, bool relative)
    {
        if (relative && value > 0)
            return $"+{value}";
        return value.ToString();
    }

    private void PopulateTargetLayerOptions()
    {
        if (_targetLayerOption == null || _component == null) return;

        _targetLayerOption.Clear();
        _targetLayerOption.AddItem("(select layer)", 0);
        _targetLayerOption.SetItemMetadata(0, -1);

        int selectedIdx = 0;
        int index = 1;
        foreach (var layer in DisplaysManager.Layers.ToList())
        {
            if (layer == null) continue;
            string label = string.IsNullOrWhiteSpace(layer.LayerName)
                ? $"Layer {layer.LayerId}"
                : layer.LayerName;
            _targetLayerOption.AddItem(label, index);
            _targetLayerOption.SetItemMetadata(index, layer.LayerId);
            if (layer.LayerId == _component.TargetLayerId)
                selectedIdx = index;
            index++;
        }

        _targetLayerOption.Select(selectedIdx);
    }

    private void OnTargetLayerSelected(long index)
    {
        if (_isSyncingUi || _component == null) return;
        if (_component.Action != ControlAction.TranslateLayer) return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;
        if (_targetLayerOption == null) return;

        int layerId = _targetLayerOption.GetItemMetadata((int)index).AsInt32();
        if (layerId == _component.TargetLayerId) return;

        RecordHistory("Edit control target layer");
        _component.TargetLayerId = layerId;

        // Seed absolute values from the selected layer's current geometry.
        if (_component.TranslateMode == ControlFadeMode.Absolute)
            SeedTranslateFromLayer();

        RefreshFromComponent();
    }

    private void SeedTranslateFromLayer()
    {
        if (_component == null || _component.TargetLayerId < 0) return;
        var layer = DisplaysManager.GetLayerById(_component.TargetLayerId);
        if (layer == null) return;

        _component.TranslateSizeX = layer.Size.X;
        _component.TranslateSizeY = layer.Size.Y;
        _component.TranslatePosX = layer.CanvasPosition.X;
        _component.TranslatePosY = layer.CanvasPosition.Y;
    }

    private void OnSizeEnableToggled(bool pressed)
    {
        if (_isSyncingUi || _component == null) return;
        if (_component.Action != ControlAction.TranslateLayer) return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;
        if (_component.TranslateSizeEnabled == pressed) return;

        RecordHistory("Edit control translate size enable");
        _component.TranslateSizeEnabled = pressed;
        RefreshFromComponent();
    }

    private void OnPosEnableToggled(bool pressed)
    {
        if (_isSyncingUi || _component == null) return;
        if (_component.Action != ControlAction.TranslateLayer) return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;
        if (_component.TranslatePositionEnabled == pressed) return;

        RecordHistory("Edit control translate position enable");
        _component.TranslatePositionEnabled = pressed;
        RefreshFromComponent();
    }

    private void CommitSizeFields()
    {
        if (_isSyncingUi || _component == null) return;
        if (_component.Action != ControlAction.TranslateLayer) return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;

        if (!TryParseSignedInt(_sizeXLineEdit?.Text, out int sx) ||
            !TryParseSignedInt(_sizeYLineEdit?.Text, out int sy))
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "Control card: invalid size values", (int)LogType.Warning);
            RefreshFromComponent();
            return;
        }

        if (_component.TranslateMode == ControlFadeMode.Absolute)
        {
            sx = Math.Max(1, sx);
            sy = Math.Max(1, sy);
        }

        if (sx == _component.TranslateSizeX && sy == _component.TranslateSizeY)
        {
            _sizeEditing = false;
            RefreshFromComponent();
            return;
        }

        RecordHistory("Edit control translate size");
        _component.TranslateSizeX = sx;
        _component.TranslateSizeY = sy;
        _sizeEditing = false;
        RefreshFromComponent();
    }

    private void CommitPosFields()
    {
        if (_isSyncingUi || _component == null) return;
        if (_component.Action != ControlAction.TranslateLayer) return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;

        if (!TryParseSignedInt(_posXLineEdit?.Text, out int px) ||
            !TryParseSignedInt(_posYLineEdit?.Text, out int py))
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "Control card: invalid position values", (int)LogType.Warning);
            RefreshFromComponent();
            return;
        }

        if (px == _component.TranslatePosX && py == _component.TranslatePosY)
        {
            _posEditing = false;
            RefreshFromComponent();
            return;
        }

        RecordHistory("Edit control translate position");
        _component.TranslatePosX = px;
        _component.TranslatePosY = py;
        _posEditing = false;
        RefreshFromComponent();
    }

    private static bool TryParseSignedInt(string text, out int value)
    {
        value = 0;
        text = (text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
            return true; // treat blank as 0
        if (text.StartsWith('+'))
            text = text[1..];
        return int.TryParse(text, out value);
    }

    private void OnSeekTimeSubmitted(string text)
    {
        CommitSeekTime(text);
        _seekTimeLineEdit?.ReleaseFocus();
    }

    private void OnSeekTimeFocusExited()
    {
        if (_seekTimeEditing)
            CommitSeekTime(_seekTimeLineEdit?.Text ?? string.Empty);
    }

    private void CommitSeekTime(string text)
    {
        if (_isSyncingUi || _component == null) return;
        if (_component.Action != ControlAction.Seek) return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;

        text = (text ?? string.Empty).Trim();
        bool negative = text.StartsWith('-');
        if (text.StartsWith('+') || text.StartsWith('-'))
            text = text[1..].Trim();

        if (string.IsNullOrEmpty(text))
        {
            if (Math.Abs(_component.SeekTimeSeconds) < 1e-9)
            {
                _seekTimeEditing = false;
                RefreshFromComponent();
                return;
            }

            RecordHistory("Edit control seek time");
            _component.SeekTimeSeconds = 0;
            _seekTimeEditing = false;
            RefreshFromComponent();
            return;
        }

        string formatted = UiUtilities.ParseAndFormatTime(text, out double seconds, out bool isValid);
        if (!isValid || seconds < 0)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "Control card: invalid seek time", (int)LogType.Warning);
            RefreshFromComponent();
            return;
        }

        if (negative)
            seconds = -seconds;

        // Absolute seeks must be non-negative.
        if (_component.SeekMode == ControlFadeMode.Absolute && seconds < 0)
            seconds = 0;

        if (Math.Abs(_component.SeekTimeSeconds - seconds) < 1e-9)
        {
            _seekTimeEditing = false;
            RefreshFromComponent();
            return;
        }

        RecordHistory("Edit control seek time");
        _component.SeekTimeSeconds = seconds;
        _seekTimeEditing = false;
        RefreshFromComponent();
    }

    private void OnAudioFadeEnableToggled(bool pressed)
    {
        if (_isSyncingUi || _component == null) return;
        if (_component.Action != ControlAction.Fade) return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;
        if (_component.FadeAudioVolumeEnabled == pressed) return;

        RecordHistory("Edit control fade audio enable");
        _component.FadeAudioVolumeEnabled = pressed;
        RefreshFromComponent();
    }

    private void OnOpacityFadeEnableToggled(bool pressed)
    {
        if (_isSyncingUi || _component == null) return;
        if (_component.Action != ControlAction.Fade) return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;
        if (_component.FadeVideoOpacityEnabled == pressed) return;

        RecordHistory("Edit control fade opacity enable");
        _component.FadeVideoOpacityEnabled = pressed;
        RefreshFromComponent();
    }

    private void OnAudioFadeSubmitted(string text)
    {
        CommitAudioFade(text);
        _audioFadeLineEdit?.ReleaseFocus();
    }

    private void OnAudioFadeFocusExited()
    {
        if (_audioFadeEditing)
            CommitAudioFade(_audioFadeLineEdit?.Text ?? string.Empty);
    }

    private void OnOpacityFadeSubmitted(string text)
    {
        CommitOpacityFade(text);
        _opacityFadeLineEdit?.ReleaseFocus();
    }

    private void OnOpacityFadeFocusExited()
    {
        if (_opacityFadeEditing)
            CommitOpacityFade(_opacityFadeLineEdit?.Text ?? string.Empty);
    }

    private void CommitAudioFade(string text)
    {
        if (_isSyncingUi || _component == null) return;
        if (_component.Action != ControlAction.Fade) return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;

        text = (text ?? string.Empty).Replace("dB", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (!float.TryParse(text, out float db))
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "Control card: invalid audio fade dB", (int)LogType.Warning);
            RefreshFromComponent();
            return;
        }

        if (_component.FadeMode == ControlFadeMode.Absolute)
            db = Mathf.Clamp(db, -60f, 0f);

        if (Math.Abs(_component.FadeAudioDb - db) < 1e-4f)
        {
            _audioFadeEditing = false;
            RefreshFromComponent();
            return;
        }

        RecordHistory("Edit control fade audio level");
        _component.FadeAudioDb = db;
        _audioFadeEditing = false;
        RefreshFromComponent();
    }

    private void CommitOpacityFade(string text)
    {
        if (_isSyncingUi || _component == null) return;
        if (_component.Action != ControlAction.Fade) return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;

        text = (text ?? string.Empty).Replace("%", "", StringComparison.Ordinal).Trim();
        if (!float.TryParse(text, out float pct))
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "Control card: invalid opacity fade %", (int)LogType.Warning);
            RefreshFromComponent();
            return;
        }

        if (_component.FadeMode == ControlFadeMode.Absolute)
            pct = Mathf.Clamp(pct, 0f, 100f);

        if (Math.Abs(_component.FadeOpacityPercent - pct) < 1e-4f)
        {
            _opacityFadeEditing = false;
            RefreshFromComponent();
            return;
        }

        RecordHistory("Edit control fade opacity");
        _component.FadeOpacityPercent = pct;
        _opacityFadeEditing = false;
        RefreshFromComponent();
    }

    private void CommitGoFade(string text)
    {
        text = (text ?? string.Empty).Trim();

        // Blank → default 0.
        if (string.IsNullOrEmpty(text))
        {
            if (_component.IsGoFadeAtDefault)
            {
                _fadeEditing = false;
                RefreshFromComponent();
                return;
            }

            RecordHistory("Edit control go fade-in");
            _component.ResetGoFadeInToDefault();
            _fadeEditing = false;
            RefreshFromComponent();
            return;
        }

        string formatted = UiUtilities.ParseAndFormatTime(text, out double seconds, out bool isValid);
        if (!isValid || seconds < 0)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "Control card: invalid go fade-in time", (int)LogType.Warning);
            RefreshFromComponent();
            return;
        }

        if (Math.Abs(_component.GoFadeInDuration - seconds) < 1e-9)
        {
            _fadeEditing = false;
            _fadeLineEdit.Text = formatted;
            UpdateResetButtons();
            return;
        }

        RecordHistory("Edit control go fade-in");
        _component.GoFadeInDuration = seconds;
        _fadeEditing = false;
        RefreshFromComponent();
    }

    private void CommitStopFade(string text)
    {
        text = (text ?? string.Empty).Trim();
        float sessionDefault = _globalData?.Settings?.StopFadeDuration ?? 0f;

        // Blank → back to session default.
        if (string.IsNullOrEmpty(text))
        {
            if (_component.IsStopFadeAtDefault)
            {
                _fadeEditing = false;
                RefreshFromComponent();
                return;
            }

            RecordHistory("Edit control stop fade");
            _component.ResetStopFadeToSessionDefault();
            _fadeEditing = false;
            RefreshFromComponent();
            return;
        }

        string formatted = UiUtilities.ParseAndFormatTime(text, out double seconds, out bool isValid);
        if (!isValid || seconds < 0)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "Control card: invalid stop fade time", (int)LogType.Warning);
            RefreshFromComponent();
            return;
        }

        // Match session default → stay on / return to session-default mode.
        if (Math.Abs(seconds - sessionDefault) < 1e-6)
        {
            if (_component.IsStopFadeAtDefault)
            {
                _fadeEditing = false;
                RefreshFromComponent();
                return;
            }

            RecordHistory("Edit control stop fade");
            _component.ResetStopFadeToSessionDefault();
            _fadeEditing = false;
            RefreshFromComponent();
            return;
        }

        if (!_component.StopFadeUsesSessionDefault &&
            Math.Abs(_component.StopFadeDuration - seconds) < 1e-9)
        {
            _fadeEditing = false;
            _fadeLineEdit.Text = formatted;
            return;
        }

        RecordHistory("Edit control stop fade");
        _component.StopFadeUsesSessionDefault = false;
        _component.StopFadeDuration = seconds;
        _fadeEditing = false;
        RefreshFromComponent();
    }

    private void OnDeletePressed()
    {
        EndPickMode(applyTarget: false);
        _inspector?.RemoveComponent(_component);
        QueueFree();
    }

    private void OnIdSubmitted(string text)
    {
        CommitId(text);
        _idLineEdit.ReleaseFocus();
    }

    private void OnIdFocusExited()
    {
        if (_idEditing)
            CommitId(_idLineEdit.Text);
    }

    private void OnNumberSubmitted(string text)
    {
        CommitNumber(text);
        _numberLineEdit.ReleaseFocus();
    }

    private void OnNumberFocusExited()
    {
        if (_numberEditing)
            CommitNumber(_numberLineEdit.Text);
    }

    private void CommitId(string text)
    {
        if (_isSyncingUi || _component == null) return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;

        text = (text ?? string.Empty).Trim();
        int newId = -1;
        if (!string.IsNullOrEmpty(text))
        {
            if (!int.TryParse(text, out newId) || newId < 0)
            {
                _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                    "Control card: invalid cue id", (int)LogType.Warning);
                RefreshFromComponent();
                return;
            }
        }

        if (newId == _component.TargetCueId &&
            (newId >= 0 || string.IsNullOrEmpty(_component.TargetCueNum)))
        {
            _idEditing = false;
            return;
        }

        // Self-target blocked for transport controls; Fade may target self (level changes).
        if (newId >= 0 && IsSelfTarget(newId) && _component.Action != ControlAction.Fade)
        {
            RejectSelfTarget();
            _idEditing = false;
            RefreshFromComponent();
            return;
        }

        RecordHistory("Edit control target id");

        if (newId < 0)
        {
            _component.TargetCueId = -1;
            _component.TargetCueNum = string.Empty;
        }
        else
        {
            var cue = CueList.FetchCueFromId(newId);
            if (cue == null)
            {
                _component.TargetCueId = newId;
                // Keep number only if it still matches; otherwise clear for honesty.
                if (!string.IsNullOrEmpty(_component.TargetCueNum))
                {
                    var byNum = CueList.FetchCueFromCueNum(_component.TargetCueNum);
                    if (byNum == null || byNum.Id != newId)
                        _component.TargetCueNum = string.Empty;
                }
                _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                    $"Control card: no cue with id {newId}", (int)LogType.Warning);
            }
            else
            {
                _component.TargetCueId = cue.Id;
                _component.TargetCueNum = cue.CueNum ?? string.Empty;
                TryAutoRenameOwner();
            }
        }

        _idEditing = false;
        RefreshFromComponent();
        UpdateResetButtons();
    }

    private void CommitNumber(string text)
    {
        if (_isSyncingUi || _component == null) return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;

        text = (text ?? string.Empty).Trim();

        if (string.Equals(_component.TargetCueNum ?? string.Empty, text, StringComparison.Ordinal) &&
            (string.IsNullOrEmpty(text) || _component.TargetCueId >= 0))
        {
            // Still resolve in case id was stale.
            if (!string.IsNullOrEmpty(text))
            {
                var existing = CueList.FetchCueFromCueNum(text);
                if (existing != null && existing.Id == _component.TargetCueId)
                {
                    _numberEditing = false;
                    return;
                }
            }
            else if (_component.TargetCueId < 0)
            {
                _numberEditing = false;
                return;
            }
        }

        if (!string.IsNullOrEmpty(text))
        {
            var resolved = CueList.FetchCueFromCueNum(text);
            if (resolved != null && IsSelfTarget(resolved.Id) &&
                _component.Action != ControlAction.Fade)
            {
                RejectSelfTarget();
                _numberEditing = false;
                RefreshFromComponent();
                return;
            }
        }

        RecordHistory("Edit control target number");

        if (string.IsNullOrEmpty(text))
        {
            _component.TargetCueId = -1;
            _component.TargetCueNum = string.Empty;
        }
        else
        {
            var cue = CueList.FetchCueFromCueNum(text);
            if (cue == null)
            {
                _component.TargetCueNum = text;
                _component.TargetCueId = -1;
                _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                    $"Control card: no cue with number \"{text}\"", (int)LogType.Warning);
            }
            else
            {
                _component.TargetCueId = cue.Id;
                _component.TargetCueNum = cue.CueNum ?? text;
                TryAutoRenameOwner();
            }
        }

        _numberEditing = false;
        RefreshFromComponent();
        UpdateResetButtons();
    }

    // =========================
    // Pick-target interaction
    // =========================

    /// <summary>
    /// Starts pick on left-press. Accepts the event so BaseButton does not latch pressed;
    /// mouse-up is owned by <see cref="_Input"/> during pick mode.
    /// </summary>
    private void OnPickTargetButtonGuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb)
            return;
        if (mb.ButtonIndex != MouseButton.Left || !mb.Pressed)
            return;

        // Prevent BaseButton internal press-attempt (would stick if mouse-up is handled globally).
        AcceptEvent();
        ReleasePickButton();

        if (_component == null) return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;
        if (_pickActive) return;

        StartPickMode();
    }

    /// <summary>
    /// Clears residual pressed/focus state on the pick button after a pick session
    /// (mirrors <see cref="ShellBar.ReleaseDragGrabber"/>).
    /// </summary>
    private void ReleasePickButton()
    {
        if (_pickTargetButton == null || !IsInstanceValid(_pickTargetButton))
            return;

        _pickTargetButton.KeepPressedOutside = false;
        _pickTargetButton.SetPressedNoSignal(false);
        _pickTargetButton.ButtonPressed = false;
        if (_pickTargetButton.HasFocus())
            _pickTargetButton.ReleaseFocus();
    }

    private void StartPickMode()
    {
        if (_pickActive) return;

        _pickActive = true;
        SetProcessInput(true);
        SetProcess(true);

        EnsurePickBadge();
        if (_pickLayer != null)
            _pickLayer.Visible = true;

        // Crosshair / target cursor for the hold duration.
        try
        {
            DisplayServer.CursorSetShape(DisplayServer.CursorShape.Cross);
        }
        catch (Exception ex)
        {
            GD.Print($"ControlComponentCard:StartPickMode - Cursor shape: {ex.Message}");
        }

        UpdatePickCursorAndBadge();
        _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
            "Control: pick a cue — release over a shell (Esc to cancel)", (int)LogType.Info);
    }

    /// <summary>
    /// Ends pick mode. When <paramref name="applyTarget"/> is true, assigns the shell under the mouse.
    /// </summary>
    private void EndPickMode(bool applyTarget)
    {
        if (!_pickActive && _pickLayer == null && _pickHoverShell == null)
            return;

        ShellBar targetShell = null;
        if (applyTarget && _pickActive)
            targetShell = FindShellBarUnderMouse();

        ClearPickHoverHighlight();

        _pickActive = false;
        SetProcessInput(false);
        SetProcess(false);

        // Restore default cursor.
        try
        {
            DisplayServer.CursorSetShape(DisplayServer.CursorShape.Arrow);
        }
        catch
        {
            // Best-effort; some platforms ignore cursor shape.
        }

        // Critical: clear latched BaseButton state (mouse-up never reached the button).
        ReleasePickButton();

        if (_pickLayer != null && IsInstanceValid(_pickLayer))
        {
            _pickLayer.QueueFree();
            _pickLayer = null;
            _pickBadge = null;
            _pickBadgeLabel = null;
            _pickBadgeIcon = null;
        }

        if (applyTarget && targetShell != null)
            ApplyTargetFromShell(targetShell);
    }

    private void ApplyTargetFromShell(ShellBar shell)
    {
        if (shell == null || _component == null) return;

        int cueId = shell.CueId;
        if (IsSelfTarget(cueId) && _component.Action != ControlAction.Fade)
        {
            RejectSelfTarget();
            return;
        }

        var cue = CueList.FetchCueFromId(cueId);
        if (cue == null)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"Control pick: cue id {cueId} not found", (int)LogType.Warning);
            return;
        }

        if (cue.Id == _component.TargetCueId &&
            string.Equals(_component.TargetCueNum ?? string.Empty, cue.CueNum ?? string.Empty, StringComparison.Ordinal))
        {
            RefreshFromComponent();
            return;
        }

        RecordHistory("Pick control target");
        _component.TargetCueId = cue.Id;
        _component.TargetCueNum = cue.CueNum ?? string.Empty;
        TryAutoRenameOwner();
        RefreshFromComponent();

        _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
            $"Control pick: target → #{_component.TargetCueNum} \"{cue.Name}\"", (int)LogType.Info);
    }

    private void EnsurePickBadge()
    {
        if (_pickLayer != null && IsInstanceValid(_pickLayer))
            return;

        _pickLayer = new CanvasLayer
        {
            Name = "ControlPickTargetLayer",
            Layer = 100
        };

        _pickBadge = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Name = "PickBadge"
        };

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.08f, 0.1f, 0.92f),
            BorderColor = new Color(0.92f, 0.44f, 0.01f, 1f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomRight = 4,
            CornerRadiusBottomLeft = 4,
            ContentMarginLeft = 6,
            ContentMarginTop = 4,
            ContentMarginRight = 8,
            ContentMarginBottom = 4
        };
        _pickBadge.AddThemeStyleboxOverride("panel", style);

        var row = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            // Small gap between cursor icon and label.
        };
        row.AddThemeConstantOverride("separation", 6);

        _pickBadgeIcon = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(16, 16)
        };
        var icon = GetThemeIcon("Right", "AtlasIcons");
        if (icon != null)
            _pickBadgeIcon.Texture = icon;
        row.AddChild(_pickBadgeIcon);

        _pickBadgeLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Text = "Pick cue…"
        };
        _pickBadgeLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.95f, 1f));
        _pickBadgeLabel.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(_pickBadgeLabel);

        _pickBadge.AddChild(row);
        _pickLayer.AddChild(_pickBadge);
        GetTree().Root.AddChild(_pickLayer);
    }

    private void UpdatePickCursorAndBadge()
    {
        // Keep crosshair sticky while over other controls that set their own cursor.
        try
        {
            DisplayServer.CursorSetShape(DisplayServer.CursorShape.Cross);
        }
        catch
        {
            // ignore
        }

        var shell = FindShellBarUnderMouse();
        SetPickHoverHighlight(shell);

        if (_pickBadge == null || !IsInstanceValid(_pickBadge))
            return;

        Vector2 mouse = GetViewport().GetMousePosition();
        // Offset so the badge sits just below-right of the cursor tip.
        _pickBadge.Position = mouse + new Vector2(18, 18);

        if (shell != null)
        {
            if (IsSelfTarget(shell.CueId) && _component?.Action != ControlAction.Fade)
            {
                _pickBadgeLabel.Text = "Cannot target self";
            }
            else
            {
                var cue = CueList.FetchCueFromId(shell.CueId);
                string num = cue?.CueNum ?? shell.CueId.ToString();
                string name = cue?.Name ?? "(unknown)";
                string action = _component != null
                    ? ControlComponent.GetActionDisplayName(_component.Action)
                    : "Target";
                _pickBadgeLabel.Text = $"{action} → #{num}  {name}";
            }
        }
        else
        {
            _pickBadgeLabel.Text = "Pick cue…";
        }
    }

    private void SetPickHoverHighlight(ShellBar shell)
    {
        if (_pickHoverShell == shell)
            return;

        ClearPickHoverHighlight();

        if (shell == null || !IsInstanceValid(shell))
            return;

        _pickHoverShell = shell;
        _pickHoverRestoreModulate = shell.Modulate;
        // Warm tint so the drop target is obvious under the crosshair.
        shell.Modulate = new Color(1.15f, 1.05f, 0.85f, 1f);
    }

    private void ClearPickHoverHighlight()
    {
        if (_pickHoverShell != null && IsInstanceValid(_pickHoverShell))
            _pickHoverShell.Modulate = _pickHoverRestoreModulate;
        _pickHoverShell = null;
        _pickHoverRestoreModulate = Colors.White;
    }

    /// <summary>
    /// Walks from the control under the mouse up the parent chain to find a <see cref="ShellBar"/>.
    /// </summary>
    private ShellBar FindShellBarUnderMouse()
    {
        var vp = GetViewport();
        if (vp == null) return null;

        Control hovered = vp.GuiGetHoveredControl();
        while (hovered != null)
        {
            if (hovered is ShellBar shell)
                return shell;
            hovered = hovered.GetParent() as Control;
        }

        return null;
    }

    /// <summary>
    /// Id of the cue currently being edited (the control component's owner).
    /// </summary>
    private int GetOwnerCueId() => _globalData?.FocusedCue ?? -1;

    /// <summary>
    /// True when <paramref name="cueId"/> is the owning control cue (self-target).
    /// </summary>
    private bool IsSelfTarget(int cueId)
    {
        int ownerId = GetOwnerCueId();
        return ownerId >= 0 && cueId == ownerId;
    }

    /// <summary>
    /// Logs a user-facing warning that self-targeting is not allowed.
    /// </summary>
    private void RejectSelfTarget()
    {
        _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
            "Control: a cue cannot target itself", (int)LogType.Warning);
    }

    /// <summary>
    /// Renames the focused cue when it still has a placeholder name and the target resolved.
    /// History was already recorded for the target edit, so the rename is included in that step.
    /// </summary>
    private void TryAutoRenameOwner()
    {
        if (_component == null) return;

        int cueId = GetOwnerCueId();
        var owner = cueId >= 0 ? CueList.FetchCueFromId(cueId) : null;
        if (owner == null) return;

        if (_component.TryAutoRenameOwnerCue(owner))
        {
            // Keep shell inspector / any listeners that don't bind NameChanged in sync.
            _globalSignals?.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"Control: renamed cue to \"{owner.Name}\"", (int)LogType.Info);
        }
    }

    private void RecordHistory(string description)
    {
        int cueId = GetOwnerCueId();
        if (cueId < 0) return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;
        _globalData?.HistoryManager?.RecordCueChange(cueId, description);
    }
}
