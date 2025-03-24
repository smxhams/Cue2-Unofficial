using System.Collections.Generic;
using System.Linq;
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

    private AudioDevice CreateAudioDevice(string deviceName, int deviceId = -1)
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
            AudioDevice newDevice = AudioDeviceHelper.GetAudioDevice(deviceName, device.Value.DeviceIdentifier, deviceId);
            if (newDevice != null) {AudioDevices.Add(newDevice.DeviceId, newDevice);}

            GD.Print(newDevice.ToString());
            return newDevice;

        }

        return null;
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

    public AudioDevice GetAudioDeviceFromId(int deviceId)
    {
        return AudioDevices[deviceId];
    }

    public AudioDevice EnableAudioDevice(string deviceName)
    {
        GD.Print(AudioDevices.Count);
        // Returns audio device, first if it already exists, if not it'll create one and return that.
        return AudioDevices.Values.FirstOrDefault(obj => obj.Name == deviceName) ?? CreateAudioDevice(deviceName);

    }

    public void DisableAudioDevice(int deviceId)
    {
        var deviceName = AudioDevices[deviceId].Name;
        AudioDevices.Remove(deviceId);
        GD.Print("Audio device: " + deviceName + " from list of enabled audio devices");
    }

    public void ResetAudioDevices()
    {
        AudioDevices.Clear();
    }
    
    public void AddAudioDeviceWithId(int deviceId, string deviceName)
    {
        CreateAudioDevice(deviceName, deviceId);
    }

}