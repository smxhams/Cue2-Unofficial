using System.Collections.Generic;
using System.Linq;
using Cue2.Base.Classes;
using Godot;

namespace Cue2.Shared;

// See memento design pattern - this is still WIP
// Undo / Redo
public class History
{
    private List<CueListState> _states = new List<CueListState>();
    private CueList _cuelist;

    public History(CueList cuelist)
    {
        _cuelist = cuelist;
    }

    public void Backup()
    {
        _states.Add(_cuelist.CreateState());
    }
    
    public void Undo()
    {
        if (_states.Count == 0) { return; }

        CueListState prevState = _states.Last();
        _states.Remove(prevState);
        _cuelist.Restore(prevState);
    }

    public void ShowHistory()
    {
        GD.Print("/n History: Here's the list of mementos:");
        foreach (var state in _states)
        {
            GD.Print(state.GetName());
        }
    }
    
}