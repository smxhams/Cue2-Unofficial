//==================================================================================//
// OscInputActionCard.cs                                                            //
// This file is part of Cue2                                                        //
// http://cue2.live/                                                                //
//==================================================================================//

using System;
using Cue2.Base.Classes.Connections;
using Cue2.Shared;
using Godot;

namespace Cue2.UI.Scenes.Settings;

/// <summary>
/// Card for assigning an OSC address to a project InputMap action.
/// Capture arms <see cref="OscListen"/> for the next message (like MIDI Input Map).
/// </summary>
public partial class OscInputActionCard : PanelContainer
{
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;
    private OscListen _oscListen;

    [Export] public string Action { get; set; } = "";

    private Label _actionNameLabel;
    private Button _bindingButton;
    private Button _resetButton;
    private Button _clearButton;

    private bool _isCapturing;
    private bool _captureSubscribed;

    private StyleBoxFlat _normalStyle;
    private StyleBoxFlat _flashStyle;
    private Tween _flashTween;

    /// <summary>
    /// Raised when a binding is rejected because another action already uses the pattern.
    /// </summary>
    public event Action<string, string> BindingConflict;

    /// <summary>
    /// Raised when this card starts capture so the parent can cancel other cards.
    /// </summary>
    public event Action<OscInputActionCard> CaptureStarted;

    /// <summary>True while waiting for the next OSC message.</summary>
    public bool IsCapturing => _isCapturing;

    public override void _Ready()
    {
        _globalSignals = GetNodeOrNull<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNodeOrNull<GlobalData>("/root/GlobalData");
        _oscListen = GetNodeOrNull<OscListen>("/root/OscListen");

        _actionNameLabel = GetNodeOrNull<Label>("%ActionName");
        _bindingButton = GetNodeOrNull<Button>("%BindingButton");
        _resetButton = GetNodeOrNull<Button>("%ResetButton");
        _clearButton = GetNodeOrNull<Button>("%ClearButton");

        CaptureNormalStyle();

        if (_bindingButton != null)
            _bindingButton.Pressed += OnBindingButtonPressed;
        if (_resetButton != null)
        {
            _resetButton.Pressed += OnResetPressed;
            try
            {
                _resetButton.Icon = GetThemeIcon("Refresh", "AtlasIcons");
                _resetButton.ExpandIcon = true;
            }
            catch { /* optional */ }
        }
        if (_clearButton != null)
        {
            _clearButton.Pressed += OnClearPressed;
            try
            {
                _clearButton.Icon = GetThemeIcon("DeleteBin", "AtlasIcons");
                _clearButton.ExpandIcon = true;
            }
            catch { /* optional */ }
        }

        ApplyActionLabel();
        RefreshDisplay();
    }

    public override void _ExitTree()
    {
        CancelCapture(emitFocusExit: true);
        if (_flashTween != null && GodotObject.IsInstanceValid(_flashTween))
        {
            _flashTween.Kill();
            _flashTween = null;
        }
        base._ExitTree();
    }

    /// <summary>
    /// Sets the InputMap action this card represents.
    /// </summary>
    public void SetAction(string actionName)
    {
        Action = actionName;
        ApplyActionLabel();
        RefreshDisplay();
    }

    private void ApplyActionLabel()
    {
        if (_actionNameLabel == null) return;
        _actionNameLabel.Text = PrettifyActionName(Action);
    }

    /// <summary>
    /// Updates the binding button and reset visibility from <see cref="OscListen"/>.
    /// </summary>
    public void RefreshDisplay()
    {
        if (_bindingButton == null) return;
        if (_isCapturing) return;

        var binding = _oscListen?.GetInputMapBinding(Action) ?? OscActionBinding.Unbound();
        if (!binding.HasBinding)
            _bindingButton.Text = "None";
        else
            _bindingButton.Text = binding.GetDisplay();

        if (_resetButton != null)
        {
            _resetButton.Visible = binding.IsNonDefault;
            if (binding.IsNonDefault)
                _resetButton.TooltipText = "Reset to default (no OSC)";
        }
    }

