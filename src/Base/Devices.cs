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
    
    
    private static readonly Dictionary<int, AudioDevice> AudioDevices = new Dictionary<int, AudioDevice>();
    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
    }

    public void CreateAudioDevice(string deviceName)
    {
        AudioOutputDevice? device = null;

        var vlcDevices = _globalData.Playback.GetAvailibleAudioDevices(); // Get devices availible to VLC
        
        // Match vlc device to name
        foreach (var i in vlcDevices)
        {
            if (i.Description == deviceName)
            {
                GD.Print("Selected device is: " + i.Description);
                device = i;
            }
        }

        if (device.HasValue)
        {
            // Gets system audio output device
            AudioDevice newDevice = AudioDeviceHelper.GetAudioDevice(deviceName, device.Value.DeviceIdentifier);
            if (newDevice != null) {AudioDevices.Add(newDevice.DeviceId, newDevice);}

            GD.Print(newDevice.ToString());
            
        }
        
        //GD.Print("Failed to create device");
    }

    public List<AudioDevice> GetAudioDevices()
    {
        var deviceList = new List<AudioDevice>();
        foreach (var device in AudioDevices)
        {
            deviceList.Add(device.Value);
        }
        return deviceList;
    }
    
}