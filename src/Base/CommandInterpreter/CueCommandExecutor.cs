using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Godot;

namespace Cue2.Base.CommandInterpreter;

public partial class CueCommandExectutor : Node
{
    private GlobalData _globalData;
    private GlobalSignals _globalSignals;
    private MediaEngine _mediaEngine;
    private AudioDevices _audioDevices;

    private VBoxContainer _activeCueList;

    private PackedScene _activeCueBarScene;
    
    private readonly List<ActiveCue> _activeCues = new List<ActiveCue>();
    
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
        _globalSignals.StopAll += StopAllCommand;

        TreeExiting += CleanUp;
    }

    public void GoCommand()
    {
        if (!ShellSelection.SelectedCues.Any())
        {
            GD.Print("CueCommandExecutor:GoCommand - No Shells Selected");
            return;
        }
        foreach (var cue1 in ShellSelection.SelectedCues)
        {
            var cue = (Cue)cue1; 
            ActivateCue(cue);
        } 
    }

    public async void ActivateCue(Cue cue)
    {
        if (cue == null)
        {
            GD.PrintErr("CueCommandExecutor:ActivateCue - Cue is null");
            return;
        }

        GD.Print($"CueCommandExecutor:ActivateCue - Activating: {cue.Name}");
        ActiveCue activeCue = null;
        
        try
        {
            activeCue = new ActiveCue(cue, _activeCueList, _mediaEngine, _audioDevices, _globalSignals);
            _activeCues.Add(activeCue);
            activeCue.Completed += () =>
            {
                _activeCues.Remove(activeCue);
            };
            await activeCue.StartAsync();
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to execute cue {cue.Name}: {ex.Message}", 2);
            GD.PrintErr($"CueCommandExecutor:ActivateCue - {ex.Message}");
            if (activeCue != null)
            {
                try
                {
                    activeCue.Cleanup();
                }
                catch (Exception cleanupEx)
                {
                    GD.PrintErr($"CueCommandExecutor:ActivateCue - Cleanup after failure: {cleanupEx.Message}");
                }
                _activeCues.Remove(activeCue);
            }
        }
    }
    
    
    private void StopAllCommand()
    {
        // ActiveCue instances also subscribe to StopAll and stop themselves.
        // Keep this as a safety net for any cues still tracked by the executor.
        foreach (var activeCue in _activeCues.ToList())
        {
            try
            {
                activeCue.StopAll(false);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"CueCommandExecutor:StopAllCommand - {ex.Message}");
            }
        }
    }

    private void CleanUp()
    {
        foreach (var activeCue in _activeCues.ToList())
        {
            try
            {
                // Cleanup() frees the ActiveCue GodotObject when done
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

