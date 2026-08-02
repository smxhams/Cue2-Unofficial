using Godot.Collections;

namespace Cue2.Domain.Cues;

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