using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Cue2.Base.Classes;
using Cue2.Shared;
using Godot;
using Hardware.Info;

namespace Cue2.Base.CommandInterpreter;

public partial class CueCommandExectutor : CueCommandInterpreter
{
    private GlobalData _globalData;
    private GlobalSignals _globalSignals;
    private MediaEngine _mediaEngine;
    private AudioDevices _audioDevices;

    private VBoxContainer _activeCueList;
    
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
    }

    public void GoCommand()
    {
        if (!_globalData.ShellSelection.SelectedShells.Any())
        {
            GD.Print("CueCommandExecutor:GoCommand - No Shells Selected");
            return;
        }
        foreach (var cue1 in _globalData.ShellSelection.SelectedShells)
        {
            var cue = (Cue)cue1; 
            ActivateCue(cue);
        } 
    }

    public async void ActivateCue(Cue cue)
    {
        //_globalData.Playback.PlayMedia(cue.FilePath);
        GD.Print($"CueCommandExecutor:ActivateCue - Activating: {cue.Name}");
        //var liveViewContainer = GetNode("/root/Cue2Base").GetNode<PanelContainer>("%ActiveCueContainer").GetNode<VBoxContainer>("%ActiveCueList");

        var audioComponent = cue.GetAudioComponent();
        if (audioComponent != null)
        {
            try
            {
                // UI Element for active cue
                var activeCueBar = LoadActiveCueBar();
                _activeCueList.AddChild(activeCueBar);
                // Init active cue
                var activeCue = new ActiveCue(cue, activeCueBar, _mediaEngine, _audioDevices, _globalSignals);
                //_activeCues.Add(activeCue);
                await activeCue.StartAsync();
                
                
            }
            catch (Exception ex)
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to execute cue {cue.Name}: {ex.Message}", 2);
                GD.PrintErr($"CueCommandExecutor:ActivateCue - {ex.Message}");
            }
        }
        

        

        if (cue.ChildCues.Count() != 0)
        {
            foreach (var child in cue.ChildCues)
            {
                var childCue = CueList.FetchCueFromId(child);
                ActivateCue(childCue);

            }
        }
        
    }
    
    
    private void StopAllCommand()
    {
        return;
    }
    
    public PanelContainer LoadActiveCueBar()
    {
        // Load in a shell bar
        PanelContainer activeBar = (PanelContainer)SceneLoader.LoadScene("uid://dt7rlfag7yr2c", out var error);
        if (activeBar == null || !string.IsNullOrEmpty(error))
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to load active cue bar: {error}", 2);
            return null;
        }

        return activeBar;
    }

}

