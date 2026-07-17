using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Godot;

namespace Cue2.Base.CommandInterpreter;

/// <summary>
/// Executes GO and pre-spawns full continue/follow chains with event-driven arming.
/// </summary>
public partial class CueCommandExectutor : Node
{
    private GlobalData _globalData;
    private GlobalSignals _globalSignals;
    private MediaEngine _mediaEngine;
    private AudioDevices _audioDevices;

    private VBoxContainer _activeCueList;

    private readonly List<ActiveCue> _activeCues = new List<ActiveCue>();

    /// <summary>
    /// Currently playing cues (for inspector live-update of visual properties).
    /// </summary>
    public IReadOnlyList<ActiveCue> ActiveCues => _activeCues;

    /// <summary>
    /// Pushes expand/stretch/opacity changes to any playing instance of a video component.
    /// </summary>
    public void RefreshPlayingVideoVisuals(VideoComponent component)
    {
        if (component == null)
            return;

        foreach (var active in _activeCues.ToList())
            active?.RefreshVideoVisuals(component);
    }
    
    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _mediaEngine = GetNode<MediaEngine>("/root/MediaEngine");
        _audioDevices = GetNode<AudioDevices>("/root/AudioDevices");
        
        _activeCueList = GetNode("/root/Cue2Base").GetNode<PanelContainer>("%ActiveCueContainer").GetNode<VBoxContainer>("%ActiveCueList");
        GD.Print("CueCommandExecutor:_Ready - Cue Command Executor Successfully added");
        
        GD.Print("Cue Command Executor Successfully added");
        
        _globalSignals.Go += GoCommand;

        TreeExiting += CleanUp;
    }

    /// <summary>
    /// GO: pre-spawn the entire continue/follow chain for each selected cue, wire event-driven
    /// arming (continue at content-phase start, follow at real content complete), advance playhead.
    /// </summary>
    public void GoCommand()
    {
        if (!ShellSelection.SelectedCues.Any())
        {
            GD.Print("CueCommandExecutor:GoCommand - No Shells Selected");
            return;
        }

        var selected = ShellSelection.SelectedCues.ToList();
        foreach (var cue1 in selected)
            ActivateSequenceFrom((Cue)cue1);

        AdvancePlayheadAfterSequences(selected);
    }

    private void AdvancePlayheadAfterSequences(List<Cue> startedCues)
    {
        if (startedCues == null || startedCues.Count == 0) return;

        var primary = startedCues[startedCues.Count - 1];
        if (primary == null) return;

        var after = primary.GetCueAfterSequence();
        var target = after ?? primary.GetSequenceEndCue();
        if (target == null) return;

        if (ShellSelection.SelectedCues.Count == 1 && ShellSelection.SelectedCues[0] == target)
            return;

        _globalData?.ShellSelection?.SelectIndividualShell(target);
    }

    /// <summary>
    /// Pre-spawns the continue/follow chain from <paramref name="head"/> and starts the head.
    /// </summary>
    public void ActivateSequenceFrom(Cue head)
    {
        if (head == null)
        {
            GD.PrintErr("CueCommandExecutor:ActivateSequenceFrom - Cue is null");
            return;
        }

        var chain = CueSequencePlanner.BuildChain(head);
        if (chain.Count == 0)
        {
            chain = new List<CueChainMember>
            {
                new CueChainMember
                {
                    Cue = head,
                    IncomingMode = FollowType.None,
                    IncomingPostWait = 0
                }
            };
        }

        GD.Print($"CueCommandExecutor:ActivateSequenceFrom - {head.Name}: {chain.Count} cue(s)");

        // Create all active rows, build UI in sequence order (so the list matches occurrence),
        // then wire events and start playback.
        var actives = new List<ActiveCue>(chain.Count);
        foreach (var member in chain)
        {
            var active = new ActiveCue(
                member.Cue,
                _activeCueList,
                _mediaEngine,
                _audioDevices,
                _globalSignals,
                member);
            actives.Add(active);
            _activeCues.Add(active);
            active.Completed += () => _activeCues.Remove(active);
        }

        // Synchronous UI insert in chain order (avoids async race reordering the VBox).
        foreach (var active in actives)
            active.PrepareUiInOrder();

        // Link chain + arming rules from each cue's Follow mode.
        for (int i = 0; i < actives.Count; i++)
        {
            if (i + 1 < actives.Count)
                actives[i].NextInChain = actives[i + 1];

            var memberCue = chain[i].Cue;
            if (i + 1 >= actives.Count) continue;

            var next = actives[i + 1];
            var current = actives[i];

            if (memberCue.Follow == FollowType.Continue)
            {
                // Continue: arm next when this cue's content phase starts (after its pre-wait).
                double postWait = Math.Max(0.0, memberCue.PostWait);
                current.ContentPhaseStarted += () =>
                {
                    if (!GodotObject.IsInstanceValid(next)) return;
                    next.ArmIncoming(FollowType.Continue, postWait);
                };
            }
            else if (memberCue.Follow == FollowType.Follow)
            {
                // Follow: arm next when this cue's content actually completes (seek-aware).
                double postWait = Math.Max(0.0, memberCue.PostWait);
                current.ContentCompleted += () =>
                {
                    if (!GodotObject.IsInstanceValid(next)) return;
                    next.ArmIncoming(FollowType.Follow, postWait);
                };
            }
        }

        // Start every row (non-head stay pending until armed). UI is already ordered.
        foreach (var active in actives)
            _ = StartActiveSafe(active);
    }

    /// <summary>
    /// Starts a single cue (and its sequence chain).
    /// </summary>
    public void ActivateCue(Cue cue)
    {
        if (cue == null) return;
        ActivateSequenceFrom(cue);
    }

    private async Task StartActiveSafe(ActiveCue activeCue)
    {
        try
        {
            await activeCue.StartAsync();
        }
        catch (Exception ex)
        {
            var name = activeCue?.Cue?.Name ?? "?";
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to execute cue {name}: {ex.Message}", 2);
            GD.PrintErr($"CueCommandExecutor:StartActiveSafe - {ex.Message}");
            try { activeCue?.Cleanup(); } catch { /* best-effort */ }
            if (activeCue != null)
                _activeCues.Remove(activeCue);
        }
    }
    
    private void CleanUp()
    {
        if (_globalSignals != null)
            _globalSignals.Go -= GoCommand;

        foreach (var activeCue in _activeCues.ToList())
        {
            try
            {
                if (GodotObject.IsInstanceValid(activeCue))
                    activeCue.Cleanup();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"CueCommandExecutor:CleanUp - {ex.Message}");
            }
        }
        _activeCues.Clear();
    }
    
}
