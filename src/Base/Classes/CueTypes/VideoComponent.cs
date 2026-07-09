using System;
using Godot;
using Godot.Collections;

namespace Cue2.Base.Classes.CueTypes;

public class VideoComponent : ICueComponent
{
    public string Type => "Video";
    public string VideoFile { get; set; }
    /// <summary>Start Time in seconds</summary>
    public double StartTime { get; set; } = 0.0; // In seconds
    public double EndTime { get; set; } = -1.0; // -1 means play until end of cue
    public int TargetLayerId { get; set; } = 0; // ID of the target layer to render on
    public bool HasAudio { get; set; }

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

    /// <summary>Scaled width in pixels</summary>
    public int ScaledWidth { get; set; } = 0;
    /// <summary>Scaled height in pixels</summary>
    public int ScaledHeight { get; set; } = 0;
    /// <summary>Offset X position in pixels</summary>
    public int OffsetX { get; set; } = 0;
    /// <summary>Offset Y position in pixels</summary>
    public int OffsetY { get; set; } = 0;
    /// <summary>Whether to use audio if available</summary>
    public bool UseAudio { get; set; } = true;

    /// <summary>Audio output patch</summary>
    public AudioOutputPatch Patch { get; set; } = null;
    /// <summary>Audio output patch ID</summary>
    public int PatchId { get; set; } = -1;
    /// <summary>Direct audio output device name</summary>
    public string DirectOutput { get; set; } = null;

    /// <summary>
    /// Returns true when a patch or direct output device has been assigned for embedded audio.
    /// </summary>
    public bool HasAudioOutputAssigned =>
        Patch != null || !string.IsNullOrEmpty(DirectOutput);

    /// <summary>Audio routing matrix with volumes</summary>
    public CuePatch Routing { get; set; } = null;
    /// <summary>Volume multiplier for embedded audio (0-1).</summary>
    public float AudioVolume { get; set; } = 1f;
    /// <summary>Serialised waveform data for display</summary>
    public byte[] WaveformData { get; set; } = null;

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
        data.Add("ScaledWidth", ScaledWidth);
        data.Add("ScaledHeight", ScaledHeight);
        data.Add("OffsetX", OffsetX);
        data.Add("OffsetY", OffsetY);
        data.Add("HasAudio", HasAudio);
        data.Add("UseAudio", UseAudio);
        if (Patch != null)
        {
            data.Add("PatchId", PatchId);
        }
        if (DirectOutput != null)
        {
            data.Add("DirectOutput", DirectOutput);
        }
        if (Routing != null)
        {
            data.Add("Routing", Routing.GetData());
        }
        data.Add("AudioVolume", AudioVolume);
        data.Add("WaveformData", WaveformData ?? System.Array.Empty<byte>());

        if (Metadata != null)
        {
            var metaDict = new Dictionary();
            metaDict.Add("Duration", Metadata.Duration);
            metaDict.Add("Width", Metadata.Width);
            metaDict.Add("Height", Metadata.Height);
            metaDict.Add("FrameRate", Metadata.FrameRate);
            metaDict.Add("Codec", Metadata.Codec);
            metaDict.Add("Format", Metadata.Format);
            metaDict.Add("AudioChannels", Metadata.AudioChannels);
            metaDict.Add("AudioSampleRate", Metadata.AudioSampleRate);
            metaDict.Add("AudioBitDepth", Metadata.AudioBitDepth);
            metaDict.Add("AudioCodec", Metadata.AudioCodec);
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
        ScaledWidth = data.ContainsKey("ScaledWidth") ? (int)data["ScaledWidth"] : 0;
        ScaledHeight = data.ContainsKey("ScaledHeight") ? (int)data["ScaledHeight"] : 0;
        OffsetX = data.ContainsKey("OffsetX") ? (int)data["OffsetX"] : 0;
        OffsetY = data.ContainsKey("OffsetY") ? (int)data["OffsetY"] : 0;
        HasAudio = data.ContainsKey("HasAudio") ? (bool)data["HasAudio"] : false;
        UseAudio = data.ContainsKey("UseAudio") ? (bool)data["UseAudio"] : true;
        PatchId = data.ContainsKey("PatchId") ? (int)data["PatchId"] : -1;

        WaveformData = data.ContainsKey("WaveformData") ? (byte[])data["WaveformData"] : null;
        if (data.ContainsKey("Routing"))
        {
            Routing = new CuePatch();
            Routing.LoadFromData((Dictionary)data["Routing"]);
        }
        AudioVolume = data.ContainsKey("AudioVolume") ? (float)(double)data["AudioVolume"] : 1f;

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
            Metadata.AudioChannels = metaDict.ContainsKey("AudioChannels") ? (int)metaDict["AudioChannels"] : 0;
            Metadata.AudioSampleRate = metaDict.ContainsKey("AudioSampleRate") ? (int)metaDict["AudioSampleRate"] : 0;
            Metadata.AudioBitDepth = metaDict.ContainsKey("AudioBitDepth") ? (int)metaDict["AudioBitDepth"] : 0;
            Metadata.AudioCodec = metaDict.ContainsKey("AudioCodec") ? (string)metaDict["AudioCodec"] : string.Empty;
        }
        else
        {
            GD.Print("VideoComponent:LoadFromData - No metadata in save data; will extract on next load.");
            Metadata = null;
        }
    }

    public double RecalculateDuration()
    {
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
}