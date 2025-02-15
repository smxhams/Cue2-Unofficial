using System.Collections.Generic;
using Cue2.Base.Classes.Devices;
using Cue2.Shared;
using Godot;
using LibVLCSharp.Shared.Structures;

namespace Cue2.Base;

public partial class Devices : Node
{
    private GlobalData _globalData;

    private int Index = 0;
    
    
    private static readonly Dictionary<int, IDevice> AudioDevices = new Dictionary<int, IDevice>();
    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
    }

    public void CreateAudioDevice(string deviceName)
    {
        AudioOutputDevice? device = null;
        
        // Check requested name against availible devices
        var deviceList = _globalData.Playback.GetAvailibleAudioDevices();
        foreach (var i in deviceList)
        {
            if (i.Description == deviceName)
            {
                GD.Print("Selected device is: " + i.Description);
                device = i;
            }
        }

        GD.Print("Audio channels: " + AudioDeviceHelper.GetAudioOutputChannels());

        if (device.HasValue)
        {
            var newDevice = new AudioDevice();
            
            newDevice.Name = device.Value.Description;
            newDevice.VLCIdentifier = device.Value.DeviceIdentifier;
            AudioDevices.Add(newDevice.DeviceId, newDevice);
        }
        else GD.Print("Failed to create device");
        
        
    }

    public List<IDevice> GetAudioDevices()
    {
        var deviceList = new List<IDevice>();
        foreach (var device in AudioDevices)
        {
            deviceList.Add(device.Value);
        }
        return deviceList;
    }
    
}