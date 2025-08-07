using Godot;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Cue2.Base.Classes;
using Cue2.Base.Classes.Devices;
using Cue2.Shared;
using LibVLCSharp.Shared.Structures;

namespace Cue2.Base.Settings;

// This is attached to AudioOutputPatchMatrix scene. Instantiated by presence of a audio patch via settings window.
public partial class AudioOutputPatchMatrix : Control
{
    [Export]
    public AudioOutputPatch Patch { get; set; } // This is set in SettingsAudioOutputPatch when created. 
    
    [Export]
    public int PatchId { get; set; }
    
    private GlobalData _globalData;
    private GlobalSignals _globalSignals; 
    //[Export] private NodePath patchMatrixPath;
    
    private List<Node> _currentDeivceList = new List<Node>();
    private List<string> _availableDeviceList;
    
    private PackedScene _deviceHeaderScene;
    private HBoxContainer _deviceContainer;
    private VBoxContainer _channelList;
    private VBoxContainer _patchMatrix;
    private LineEdit _patchName;
    private Button _deletePatchButton;
    private Button _addChannelButton;
    
    private int _deviceCount = 0;
    
    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals"); // Global
        
        // This is "PatchMatrixDeviceHeader" header
        _deviceHeaderScene = SceneLoader.LoadPackedScene("uid://cisr40jsg2jgp", out string error);
        
        _deviceContainer = GetNode<HBoxContainer>("%DeviceOutputsListHBoxContainer");
        _patchMatrix = GetNode<VBoxContainer>("%PatchMatrixContainer");
        _channelList = GetNode<VBoxContainer>("%ChannelList"); 
        
        
        
        // Load its patch info
        GD.Print("Patch matrix loaded with id: " + PatchId + " and name: " + Patch.Name);
        
        _patchName = GetNode<LineEdit>("%PatchName");
        _patchName.Text = Patch.Name;
        //_patchName.TextChanged += PatchNameOnTextChanged;
        
        _deletePatchButton = GetNode<Button>("%DeletePatchButton");
        _deletePatchButton.Pressed += DeletePatchButtonPressed;

        _addChannelButton = GetNode<Button>("%AddChannelButton");
        _addChannelButton.Pressed += AddChannelButtonPressed;
        
