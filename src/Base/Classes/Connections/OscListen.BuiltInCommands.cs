//==================================================================================//
// OscListen.BuiltInCommands.cs                                                     //
// This file is part of Cue2                                                        //
// http://cue2.live/                                                                //
//==================================================================================//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Godot;

namespace Cue2.Base.Classes.Connections;

/// <summary>
/// Documentation entry for a fixed built-in OSC command (settings UI).
/// </summary>
public readonly struct OscBuiltInCommandInfo
{
    /// <summary>Category heading (e.g. Playback, Cue control).</summary>
    public string Category { get; init; }

    /// <summary>Path pattern shown in the UI (e.g. <c>/GoID/###</c>).</summary>
    public string Pattern { get; init; }

    /// <summary>Short tooltip / description of what the command does.</summary>
    public string Description { get; init; }
}

/// <summary>
/// Built-in OSC path handlers for show control (playback, cue targeting, selection, arming).
/// Paths are fixed and not user-editable — see <see cref="BuiltInCommandCatalog"/>.
/// </summary>
public partial class OscListen
{
    /// <summary>
    /// Full catalog of fixed built-in OSC commands for documentation / settings UI.
    /// </summary>
    public static readonly OscBuiltInCommandInfo[] BuiltInCommandCatalog =
    {
        // ── Global playback ─────────────────────────────────────────────────
        new()
        {
            Category = "Playback",
            Pattern = "/Go",
            Description = "GO the currently selected (standby) cue(s). Same as the main GO button. Disarmed selection is skipped and playhead advances."
        },
        new()
        {
            Category = "Playback",
            Pattern = "/StopAll",
            Description = "Stop all playing cues using the session stop-fade. Optional path /StopAll/{seconds} overrides fade duration (0 = immediate)."
        },
        new()
        {
            Category = "Playback",
            Pattern = "/PauseAll",
            Description = "Pause all playing cues."
        },
        new()
        {
            Category = "Playback",
            Pattern = "/ResumeAll",
            Description = "Resume all paused cues."
        },
        new()
        {
            Category = "Playback",
            Pattern = "/Panic",
            Description = "MIDI panic: send All Notes Off / All Sound Off on every open MIDI output (does not stop cues)."
        },

        // ── Playhead / selection ────────────────────────────────────────────
        new()
        {
            Category = "Selection",
            Pattern = "/SelectNext",
            Description = "Move selection to the next cue in the cuelist."
        },
        new()
        {
            Category = "Selection",
            Pattern = "/SelectPrevious",
            Description = "Move selection to the previous cue in the cuelist."
        },
        new()
        {
            Category = "Selection",
            Pattern = "/SelectID/###",
            Description = "Select the cue with this internal id (does not GO)."
        },
        new()
        {
            Category = "Selection",
            Pattern = "/SelectNum/###",
            Description = "Select the first cue whose cue number matches (does not GO)."
        },
        new()
        {
            Category = "Selection",
            Pattern = "/SelectName/###",
            Description = "Select the first cue whose name matches exactly (does not GO)."
        },

        // ── GO by target ────────────────────────────────────────────────────
        new()
        {
            Category = "Cue GO",
            Pattern = "/GoID/###",
            Description = "GO the cue with this internal id if armed. Does not move playhead selection."
        },
        new()
        {
            Category = "Cue GO",
            Pattern = "/GoNum/###",
            Description = "GO every armed cue with this cue number (e.g. /GoNum/1.2)."
        },
        new()
        {
            Category = "Cue GO",
            Pattern = "/GoName/###",
            Description = "GO every armed cue with this exact name. Prefer ID/Num when names contain spaces."
        },

        // ── Stop / Pause / Resume / StartNow by target ──────────────────────
        new()
        {
            Category = "Cue control",
            Pattern = "/StopID/###[/{fade}]",
            Description = "Stop playing instance(s) of cue id. Optional fade seconds after id (0 = cut). Default = session stop-fade."
        },
        new()
        {
            Category = "Cue control",
            Pattern = "/StopNum/###[/{fade}]",
            Description = "Stop playing instance(s) of cue(s) with this number. Optional trailing fade seconds."
        },
        new()
        {
            Category = "Cue control",
            Pattern = "/StopName/###",
            Description = "Stop playing instance(s) of cue(s) with this exact name (session stop-fade)."
        },
        new()
        {
            Category = "Cue control",
            Pattern = "/PauseID/###",
            Description = "Pause playing instance(s) of cue id."
        },
        new()
        {
            Category = "Cue control",
            Pattern = "/PauseNum/###",
            Description = "Pause playing instance(s) of cue number."
        },
        new()
        {
            Category = "Cue control",
            Pattern = "/PauseName/###",
            Description = "Pause playing instance(s) of cue name."
        },
        new()
        {
            Category = "Cue control",
            Pattern = "/ResumeID/###",
            Description = "Resume paused instance(s) of cue id."
        },
        new()
        {
            Category = "Cue control",
            Pattern = "/ResumeNum/###",
            Description = "Resume paused instance(s) of cue number."
        },
        new()
        {
            Category = "Cue control",
            Pattern = "/ResumeName/###",
            Description = "Resume paused instance(s) of cue name."
        },
        new()
        {
            Category = "Cue control",
            Pattern = "/StartNowID/###",
            Description = "Skip pre-wait / continue lead-in and start content now for waiting instance(s) of cue id."
        },
        new()
        {
            Category = "Cue control",
            Pattern = "/StartNowNum/###",
            Description = "Start Now for waiting instance(s) of cue number."
        },
        new()
        {
            Category = "Cue control",
            Pattern = "/StartNowName/###",
            Description = "Start Now for waiting instance(s) of cue name."
        },

        // ── Seek ────────────────────────────────────────────────────────────
        new()
        {
            Category = "Seek",
            Pattern = "/SeekID/{id}/{sec}",
            Description = "Seek playing cue id to absolute body-timeline seconds (pre-wait + content)."
        },
        new()
        {
            Category = "Seek",
            Pattern = "/SeekRelID/{id}/{sec}",
            Description = "Seek playing cue id by a relative offset in seconds (negative allowed)."
        },
        new()
        {
            Category = "Seek",
            Pattern = "/SeekNum/{num}/{sec}",
            Description = "Absolute seek for playing cue(s) with this cue number."
        },
        new()
        {
            Category = "Seek",
            Pattern = "/SeekRelNum/{num}/{sec}",
            Description = "Relative seek for playing cue(s) with this cue number."
        },

        // ── Arm ─────────────────────────────────────────────────────────────
        new()
        {
            Category = "Arm",
            Pattern = "/ArmID/###",
            Description = "Arm the cue with this id (allows GO / triggers)."
        },
        new()
        {
            Category = "Arm",
            Pattern = "/DisarmID/###",
            Description = "Disarm the cue with this id (blocks GO / triggers)."
        },
        new()
        {
            Category = "Arm",
            Pattern = "/ArmNum/###",
            Description = "Arm every cue with this cue number."
        },
        new()
        {
            Category = "Arm",
            Pattern = "/DisarmNum/###",
            Description = "Disarm every cue with this cue number."
        },
        new()
        {
            Category = "Arm",
            Pattern = "/ArmName/###",
            Description = "Arm every cue with this exact name."
        },
        new()
        {
            Category = "Arm",
            Pattern = "/DisarmName/###",
            Description = "Disarm every cue with this exact name."
        },

        // ── Document (optional remote) ──────────────────────────────────────
        new()
        {
            Category = "Document",
            Pattern = "/Save",
            Description = "Save the current session (same as Save shortcut)."
        },
        new()
        {
            Category = "Document",
            Pattern = "/Undo",
            Description = "Undo the last document edit."
        },
        new()
        {
            Category = "Document",
            Pattern = "/Redo",
            Description = "Redo the last undone document edit."
        },
    };

