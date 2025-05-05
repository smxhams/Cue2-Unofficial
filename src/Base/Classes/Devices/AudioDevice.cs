using Godot;

namespace Cue2.Base.Classes.Devices;

public class AudioDevice : IDevice
{
    private static int _nextId = 0;
    public int DeviceId { get; }
    public string Name { get; set; }
    
    public int Channels { get; set; } = -1;
    public int SampleRate { get; set; } = -1;
    public int BitDepth { get; set; } = -1;
    public float VolumeLevel { get; set; } = -1;
    
    public string VLCIdentifier { get; set; }

    public AudioDevice(string name, int channels, string vlcId, int forcedId = -1)
    {
        if (forcedId != -1)
        {
            // If Id provided, will set using that. For example, loading from a save.
            DeviceId = forcedId;
            if (forcedId >= _nextId)
            {
                // Set next ID to be highest ID, to avoid ID conflict.
                _nextId = forcedId + 1;
            }

        }
        else
        {
            DeviceId = _nextId++;
        }
        Name = name;
        Channels = channels;
        VLCIdentifier = vlcId;
        

    }
    
    public override string ToString()
    {
        return $"Device: {Name}\n" +
               $"ID: {DeviceId}\n" +
               $"Channels: {Channels}\n" +
               $"Sample Rate: {SampleRate} Hz\n" +
               $"Bit Depth: {BitDepth}-bit\n" +
               $"Volume: {VolumeLevel * 100:F1}%\n" +
               $"VLC Identifier: {VLCIdentifier}";
    }
    
}