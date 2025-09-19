using Godot.Collections;

namespace Cue2.Base.Classes.CueTypes;

public interface ICueComponent
{
    string Type { get; }
    Dictionary GetData();
    void LoadFromData(Dictionary data);
}