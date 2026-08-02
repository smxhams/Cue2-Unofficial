using Godot.Collections;

namespace Cue2.Domain.Cues;

public interface ICueComponent
{
    string Type { get; }
    Dictionary GetData();
    void LoadFromData(Dictionary data);
}