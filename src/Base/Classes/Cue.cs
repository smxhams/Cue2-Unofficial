using Godot;

namespace Cue2.Base.Classes;

public class Cue : ICue
{
    private static int _nextId = 0;
    public int Id { get; set; }
    public string Name { get; set; }
    public string Command { get; set; }
    public string CueNum { get; set; }
    
    public Node ShellBar { get; set; }

    // Maybe work out a way to remove below in future
    public string FilePath { get; set; }
    
    public string Type { get; set; }
    
    public Cue() // Cue Constructor
    {
        Id = _nextId++;
        Name = "New cue number " + Id.ToString();
        CueNum = Id.ToString();
        Command = "";
        
    }
}