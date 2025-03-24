using System;
using System.Collections;
using System.Runtime.Serialization;
using Godot;
using System.Collections.Generic;
using System.Data.Common;
using Godot.Collections;

namespace Cue2.Base.Classes;

public class Cue : ICue
{
    private static int _nextId = 0;
    public int Id { get; set; }
    public string Name { get; set; }
    public string Command { get; set; }
    public string CueNum { get; set; }
    
    public Node ShellBar { get; set; }

    public int ParentId = -1;

    public List<int> ChildCues = new List<int>();
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

    public Cue (Dictionary data) // Cue Constructor
    {
        // This is used when loading cue form file, I'm quite unhappy with it. 
        Id = data["Id"].AsInt32();
        if (Id >= _nextId) _nextId = Id + 1;
        Name = (string)data["Name"];
        CueNum = (string)data["CueNum"];
        Command = (string)data["Command"];
        FilePath = (string)data["FilePath"];
        Type = (string)data["Type"];
        ParentId = (int)data["ParentId"];
        var childArray = data["ChildCues"].AsGodotArray();
        foreach (var childInt in childArray)
        {
            ChildCues.Add(childInt.AsInt32());
        }
    }
    
    
    
    public void AddChildCue(int childId)
    {
        ChildCues.Add(childId);
    }

    public void RemoveChildCue(int childId)
    {
        ChildCues.Remove(childId);
    }

    public void SetParent(int parentId)
    {
        ParentId = parentId;
    }

    public Hashtable GetData()
    {
        var dict = new Hashtable();
        dict.Add("Id", Id.ToString());
        dict.Add("Name", Name);
        dict.Add("Command", Command);
        dict.Add("CueNum", CueNum);
        dict.Add("FilePath", FilePath);
        dict.Add("Type", Type);
        dict.Add("ParentId", ParentId.ToString());
        dict.Add("ChildCues", ChildCues);
        return dict;
        
    }
    
}