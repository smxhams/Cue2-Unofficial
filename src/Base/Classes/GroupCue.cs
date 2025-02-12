using Godot;

namespace Cue2.Base.Classes;

public class GroupCue : ICue
{
    public int Id { get; }
    public string Name { get; set; }
    public string Command { get; set; }
    public string CueNum { get; set; }
    public Node ShellBar { get; set; }
}