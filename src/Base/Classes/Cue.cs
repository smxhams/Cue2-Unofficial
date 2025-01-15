using System;
using System.Runtime.Serialization;
using Godot;
using System.Collections.Generic;
using System.Data.Common;

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

    public Cue (Dictionary<string, string> data) // Cue Constructor
    {
        // This is used when loading cue form file, I'm quite unhappy with it. 
        Id = int.Parse(data.GetValueOrDefault("Id", "0"));
        Name = data.GetValueOrDefault("Name", "New cue number " + Id.ToString());
        CueNum = data.GetValueOrDefault("CueNum", Id.ToString());
        Command = data.GetValueOrDefault("Command", "");
        FilePath = data.GetValueOrDefault("FilePath", "");
        Type = data.GetValueOrDefault("Type", "");
    }
    

    public Dictionary<string, string> GetData()
    {
        var dict = new Dictionary<string, string>();
        dict.Add("Id", Id.ToString());
        dict.Add("Name", Name);
        dict.Add("Command", Command);
        dict.Add("CueNum", CueNum);
        dict.Add("FilePath", FilePath);
        dict.Add("Type", Type);
        return dict;
        
    }
}