    // Keep legacy constants so any external references still compile.
    /// <summary>Legacy pattern constant for Go by id.</summary>
    public const string CmdGoIdPattern = "/GoID/###";
    /// <summary>Legacy pattern constant for Go by number.</summary>
    public const string CmdGoNumPattern = "/GoNum/###";
    /// <summary>Legacy pattern constant for Go by name.</summary>
    public const string CmdGoNamePattern = "/GoName/###";

    /// <summary>
    /// Handles fixed show-control paths. Returns true when the address matched a built-in
    /// command (whether or not the action fully succeeded).
    /// </summary>
    private bool TryFireBuiltInCommands(OscInputMessage msg)
    {
        string address = msg.Address;
        if (string.IsNullOrEmpty(address) || address[0] != '/') return false;

        // Split: "/GoID/12/1.5" → ["GoID", "12", "1.5"]
        string[] parts = address.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        string cmd = parts[0];
        try
        {
            return cmd switch
            {
                // Global playback
                "Go" when parts.Length == 1 => ExecGlobalSignal(nameof(GlobalSignals.Go), "Go"),
                "StopAll" => ExecStopAll(parts, msg),
                "PauseAll" when parts.Length == 1 => ExecGlobalSignal(nameof(GlobalSignals.PauseAll), "PauseAll"),
                "ResumeAll" when parts.Length == 1 => ExecGlobalSignal(nameof(GlobalSignals.ResumeAll), "ResumeAll"),
                "Panic" when parts.Length == 1 => ExecMidiPanic(),

                // Selection (global)
                "SelectNext" when parts.Length == 1 => ExecGlobalSignal(nameof(GlobalSignals.SelectNextCue), "SelectNext"),
                "SelectPrevious" when parts.Length == 1 => ExecGlobalSignal(nameof(GlobalSignals.SelectPreviousCue), "SelectPrevious"),
                "SelectID" => ExecSelect(parts, CueLookup.Id),
                "SelectNum" => ExecSelect(parts, CueLookup.Num),
                "SelectName" => ExecSelect(parts, CueLookup.Name),

                // GO
                "GoID" => ExecGo(parts, CueLookup.Id),
                "GoNum" => ExecGo(parts, CueLookup.Num),
                "GoName" => ExecGo(parts, CueLookup.Name),

                // Stop / Pause / Resume / StartNow
                "StopID" => ExecControl(parts, msg, CueLookup.Id, ControlAction.Stop),
                "StopNum" => ExecControl(parts, msg, CueLookup.Num, ControlAction.Stop),
                "StopName" => ExecControl(parts, msg, CueLookup.Name, ControlAction.Stop),
                "PauseID" => ExecControl(parts, msg, CueLookup.Id, ControlAction.Pause),
                "PauseNum" => ExecControl(parts, msg, CueLookup.Num, ControlAction.Pause),
                "PauseName" => ExecControl(parts, msg, CueLookup.Name, ControlAction.Pause),
                "ResumeID" => ExecControl(parts, msg, CueLookup.Id, ControlAction.Resume),
                "ResumeNum" => ExecControl(parts, msg, CueLookup.Num, ControlAction.Resume),
                "ResumeName" => ExecControl(parts, msg, CueLookup.Name, ControlAction.Resume),
                "StartNowID" => ExecControl(parts, msg, CueLookup.Id, ControlAction.StartNow),
                "StartNowNum" => ExecControl(parts, msg, CueLookup.Num, ControlAction.StartNow),
                "StartNowName" => ExecControl(parts, msg, CueLookup.Name, ControlAction.StartNow),

                // Seek
                "SeekID" => ExecSeek(parts, msg, CueLookup.Id, relative: false),
                "SeekRelID" => ExecSeek(parts, msg, CueLookup.Id, relative: true),
                "SeekNum" => ExecSeek(parts, msg, CueLookup.Num, relative: false),
                "SeekRelNum" => ExecSeek(parts, msg, CueLookup.Num, relative: true),

                // Arm
                "ArmID" => ExecArm(parts, CueLookup.Id, armed: true),
                "DisarmID" => ExecArm(parts, CueLookup.Id, armed: false),
                "ArmNum" => ExecArm(parts, CueLookup.Num, armed: true),
                "DisarmNum" => ExecArm(parts, CueLookup.Num, armed: false),
                "ArmName" => ExecArm(parts, CueLookup.Name, armed: true),
                "DisarmName" => ExecArm(parts, CueLookup.Name, armed: false),

                // Document
                "Save" when parts.Length == 1 => ExecGlobalSignal(nameof(GlobalSignals.Save), "Save"),
                "Undo" when parts.Length == 1 => ExecGlobalSignal(nameof(GlobalSignals.Undo), "Undo"),
                "Redo" when parts.Length == 1 => ExecGlobalSignal(nameof(GlobalSignals.Redo), "Redo"),

                _ => false
            };
        }
        catch (Exception ex)
        {
            LogBuiltIn($"{cmd}: error — {ex.Message}", LogType.Error);
            return true;
        }
    }

