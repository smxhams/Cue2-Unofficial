using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Cue2.UI.Utilities;
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

    /// <summary>
    /// Pushes text/style changes to any playing instance of a text component.
    /// </summary>
    /// <param name="component">Text component to refresh on active cues.</param>
    public void RefreshPlayingTextVisuals(TextComponent component)
    {
        if (component == null)
            return;

        foreach (var active in _activeCues.ToList())
            active?.RefreshTextVisuals(component);
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
    /// GO: pre-spawn the entire continue/follow chain for each selected armed cue, wire event-driven
    /// arming (continue at content-phase start, follow at real content complete), advance playhead.
    /// Disarmed cues do not play; selection moves to the next eligible cue.
    /// </summary>
    public void GoCommand()
    {
        if (!ShellSelection.SelectedCues.Any())
        {
            GD.Print("CueCommandExecutor:GoCommand - No Shells Selected");
            return;
        }

        var selected = ShellSelection.SelectedCues.ToList();
        foreach (var cue in selected)
        {
            if (!cue.Armed)
            {
                GD.Print($"CueCommandExecutor:GoCommand - Skipping disarmed cue {cue.Name} (id={cue.Id})");
                continue;
            }

            ActivateSequenceFrom(cue);
        }

        AdvancePlayheadAfterGo(selected);
    }

    /// <summary>
    /// Advances selection after GO: after a played sequence, or past a disarmed cue that was GO'd.
    /// Cues with <see cref="Cue.ShouldSkipOnPlayhead"/> are walked over.
    /// </summary>
    /// <param name="selectedCues">Cues that were selected when GO was pressed (order preserved).</param>
    private void AdvancePlayheadAfterGo(List<Cue> selectedCues)
    {
        if (selectedCues == null || selectedCues.Count == 0) return;

        var primary = selectedCues[selectedCues.Count - 1];
        if (primary == null) return;

        Cue target;
        if (primary.Armed)
        {
            // Played (or chain head): stand on first cue after the sequence, skipping bypass targets.
            var after = primary.GetCueAfterSequence();
            if (after == null)
            {
                // No cue after sequence — stay on sequence end (existing behaviour).
                target = primary.GetSequenceEndCue();
            }
            else
            {
                target = Cue.ResolvePlayheadTarget(after) ?? primary.GetSequenceEndCue();
            }
        }
        else
        {
            // Disarmed GO: do not play; move to next eligible sibling.
            target = Cue.ResolvePlayheadTarget(primary.GetNextSiblingCue());
            if (target == null)
                return; // Nothing after — leave selection on the disarmed cue.
        }

        if (target == null) return;

        if (ShellSelection.SelectedCues.Count == 1 && ShellSelection.SelectedCues[0] == target)
            return;

        _globalData?.ShellSelection?.SelectIndividualShell(target);
    }

    /// <summary>
    /// Pre-spawns the continue/follow chain from <paramref name="head"/> and starts the head.
    /// Fire-and-forget wrapper for manual GO / playhead (does not block the caller).
    /// </summary>
    /// <param name="head">Chain head cue to activate.</param>
    /// <param name="controlGoFadeIn">
    /// Optional fade-in seconds for the head cue when started via a control GO.
    /// When null or ≤ 0, playback uses each component's own fade-in.
    /// </param>
    public void ActivateSequenceFrom(Cue head, double? controlGoFadeIn = null)
    {
        _ = ActivateSequenceFromAsync(head, controlGoFadeIn);
    }

    /// <summary>
    /// Pre-spawns the continue/follow chain from <paramref name="head"/> and starts the head,
    /// awaiting until the head has entered pre-wait or content (so control actions can chain).
    /// </summary>
    /// <param name="head">Chain head cue to activate.</param>
    /// <param name="controlGoFadeIn">Optional control GO fade-in seconds for the head.</param>
    public async Task ActivateSequenceFromAsync(Cue head, double? controlGoFadeIn = null)
    {
        if (head == null)
        {
            GD.PrintErr("CueCommandExecutor:ActivateSequenceFromAsync - Cue is null");
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

        GD.Print($"CueCommandExecutor:ActivateSequenceFromAsync - {head.Name}: {chain.Count} cue(s)");

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

        // Control GO fade-in applies to the head instance only (not continue/follow peers).
        if (controlGoFadeIn.HasValue && controlGoFadeIn.Value > 1e-9 && actives.Count > 0)
            actives[0].SetControlFadeInDuration(controlGoFadeIn.Value);

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

        // Non-head chain members stay pending until armed — start them without waiting.
        for (int i = 1; i < actives.Count; i++)
            _ = StartActiveSafe(actives[i]);

        // Await head until pre-wait is running or content has triggered so a following
        // control action (Start Now, Pause, …) can see a live instance.
        if (actives.Count > 0)
            await StartActiveSafe(actives[0]);
    }

    /// <summary>
    /// Starts a single cue (and its sequence chain).
    /// </summary>
    public void ActivateCue(Cue cue)
    {
        if (cue == null) return;
        ActivateSequenceFrom(cue);
    }

    /// <summary>
    /// Applies a control-component action to the target cue (by id).
    /// </summary>
    /// <param name="action">GO, Pause, Stop, Resume, or Start Now.</param>
    /// <param name="targetCueId">Id of the cue to control.</param>
    /// <param name="stopFadeDuration">
    /// Optional fade-out seconds for Stop. When null, uses session <see cref="Settings.StopFadeDuration"/>.
    /// When 0, stops immediately.
    /// </param>
    /// <param name="goFadeInDuration">
    /// Optional fade-in seconds for GO. When null or 0, target component fade-ins are used as-is.
    /// </param>
    /// <summary>
    /// Applies a full control component (including Fade property animation), awaiting completion.
    /// </summary>
    /// <param name="control">Control component to execute.</param>
    /// <param name="sourceCueId">Owning cue id (self-target guard for non-Fade actions).</param>
    /// <param name="sessionStopFadeDuration">Session default stop fade for Stop actions.</param>
    public async Task ApplyControlComponentAsync(
        ControlComponent control,
        int sourceCueId = -1,
        float sessionStopFadeDuration = 0f)
    {
        if (control == null)
        {
            GD.PrintErr("CueCommandExecutor:ApplyControlComponentAsync - control is null");
            return;
        }

        // Translate Layer targets a canvas layer, not a cue.
        if (control.Action == ControlAction.TranslateLayer)
        {
            await ApplyTranslateLayerAsync(control);
            return;
        }

        int targetCueId = control.TargetCueId;
        if (targetCueId < 0)
        {
            GD.Print("CueCommandExecutor:ApplyControlComponentAsync - Invalid target cue id");
            return;
        }

        double? stopFade = control.Action == ControlAction.Stop
            ? control.ResolveStopFadeDuration(sessionStopFadeDuration)
            : null;
        double? goFadeIn = control.Action == ControlAction.Go
            ? Math.Max(0.0, control.GoFadeInDuration)
            : null;

        switch (control.Action)
        {
            case ControlAction.Go:
            {
                var cue = CueList.FetchCueFromId(targetCueId);
                if (cue == null)
                {
                    _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                        $"Control GO: cue id {targetCueId} not found", (int)LogType.Warning);
                    return;
                }

                if (!cue.Armed)
                {
                    GD.Print($"CueCommandExecutor:ApplyControlComponentAsync - Target cue {cue.Name} is disarmed; skipping GO");
                    _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                        $"Control GO skipped: \"{cue.Name}\" is disarmed", (int)LogType.Info);
                    return;
                }

                await ActivateSequenceFromAsync(cue, goFadeIn);
                break;
            }

            case ControlAction.Pause:
            {
                var matches = FindActiveCuesById(targetCueId).ToList();
                if (matches.Count == 0)
                {
                    GD.Print($"CueCommandExecutor:ApplyControlComponentAsync - No playing instance of cue id {targetCueId} to pause");
                    return;
                }

                foreach (var active in matches)
                    active.RequestPause();
                break;
            }

            case ControlAction.Stop:
            {
                var matches = FindActiveCuesById(targetCueId).ToList();
                if (matches.Count == 0)
                {
                    GD.Print($"CueCommandExecutor:ApplyControlComponentAsync - No playing instance of cue id {targetCueId} to stop");
                    return;
                }

                foreach (var active in matches)
                    active.StopAll(propagateToChildren: true, fadeDurationOverride: stopFade);
                break;
            }

            case ControlAction.Resume:
            {
                var matches = FindActiveCuesById(targetCueId).ToList();
                if (matches.Count == 0)
                {
                    GD.Print($"CueCommandExecutor:ApplyControlComponentAsync - No playing instance of cue id {targetCueId} to resume");
                    return;
                }

                foreach (var active in matches)
                    active.RequestResume();
                break;
            }

            case ControlAction.StartNow:
            {
                var matches = FindActiveCuesById(targetCueId).ToList();
                if (matches.Count == 0)
                {
                    GD.Print($"CueCommandExecutor:ApplyControlComponentAsync - No waiting instance of cue id {targetCueId} for Start Now");
                    return;
                }

                foreach (var active in matches)
                    active.RequestStartNow();
                break;
            }

            case ControlAction.Fade:
                await ApplyPropertyFadeAsync(control);
                break;

            case ControlAction.Seek:
            {
                var matches = FindActiveCuesById(targetCueId).ToList();
                if (matches.Count == 0)
                {
                    GD.Print($"CueCommandExecutor:ApplyControlComponentAsync - No playing instance of cue id {targetCueId} to seek");
                    _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                        $"Control Seek: no playing instance of cue id {targetCueId}", (int)LogType.Warning);
                    return;
                }

                bool relative = control.SeekMode == ControlFadeMode.Relative;
                foreach (var active in matches)
                    active.RequestSeek(control.SeekTimeSeconds, relative);
                break;
            }

            default:
                GD.PrintErr($"CueCommandExecutor:ApplyControlComponentAsync - Unknown action {control.Action}");
                break;
        }
    }

    /// <summary>
    /// Animates (or snaps) a canvas target layer's size and/or position.
    /// </summary>
    private async Task ApplyTranslateLayerAsync(ControlComponent control)
    {
        var displays = _globalData?.DisplaysManager;
        if (displays == null && Engine.GetMainLoop() is SceneTree st)
            displays = st.Root.GetNodeOrNull<DisplaysManager>("/root/DisplaysManager");

        if (displays == null)
        {
            GD.PrintErr("CueCommandExecutor:ApplyTranslateLayerAsync - DisplaysManager not found");
            return;
        }

        var layer = DisplaysManager.GetLayerById(control.TargetLayerId);
        if (layer == null)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"Control Translate Layer: layer id {control.TargetLayerId} not found", (int)LogType.Warning);
            return;
        }

        if (layer.Locked)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"Control Translate Layer: layer '{layer.LayerName}' is locked", (int)LogType.Warning);
            GD.Print(
                $"CueCommandExecutor:ApplyTranslateLayerAsync - Layer '{layer.LayerName}' (id {layer.LayerId}) is locked; skipping translate");
            return;
        }

        if (!control.TranslateSizeEnabled && !control.TranslatePositionEnabled)
        {
            GD.Print("CueCommandExecutor:ApplyTranslateLayerAsync - Neither size nor position enabled");
            return;
        }

        Vector2I startPos = layer.CanvasPosition;
        Vector2I startSize = layer.Size;
        Vector2I endPos = startPos;
        Vector2I endSize = startSize;

        bool relative = control.TranslateMode == ControlFadeMode.Relative;

        if (control.TranslatePositionEnabled)
        {
            endPos = relative
                ? new Vector2I(startPos.X + control.TranslatePosX, startPos.Y + control.TranslatePosY)
                : new Vector2I(control.TranslatePosX, control.TranslatePosY);
        }

        if (control.TranslateSizeEnabled)
        {
            endSize = relative
                ? new Vector2I(startSize.X + control.TranslateSizeX, startSize.Y + control.TranslateSizeY)
                : new Vector2I(control.TranslateSizeX, control.TranslateSizeY);
            endSize = new Vector2I(Math.Max(1, endSize.X), Math.Max(1, endSize.Y));
        }

        double duration = Math.Max(0.0, control.TranslateDuration);
        GD.Print(
            $"CueCommandExecutor:ApplyTranslateLayerAsync - layer '{layer.LayerName}' mode={control.TranslateMode} " +
            $"dur={duration:0.###}s pos {startPos}→{endPos} size {startSize}→{endSize}");

        if (duration <= 1e-9)
        {
            displays.ApplyLayerGeometryLive(
                layer.LayerId,
                control.TranslatePositionEnabled ? endPos : null,
                control.TranslateSizeEnabled ? endSize : null);
            return;
        }

        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < duration)
        {
            float t = (float)Math.Clamp(timer.Elapsed.TotalSeconds / duration, 0.0, 1.0);
            Vector2I pos = new Vector2I(
                Mathf.RoundToInt(Mathf.Lerp(startPos.X, endPos.X, t)),
                Mathf.RoundToInt(Mathf.Lerp(startPos.Y, endPos.Y, t)));
            Vector2I size = new Vector2I(
                Math.Max(1, Mathf.RoundToInt(Mathf.Lerp(startSize.X, endSize.X, t))),
                Math.Max(1, Mathf.RoundToInt(Mathf.Lerp(startSize.Y, endSize.Y, t))));

            displays.ApplyLayerGeometryLive(
                layer.LayerId,
                control.TranslatePositionEnabled ? pos : null,
                control.TranslateSizeEnabled ? size : null);
            await Task.Delay(16);
        }

        displays.ApplyLayerGeometryLive(
            layer.LayerId,
            control.TranslatePositionEnabled ? endPos : null,
            control.TranslateSizeEnabled ? endSize : null);
    }

    /// <summary>
    /// Fades target cue audio volume and/or video opacity per the control component settings.
    /// Volume applies to a dedicated <see cref="AudioComponent"/> and/or video embedded audio
    /// when <see cref="VideoComponent.HasAudio"/> and <see cref="VideoComponent.UseAudio"/> are set.
    /// </summary>
    private async Task ApplyPropertyFadeAsync(ControlComponent control)
    {
        var cue = CueList.FetchCueFromId(control.TargetCueId);
        if (cue == null)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"Control Fade: cue id {control.TargetCueId} not found", (int)LogType.Warning);
            return;
        }

        var audio = cue.GetAudioComponent();
        var video = cue.GetVideoComponent();
        bool videoEmbeddedAudio = ControlComponent.VideoHasEmbeddedAudio(video);

        bool doDedicatedAudio = control.FadeAudioVolumeEnabled && audio != null;
        bool doVideoAudio = control.FadeAudioVolumeEnabled && videoEmbeddedAudio;
        bool doOpacity = control.FadeVideoOpacityEnabled && video != null;

        if (!doDedicatedAudio && !doVideoAudio && !doOpacity)
        {
            GD.Print($"CueCommandExecutor:ApplyPropertyFadeAsync - Nothing to fade on \"{cue.Name}\"");
            return;
        }

        float startDedicatedLin = doDedicatedAudio ? Mathf.Clamp((float)audio.Volume, 0f, 1f) : 0f;
        float endDedicatedLin = startDedicatedLin;
        if (doDedicatedAudio)
            endDedicatedLin = ResolveFadeAudioLinear(startDedicatedLin, control);

        float startVideoAudLin = doVideoAudio ? Mathf.Clamp((float)video.Volume, 0f, 1f) : 0f;
        float endVideoAudLin = startVideoAudLin;
        if (doVideoAudio)
            endVideoAudLin = ResolveFadeAudioLinear(startVideoAudLin, control);

        float startOpacity = doOpacity ? Mathf.Clamp(video.Opacity, 0f, 1f) : 1f;
        float endOpacity = startOpacity;
        if (doOpacity)
        {
            float startPct = startOpacity * 100f;
            float endPct = control.FadeMode == ControlFadeMode.Absolute
                ? control.FadeOpacityPercent
                : startPct + control.FadeOpacityPercent;
            endPct = Mathf.Clamp(endPct, 0f, 100f);
            endOpacity = endPct / 100f;
        }

        double duration = Math.Max(0.0, control.PropertyFadeDuration);
        GD.Print(
            $"CueCommandExecutor:ApplyPropertyFadeAsync - \"{cue.Name}\" mode={control.FadeMode} " +
            $"dur={duration:0.###}s audioComp={doDedicatedAudio} videoAud={doVideoAudio} opacity={doOpacity}");

        if (duration <= 1e-9)
        {
            ApplyPropertyFadeSample(audio, video, doDedicatedAudio, endDedicatedLin,
                doVideoAudio, endVideoAudLin, doOpacity, endOpacity);
            return;
        }

        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < duration)
        {
            float t = (float)Math.Clamp(timer.Elapsed.TotalSeconds / duration, 0.0, 1.0);
            ApplyPropertyFadeSample(
                audio, video,
                doDedicatedAudio, Mathf.Lerp(startDedicatedLin, endDedicatedLin, t),
                doVideoAudio, Mathf.Lerp(startVideoAudLin, endVideoAudLin, t),
                doOpacity, Mathf.Lerp(startOpacity, endOpacity, t));
            await Task.Delay(16);
        }

        ApplyPropertyFadeSample(audio, video, doDedicatedAudio, endDedicatedLin,
            doVideoAudio, endVideoAudLin, doOpacity, endOpacity);
    }

    /// <summary>
    /// Computes end linear volume for absolute/relative audio fade from a start linear level.
    /// </summary>
    private static float ResolveFadeAudioLinear(float startLinear, ControlComponent control)
    {
        float startDb = UiUtilities.LinearToDb(Mathf.Clamp(startLinear, 0f, 1f));
        float endDb = control.FadeMode == ControlFadeMode.Absolute
            ? control.FadeAudioDb
            : startDb + control.FadeAudioDb;
        endDb = Mathf.Clamp(endDb, -60f, 0f);
        return UiUtilities.DbToLinear(endDb);
    }

    /// <summary>
    /// Writes one sample of a property fade to components and refreshes live video visuals.
    /// </summary>
    private void ApplyPropertyFadeSample(
        AudioComponent audio,
        VideoComponent video,
        bool doDedicatedAudio,
        float dedicatedAudioLinear,
        bool doVideoAudio,
        float videoAudioLinear,
        bool doOpacity,
        float opacity)
    {
        if (doDedicatedAudio && audio != null)
            audio.Volume = Mathf.Clamp(dedicatedAudioLinear, 0f, 1f);

        if (doVideoAudio && video != null)
            video.Volume = Mathf.Clamp(videoAudioLinear, 0f, 1f);

        if (doOpacity && video != null)
        {
            video.Opacity = Mathf.Clamp(opacity, 0f, 1f);
            RefreshPlayingVideoVisuals(video);
        }
    }

    /// <summary>
    /// Fire-and-forget control action (legacy/manual callers).
    /// </summary>
    public void ApplyControlAction(
        ControlAction action,
        int targetCueId,
        double? stopFadeDuration = null,
        double? goFadeInDuration = null)
    {
        var stub = new ControlComponent
        {
            Action = action,
            TargetCueId = targetCueId,
            StopFadeUsesSessionDefault = !stopFadeDuration.HasValue,
            StopFadeDuration = stopFadeDuration ?? 0,
            GoFadeInDuration = goFadeInDuration ?? 0
        };
        if (stopFadeDuration.HasValue)
        {
            stub.StopFadeUsesSessionDefault = false;
            stub.StopFadeDuration = stopFadeDuration.Value;
        }

        _ = ApplyControlComponentAsync(stub, -1, _globalData?.Settings?.StopFadeDuration ?? 0f);
    }

    /// <summary>
    /// Finds all live <see cref="ActiveCue"/> instances (including nested children) for a cue id.
    /// </summary>
    /// <param name="cueId">Cue identity to match.</param>
    /// <returns>Matching active instances (may be empty).</returns>
    private IEnumerable<ActiveCue> FindActiveCuesById(int cueId)
    {
        foreach (var root in _activeCues.ToList())
        {
            if (!GodotObject.IsInstanceValid(root)) continue;
            foreach (var active in root.EnumerateSelfAndDescendants())
            {
                if (active?.Cue != null && active.Cue.Id == cueId)
                    yield return active;
            }
        }
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
