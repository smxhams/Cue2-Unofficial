using System;
using Godot;
using Godot.Collections;

namespace Cue2.Base.Classes.CueTypes;

/// <summary>
/// Legacy simplified display presets (pre Expand/Stretch split). Kept for save migration only.
/// </summary>
public enum VideoDisplayMode
{
    /// <summary>Letterbox — maps to IgnoreSize + KeepAspectCentered.</summary>
    Fit = 0,
    /// <summary>Cover — maps to IgnoreSize + KeepAspectCovered.</summary>
    Fill = 1,
    /// <summary>Distort to fill — maps to IgnoreSize + Scale.</summary>
    Stretch = 2,
}

public class VideoComponent : ICueComponent
{
    public string Type => "Video";
    public string VideoFile { get; set; }
    /// <summary>Start Time in seconds</summary>
    public double StartTime { get; set; } = 0.0; // In seconds
    public double EndTime { get; set; } = -1.0; // -1 means play until end of cue
    public int TargetLayerId { get; set; } = 0; // ID of the target layer to render on

    /// <summary>
    /// Godot <see cref="TextureRect.ExpandMode"/> for how the control size interacts with the texture.
    /// Default matches previous Fit/Fill/Stretch behaviour (ignore size, fill host rect).
    /// </summary>
    public TextureRect.ExpandModeEnum TextureExpandMode { get; set; } =
        TextureRect.ExpandModeEnum.IgnoreSize;

    /// <summary>
    /// Godot <see cref="TextureRect.StretchMode"/> for how the frame is drawn inside the control.
    /// Default is Keep Aspect Centered (Fit).
    /// </summary>
    public TextureRect.StretchModeEnum TextureStretchMode { get; set; } =
        TextureRect.StretchModeEnum.KeepAspectCentered;

    /// <summary>
    /// Visual opacity of the video on the target layer (0 = invisible, 1 = fully opaque).
    /// Inspector edits this as a percentage.
    /// </summary>
    public float Opacity { get; set; } = 1f;

    public bool HasAudio { get; set; }

    /// <summary>
    /// Applies TextureRect expand + stretch modes for layer display.
    /// Host should be the layer rectangle with <c>ClipContents = true</c> for covered modes.
    /// </summary>
    public static void ApplyTextureLayout(
        TextureRect rect,
        TextureRect.ExpandModeEnum expandMode,
        TextureRect.StretchModeEnum stretchMode)
    {
        if (rect == null || !GodotObject.IsInstanceValid(rect))
            return;

        rect.ExpandMode = expandMode;
        rect.StretchMode = stretchMode;

        // When ignoring size, fill the layer host so stretch modes have a defined rect.
        if (expandMode == TextureRect.ExpandModeEnum.IgnoreSize
            && rect.GetParent() is Control parent
            && parent.Size.X > 0 && parent.Size.Y > 0)
        {
            rect.Position = Vector2.Zero;
            rect.Size = parent.Size;
        }
    }

    /// <summary>
    /// Convenience: apply this component's expand/stretch settings to a TextureRect.
    /// </summary>
    public void ApplyTextureLayout(TextureRect rect)
    {
        ApplyTextureLayout(rect, TextureExpandMode, TextureStretchMode);
    }

    /// <summary>
    /// Maps legacy Fit/Fill/Stretch presets onto Expand + Stretch modes.
    /// </summary>
    public static void ApplyLegacyDisplayMode(
        out TextureRect.ExpandModeEnum expand,
        out TextureRect.StretchModeEnum stretch,
        VideoDisplayMode legacy)
    {
        expand = TextureRect.ExpandModeEnum.IgnoreSize;
        stretch = legacy switch
        {
            VideoDisplayMode.Fill => TextureRect.StretchModeEnum.KeepAspectCovered,
            VideoDisplayMode.Stretch => TextureRect.StretchModeEnum.Scale,
            _ => TextureRect.StretchModeEnum.KeepAspectCentered,
        };
    }

