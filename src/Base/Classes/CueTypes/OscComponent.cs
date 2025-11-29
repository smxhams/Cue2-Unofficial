using Cue2.Base.Classes.Connections;
using Godot.Collections;

namespace Cue2.Base.Classes.CueTypes;


public class OscComponent : ICueComponent
{
    public string Type => "OscComponent";
    public int OscConnectionId;
    public string Command;
    public CueOscConnection OscConnection;



    public Dictionary GetData()
    {
        return new Dictionary()
        {
            { "Command", Command },
            { "OscConnectionId", OscConnectionId },
        };
    }

    public void LoadFromData(Dictionary data)
    {
        Command = data.TryGetValue("Command", out var value) ? (string)value : Command;
        OscConnectionId = data.TryGetValue("OscConnectionId", out value) ? (int)value : OscConnectionId;
    }
}