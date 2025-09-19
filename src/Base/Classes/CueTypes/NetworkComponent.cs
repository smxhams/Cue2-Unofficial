using Godot.Collections;

namespace Cue2.Base.Classes.CueTypes;

public class NetworkComponent : ICueComponent
{
    public string Type => "Network";

    public Dictionary GetData()
    {
        return new Dictionary();
    }

    public void LoadFromData(Dictionary data)
    {
        
    }
}