    private enum CueLookup
    {
        Id,
        Num,
        Name
    }

    // ── Global helpers ──────────────────────────────────────────────────────

    private bool ExecGlobalSignal(string signalName, string label)
    {
        if (_globalSignals == null)
        {
            LogBuiltIn($"{label}: GlobalSignals missing", LogType.Error);
            return true;
        }

        _globalSignals.EmitSignal(signalName);
        LogBuiltIn($"{label}: fired", LogType.Info);
        return true;
    }

    private bool ExecStopAll(string[] parts, OscInputMessage msg)
    {
        double? fade = null;
        if (parts.Length >= 2 && TryParseDouble(parts[1], out double pathFade))
            fade = Math.Max(0, pathFade);
        else if (msg.FirstFloat.HasValue)
            fade = Math.Max(0, msg.FirstFloat.Value);

        // Session StopAll uses each ActiveCue's GlobalStopAll (session fade).
        // When an explicit fade is requested, stop each root active with override.
        if (!fade.HasValue)
            return ExecGlobalSignal(nameof(GlobalSignals.StopAll), "StopAll");

        var executor = _globalData?.CueCommandExectutor;
        if (executor == null)
        {
            LogBuiltIn("StopAll: executor missing", LogType.Error);
            return true;
        }

        int count = 0;
        foreach (var active in executor.ActiveCues.ToList())
        {
            if (active == null || !GodotObject.IsInstanceValid(active)) continue;
            active.StopAll(propagateToChildren: true, fadeDurationOverride: fade.Value);
            count++;
        }

        LogBuiltIn($"StopAll: stopped {count} root active cue(s) with fade={fade.Value:0.###}s", LogType.Info);
        return true;
    }

