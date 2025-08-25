using System;
using System.Collections;
using System.Runtime.Serialization;
using Godot;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Cue2.Shared;
using Godot.Collections;
using LibVLCSharp.Shared;
using Array = Godot.Collections.Array;

namespace Cue2.Base.Classes;



public interface ICueComponent
{
    string Type { get; }
    Dictionary GetData();
    void LoadFromData(Dictionary data);
}

public class AudioComponent : ICueComponent
{
    public string Type => "Audio";
    public AudioOutputPatch Patch { get; set; }
    public int PatchId { get; set; } = -1; // This value is used to link patch whence loaded
    public String DirectOutput { get; set; }
    public string AudioFile { get; set; }
    public double StartTime { get; set; } = 0.0; // In seconds
    public double EndTime { get; set; } = -1.0; // -1 means play until end of cue

    public double Duration { get; set; } = 0.0;
    
    public double TotalDuration { get; set; } = 0.0;
    public double FileDuration { get; set; } = 0.0;
    public double Volume { get; set; } = 1.0f;
    public bool Loop { get; set; } = false;
    public int PlayCount { get; set; } = 1;

    public int ChannelCount { get; set; } = 2; // Default stereo, set from metadata

    public CuePatch Routing { get; set; }

    public byte[] WaveformData { get; set; } // Serialised waveform for display
    

    public Dictionary GetData()
    {
        var data = new Dictionary();
        data.Add("PatchId", Patch?.Id ?? -1); // Reference patch by ID
        data.Add("DirectOutput", DirectOutput);
        data.Add("AudioFile", AudioFile);
        data.Add("StartTime", StartTime);
        data.Add("EndTime", EndTime);
        data.Add("Duration", Duration);
        data.Add("FileDuration", FileDuration);
        data.Add("Loop", Loop);
        data.Add("Volume", Volume);
        data.Add("PlayCount", PlayCount);
        data.Add("ChannelCount", ChannelCount);
        if (Routing != null)
        {
            data.Add("Routing", Routing.GetData());
        }
        data.Add("WaveformData", WaveformData ?? System.Array.Empty<byte>());
        
        return data;
    }

    public void LoadFromData(Dictionary data)
    {
        if (!data.ContainsKey("AudioFile")) 
        {
            GD.PrintErr("AudioComponent:LoadFromData - Missing 'AudioFile' key.");
            return;
        }
        AudioFile = (string)data["AudioFile"];
        StartTime = data.ContainsKey("StartTime") ? (double)data["StartTime"] : 0.0;
        EndTime = data.ContainsKey("EndTime") ? (double)data["EndTime"] : -1.0;
        Duration = data.ContainsKey("Duration") ? (double)data["Duration"] : 0.0;
        FileDuration = data.ContainsKey("FileDuration") ? (double)data["FileDuration"] : 0.0;
        Loop = data.ContainsKey("Loop") ? (bool)data["Loop"] : false;
        Volume = data.ContainsKey("Volume") ? (float)data["Volume"] : 1.0f;
        PlayCount = data.ContainsKey("PlayCount") ? (int)data["PlayCount"] : 1;
        WaveformData = data.ContainsKey("WaveformData") ? (byte[])data["WaveformData"] : null;
        PatchId = data.ContainsKey("PatchId") ? (int)data["PatchId"] : -1;
        ChannelCount = data.ContainsKey("ChannelCount") ? (int)data["ChannelCount"] : 2;
        if (data.ContainsKey("Routing"))
        {
            Routing = new CuePatch();
            Routing.LoadFromData((Dictionary)data["Routing"]);
        }
        DirectOutput = data.ContainsKey("DirectOutput") ? (string)data["DirectOutput"] : null;
    }

}

public class VideoComponent : ICueComponent
{
    public string Type => "Video";
    public string VideoFile { get; set; }
    public double StartTime { get; set; } = 0.0; // In seconds
    public double EndTime { get; set; } = -1.0; // -1 means play until end of cue
    public bool HasAudio { get; set; }
    public AudioComponent EmbeddedAudio { get; set; }
    
    
    public Dictionary GetData()
    {
        var data = new Dictionary();
        data.Add("VideoFile", VideoFile);
        data.Add("StartTime", StartTime);
        data.Add("EndTime", EndTime);
        data.Add("HasAudio", HasAudio);
        if (HasAudio && EmbeddedAudio != null)
        {
            data.Add("EmbeddedAudio", EmbeddedAudio.GetData());
        }
        return data;
    }

