using System;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Godot;

namespace Cue2.Shared;

/// <summary>
/// Listens for project InputMap actions, cue hotkey triggers, and wall-clock cue triggers.
/// Pauses app shortcuts while text fields (or rebind UIs) have focus.
/// </summary>
public partial class InputActionsListener : Node
{
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;
    private Timer _focusExitTimer;

    private bool _listenForInput = true;

    /// <summary>Previous local time sample for wall-clock edge detection (unset until first poll).</summary>
    private DateTime? _lastClockPollLocal;

    // Data-driven mapping to avoid long chain of if statements.
    // Key = input action name, Value = (handler, useExactMatch)
    private readonly System.Collections.Generic.Dictionary<string, (System.Action handler, bool exact)> _actionMap = new();

    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNodeOrNull<GlobalData>("/root/GlobalData");

        _focusExitTimer = new Timer { WaitTime = 0.1, OneShot = true };
        AddChild(_focusExitTimer);
        _focusExitTimer.Timeout += OnFocusExitTimerTimeout;

        // Signals sent from all text edit feilds when focused. This is used to toggle input actions.
        _globalSignals.TextEditFocusEntered += SetListeningFalse;
        _globalSignals.TextEditFocusExited += OnTextEditFocusExited;

        // Seed clock poll so we do not retroactively fire cues that already passed today.
        _lastClockPollLocal = DateTime.Now;

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
        // Undo/Redo are handled separately so they still work while a LineEdit/SpinBox has focus.
    }

    private void Register(string action, string signalName, string logName, bool exact)
    {
        _actionMap[action] = (() =>
        {
            GD.Print($"InputActionsListener:Actions - Input Action: {logName}");
            _globalSignals.EmitSignal(signalName);
        }, exact);
    }

    /// <summary>
    /// Invokes the same handler as the keyboard InputMap for <paramref name="actionName"/>
    /// (used by MIDI Input Map bindings). Undo/Redo always fire even when typing-focused.
    /// Other actions respect the text-field listen gate.
    /// </summary>
    /// <param name="actionName">Project InputMap action name (e.g. "Go").</param>
    /// <returns><c>true</c> when a handler was invoked.</returns>
    public bool TryTriggerAction(string actionName)
    {
        if (string.IsNullOrEmpty(actionName)) return false;

        // Undo/Redo always available (same as keyboard path).
        if (actionName == "Undo")
        {
            GD.Print("InputActionsListener:TryTriggerAction - MIDI Action: Undo");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Undo));
            return true;
        }
        if (actionName == "Redo")
        {
            GD.Print("InputActionsListener:TryTriggerAction - MIDI Action: Redo");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Redo));
            return true;
        }

        if (!_listenForInput) return false;

        if (_actionMap.TryGetValue(actionName, out var entry))
        {
            entry.handler();
            return true;
        }

        return false;
    }

    public override void _Process(double delta)
    {
        // Wall-clock triggers always run (independent of text-field focus / hotkeys).
        PollClockTriggers();

        if (!Input.IsAnythingPressed()) return;

        // Always process document undo/redo — do not block when a text field has focus
        // (SpinBox / LineEdit focus previously left listening disabled and "broke" undo).
        if (Input.IsActionJustPressed("Undo", true))
        {
            GD.Print("InputActionsListener:Actions - Input Action: Undo");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Undo));
            return;
        }
        if (Input.IsActionJustPressed("Redo", true))
        {
            GD.Print("InputActionsListener:Actions - Input Action: Redo");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Redo));
            return;
        }

        if (!_listenForInput) return;

        Actions();
    }

    /// <summary>
    /// Fires enabled wall-clock cue triggers when local time crosses their target time of day.
    /// Each cue GO's at most once per crossing (typically once per day). Does not move playhead.
    /// </summary>
    private void PollClockTriggers()
    {
        var now = DateTime.Now;
        if (_lastClockPollLocal is not DateTime previous)
        {
            _lastClockPollLocal = now;
            return;
        }

        // Avoid zero-length or reverse samples (clock adjustments / same tick).
        if (now <= previous)
        {
            if (now < previous)
                _lastClockPollLocal = now; // clock went backwards — resync without firing
            return;
        }

        try
        {
            if (CueList.CueIndex == null || CueList.CueIndex.Count == 0)
                return;

            var executor = _globalData?.CueCommandExectutor;
            if (executor == null)
                return;

            foreach (Cue cue in CueList.CueIndex.Values)
            {
                if (cue == null || !cue.CanFireClock) continue;
                if (!cue.ClockCrossedBetween(previous, now)) continue;

                string display = cue.GetClockDisplay();
                GD.Print($"InputActionsListener:PollClockTriggers - Clock GO: \"{cue.Name}\" (id={cue.Id}) at {display}");
                _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                    $"Clock trigger: \"{cue.Name}\" at {display}", (int)LogType.Info);
                executor.ActivateSequenceFrom(cue);
            }
        }
        finally
        {
            _lastClockPollLocal = now;
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

    /// <summary>
    /// Fires enabled cue hotkeys that match a newly pressed key combo.
    /// Multiple cues may share a hotkey; each armed+enabled match is GO'd without moving playhead.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_listenForInput) return;
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;
        if (IsModifierOnlyKey(keyEvent.Keycode)) return;

        if (CueList.CueIndex == null || CueList.CueIndex.Count == 0) return;

        var executor = _globalData?.CueCommandExectutor;
        if (executor == null) return;

        bool any = false;
        foreach (Cue cue in CueList.CueIndex.Values)
        {
            if (cue == null || !cue.CanFireHotkey) continue;
            if (!cue.HotkeyMatches(keyEvent)) continue;

            GD.Print($"InputActionsListener:_UnhandledInput - Cue hotkey GO: \"{cue.Name}\" (id={cue.Id})");
            executor.ActivateSequenceFrom(cue);
            any = true;
        }

        if (any)
            GetViewport().SetInputAsHandled();
    }

    private static bool IsModifierOnlyKey(Key keycode)
    {
        return keycode is Key.Ctrl or Key.Shift or Key.Alt or Key.Meta;
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

