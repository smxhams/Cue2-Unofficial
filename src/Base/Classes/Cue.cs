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
    public string AudioFile { get; set; }
    public double StartTime { get; set; } = 0.0; // In seconds
    public double EndTime { get; set; } = -1.0; // -1 means play until end of cue

    public double Duration { get; set; } = 0.0;
    public double FileDuration { get; set; } = 0.0;
    public double Volume { get; set; } = 1.0f;
    public bool Loop { get; set; } = false;
    public int PlayCount { get; set; } = 1;
    public byte[] WaveformData { get; set; } // Serialised waveform for display

    public Dictionary GetData()
    {
        var data = new Dictionary();
        data.Add("PatchId", Patch?.Id ?? -1); // Reference patch by ID
        data.Add("AudioFile", AudioFile);
        data.Add("StartTime", StartTime);
        data.Add("EndTime", EndTime);
        data.Add("Duration", Duration);
        data.Add("FileDuration", FileDuration);
        data.Add("Loop", Loop);
        data.Add("Volume", Volume);
        data.Add("PlayCount", PlayCount);
        data.Add("WaveformData", WaveformData);
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
    
    
    public void ExtractAudioIfPresent(string filePath, GlobalSignals globalSignals)
    {
        try
        {
            using var libVLC = new LibVLCSharp.Shared.LibVLC();
            using var media = new LibVLCSharp.Shared.Media(libVLC, filePath);
            media.Parse(); // Parse media metadata

            if (media.Tracks.Any(t => t.TrackType == TrackType.Audio))
            {
                HasAudio = true;
                EmbeddedAudio = new AudioComponent
                {
                    AudioFile = filePath // Reuse video file for audio extraction
                    // Inherit other defaults or analyze further if needed
                };
                globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Audio track detected in video: {filePath}", 0);
            }
            else
            {
                HasAudio = false;
                globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"No audio in video: {filePath}", 0);
            }
        }
        catch (Exception ex)
        {
            globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Error checking video audio: {ex.Message}", 2);
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
        var audioComp = new AudioComponent { AudioFile = audioFile, Patch = patch };
        Components.Add(audioComp);
        return audioComp;
    }

    public void AddVideoComponent(string videoFile, GlobalSignals globalSignals)
    {
        var videoComp = new VideoComponent { VideoFile = videoFile };
        videoComp.ExtractAudioIfPresent(videoFile, globalSignals);
        Components.Add(videoComp);
    }

    public void AddNetworkComponent(/* params */)
    {
        var netComp = new NetworkComponent { /* init */ };
        Components.Add(netComp);
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
