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
    public double Volume { get; set; } = 1.0f;
    public bool Loop { get; set; } = false;
    public int PlayCount { get; set; } = 1;
    

    public CuePatch Routing { get; set; }

    public byte[] WaveformData { get; set; } // Serialised waveform for display
    
    /// <summary>
    /// Full metadata from file (duration, channels, sample rate, bit depth, codec, format).
    /// Set via inspector on load; used for UI/display and playback routing.
    /// </summary>
    public AudioFileMetadata Metadata { get; set; } = null;

    public Dictionary GetData()
    {
        var data = new Dictionary();
        data.Add("PatchId", Patch?.Id ?? -1); // Reference patch by ID
        data.Add("DirectOutput", DirectOutput);
        data.Add("AudioFile", AudioFile);
        data.Add("StartTime", StartTime);
        data.Add("EndTime", EndTime);
        data.Add("Duration", Duration);
        data.Add("Loop", Loop);
        data.Add("Volume", Volume);
        data.Add("PlayCount", PlayCount);
        if (Routing != null)
        {
            data.Add("Routing", Routing.GetData());
        }
        data.Add("WaveformData", WaveformData ?? System.Array.Empty<byte>());
        

        if (Metadata != null) 
        { 
            var metaDict = new Dictionary(); 
            metaDict.Add("Duration", Metadata.Duration); 
            metaDict.Add("Channels", Metadata.Channels); 
            metaDict.Add("SampleRate", Metadata.SampleRate); 
            metaDict.Add("BitDepth", Metadata.BitDepth); 
            metaDict.Add("Codec", Metadata.Codec); 
            metaDict.Add("Format", Metadata.Format); 
            data.Add("Metadata", metaDict); 
        }
        
        
        return data;
    }

    public double RecalculateDuration()
    {
        Duration = EndTime < 0 ? Metadata.Duration - StartTime 
            : EndTime - StartTime;
        TotalDuration = Loop ? -1.0 : Duration * PlayCount;
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
        Loop = data.ContainsKey("Loop") ? (bool)data["Loop"] : false;
        Volume = data.ContainsKey("Volume") ? (float)data["Volume"] : 1.0f;
        PlayCount = data.ContainsKey("PlayCount") ? (int)data["PlayCount"] : 1;
        WaveformData = data.ContainsKey("WaveformData") ? (byte[])data["WaveformData"] : null;
        PatchId = data.ContainsKey("PatchId") ? (int)data["PatchId"] : -1;
        if (data.ContainsKey("Routing"))
        {
            Routing = new CuePatch();
            Routing.LoadFromData((Dictionary)data["Routing"]);
        }
        DirectOutput = data.ContainsKey("DirectOutput") ? (string)data["DirectOutput"] : null;
        
        if (data.ContainsKey("Metadata")) 
        { 
            var metaDict = (Dictionary)data["Metadata"]; 
            Metadata = new AudioFileMetadata(); 
            Metadata.Duration = metaDict.ContainsKey("Duration") ? (double)metaDict["Duration"] : 0.0; 
            Metadata.Channels = metaDict.ContainsKey("Channels") ? (int)metaDict["Channels"] : 0; 
            Metadata.SampleRate = metaDict.ContainsKey("SampleRate") ? (int)metaDict["SampleRate"] : 0; 
            Metadata.BitDepth = metaDict.ContainsKey("BitDepth") ? (int)metaDict["BitDepth"] : 0; 
            Metadata.Codec = metaDict.ContainsKey("Codec") ? (string)metaDict["Codec"] : "unknown"; 
            Metadata.Format = metaDict.ContainsKey("Format") ? (string)metaDict["Format"] : "unknown"; 
            // Sync legacy fields from metadata (for backward compat) 
            GD.Print("AudioComponent:LoadFromData - Metadata loaded from save data.");
        } 
        else 
        { 
            GD.Print("AudioComponent:LoadFromData - No metadata in save data; will extract on next load.");
            Metadata = null; 
        } 
    }

}