    public void LoadFromData(Dictionary data)
    {
        if (!data.ContainsKey("VideoFile")) 
        {
            GD.PrintErr("VideoComponent:LoadFromData - Missing 'VideoFile' key.");
            return;
        }
        VideoFile = (string)data["VideoFile"];
        StartTime = data.ContainsKey("StartTime") ? (double)data["StartTime"] : 0.0;
        EndTime = data.ContainsKey("EndTime") ? (double)data["EndTime"] : -1.0;
        HasAudio = (bool)data["HasAudio"];
        if (HasAudio && data.ContainsKey("EmbeddedAudio"))
        {
            EmbeddedAudio = new AudioComponent();
            EmbeddedAudio.LoadFromData((Dictionary)data["EmbeddedAudio"]);
        }
    }
    
    
}


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

/// <summary>
/// Enum for cue follow types
/// </summary>
public enum FollowType
{
    None,
    Continue, // Continue will tell the next cue in cuelist to trigger when post-wait has elapsed. 
    Follow // Follow will tell the next cue in cuelist to trigger at the same time
}

public class Cue : ICue
{
    private static int _nextId = 0;
    public int Id { get; set; }
    public string Name { get; set; }
    public string Command { get; set; }
    public string CueNum { get; set; }
    
    public Node ShellBar { get; set; }

    public int ParentId = -1;

    public List<int> ChildCues = new List<int>(); // List of child cue ID's
    
    public double PreWait { get; set; } = 0.0;
    public double Duration { get; set; } = 0.0; // Duration of cue's contents excluding pre/post wait. This includes any child cues.
    public double TotalDuration { get; set; } = 0.0;
    public double PostWait { get; set; } = 0.0;
    public FollowType Follow = FollowType.None;

    
    public List<ICueComponent> Components = new List<ICueComponent>();
    
    public Cue() // // Default constructor for base cue
    {
        Id = _nextId++;
        Name = "New cue number " + Id.ToString();
        CueNum = Id.ToString();
        Command = "";
    }

