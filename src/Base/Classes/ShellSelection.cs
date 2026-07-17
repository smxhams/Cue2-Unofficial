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

        _globalSignals.SelectNextCue += SelectNextCue;
        _globalSignals.SelectPreviousCue += SelectPreviousCue;
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

        if (SelectedCues.Count == 0 || pressedCue?.ShellBar == null)
            return;

        var startShell = SelectedCues.Last().ShellBar;
        if (startShell == null) return;

        int startIndex = allShellBars.IndexOf(startShell);
        int pressedIndex = allShellBars.IndexOf(pressedCue.ShellBar);
        if (startIndex < 0 || pressedIndex < 0) return;

        int start = Math.Min(startIndex, pressedIndex);
        int end = Math.Max(startIndex, pressedIndex);
        // Expand selection silently — only emit ShellFocused once for the pressed cue.
        // (Per-cue AddSelection would flood async audio/video inspectors mid multi-select.)
        for (int i = start; i <= end; i++)
        {
            var sb = allShellBars[i];
            int cueId = sb.Get("CueId").AsInt32();
            Cue cue = CueList.FetchCueFromId(cueId);
            if (cue == null || SelectedCues.Contains(cue))
                continue;
            cue.ShellBar?.Select();
            SelectedCues.Add(cue);
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
                // Only recurse into child groups if they are currently expanded (visible).
                // This prevents next/previous selection from landing on cues hidden inside collapsed groups.
                if (childContainer != null && childContainer.Visible)
                {
                    result.AddRange(GetAllShellBarsInOrder(childContainer));
                }
            }
        }
        return result;
    }
    
    public void SelectAllShells()
    {
        var visibleCues = GetAllCuesInOrder();
        if (visibleCues.Count == 0) return;

        // Clear current
        foreach (var cue in SelectedCues.ToList())
        {
            cue.ShellBar.Deselect();
        }
        SelectedCues.Clear();

        foreach (var cue in visibleCues)
        {
            cue.ShellBar.Select();
            SelectedCues.Add(cue);
        }

        if (visibleCues.Count > 0)
            _globalSignals.EmitSignal(nameof(GlobalSignals.ShellFocused), visibleCues.Last().Id);
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

    /// <summary>
    /// Returns a flat list of all *visible* cues in visual/document order.
    /// Cues inside collapsed groups are excluded.
    /// </summary>
    /// <summary>
    /// Returns the ordered list of currently visible cues for navigation/selection.
    /// Respects group expansion state.
    /// </summary>
    public List<Cue> GetAllCuesInOrder()
    {
        var container = _globalData?.Cuelist?.GetNode<VBoxContainer>("%CueContainer");
        if (container == null) return new List<Cue>();

        var shellBars = GetAllShellBarsInOrder(container);
        var cues = new List<Cue>(shellBars.Count);
        foreach (var sb in shellBars)
        {
            int cueId = sb.Get("CueId").AsInt32();
            var cue = CueList.FetchCueFromId(cueId);
            if (cue != null)
                cues.Add(cue);
        }
        return cues;
    }

    public void SelectNextCue()
    {
        var ordered = GetAllCuesInOrder();
        if (ordered.Count == 0) return;

        Cue target;
        if (SelectedCues.Count == 0)
        {
            target = ordered[0];
        }
        else
        {
            var current = SelectedCues.Last();
            int idx = ordered.IndexOf(current);
            if (idx < 0)
            {
                // Current selection is hidden (e.g. in a collapsed group). Start from beginning.
                target = ordered[0];
            }
            else
            {
                int next = (idx + 1) % ordered.Count;
                target = ordered[next];
            }
        }
        SelectIndividualShell(target);
    }

    public void SelectPreviousCue()
    {
        var ordered = GetAllCuesInOrder();
        if (ordered.Count == 0) return;

        Cue target;
        if (SelectedCues.Count == 0)
        {
            target = ordered[ordered.Count - 1];
        }
        else
        {
            var current = SelectedCues.Last();
            int idx = ordered.IndexOf(current);
            if (idx < 0)
            {
                // Current selection is hidden (e.g. in a collapsed group). Start from end.
                target = ordered[ordered.Count - 1];
            }
            else
            {
                int prev = (idx - 1 + ordered.Count) % ordered.Count;
                target = ordered[prev];
            }
        }
        SelectIndividualShell(target);
    }
}