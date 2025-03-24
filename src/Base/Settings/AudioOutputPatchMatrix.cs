using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Cue2.Base.Classes;
using Cue2.Base.Classes.Devices;
using Cue2.Shared;
using LibVLCSharp.Shared.Structures;

namespace Cue2.Base.Settings;

// This is attached to AudioOutputPatchMatrix scene. Instantiated by presence of a audio patch via settings window.
public partial class AudioOutputPatchMatrix : Control
{
    [Export]
    public AudioOutputPatch Patch { get; set; }
    
    [Export]
    public int PatchId { get; set; }
    
    private GlobalData _globalData;
    //[Export] private NodePath patchMatrixPath;
    
    private List<Node> _currentDeivceList = new List<Node>();
    private AudioOutputDevice[] _availableDeviceList;
    
    private PackedScene _deviceOutputPatchMatrixScene;
    private HBoxContainer _deviceContainer;
    private VBoxContainer _patchMatrix;
    private LineEdit _patchName;
    private Button _deletePatchButton;
    
    private int _deviceCount = 0;
    
    
    
    
    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        
        _deviceOutputPatchMatrixScene = GD.Load<PackedScene>("uid://cisr40jsg2jgp");
        _deviceContainer = GetNode<HBoxContainer>("%DeviceOutputsListHBoxContainer");
        
        GD.Print("Patch matrix loaded with id: " + PatchId + " and name: " + Patch.Name);
        _patchMatrix = GetNode<VBoxContainer>("%PatchMatrixContainer");
        
        _patchName = GetNode<LineEdit>("%PatchName");
        _patchName.Text = Patch.Name;
        _patchName.TextChanged += PatchNameOnTextChanged;
        
        _deletePatchButton = GetNode<Button>("%DeletePatchButton");
        _deletePatchButton.Pressed += DeletePatchButtonPressed;
        
