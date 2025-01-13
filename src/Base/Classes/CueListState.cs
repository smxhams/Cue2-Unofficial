using System;
using System.Collections.Generic;

namespace Cue2.Base.Classes;

// Momento
public class CueListState
{
    private readonly List<ICue> _cuelist;
    private readonly Dictionary<int, Cue> _cueIndex;
    
    // State meta data
    private readonly DateTime _stateCreatedAt;
    
    public CueListState(List<ICue> cuelist, Dictionary<int, Cue> cueIndex)
    {
        _cuelist = cuelist;
        _cueIndex = cueIndex;
        _stateCreatedAt = DateTime.Now;
    }
    
    public List<ICue> GetCuelist()
    {
        return _cuelist;
    }
    
    public Dictionary<int, Cue> GetCueIndex()
    {
        return _cueIndex;
    }
    
    public DateTime GetStateCreatedAt()
    {
        return _stateCreatedAt;
    }

    public string GetName()
    {
        return $"{_stateCreatedAt} / {_cuelist}";
    }
}