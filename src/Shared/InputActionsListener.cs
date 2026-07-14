using Godot;

namespace Cue2.Shared;


public partial class InputActionsListener : Node
{
    private GlobalSignals _globalSignals;
    private Timer _focusExitTimer;

    private bool _listenForInput = true;

    // Data-driven mapping to avoid long chain of if statements.
    // Key = input action name, Value = (handler, useExactMatch)
    private readonly System.Collections.Generic.Dictionary<string, (System.Action handler, bool exact)> _actionMap = new();

    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

        _focusExitTimer = new Timer { WaitTime = 0.1, OneShot = true };
        AddChild(_focusExitTimer);
        _focusExitTimer.Timeout += OnFocusExitTimerTimeout;

        // Signals sent from all text edit feilds when focused. This is used to toggle input actions.
        _globalSignals.TextEditFocusEntered += SetListeningFalse;
        _globalSignals.TextEditFocusExited += OnTextEditFocusExited;

        RegisterActions();
    }

    private void RegisterActions()
    {
        Register("NewSession", nameof(GlobalSignals.NewSession), "New Session", true);
        Register("OpenSession", nameof(GlobalSignals.OpenSession), "Open Session", true);
        Register("SaveSession", nameof(GlobalSignals.Save), "Save", true);
        Register("SaveAsSession", nameof(GlobalSignals.SaveAs), "Save As", true);
        Register("Go", nameof(GlobalSignals.Go), "Go", false);
        Register("StopAll", nameof(GlobalSignals.StopAll), "Stop All", false);
        Register("CreateCue", nameof(GlobalSignals.CreateCue), "Create Cue", true);
        Register("DeleteCue", nameof(GlobalSignals.DeleteSelectedCues), "Delete Selected Cues", true);
        Register("DuplicateSelectedCues", nameof(GlobalSignals.DuplicateSelectedCues), "Duplicate Selected Cues", true);
        Register("GroupSelectedCues", nameof(GlobalSignals.GroupSelectedCues), "Group Selected Cues", true);
        Register("SelectNext", nameof(GlobalSignals.SelectNextCue), "Select Next Cue", true);
        Register("SelectPrevious", nameof(GlobalSignals.SelectPreviousCue), "Select Previous Cue", true);
        Register("PauseAll", nameof(GlobalSignals.PauseAll), "Pause All Cues", true);
        Register("ResumeAll", nameof(GlobalSignals.ResumeAll), "Resume All Cues", true);
        Register("ToggleSettings", nameof(GlobalSignals.ToggleSettingsWindow), "Toggle Settings Window", true);
        Register("ToggleLog", nameof(GlobalSignals.ToggleLogWindow), "Toggle Log Window", true);
        Register("ExpandOneLayer", nameof(GlobalSignals.CuelistExpandOneLayer), "Expand One Group Layer", true);
        Register("CollapseOneLayer", nameof(GlobalSignals.CuelistCollapseOneLayer), "Collapse One Group Layer", true);
        Register("ToggleExpandAll", nameof(GlobalSignals.ToggleExpandAll), "Toggle Expand/Collapse Groups", true);
    }

    private void Register(string action, string signalName, string logName, bool exact)
    {
        _actionMap[action] = (() =>
        {
            GD.Print($"InputActionsListener:Actions - Input Action: {logName}");
            _globalSignals.EmitSignal(signalName);
        }, exact);
    }

    public override void _Process(double delta)
    {
        if (!_listenForInput) return;

        if (Input.IsAnythingPressed())
        {
            Actions();
        }
    }

    private void Actions()
    {
        foreach (var kvp in _actionMap)
        {
            if (Input.IsActionJustPressed(kvp.Key, kvp.Value.exact))
            {
                kvp.Value.handler();
            }
        }
    }
    
    private void SetListening(bool listening) => _listenForInput = listening;

    private void OnTextEditFocusExited()
    {
        //GD.Print($"InputActionsListener:OnTextEditFocusExited - Starting timer to re-enable input listening");
        _focusExitTimer.Start();
    }

    private void OnFocusExitTimerTimeout()
    {
        //GD.Print($"InputActionsListener:OnFocusExitTimerTimeout - Re-enabling input listening");
        SetListening(true);
    }

    private void SetListeningFalse()
    {
        //GD.Print($"InputActionsListener:SetListeningFalse");
        _focusExitTimer.Stop(); // Stop the timer if focus entered again
        SetListening(false);
    }
}

