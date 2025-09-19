using Godot;
using Godot.Collections;

namespace Cue2.Base.Classes.CueTypes;

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