    public Cue(Dictionary data) // Load from saved data - Using full namespace //!!!
    {
        if (!data.ContainsKey("Id"))
        {
            GD.PrintErr("Cue:Constructor - Missing 'Id' key in data.");
            return;
        }
        Id = data["Id"].AsInt32();
        if (Id >= _nextId) _nextId = Id + 1;
        Name = data.ContainsKey("Name") ? (string)data["Name"] : "Unnamed Cue";
        CueNum = data.ContainsKey("CueNum") ? (string)data["CueNum"] : Id.ToString();
        Command = data.ContainsKey("Command") ? (string)data["Command"] : "";
        ParentId = data.ContainsKey("ParentId") ? (int)data["ParentId"] : -1;
        if (data.ContainsKey("ChildCues"))
        {
            var childArray = data["ChildCues"].AsGodotArray();
            foreach (var childInt in childArray)
            {
                ChildCues.Add(childInt.AsInt32());
            }
        }
        PreWait = data.ContainsKey("PreWait") ? (double)data["PreWait"] : 0.0;
        Duration = data.ContainsKey("Duration") ? (double)data["Duration"] : 0.0;
        TotalDuration = data.ContainsKey("TotalDuration") ? (double)data["TotalDuration"] : 0.0;
        PostWait = data.ContainsKey("PostWait") ? (double)data["PostWait"] : 0.0;
        Follow = data.ContainsKey("Follow") ? (FollowType)(int)data["Follow"] : FollowType.None;
        
        if (data.ContainsKey("Components"))
        {
            var compData = data["Components"].AsGodotArray();
            foreach (var compVar in compData)
            {
                if (compVar.VariantType != Variant.Type.Dictionary)
                {
                    GD.PrintErr("Cue:Constructor - Component data is not a dictionary.");
                    continue;
                }
                var compHash = compVar.AsGodotDictionary();
                if (!compHash.ContainsKey("Type"))
                {
                    GD.PrintErr("Cue:Constructor - Missing 'Type' in component data.");
                    continue;
                }
                string type = (string)compHash["Type"];
                ICueComponent comp = type switch
                {
                    "Audio" => new AudioComponent(),
                    "Video" => new VideoComponent(),
                    "Network" => new NetworkComponent(),
                    _ => null
                };
                if (comp != null)
                {
                    try
                    {
                        comp.LoadFromData(compHash);
                        Components.Add(comp);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"Cue:Constructor - Error loading component '{type}': {ex.Message}");
                    }
                }
                else
                {
                    GD.PrintErr($"Cue:Constructor - Unknown component type '{type}'.");
                }
            }
        }
    }
    
    // Methods to add components dynamically
    public AudioComponent AddAudioComponent(string audioFile, AudioOutputPatch patch = null)
    {
        if (Components.FirstOrDefault(c => c.Type == "Audio") is AudioComponent existing)
        {
            GD.Print($"Cue:AddAudioComponent - Audio component already exists in cue {Id}. Returning existing.");
            return existing;
        }
        var audioComp = new AudioComponent { AudioFile = audioFile, Patch = patch };
        Components.Add(audioComp);
        return audioComp;
    }
    
    public AudioComponent GetAudioComponent()
    {
        return Components.FirstOrDefault(c => c.Type == "Audio") as AudioComponent;
    }

    public VideoComponent AddVideoComponent(string videoFile, GlobalSignals globalSignals)
    {
        if (Components.FirstOrDefault(c => c.Type == "Video") is VideoComponent existing)
        {
            GD.Print($"Cue:AddVideoComponent - Video component already exists in cue {Id}. Returning existing.");
            return existing;
        }
        var videoComp = new VideoComponent { VideoFile = videoFile };
        //videoComp.ExtractAudioIfPresent(videoFile, globalSignals);
        Components.Add(videoComp);
        return videoComp;
    }

    public void AddNetworkComponent(/* params */)
    {
        var netComp = new NetworkComponent { /* init */ };
        Components.Add(netComp);
    }

    public double CalculateTotalDuration()
    {
        var contentsDuration = 0.0;
        foreach (var comp in Components)
        {
            if (comp.Type == "Audio")
            {
                if (contentsDuration < ((AudioComponent)comp).Duration) contentsDuration = ((AudioComponent)comp).Duration;
            }
            else if (comp.Type == "Video")
            {
                //contentsDuration = ((VideoComponent)comp).Duration;
            }
        }
        var childDuration = DurationOfChildren();
        if (childDuration > contentsDuration) contentsDuration = childDuration;
        Duration = contentsDuration;
        TotalDuration = PreWait + contentsDuration + PostWait;
        return TotalDuration;
    }

    private double DurationOfChildren()
    {
        var longestDuration = 0.0;
        foreach (var childId in ChildCues)
        {
            var childCue = CueList.FetchCueFromId(childId);
            if (childCue != null)
            {
                var childDuration = childCue.CalculateTotalDuration();
                if (childDuration > longestDuration) longestDuration = childDuration;
            }
        }
        return longestDuration;
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

    public Dictionary GetData()
    {
        var dict = new Dictionary();
        dict.Add("Id", Id.ToString());
        dict.Add("Name", Name);
        dict.Add("Command", Command);
        dict.Add("CueNum", CueNum);
        dict.Add("ParentId", ParentId.ToString());
        dict.Add("ChildCues", new Array<int>(ChildCues));
        dict.Add("PreWait", PreWait);
        dict.Add("Duration", Duration);
        dict.Add("TotalDuration", TotalDuration);
        dict.Add("PostWait", PostWait);
        dict.Add("Follow", (int)Follow);

        var compData = new Array();
        foreach (var comp in Components)
        {
            var compDict = comp.GetData();
            compDict.Add("Type", comp.Type);
            compData.Add(compDict);
        }
        dict.Add("Components", compData);

        return dict;
    }
    
}