    private bool ExecMidiPanic()
    {
        var midi = GetNodeOrNull<MidiManager>("/root/MidiManager");
        if (midi == null)
        {
            LogBuiltIn("Panic: MidiManager missing", LogType.Warning);
            return true;
        }

        midi.PanicAllOutputs();
        LogBuiltIn("Panic: MIDI All Notes/Sound Off", LogType.Info);
        return true;
    }

    // ── Cue resolve ─────────────────────────────────────────────────────────

    private List<Cue> ResolveCues(string[] parts, CueLookup lookup, int tokenStartIndex = 1)
    {
        var result = new List<Cue>();
        if (parts.Length <= tokenStartIndex)
            return result;

        string token = lookup == CueLookup.Name
            ? string.Join("/", parts.Skip(tokenStartIndex))
            : parts[tokenStartIndex];

        if (string.IsNullOrWhiteSpace(token))
            return result;

        if (CueList.CueIndex == null || CueList.CueIndex.Count == 0)
            return result;

        switch (lookup)
        {
            case CueLookup.Id:
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                    && CueList.CueIndex.TryGetValue(id, out Cue byId) && byId != null)
                {
                    result.Add(byId);
                }
                break;

            case CueLookup.Num:
            {
                string num = token.Trim();
                foreach (Cue cue in CueList.CueIndex.Values)
                {
                    if (cue == null) continue;
                    if (string.Equals((cue.CueNum ?? string.Empty).Trim(), num, StringComparison.Ordinal))
                        result.Add(cue);
                }
                break;
            }

            case CueLookup.Name:
            {
                string name = token.Trim();
                foreach (Cue cue in CueList.CueIndex.Values)
                {
                    if (cue == null) continue;
                    if (string.Equals((cue.Name ?? string.Empty).Trim(), name, StringComparison.Ordinal))
                        result.Add(cue);
                }
                break;
            }
        }

