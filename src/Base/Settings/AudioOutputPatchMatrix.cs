using Godot;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Channels;
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
    private PackedScene _deviceOutputHeaderScene;
    private PackedScene _checkBoxScene;
    
    
    private HBoxContainer _deviceContainer;
    private VBoxContainer _channelList;
    private GridContainer _patchMatrix;
    private LineEdit _patchName;
    private Button _deletePatchButton;
    private Button _addChannelButton;
    
    private int _deviceCount = 0;
    
    
    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals"); // Global
        
        // This is "PatchMatrixDeviceHeader" header
        _deviceHeaderScene = SceneLoader.LoadPackedScene("uid://cisr40jsg2jgp", out string _);
        
        // This is "PatchMatrixDeviceOutputHeader"
        _deviceOutputHeaderScene = SceneLoader.LoadPackedScene("uid://bmi0eibnauemp", out string _);
        
        // This is "AudioPatchMatrixCheckBox"
        _checkBoxScene = SceneLoader.LoadPackedScene("uid://cbdaknpeq3im1", out string _); // Check box
            
            
        _deviceContainer = GetNode<HBoxContainer>("%DeviceOutputsListHBoxContainer");
        _patchMatrix = GetNode<GridContainer>("%PatchMatrixContainer");
        _channelList = GetNode<VBoxContainer>("%ChannelList"); 
        
        
        
        // Load its patch info
        GD.Print("Patch matrix loaded with id: " + PatchId + " and name: " + Patch.Name);
        
        _patchName = GetNode<LineEdit>("%PatchName");
        _patchName.Text = Patch.Name;
        GD.Print($"Patch name: {Patch.Name}");
        _patchName.TextChanged += PatchNameOnTextChanged;
        
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
        var sortedChannels = Patch.Channels.OrderBy(kv => kv.Key).ToList();
        foreach (var channel in sortedChannels)
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
                NewUsedButNotFoundDeviceColumn(device.Key, new Dictionary<string, List<int>>());
                unusedDeviceList.Remove(device.Key);
            }
        }

        foreach (var device in unusedDeviceList)
        {
            NewUnusedDeviceColumn(device);
        }
        

        _deviceCount = _availableDeviceList.Count;
        
        // Then build all check boxs between cue output channels and devices/devicechannels.
        BuildPatchMatrix();
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
    
    private async void NewUsedDeviceColumn(string deviceName, List<OutputChannel> outputChannels)
    {
        GD.Print($"AudioOutputPatchMatrix:NewUsedDeviceColumn - Add device {deviceName} column as USED");
        var header = LoadDeviceOutputDeviceHeader(deviceName, true);
    
        var specs = _globalData.AudioDevices.GetReadableAudioDeviceSpecs(deviceName);
        header.GetChild<Label>(1).TooltipText = deviceName;
        foreach (var spec in specs)
        {
            header.GetChild<Label>(1).TooltipText += "\n" + spec;
        }
        
        // Add device outputs
        for (int outputIndex = 0; outputIndex < outputChannels.Count; outputIndex++)
        {
            var outHeader = _deviceOutputHeaderScene.Instantiate();
            _deviceContainer.AddChild(outHeader);
        
            var outputNameEdit = outHeader.GetNode<LineEdit>("OutputName");
            outputNameEdit.Text = outputChannels[outputIndex].Name;
            outHeader.Set("ParentDevice", deviceName);
            outHeader.Set("OutputIndex", outputIndex);
        
            outputNameEdit.TextChanged += (string newText) =>
            {
                int idx = (int)outHeader.Get("OutputIndex");
                if (!Patch.RenameDeviceChannel(deviceName, idx, newText))
                {
                    // Revert to current (unchanged) name on failure
                    string currentName = Patch.GetDeviceOutputName(deviceName, idx);
                    if (currentName != null)
                    {
                        outputNameEdit.Text = currentName;
                    }
                    else
                    {
                        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to revert output name for device '{deviceName}' at index {idx}", 2);
                    }
                }
            };
        }
    }
    
    private async void NewUsedButNotFoundDeviceColumn(string deviceName, Dictionary<string, List<int>> outputChannels)
    {
        GD.Print($"Add device {deviceName} column as USED BUT NOT FOUND");
        var header = LoadDeviceOutputDeviceHeader(deviceName);
    }
    
    private async void NewUnusedDeviceColumn(string deviceName)
    {
        GD.Print($"Add device {deviceName} column as UNUSED");
        var header = LoadDeviceOutputDeviceHeader(deviceName);
        header.GetChild<Label>(1).TooltipText = $"{deviceName}: Currently disabled";
    }
    
    private void AddChannelButtonPressed()
    {
        Patch.NewChannel("New Channel");
        SyncAudioDeviceDisplays();
    }


    private Node LoadDeviceOutputDeviceHeader(string name, bool state = false)
    {
        Node instance = _deviceHeaderScene.Instantiate();
        instance.Set("DeviceName", name);
        _deviceContainer.AddChild(instance);
        instance.GetNode<Label>("Label").Text = name;
        _currentDeivceList.Add(instance);
        instance.Name = name; 
        
        CheckButton toggleDeviceButton = instance.GetNode<CheckButton>("ToggleDeviceButton");
        toggleDeviceButton.SetPressed(state);
        
        // Connect functions to the use device check button. 
        toggleDeviceButton.Toggled += (bool presssed) =>
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

                int outputCount = enabledDevice.Channels;
                Patch.AddDeviceOutputs(name, outputCount);
                instance.Set("DeviceId", enabledDevice.DeviceId);

            }
            else
            {
                Patch.RemoveOutputDevice(name);
            }
            SyncAudioDeviceDisplays();
        };
        return instance;
    }

    private async void BuildPatchMatrix()
        // This loads checkboxs between channels and devices.
    {
        // For now remove everything and start over on each build - eventauly should build once and update
        var children = _patchMatrix.GetChildren();
        foreach (var child in children)
        {
            child.QueueFree();
        }

        await ToSignal(GetTree(), "process_frame");

        var deviceHeaders = _deviceContainer.GetChildren();

        // Calculate column count
        var culumnCount = deviceHeaders.Count;
        _patchMatrix.Columns = culumnCount;

        var sortedChannels = Patch.Channels.OrderBy(kv => kv.Key).ToList();

        foreach (var channel in sortedChannels)
        {
            int channelId = channel.Key;
            GD.Print($"{channel.Key} : {channel.Value}");

            for (int col = 0; col < culumnCount; col++)
            {
                var header = deviceHeaders[col];

                // Determine if this is an output header (has "ParentDevice" property set)
                var parentDeviceVar = header.Get("ParentDevice");
                if (parentDeviceVar.VariantType != Variant.Type.Nil)
                {
                    string deviceName = parentDeviceVar.ToString();
                    GD.Print($"{header.Name} has parent {deviceName}");
                    var outputIndexVar = header.Get("OutputIndex");
                    int outputIndex = outputIndexVar.AsInt32();

                    CheckBox checkBox = _checkBoxScene.Instantiate<CheckBox>();

                    // Check if this channel is already routed to this output
                    if (Patch.OutputDevices.TryGetValue(deviceName, out var outputs) &&
                        outputIndex >= 0 && outputIndex < outputs.Count)
                    {
                        var channelList = outputs[outputIndex].RoutedChannels;
                        checkBox.ButtonPressed = channelList.Contains(channelId);
                    }
                    else
                    {
                        checkBox.ButtonPressed = false;
                        _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                            $"Output at index {outputIndex} not found for device {deviceName} during matrix build", 1);
                    }

                    checkBox.Toggled += (bool pressed) =>
                    {
                        try
                        {
                            if (Patch.OutputDevices.TryGetValue(deviceName, out var outputsInner) &&
                                outputIndex >= 0 && outputIndex < outputsInner.Count)
                            {
                                var channelListInner = outputsInner[outputIndex].RoutedChannels;
                                if (pressed)
                                {
                                    if (!channelListInner.Contains(channelId))
                                    {
                                        channelListInner.Add(channelId);
                                        GD.Print($"Routed channel {channelId} to {deviceName}:index {outputIndex}");
                                    }
                                }
                                else
                                {
                                    channelListInner.Remove(channelId);
                                    GD.Print($"Unrouted channel {channelId} from {deviceName}:index {outputIndex}");
                                }
                                // Optional: Save or update settings after change
                                //_globalData.Settings.UpdatePatch(Patch);
                            }
                            else
                            {
                                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                                    $"Failed to update routing for {deviceName}:index {outputIndex}", 2);
                            }
                        }
                        catch (Exception ex)
                        {
                            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                                $"Error updating channel routing: {ex.Message}", 2);
                        }
                    };


                    _patchMatrix.AddChild(checkBox);
                }
                else
                {
                    Control empty = new Control();
                    empty.CustomMinimumSize = new Vector2(32, 32);
                    _patchMatrix.AddChild(empty);
                }
            }
        }
    }
        
    

    private void PatchNameOnTextChanged(string newtext)
    {
        Patch.Name = newtext;
        _globalData.Settings.UpdatePatch(Patch);
    }
    
    private void _onRefreshButtonPressed()
    {
        SyncAudioDeviceDisplays();
    }
    
    

    
}