    /// <summary>
    /// Parses an int-like Variant to an enum value of type <typeparamref name="TEnum"/>.
    /// </summary>
    public static TEnum ParseEnumVariant<TEnum>(Variant value, TEnum fallback) where TEnum : struct, Enum
    {
        try
        {
            int modeVal = value.VariantType switch
            {
                Variant.Type.Int => (int)value,
                Variant.Type.Float => (int)(double)value,
                Variant.Type.String => int.TryParse((string)value, out int parsed) ? parsed : Convert.ToInt32(fallback),
                _ => (int)value
            };
            return Enum.IsDefined(typeof(TEnum), modeVal) ? (TEnum)(object)modeVal : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// Loads expand/stretch modes, migrating legacy DisplayMode when present.
    /// </summary>
    private void LoadTextureLayoutFromData(Dictionary data)
    {
        bool hasExpand = data.ContainsKey("TextureExpandMode");
        bool hasStretch = data.ContainsKey("TextureStretchMode");

        if (hasExpand || hasStretch)
        {
            TextureExpandMode = hasExpand
                ? ParseEnumVariant(data["TextureExpandMode"], TextureRect.ExpandModeEnum.IgnoreSize)
                : TextureRect.ExpandModeEnum.IgnoreSize;
            TextureStretchMode = hasStretch
                ? ParseEnumVariant(data["TextureStretchMode"], TextureRect.StretchModeEnum.KeepAspectCentered)
                : TextureRect.StretchModeEnum.KeepAspectCentered;
            return;
        }

        // Migrate pre-expand/stretch saves that only had DisplayMode (Fit/Fill/Stretch).
        if (data.ContainsKey("DisplayMode"))
        {
            var legacy = ParseEnumVariant(data["DisplayMode"], VideoDisplayMode.Fit);
            ApplyLegacyDisplayMode(out var expand, out var stretch, legacy);
            TextureExpandMode = expand;
            TextureStretchMode = stretch;
            return;
        }

        TextureExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        TextureStretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
    }

    /// <summary>
    /// Parses opacity from save data, clamped to 0–1.
    /// </summary>
    public static float ParseOpacity(Variant value)
    {
        try
        {
            float v = value.VariantType switch
            {
                Variant.Type.Float => (float)(double)value,
                Variant.Type.Int => (int)value,
                Variant.Type.String => float.TryParse((string)value, out float p) ? p : 1f,
                _ => (float)(double)value
            };
            // Guard against accidental percentage storage (e.g. 50 meaning 50%)
            if (v > 1f)
                v /= 100f;
            return Mathf.Clamp(v, 0f, 1f);
        }
        catch
        {
            return 1f;
        }
    }

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
        data.Add("TextureExpandMode", (int)TextureExpandMode);
        data.Add("TextureStretchMode", (int)TextureStretchMode);
        data.Add("Opacity", Opacity);
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
        data.Add("PatchId", Patch?.Id ?? PatchId); // Reference patch by ID; fall back to stored PatchId
        data.Add("DirectOutput", DirectOutput ?? string.Empty);
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
        StartTime = data.ContainsKey("StartTime") ? data["StartTime"].AsDouble() : 0.0;
        EndTime = data.ContainsKey("EndTime") ? data["EndTime"].AsDouble() : -1.0;
        TargetLayerId = data.ContainsKey("TargetLayerId") ? data["TargetLayerId"].AsInt32() : 0;
        LoadTextureLayoutFromData(data);
        Opacity = data.ContainsKey("Opacity") ? ParseOpacity(data["Opacity"]) : 1f;
        Duration = data.ContainsKey("Duration") ? data["Duration"].AsDouble() : 0.0;
        Loop = data.ContainsKey("Loop") ? data["Loop"].AsBool() : false;
        Volume = data.ContainsKey("Volume") ? data["Volume"].AsSingle() : 1.0f;
        PlayCount = data.ContainsKey("PlayCount") ? data["PlayCount"].AsInt32() : 1;
        FadeInDuration = data.ContainsKey("FadeInDuration") ? data["FadeInDuration"].AsDouble() : 0.0;
        FadeOutDuration = data.ContainsKey("FadeOutDuration") ? data["FadeOutDuration"].AsDouble() : 0.0;
        ScaledWidth = data.ContainsKey("ScaledWidth") ? data["ScaledWidth"].AsInt32() : 0;
        ScaledHeight = data.ContainsKey("ScaledHeight") ? data["ScaledHeight"].AsInt32() : 0;
        OffsetX = data.ContainsKey("OffsetX") ? data["OffsetX"].AsInt32() : 0;
        OffsetY = data.ContainsKey("OffsetY") ? data["OffsetY"].AsInt32() : 0;
        HasAudio = data.ContainsKey("HasAudio") ? data["HasAudio"].AsBool() : false;
        UseAudio = data.ContainsKey("UseAudio") ? data["UseAudio"].AsBool() : true;
        PatchId = data.ContainsKey("PatchId") ? data["PatchId"].AsInt32() : -1;
        DirectOutput = data.ContainsKey("DirectOutput") ? data["DirectOutput"].AsString() : null;

        WaveformData = TryReadByteArray(data, "WaveformData");
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

    /// <summary>
    /// Reads a byte[] field that may arrive as raw bytes, PackedByteArray, or a JSON number array.
    /// </summary>
    private static byte[] TryReadByteArray(Godot.Collections.Dictionary data, string key)
    {
        if (data == null || !data.ContainsKey(key)) return null;
        try
        {
            var variant = data[key];
            if (variant.VariantType == Variant.Type.Nil) return null;
            if (variant.AsByteArray() is { Length: > 0 } packed)
                return packed;
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