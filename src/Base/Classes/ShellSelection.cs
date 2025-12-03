using System;
using System.Collections.Generic;
using System.Linq;
using Cue2.Shared;
using Godot;

namespace Cue2.Base.Classes;

public partial class ShellSelection : Node
{
    private GlobalData _globalData;
    private GlobalSignals _globalSignals;


    public List<ICue> SelectedCues = new();

    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        
    }

    public void SelectIndividualShell(ICue selectedCue)
    {
        if (SelectedCues.Any())
        {
            foreach (var cue in SelectedCues.ToList())
            {
                SelectedCues.Remove(cue);
                cue.ShellBar.Deselect();
            }
        }
        
        AddSelection(selectedCue);
    }
    
    public void SelectThrough(ICue pressedCue)
    {
        var cueContainer = _globalData.Cuelist.GetNode<VBoxContainer>("%CueContainer");
        
        var startShell = SelectedCues.Last().ShellBar;
        int startShellPosition = startShell.GetIndex();
        int pressedCuePosition = pressedCue.ShellBar.GetIndex();
        int start = Math.Min(startShellPosition, pressedCuePosition);
        int end = Math.Max(startShellPosition, pressedCuePosition);
        for (int i = start; i <= end; i++)
        {
            int cueId = cueContainer.GetChild(i).Get("CueId").AsInt32();
            ICue cue = CueList.FetchCueFromId(cueId);
            if (SelectedCues.Contains(cue) == false)
            {
                AddSelection(cue);
            }
        }
        _globalSignals.EmitSignal(nameof(GlobalSignals.ShellFocused), pressedCue.Id);
    }
    
    public void SelectAllShells()
    {
        GD.Print("Selecting All Shells");
    }
    
    public void AddSelection(ICue cue)
    {
        cue.ShellBar.Select();
        SelectedCues.Add(cue);
        _globalSignals.EmitSignal(nameof(GlobalSignals.ShellFocused), cue.Id);
    }
    
    public void RemoveSelection(int shellIndex)
    {
        //
    }
}