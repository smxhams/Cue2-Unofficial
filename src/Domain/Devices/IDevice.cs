namespace Cue2.Domain.Devices;

public interface IDevice
{
    int DeviceId { get; }
    string Name { get; set; }
    
}