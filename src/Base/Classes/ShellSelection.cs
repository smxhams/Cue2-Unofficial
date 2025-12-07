using System;
using System.Collections.Generic;
using System.Linq;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Godot;

namespace Cue2.Base.Classes;

public partial class ShellSelection : Node
{
    private GlobalData _globalData;
    private GlobalSignals _globalSignals;


    public static List<Cue> SelectedCues = new();

    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        
    }

    public void SelectIndividualShell(Cue selectedCue)
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
    
    public void SelectThrough(Cue pressedCue)
    {
        var cueContainer = _globalData.Cuelist.GetNode<VBoxContainer>("%CueContainer");
        var allShellBars = GetAllShellBarsInOrder(cueContainer);

        var startShell = SelectedCues.Last().ShellBar;
        int startIndex = allShellBars.IndexOf(startShell);
        int pressedIndex = allShellBars.IndexOf(pressedCue.ShellBar);
        int start = Math.Min(startIndex, pressedIndex);
        int end = Math.Max(startIndex, pressedIndex);
        for (int i = start; i <= end; i++)
        {
            var sb = allShellBars[i];
            int cueId = sb.Get("CueId").AsInt32();
            Cue cue = CueList.FetchCueFromId(cueId);
            if (!SelectedCues.Contains(cue))
            {
                AddSelection(cue);
            }
        }
        _globalSignals.EmitSignal(nameof(GlobalSignals.ShellFocused), pressedCue.Id);
    }

    private List<ShellBar> GetAllShellBarsInOrder(VBoxContainer container)
    {
        List<ShellBar> result = new();
        foreach (var child in container.GetChildren())
        {
            if (child is ShellBar sb)
            {
                result.Add(sb);
                var childContainer = sb.GetNode<VBoxContainer>("%ShellChildContainer");
                if (childContainer != null)
                {
                    result.AddRange(GetAllShellBarsInOrder(childContainer));
                }
            }
        }
        return result;
    }
    
    public void SelectAllShells()
    {
        GD.Print("Selecting All Shells");
    }
    
    public void AddSelection(Cue cue)
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