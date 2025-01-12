using System.Collections.Generic;
using Cue2.Base.Classes;

namespace Cue2.Shared;


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
        if (_states.Count > 0)
        {
            _cuelist.Restore(_states[_states.Count - 1]);
            _states.RemoveAt(_states.Count - 1);
        }
    }
}