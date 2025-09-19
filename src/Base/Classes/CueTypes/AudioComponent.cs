using Godot;
using Godot.Collections;

namespace Cue2.Base.Classes.CueTypes;

public class AudioComponent : ICueComponent
{
    public string Type => "Audio";
    public AudioOutputPatch Patch { get; set; }
    public int PatchId { get; set; } = -1; // This value is used to link patch whence loaded
    public string DirectOutput { get; set; }
    public string AudioFile { get; set; }
    public double StartTime { get; set; } = 0.0; // In seconds
    public double EndTime { get; set; } = -1.0; // -1 means play until end of cue

    /// <summary>
    /// Duration is length of audio between start and endtime
    /// </summary>
    public double Duration { get; set; } = 0.0;
    
    /// <summary>
    /// TotalDuration is time the audio plays including playcount. ((Endtime-Starttime) * playcount)
    /// </summary>
    /// <value>Returns -1 if looping enabled</value>
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

    public double RecalculateDuration()
    {
        Duration = EndTime < 0 ? FileDuration - StartTime 
            : EndTime - StartTime;
        TotalDuration = Duration * PlayCount;
        return Duration;
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