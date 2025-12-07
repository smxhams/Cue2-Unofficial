using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Godot;

namespace Cue2.Base.CommandInterpreter;

public partial class CueCommandExectutor : CueCommandInterpreter
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
        GD.Print($"CueCommandExecutor:ActivateCue - Activating: {cue.Name}");
        
        try
        {
            var activeCue = new ActiveCue(cue, _activeCueList, _mediaEngine, _audioDevices, _globalSignals);
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
        }
        
    }
    
    
    private void StopAllCommand()
    {
        return;
    }

    private void CleanUp()
    {
        foreach (var activeCue in _activeCues)
        {
            activeCue.Cleanup();
            activeCue.Dispose();
        }
        _activeCues.Clear();
        
    }
    
}

