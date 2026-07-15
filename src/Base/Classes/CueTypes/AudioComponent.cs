using System;
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

    /// <summary>
    /// Returns true when a patch or direct output device has been assigned for playback.
    /// </summary>
    public bool HasOutputAssigned =>
        Patch != null || !string.IsNullOrEmpty(DirectOutput);
    
    /// <summary>
    /// Start time, double in seconds. 
    /// </summary>
    public double StartTime { get; set; } = 0.0; // In seconds
    public double EndTime { get; set; } = -1.0; // -1 means play until end of cue
    public CuePatch Routing { get; set; }

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
    
    public double FadeInDuration { get; set; } = 0.0; // In seconds
    public double FadeOutDuration { get; set; } = 0.0; // In seconds

    public byte[] WaveformData { get; set; } // Serialised waveform for display
    
    /// <summary>
    /// Full metadata from file (duration, channels, sample rate, bit depth, codec, format).
    /// Set via inspector on load; used for UI/display and playback routing.
    /// </summary>
    public AudioFileMetadata Metadata { get; set; } = null;

    public Dictionary GetData()
    {
        var data = new Dictionary();
        // Prefer live Patch object id, but fall back to stored PatchId so history/save never drops routing.
        data.Add("PatchId", Patch?.Id ?? PatchId);
        data.Add("DirectOutput", DirectOutput ?? string.Empty);
        data.Add("AudioFile", AudioFile);
        data.Add("StartTime", StartTime);
        data.Add("EndTime", EndTime);
        data.Add("Duration", Duration);
        data.Add("Loop", Loop);
        data.Add("Volume", Volume);
        data.Add("PlayCount", PlayCount);
        data.Add("FadeInDuration", FadeInDuration);
        data.Add("FadeOutDuration", FadeOutDuration);
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
        // Metadata is filled asynchronously after file drop — do not NRE before it arrives
        if (Metadata == null)
        {
            Duration = 0.0;
            TotalDuration = Loop ? -1.0 : 0.0;
            return Duration;
        }

        double fileDuration = Metadata.Duration;
        if (fileDuration < 0) fileDuration = 0;

        Duration = EndTime < 0
            ? Math.Max(0, fileDuration - StartTime)
            : Math.Max(0, EndTime - StartTime);
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
        StartTime = data.ContainsKey("StartTime") ? data["StartTime"].AsDouble() : 0.0;
        EndTime = data.ContainsKey("EndTime") ? data["EndTime"].AsDouble() : -1.0;
        Duration = data.ContainsKey("Duration") ? data["Duration"].AsDouble() : 0.0;
        Loop = data.ContainsKey("Loop") ? data["Loop"].AsBool() : false;
        Volume = data.ContainsKey("Volume") ? data["Volume"].AsSingle() : 1.0f;
        PlayCount = data.ContainsKey("PlayCount") ? data["PlayCount"].AsInt32() : 1;
        FadeInDuration = data.ContainsKey("FadeInDuration") ? data["FadeInDuration"].AsDouble() : 0.0;
        FadeOutDuration = data.ContainsKey("FadeOutDuration") ? data["FadeOutDuration"].AsDouble() : 0.0;
        WaveformData = TryReadByteArray(data, "WaveformData");
        PatchId = data.ContainsKey("PatchId") ? data["PatchId"].AsInt32() : -1;
        // Runtime Patch reference is re-linked after load; clear here so a stale object cannot win.
        Patch = null;
        if (data.ContainsKey("Routing") && data["Routing"].VariantType == Variant.Type.Dictionary)
        {
            Routing = new CuePatch();
            Routing.LoadFromData((Dictionary)data["Routing"]);
        }
        else
        {
            Routing = null;
        }
        if (data.ContainsKey("DirectOutput") && data["DirectOutput"].VariantType != Variant.Type.Nil)
        {
            var direct = data["DirectOutput"].AsString();
            DirectOutput = string.IsNullOrEmpty(direct) ? null : direct;
        }
        else
        {
            DirectOutput = null;
        }
        
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
        } 
        else 
        { 
            GD.Print("AudioComponent:LoadFromData - No metadata in save data; will extract on next load.");
            Metadata = null; 
        } 
    }

    /// <summary>
    /// Reads a byte[] field that may arrive as raw bytes, PackedByteArray, or a JSON number array.
    /// </summary>
    private static byte[] TryReadByteArray(Dictionary data, string key)
    {
        if (data == null || !data.ContainsKey(key)) return null;
        try
        {
            var variant = data[key];
            if (variant.VariantType == Variant.Type.Nil) return null;
            if (variant.AsByteArray() is { Length: > 0 } packed)
                return packed;
            // Empty PackedByteArray is valid (stripped history snapshots).
            if (variant.VariantType == Variant.Type.PackedByteArray)
                return variant.AsByteArray();
            if (variant.Obj is byte[] bytes)
                return bytes;
            if (variant.VariantType == Variant.Type.Array)
            {
                var arr = variant.AsGodotArray();
                var result = new byte[arr.Count];
                for (int i = 0; i < arr.Count; i++)
                    result[i] = (byte)arr[i].AsInt32();
                return result;
            }
        }
        catch
        {
            // Leave waveform null; UI regenerates peaks from media when needed.
        }
        return null;
    }

}