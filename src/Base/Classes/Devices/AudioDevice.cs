using Godot;

namespace Cue2.Base.Classes.Devices;

public class AudioDevice : IDevice
{
    private static int _nextId = 0;
    public int DeviceId { get; }
    public string Name { get; set; }
    
    public string VLCIdentifier { get; set; }

    public AudioDevice()
    {
        DeviceId = _nextId++;
    }
}