        SyncAudioDeviceDisplays();
    }

    private void DeletePatchButtonPressed()
    {
        _globalData.Settings.DeletePatch(Patch.Id);
        QueueFree();
    }


    public int GetId() => PatchId;


    public void SyncAudioDeviceDisplays()
    {
        GD.Print("Syncing devices in audio output patch matrix");
        // For now we remove devices and start fresh while developing, in future match agaisnt info instead.
        
        var children = _deviceContainer.GetChildren();
        foreach (var child in children)
        {
            // There is a bug where the signal is emmited from use device button, which then deletes its immediately. This needs to be queuefree() once implemented not deleteing and recreating each sync.
            child.Free();
        }
        
        _availableDeviceList = _globalData.Playback.GetAvailibleAudioDevices();
        
        _currentDeivceList.Clear();
        
        // First load devices in existing use of patch.
        var keys = Patch.Channels[0].Outputs.Keys; // Note we only check one channel as every channel should have same number of outputs.
        var uniqueDevices = keys.Select(key => int.Parse(key.Split(':')[0])).Distinct().ToList();
        foreach (var i in uniqueDevices)
        {
            var usedDevice = _globalData.Devices.GetAudioDeviceFromId(i);
            var createdHeader = LoadDeviceOutputPatchHeader(usedDevice.Name, true);
            LoadEnabledDeviceChannels(usedDevice, createdHeader);
             
            
        }
        
        // Then load all availible unused devices.
        foreach (AudioOutputDevice device in _availableDeviceList)
        {
            bool alreadyExistsInList = _currentDeivceList.Any(node => node.Name == device.Description);
            if (!alreadyExistsInList)
            {
                LoadDeviceOutputPatchHeader(device.Description, false);
            }
        }

        _deviceCount = _availableDeviceList.Length;
        
        // Then build all check boxs between cue output channels and devices/devicechannels.
        BuildPatchMatrix();
    }

    private Node LoadDeviceOutputPatchHeader(string name, bool enabled)
    {
        Node instance = _deviceOutputPatchMatrixScene.Instantiate();
        instance.Set("DeviceName", name);
        _deviceContainer.AddChild(instance);
        instance.GetNode<Label>("Label").Text = name;
        _currentDeivceList.Add(instance);

        instance.Name = name;
        instance.GetChild<Label>(1).TooltipText = name;
        _currentDeivceList.Add(instance);
                
        CheckButton useDeviceButton = instance.GetNode<CheckButton>("UseDeviceButton");
        useDeviceButton.SetPressed(enabled);
        
        // Connect functions to the use device check button. 
        useDeviceButton.Toggled += (bool presssed) =>
        {
            if (presssed)
            {
                AudioDevice enabledDevice = _globalData.Devices.EnableAudioDevice(name);
                AddDeviceToPatch(enabledDevice);
                instance.Set("DeviceId", enabledDevice.DeviceId);

            }
            else
            {
                RemoveDeviceFromPatch((int)instance.Get("DeviceId"));
                _globalData.Devices.DisableAudioDevice((int)instance.Get("DeviceId"));
            }
            SyncAudioDeviceDisplays();
        };
        return instance;
    }
    private void LoadEnabledDeviceChannels(AudioDevice device, Node parent)
    {
        PackedScene deviceChannel = GD.Load<PackedScene>("uid://df7ig5m4ys00r"); // Device channel ui (DeviceOutputChannel.tscn)
        var parentIndex = parent.GetIndex();
        for (int i = 0; i < device.Channels; i++)
        {
            Node instance = deviceChannel.Instantiate();
            instance.Set("DeviceCId", device.DeviceId);
            instance.Set("DeviceChannel", i);
            _deviceContainer.AddChild(instance);
            _deviceContainer.MoveChild(instance, parentIndex + i + 1);
            instance.GetNode<Label>("Label").Text = (i+1).ToString();
        }
    }
    
    private void AddDeviceToPatch(AudioDevice device)
    {
        // Loop # of channels and # of channels the device has.
        for (int cueChannel = 0; cueChannel < Patch.Channels.Count; cueChannel++)
        {
            for (int deviceChannel = 0; deviceChannel < device.Channels; deviceChannel++)
            {
                Patch.SetChannel(cueChannel, (device.DeviceId + ":" + deviceChannel), false);
            }
        }
        _globalData.Settings.UpdatePatch(Patch);
    }


    public int GetDisplayedDeviceQuantity()
    {
        return _currentDeivceList.Count;
    }

    private void BuildPatchMatrix()
    {
        // For now remove everything and start over on each build - eventauly should build once and update
        var children = _patchMatrix.GetChildren();
        foreach (var child in children)
        {
            foreach (var cb in child.GetChildren())
            {
                cb.Free();
            }
        }
        
        PackedScene checkBoxScene = GD.Load<PackedScene>("uid://cbdaknpeq3im1"); // Checkbox
        children = _patchMatrix.GetChildren(); // Should be HboxContainers for each channel
        var devicesArray = _deviceContainer.GetChildren();
        
        // Loops all channels and availible devices and gives it a disabled checkbox.
        for (int channel = 0; channel < Patch.Channels.Count; channel++) // 0 - 6 until add/remove channel functionality added.
        {
            for (int i = 0; i < devicesArray.Count(); i++)  
            {
                Node checkBoxInstance = checkBoxScene.Instantiate<AudioMatrixCheckBox>();
                
                var hasId = devicesArray[i].Get("DeviceCId");
                var hasChannel =  devicesArray[i].Get("DeviceChannel");
                
                string idch = hasId.ToString() + ":" + ((int)hasChannel).ToString();
                
                if (idch != ":-1")
                {
                    bool existsInPatch = Patch.Channels[channel].Outputs.ContainsKey(idch);
                    if (existsInPatch)
                    {
                        //GD.Print(channel + " : " +idch + Patch.Channels[channel].Outputs[idch]);
                        checkBoxInstance.Set("Channel", channel);
                        //GD.Print("Deivce channel being set: "+ devicesArray[i].Get("DeviceChannel"));
                        checkBoxInstance.Set("DeviceChannel", devicesArray[i].Get("DeviceChannel"));
                        checkBoxInstance.Set("DeviceId", devicesArray[i].Get("DeviceCId"));
                        if (checkBoxInstance is CheckBox checkBox)
                        {
                            checkBox.Disabled = false;
                            if (Patch.Channels[channel].Outputs[idch])
                            {
                                checkBox.ButtonPressed = true;
                            }
                            checkBox.Toggled += (buttonPressed) =>
                            {
                                PatchCheckBoxToggled(checkBox, buttonPressed);
                            };
                        }
                        
                    }
                }
                
                //checkBoxInstance
                children[channel].AddChild(checkBoxInstance);
                
                //GD.Print("Made Check box for Channel #: " + channel + " and Device: " + devicesArray[i].Get("DeviceName"));
            }
        }
    }

    private void PatchCheckBoxToggled(CheckBox checkBox, bool buttonPressed)
    {
        var hasId = checkBox.Get("DeviceId");
        var hasChannel =  checkBox.Get("Channel");
        var hasDeviceChannel =  checkBox.Get("DeviceChannel");
        string idch = hasId.ToString() + ":" + ((int)hasDeviceChannel).ToString();
        Patch.SetChannel((int)hasChannel, idch, buttonPressed);
        _globalData.Settings.UpdatePatch(Patch);
        GD.Print((int)hasChannel + " : " +idch + Patch.Channels[(int)hasChannel].Outputs[idch]);
    }

    private void RemoveDeviceFromPatch(int deviceId)
    {
        for (int channel = 0; channel < Patch.Channels.Count; channel++)
        {
            var keys = Patch.Channels[channel].Outputs.Keys
                .Where(key => int.Parse(key.Split(':')[0]) == deviceId)
                .ToList();
            foreach (var key in keys)
            {
                Patch.Channels[channel].Outputs.Remove(key);
            }
        }
        _globalData.Settings.UpdatePatch(Patch);
    }

    private void _onRefreshButtonPressed()
    {
        SyncAudioDeviceDisplays();
        BuildPatchMatrix();
    }
    
    private void PatchNameOnTextChanged(string newtext)
    {
        Patch.Name = newtext;
        _globalData.Settings.UpdatePatch(Patch);
    }
    
    

    
}