        return result;
    }

    private static string LookupLabel(CueLookup lookup) => lookup switch
    {
        CueLookup.Id => "ID",
        CueLookup.Num => "Num",
        CueLookup.Name => "Name",
        _ => "?"
    };

    // ── GO ──────────────────────────────────────────────────────────────────

    private bool ExecGo(string[] parts, CueLookup lookup)
    {
        var cues = ResolveCues(parts, lookup);
        if (cues.Count == 0)
        {
            LogBuiltIn($"Go{LookupLabel(lookup)}: no matching cue", LogType.Warning);
            return true;
        }

        foreach (var cue in cues)
            TryActivateCue(cue, $"Go{LookupLabel(lookup)}");
        return true;
    }

    private void TryActivateCue(Cue cue, string via)
    {
        if (cue == null) return;

        if (!cue.Armed)
        {
            LogBuiltIn($"{via}: skipped disarmed \"{cue.Name}\" (id={cue.Id})", LogType.Info);
            return;
        }

        var executor = _globalData?.CueCommandExectutor;
        if (executor == null)
        {
            LogBuiltIn($"{via}: executor missing", LogType.Error);
            return;
        }

        GD.Print($"OscListen:TryActivateCue - OSC {via}: \"{cue.Name}\" (id={cue.Id} num={cue.CueNum})");
        _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
            $"OSC {via}: GO \"{cue.Name}\" (#{cue.CueNum}, id={cue.Id})", (int)LogType.Info);
        executor.ActivateSequenceFrom(cue);
    }

    // ── Control actions (Stop/Pause/Resume/StartNow) ────────────────────────

    private bool ExecControl(string[] parts, OscInputMessage msg, CueLookup lookup, ControlAction action)
    {
        var cues = ResolveCues(parts, lookup);
        if (cues.Count == 0)
        {
            LogBuiltIn($"{action}{LookupLabel(lookup)}: no matching cue", LogType.Warning);
            return true;
        }

        var executor = _globalData?.CueCommandExectutor;
        if (executor == null)
        {
            LogBuiltIn($"{action}: executor missing", LogType.Error);
            return true;
        }

        double? stopFade = null;
        if (action == ControlAction.Stop)
        {
            // Optional path segment after the cue token: /StopID/12/1.5
            // For Id/Num the fade is parts[2]; for Name we only use FirstFloat (name may have slashes).
            if (lookup != CueLookup.Name && parts.Length >= 3 && TryParseDouble(parts[2], out double pathFade))
                stopFade = Math.Max(0, pathFade);
            else if (msg.FirstFloat.HasValue)
                stopFade = Math.Max(0, msg.FirstFloat.Value);
        }

        foreach (var cue in cues)
        {
            executor.ApplyControlAction(action, cue.Id, stopFadeDuration: stopFade);
            string fadeNote = stopFade.HasValue ? $" fade={stopFade.Value:0.###}s" : string.Empty;
            LogBuiltIn($"{action}{LookupLabel(lookup)}: \"{cue.Name}\" (id={cue.Id}){fadeNote}", LogType.Info);
        }

        return true;
    }

    // ── Seek ────────────────────────────────────────────────────────────────

    private bool ExecSeek(string[] parts, OscInputMessage msg, CueLookup lookup, bool relative)
    {
        // /SeekID/{id}/{sec}  or  /SeekID/{id} + float arg
        var cues = ResolveCues(parts, lookup);
        if (cues.Count == 0)
        {
            LogBuiltIn($"{(relative ? "SeekRel" : "Seek")}{LookupLabel(lookup)}: no matching cue", LogType.Warning);
            return true;
        }

        double seconds;
        if (parts.Length >= 3 && TryParseDouble(parts[2], out double pathSec))
            seconds = pathSec;
        else if (msg.FirstFloat.HasValue)
            seconds = msg.FirstFloat.Value;
        else
        {
            LogBuiltIn($"{(relative ? "SeekRel" : "Seek")}: missing seconds (path or float arg)", LogType.Warning);
            return true;
        }

        var executor = _globalData?.CueCommandExectutor;
        if (executor == null)
        {
            LogBuiltIn("Seek: executor missing", LogType.Error);
            return true;
        }

        foreach (var cue in cues)
        {
            // ApplyControlAction Seek uses ControlComponent fields — use stub via component API.
            var stub = new ControlComponent
            {
                Action = ControlAction.Seek,
                TargetCueId = cue.Id,
                SeekTimeSeconds = seconds,
                SeekMode = relative ? ControlFadeMode.Relative : ControlFadeMode.Absolute
            };
            _ = executor.ApplyControlComponentAsync(stub, -1, _globalData?.Settings?.StopFadeDuration ?? 0f);
            LogBuiltIn(
                $"{(relative ? "SeekRel" : "Seek")}{LookupLabel(lookup)}: \"{cue.Name}\" (id={cue.Id}) → {seconds:0.###}s",
                LogType.Info);
        }

        return true;
    }

    // ── Select ──────────────────────────────────────────────────────────────

    private bool ExecSelect(string[] parts, CueLookup lookup)
    {
        var cues = ResolveCues(parts, lookup);
        if (cues.Count == 0)
        {
            LogBuiltIn($"Select{LookupLabel(lookup)}: no matching cue", LogType.Warning);
            return true;
        }

        var selection = _globalData?.ShellSelection;
        if (selection == null)
        {
            LogBuiltIn("Select: ShellSelection missing", LogType.Error);
            return true;
        }

        // Select first match (playhead style).
        var cue = cues[0];
        selection.SelectIndividualShell(cue, recordHistory: false);
        LogBuiltIn($"Select{LookupLabel(lookup)}: \"{cue.Name}\" (id={cue.Id})", LogType.Info);
        return true;
    }

    // ── Arm / Disarm ────────────────────────────────────────────────────────

    private bool ExecArm(string[] parts, CueLookup lookup, bool armed)
    {
        var cues = ResolveCues(parts, lookup);
        if (cues.Count == 0)
        {
            LogBuiltIn($"{(armed ? "Arm" : "Disarm")}{LookupLabel(lookup)}: no matching cue", LogType.Warning);
            return true;
        }

        string verb = armed ? "Arm" : "Disarm";
        foreach (var cue in cues)
        {
            if (cue.Armed == armed)
            {
                LogBuiltIn($"{verb}: \"{cue.Name}\" already {(armed ? "armed" : "disarmed")}", LogType.Info);
                continue;
            }

            // ArmedChanged notifies ShellBar for UI refresh.
            cue.Armed = armed;
            LogBuiltIn($"{verb}: \"{cue.Name}\" (id={cue.Id})", LogType.Info);
        }

        return true;
    }

    // ── Logging / parse ─────────────────────────────────────────────────────

    private void LogBuiltIn(string message, LogType type)
    {
        GD.Print($"OscListen:BuiltIn - {message}");
        _globalSignals?.EmitSignal(nameof(GlobalSignals.Log), $"OSC {message}", (int)type);
        if (_monitorEnabled)
            EnqueueMonitorLine($"— {message}");
    }

    private static bool TryParseDouble(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
