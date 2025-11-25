using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SDL3;

namespace Cue2.Base.Classes;

public interface IAudioPlayback
{
    /// <summary>Audio output patch</summary>
    public AudioOutputPatch Patch { get; set; }

    /// <summary>Direct audio output device name</summary>
    public string DirectOutput { get; set; }

    /// <summary>Audio routing matrix with volumes</summary>
    public CuePatch Routing { get; set; }
    
    public Dictionary<uint, IntPtr> DeviceStreams { get; set; }
    public int SourceChannels { get; set; }
    public int SourceSampleRate { get; set; }
    public int SourceBytesPerFrame { get; set; }
    public SDL.AudioFormat SourceFormat { get; set; }
}