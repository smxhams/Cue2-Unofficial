using Godot;
using Godot.Collections;

namespace Cue2.Base.Classes.CueTypes;

public class VideoComponent : ICueComponent
{
    public string Type => "Video";
    public string VideoFile { get; set; }
    public double StartTime { get; set; } = 0.0; // In seconds
    public double EndTime { get; set; } = -1.0; // -1 means play until end of cue
    public int TargetLayerId { get; set; } = 0; // ID of the target layer to render on
    public bool HasAudio { get; set; }
    public AudioComponent EmbeddedAudio { get; set; }

    /// <summary>
    /// Duration is length of video between start and endtime
    /// </summary>
    public double Duration { get; set; } = 0.0;

    /// <summary>
    /// TotalDuration is time the video plays including playcount. ((Endtime-Starttime) * playcount)
    /// </summary>
    /// <value>Returns -1 if looping enabled</value>
    public double TotalDuration { get; set; } = 0.0;
    public double Volume { get; set; } = 1.0f;
    public bool Loop { get; set; } = false;
    public int PlayCount { get; set; } = 1;

    public double FadeInDuration { get; set; } = 0.0; // In seconds
    public double FadeOutDuration { get; set; } = 0.0; // In seconds

    /// <summary>
    /// Full metadata from file (duration, width, height, frame rate, codec, format).
    /// Set via inspector on load; used for UI/display and playback routing.
    /// </summary>
    public VideoFileMetadata Metadata { get; set; } = null;
    
    
    public Godot.Collections.Dictionary GetData()
    {
        var data = new Dictionary();
        data.Add("VideoFile", VideoFile);
        data.Add("StartTime", StartTime);
        data.Add("EndTime", EndTime);
        data.Add("TargetLayerId", TargetLayerId);
        data.Add("Duration", Duration);
        data.Add("Loop", Loop);
        data.Add("Volume", Volume);
        data.Add("PlayCount", PlayCount);
        data.Add("FadeInDuration", FadeInDuration);
        data.Add("FadeOutDuration", FadeOutDuration);
        data.Add("HasAudio", HasAudio);
        if (HasAudio && EmbeddedAudio != null)
        {
            data.Add("EmbeddedAudio", EmbeddedAudio.GetData());
        }

        if (Metadata != null)
        {
            var metaDict = new Dictionary();
            metaDict.Add("Duration", Metadata.Duration);
            metaDict.Add("Width", Metadata.Width);
            metaDict.Add("Height", Metadata.Height);
            metaDict.Add("FrameRate", Metadata.FrameRate);
            metaDict.Add("Codec", Metadata.Codec);
            metaDict.Add("Format", Metadata.Format);
            data.Add("Metadata", metaDict);
        }

        return data;
    }

    public void LoadFromData(Godot.Collections.Dictionary data)
    {
        if (!data.ContainsKey("VideoFile"))
        {
            GD.PrintErr("VideoComponent:LoadFromData - Missing 'VideoFile' key.");
            return;
        }
        VideoFile = (string)data["VideoFile"];
        StartTime = data.ContainsKey("StartTime") ? (double)data["StartTime"] : 0.0;
        EndTime = data.ContainsKey("EndTime") ? (double)data["EndTime"] : -1.0;
        TargetLayerId = data.ContainsKey("TargetLayerId") ? (int)data["TargetLayerId"] : 0;
        Duration = data.ContainsKey("Duration") ? (double)data["Duration"] : 0.0;
        Loop = data.ContainsKey("Loop") ? (bool)data["Loop"] : false;
        Volume = data.ContainsKey("Volume") ? (float)data["Volume"] : 1.0f;
        PlayCount = data.ContainsKey("PlayCount") ? (int)data["PlayCount"] : 1;
        FadeInDuration = data.ContainsKey("FadeInDuration") ? (double)data["FadeInDuration"] : 0.0;
        FadeOutDuration = data.ContainsKey("FadeOutDuration") ? (double)data["FadeOutDuration"] : 0.0;
        HasAudio = data.ContainsKey("HasAudio") ? (bool)data["HasAudio"] : false;
        if (HasAudio && data.ContainsKey("EmbeddedAudio"))
        {
            EmbeddedAudio = new AudioComponent();
            EmbeddedAudio.LoadFromData((Dictionary)data["EmbeddedAudio"]);
        }

        if (data.ContainsKey("Metadata"))
        {
            var metaDict = (Dictionary)data["Metadata"];
            Metadata = new VideoFileMetadata();
            Metadata.Duration = metaDict.ContainsKey("Duration") ? (double)metaDict["Duration"] : 0.0;
            Metadata.Width = metaDict.ContainsKey("Width") ? (int)metaDict["Width"] : 0;
            Metadata.Height = metaDict.ContainsKey("Height") ? (int)metaDict["Height"] : 0;
            Metadata.FrameRate = metaDict.ContainsKey("FrameRate") ? (float)metaDict["FrameRate"] : 0.0f;
            Metadata.Codec = metaDict.ContainsKey("Codec") ? (string)metaDict["Codec"] : "unknown";
            Metadata.Format = metaDict.ContainsKey("Format") ? (string)metaDict["Format"] : "unknown";
            GD.Print("VideoComponent:LoadFromData - Metadata loaded from save data.");
        }
        else
        {
            GD.Print("VideoComponent:LoadFromData - No metadata in save data; will extract on next load.");
            Metadata = null;
        }
    }

    public double RecalculateDuration()
    {
        Duration = EndTime < 0 ? Metadata.Duration - StartTime
            : EndTime - StartTime;
        TotalDuration = Loop ? -1.0 : Duration * PlayCount;
        return Duration;
    }
}