    private void OnBindingButtonPressed()
    {
        if (_globalData?.HistoryManager?.IsRestoring == true) return;

        if (_isCapturing)
        {
            CancelCapture(emitFocusExit: true);
            return;
        }

        if (_oscListen == null)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "OSC Input Map: OscListen not found.", (int)LogType.Warning);
            return;
        }

        if (!_oscListen.OscListenEnabled || !_oscListen.IsListening)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "OSC Input Map: enable OSC Listener first (Settings → OSC Listener).",
                (int)LogType.Warning);
            return;
        }

        CaptureStarted?.Invoke(this);
        StartCapture();
    }

    private void StartCapture()
    {
        _isCapturing = true;
        if (_bindingButton != null)
            _bindingButton.Text = "OSC… (Esc)";

        if (!_captureSubscribed && _oscListen != null)
        {
            _oscListen.OscCaptured += OnOscCaptured;
            _captureSubscribed = true;
        }

        _oscListen?.StartCapture();
        _globalSignals?.EmitSignal(nameof(GlobalSignals.TextEditFocusEntered));
        GD.Print($"OscInputActionCard:StartCapture - '{Action}'");
    }

    /// <summary>
    /// Cancels capture on this card. Safe when not capturing.
    /// </summary>
    /// <param name="emitFocusExit">When false, another card is taking over capture immediately.</param>
    public void CancelCapture(bool emitFocusExit = true)
    {
        if (!_isCapturing && !_captureSubscribed) return;

        _isCapturing = false;
        if (_captureSubscribed && _oscListen != null)
        {
            _oscListen.OscCaptured -= OnOscCaptured;
            _captureSubscribed = false;
        }

        if (_oscListen != null && _oscListen.IsCapturing)
            _oscListen.CancelCapture();

        RefreshDisplay();
        if (emitFocusExit)
            _globalSignals?.EmitSignal(nameof(GlobalSignals.TextEditFocusExited));
        GD.Print($"OscInputActionCard:CancelCapture - '{Action}'");
    }

    private void OnOscCaptured(string address, string argsDisplay)
    {
        if (!_isCapturing) return;

        _isCapturing = false;
        if (_captureSubscribed && _oscListen != null)
        {
            _oscListen.OscCaptured -= OnOscCaptured;
            _captureSubscribed = false;
        }

        _globalSignals?.EmitSignal(nameof(GlobalSignals.TextEditFocusExited));

        // When the captured message has arguments, match them by default
        // (useful for /go 1 vs /go 2 style control). Address-only if no args.
        bool matchArgs = !string.IsNullOrEmpty(argsDisplay);

        var candidate = new OscActionBinding();
        candidate.SetFromMessage(address, argsDisplay, matchArgs);

        string conflict = _oscListen?.FindConflictingInputMapAction(Action, candidate);
        if (!string.IsNullOrEmpty(conflict))
        {
            string combo = candidate.GetDisplay();
            BindingConflict?.Invoke(conflict, combo);
            GD.Print($"OscInputActionCard:OnOscCaptured - Rejected '{combo}' for '{Action}'; used by '{conflict}'");
            RefreshDisplay();
            return;
        }

        RecordHistory("Set OSC Input Map binding");
        _oscListen?.SetInputMapBinding(Action, candidate);
        RefreshDisplay();
        GD.Print($"OscInputActionCard:OnOscCaptured - '{Action}' ← {candidate.GetDisplay()}");
    }

    private void OnClearPressed()
    {
        if (_globalData?.HistoryManager?.IsRestoring == true) return;
        if (_oscListen == null) return;

        var current = _oscListen.GetInputMapBinding(Action);
        if (!current.HasBinding) return;

        if (_isCapturing)
            CancelCapture(emitFocusExit: true);

        RecordHistory("Clear OSC Input Map binding");
        _oscListen.SetInputMapBinding(Action, OscActionBinding.Unbound());
        RefreshDisplay();
    }

    private void OnResetPressed()
    {
        OnClearPressed();
    }

    private void RecordHistory(string description)
    {
        var history = _globalData?.HistoryManager;
        if (history == null || history.IsRestoring) return;
        history.RecordSettingsChange(description, null, "OscInputMap");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_isCapturing) return;
        if (@event is not InputEventKey key || !key.Pressed) return;
        if (key.Keycode != Key.Escape) return;

        CancelCapture(emitFocusExit: true);
        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// Briefly highlights this card in red when another rebind collides with it.
    /// </summary>
    public void FlashConflict()
    {
        CaptureNormalStyle();
        EnsureFlashStyle();

        if (_flashTween != null && GodotObject.IsInstanceValid(_flashTween))
        {
            _flashTween.Kill();
            _flashTween = null;
        }

        _flashStyle.BorderColor = GlobalStyles.Danger;
        _flashStyle.BgColor = new Color(0.32f, 0.08f, 0.08f, 1f);
        AddThemeStyleboxOverride("panel", _flashStyle);

        _flashTween = CreateTween();
        _flashTween.SetParallel(true);
        _flashTween.TweenProperty(_flashStyle, "border_color", _normalStyle.BorderColor, 0.85f)
            .SetDelay(0.45f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        _flashTween.TweenProperty(_flashStyle, "bg_color", _normalStyle.BgColor, 0.85f)
            .SetDelay(0.45f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        _flashTween.SetParallel(false);
        _flashTween.TweenCallback(Callable.From(RestoreNormalStyle));
    }

    private void CaptureNormalStyle()
    {
        if (_normalStyle != null) return;
        var existing = GetThemeStylebox("panel");
        if (existing is StyleBoxFlat flat)
            _normalStyle = (StyleBoxFlat)flat.Duplicate();
        else
        {
            _normalStyle = new StyleBoxFlat
            {
                BgColor = new Color(0.12f, 0.12f, 0.12f, 1f),
                BorderColor = new Color(0.35f, 0.35f, 0.35f, 1f),
            };
            _normalStyle.SetBorderWidthAll(1);
            _normalStyle.SetCornerRadiusAll(4);
        }
    }

    private void EnsureFlashStyle()
    {
        if (_flashStyle != null) return;
        CaptureNormalStyle();
        _flashStyle = (StyleBoxFlat)_normalStyle.Duplicate();
        _flashStyle.SetBorderWidthAll(Math.Max(2, _normalStyle.GetBorderWidth(Side.Left)));
    }

    private void RestoreNormalStyle()
    {
        if (_normalStyle != null)
            AddThemeStyleboxOverride("panel", _normalStyle);
        else
            RemoveThemeStyleboxOverride("panel");
    }

    private static string PrettifyActionName(string action)
    {
        if (string.IsNullOrEmpty(action)) return "";
        string result = "";
        for (int i = 0; i < action.Length; i++)
        {
            char c = action[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(action[i - 1]) || char.IsDigit(action[i - 1])))
                result += " ";
            result += c;
        }
        return result;
    }
}
