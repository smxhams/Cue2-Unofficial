using System;
using Godot;
using Cue2.Shared;

namespace Cue2.UI.Scenes.Settings;

/// <summary>
/// A card displayed in the Input Map settings representing a single InputMap action.
/// Allows viewing the current binding and rebinding or clearing it.
/// </summary>
public partial class InputActionCard : PanelContainer
{
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;

    [Export] public string Action { get; set; } = "";

    private Label _actionNameLabel;
    private Button _bindingButton;
    private Button _resetButton;
    private Button _clearButton;

    private bool _isListeningForInput;

    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNode<GlobalData>("/root/GlobalData");

        _actionNameLabel = GetNode<Label>("%ActionName");
        _bindingButton = GetNode<Button>("%BindingButton");
        _resetButton = GetNode<Button>("%ResetButton");
        _clearButton = GetNode<Button>("%ClearButton");

        _bindingButton.Pressed += OnBindingButtonPressed;
        _resetButton.Pressed += OnResetButtonPressed;
        _clearButton.Pressed += OnClearButtonPressed;

        _clearButton.Icon = GetThemeIcon("DeleteBin", "AtlasIcons");
        _resetButton.Icon = GetThemeIcon("Refresh", "AtlasIcons");

        RefreshDisplay();
    }

    /// <summary>
    /// Sets the action this card represents and refreshes its display.
    /// </summary>
    /// <param name="actionName">The InputMap action name (e.g. "Go", "CreateCue").</param>
    public void SetAction(string actionName)
    {
        Action = actionName;
        if (_actionNameLabel != null)
        {
            _actionNameLabel.Text = PrettifyActionName(actionName);
        }
        RefreshDisplay();
    }

    private static string PrettifyActionName(string action)
    {
        if (string.IsNullOrEmpty(action)) return "";
        // Insert spaces before capitals and treat common patterns
        string result = "";
        for (int i = 0; i < action.Length; i++)
        {
            char c = action[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(action[i-1]) || char.IsDigit(action[i-1])))
            {
                result += " ";
            }
            result += c;
        }
        return result;
    }

    /// <summary>
    /// Updates the binding button text from the current state of InputMap for this action.
    /// Also updates visibility and tooltip of the reset-to-default button.
    /// </summary>
    public void RefreshDisplay()
    {
        if (_bindingButton == null) return;
        if (string.IsNullOrEmpty(Action) || !InputMap.HasAction(Action))
        {
            _bindingButton.Text = "Unbound";
            UpdateResetButton();
            return;
        }

        var events = InputMap.ActionGetEvents(Action);
        if (events.Count == 0)
        {
            _bindingButton.Text = "Unbound";
            UpdateResetButton();
            return;
        }

        // Build a compact representation. For v1 show up to first two bindings.
        var parts = new System.Collections.Generic.List<string>();
        int shown = 0;
        foreach (InputEvent ev in events)
        {
            if (shown >= 2) break;
            string s = GlobalData.FormatInputEvent(ev);
            if (!string.IsNullOrEmpty(s))
            {
                parts.Add(s);
                shown++;
            }
        }
        _bindingButton.Text = string.Join(" / ", parts);
        if (events.Count > 2)
        {
            _bindingButton.Text += " …";
        }

        UpdateResetButton();
    }

    private void UpdateResetButton()
    {
        if (_resetButton == null) return;

        bool atDefault = _globalData != null && _globalData.IsInputActionAtDefault(Action);
        _resetButton.Visible = !atDefault;

        if (!atDefault)
        {
            string defaultText = _globalData?.GetDefaultBindingDisplay(Action) ?? "default";
            _resetButton.TooltipText = $"Reset to default: {defaultText}";
        }
    }

    private void OnBindingButtonPressed()
    {
        if (_isListeningForInput)
        {
            CancelListening();
            return;
        }

        StartListening();
    }

    private void OnClearButtonPressed()
    {
        if (string.IsNullOrEmpty(Action) || !InputMap.HasAction(Action)) return;

        InputMap.ActionEraseEvents(Action);
        GD.Print($"InputActionCard:OnClearButtonPressed - Cleared events for action '{Action}'");
        RefreshDisplay();
    }

    private void OnResetButtonPressed()
    {
        if (string.IsNullOrEmpty(Action) || _globalData == null) return;

        _globalData.ResetInputActionToDefault(Action);
        GD.Print($"InputActionCard:OnResetButtonPressed - Reset '{Action}' to default via refresh button.");
        RefreshDisplay();
    }

    private void StartListening()
    {
        _isListeningForInput = true;
        _bindingButton.Text = "Press key... (Esc cancels)";
        // Pause global input action listener (reuses existing focus signals as a coordination mechanism)
        _globalSignals.EmitSignal(nameof(GlobalSignals.TextEditFocusEntered));
        GD.Print($"InputActionCard:StartListening - Listening for new binding for '{Action}'");
    }

    private void CancelListening()
    {
        _isListeningForInput = false;
        RefreshDisplay();
        _globalSignals.EmitSignal(nameof(GlobalSignals.TextEditFocusExited));
        GD.Print($"InputActionCard:CancelListening - Cancelled listening for '{Action}'");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_isListeningForInput) return;
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed) return;

        if (keyEvent.Keycode == Key.Escape)
        {
            CancelListening();
            GetViewport().SetInputAsHandled();
            return;
        }

        // Apply the captured key as the (sole) binding for this action.
        ApplyNewBinding(keyEvent);
        _isListeningForInput = false;
        RefreshDisplay();
        GetViewport().SetInputAsHandled();
        _globalSignals.EmitSignal(nameof(GlobalSignals.TextEditFocusExited));
        GD.Print($"InputActionCard:_UnhandledInput - Set binding for '{Action}' to {GlobalData.FormatInputEvent(keyEvent)}");
    }

    private void ApplyNewBinding(InputEventKey source)
    {
        if (string.IsNullOrEmpty(Action) || !InputMap.HasAction(Action)) return;

        // For v1: replace all existing bindings with this single key event.
        InputMap.ActionEraseEvents(Action);

        var newEvent = new InputEventKey
        {
            Keycode = source.Keycode,
            PhysicalKeycode = source.PhysicalKeycode,
            CtrlPressed = source.CtrlPressed,
            ShiftPressed = source.ShiftPressed,
            AltPressed = source.AltPressed,
            MetaPressed = source.MetaPressed,
            // Echo etc left default
        };

        InputMap.ActionAddEvent(Action, newEvent);
    }

    public override void _ExitTree()
    {
        if (_isListeningForInput)
        {
            // Ensure we re-enable global listener if card is removed while listening
            _globalSignals.EmitSignal(nameof(GlobalSignals.TextEditFocusExited));
        }
    }
}