        SyncAudioDeviceDisplays();
    }

    private void DeletePatchButtonPressed()
    {
        _globalData.Settings.DeletePatch(Patch.Id);
        QueueFree();
    }

    public int GetId() => PatchId;


    public async void SyncAudioDeviceDisplays()
    {
        GD.Print("Syncing devices in audio output patch matrix");
        // For now we remove devices and start fresh while developing, in future match agaisnt info instead.
        
        var deviceHeaders = _deviceContainer.GetChildren();
        foreach (var deviceHeader in deviceHeaders)
        {
            deviceHeader.QueueFree();
        }
        var channelRows = _channelList.GetChildren();
        foreach (var channelRow in channelRows)
        {
            if (channelRow.Name == "AddChannelButton") continue; // Excempt add channel butrton from being deleted.
            channelRow.QueueFree();
        }

        await ToSignal(GetTree(), "process_frame");

        _currentDeivceList.Clear();
        _availableDeviceList = _globalData.AudioDevices.GetAvailibleAudioDevicseNames();
        
        // CHANNELS (ROWS)
        foreach (var channel in Patch.Channels)
        {
            NewChannelRow(channel);
        }

        // DEVICES (COLUMNS)
        var unusedDeviceList = _availableDeviceList;
        
        foreach (var device in Patch.OutputDevices)
        {
            if (_availableDeviceList.Contains(device.Key))
            {
                NewUsedDeviceColumn(device.Key, device.Value);
                unusedDeviceList.Remove(device.Key);
            }
                
            else
            {
                NewUsedButNotFoundDeviceColumn(device.Key, device.Value);
                unusedDeviceList.Remove(device.Key);
            }
        }

        foreach (var device in unusedDeviceList)
        {
            NewUnusedDeviceColumn(device);
        }
        

        _deviceCount = _availableDeviceList.Count;
        
        // Then build all check boxs between cue output channels and devices/devicechannels.
        //BuildPatchMatrix();
    }

    private async void NewChannelRow(KeyValuePair<int, string> channel)
    {
        HBoxContainer channelHBox = new HBoxContainer();
        channelHBox.Name = $"{channel.Key}HBox";
        _channelList.AddChild(channelHBox);
        int currentIndex = channelHBox.GetIndex();
        if (currentIndex > 0) _channelList.MoveChild(channelHBox, currentIndex - 1);
        LineEdit channelLabel = new LineEdit();
        channelLabel.Text = channel.Value;
        channelHBox.AddChild(channelLabel);
        
        channelLabel.SetMaxLength(24);
        channelLabel.SetHSizeFlags(SizeFlags.ExpandFill);
        channelLabel.SetHorizontalAlignment(HorizontalAlignment.Right);
        channelLabel.CustomMinimumSize = new Vector2(0, 32);
        channelLabel.SetMouseFilter(MouseFilterEnum.Pass);
        channelLabel.TooltipText =
            $"Channel: {channel.Value}, cues get routed to this channel. " +
            $"From here you route this to a physical output device."; 
        
        channelLabel.TextChanged += (newText) =>
        {
            try
            {
                Patch.RenameChannel(channel.Key, channelLabel.Text);
            }
            catch (Exception ex)
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to rename channel {channel.Key}: {ex.Message}", 2);
                Console.WriteLine(ex);
                throw;
            }
        };
    }
    
    private async void NewUsedDeviceColumn(string deviceName, Dictionary<int, List<int>> outPutChannels)
    {
        GD.Print($"Add device {deviceName} column as USED");
        LoadDeviceOutputPatchHeader(deviceName, true);
    }
    
    private async void NewUsedButNotFoundDeviceColumn(string deviceName, Dictionary<int, List<int>> outPutChannels)
    {
        GD.Print($"Add device {deviceName} column as USED BUT NOT FOUND");
    }
    
    private async void NewUnusedDeviceColumn(string deviceName)
    {
        GD.Print($"Add device {deviceName} column as UNUSED");
        LoadDeviceOutputPatchHeader(deviceName, false);
    }
    
    private void AddChannelButtonPressed()
    {
        Patch.NewChannel("New Channel");
        SyncAudioDeviceDisplays();
    }


    private Node LoadDeviceOutputPatchHeader(string name, bool enabled)
    {
        var header = new Panel();
        header.Name = name;
        header.Set("", name);
        
        /*Node instance = _deviceHeaderScene.Instantiate();
        instance.Set("DeviceName", name);
        _deviceContainer.AddChild(instance);
        instance.GetNode<Label>("Label").Text = name;
        _currentDeivceList.Add(instance);

        instance.Name = name; 
        var specs = _globalData.AudioDevices.GetReadableAudioDeviceSpecs(name);
        instance.GetChild<Label>(1).TooltipText = name;
        foreach (var spec in specs)
        {
            instance.GetChild<Label>(1).TooltipText += "\n" + spec;
        }

        _currentDeivceList.Add(instance);
                
        CheckButton useDeviceButton = instance.GetNode<CheckButton>("UseDeviceButton");
        useDeviceButton.SetPressed(enabled);
        
        // Connect functions to the use device check button. 
        useDeviceButton.Toggled += (bool presssed) =>
        {
            if (presssed)
            {
                AudioDevice enabledDevice = _globalData.AudioDevices.OpenAudioDevice(name, out string error);
                GD.Print("When enabling audio device: " + error);
                if (enabledDevice == null)
                {
                    GD.Print("ERROR WHEN ENABLING DEVICE");
                    return;
                }
                AddDeviceToPatch(enabledDevice); 
                instance.Set("DeviceId", enabledDevice.DeviceId);

            }
            else
            {
                RemoveDeviceFromPatch((int)instance.Get("DeviceId"));
                _globalData.Devices.DisableAudioDevice((int)instance.Get("DeviceId")); // TODO: This needs to be in AudioDevices
            }
            SyncAudioDeviceDisplays();
        };*/
        return ;
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
        //_globalData.Settings.UpdatePatch(Patch);
    }
    
    private async void BuildPatchMatrix()
    // This loads checkboxs between channels and devices.
    {
        // For now remove everything and start over on each build - eventauly should build once and update
        var children = _patchMatrix.GetChildren();
        foreach (var child in children)
        {
            foreach (var cb in child.GetChildren())
            {
                cb.QueueFree();
            }
        }
        await ToSignal(GetTree(), "process_frame");
        children = _patchMatrix.GetChildren(); // Should be HboxContainers for each channel
        var devicesArray = _deviceContainer.GetChildren();
        
        // Loops all channels and availible devices and gives it a disabled checkbox.
        for (int channel = 0; channel < Patch.Channels.Count; channel++) // 0 - 6 until add/remove channel functionality added.
        {
            for (int i = 0; i < devicesArray.Count(); i++)
            {
                var hasId = devicesArray[i].Get("DeviceCId");
                var hasChannel =  devicesArray[i].Get("DeviceChannel");
                string idch = hasId.ToString() + ":" + ((int)hasChannel).ToString();
                
                if (idch != ":-1")
                {
                    bool existsInPatch = Patch.Channels[channel].Outputs.ContainsKey(idch);
                    if (existsInPatch)
                    {
                        Node checkBoxInstance = SceneLoader.LoadScene("uid://cbdaknpeq3im1", out string error); // Check box
                        //GD.Print(channel + " : " +idch + Patch.Channels[channel].Outputs[idch]);
                        checkBoxInstance.Set("Channel", channel);
                        //GD.Print("Deivce channel being set: "+ devicesArray[i].Get("DeviceChannel"));
                        checkBoxInstance.Set("DeviceChannel", devicesArray[i].Get("DeviceChannel"));
                        checkBoxInstance.Set("DeviceId", devicesArray[i].Get("DeviceCId"));
                        children[channel].AddChild(checkBoxInstance);
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
                    else
                    {
                        var  blank = new BoxContainer();
                        children[channel].AddChild(blank);
                        blank.SetCustomMinimumSize(new Vector2(32,32));
                    }
                }
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
