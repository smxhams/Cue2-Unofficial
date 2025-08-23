using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Cue2.Base.Classes;
using Cue2.Base.Classes.Devices;
using Cue2.Shared;

namespace Cue2.Base.Settings;

/// <summary>
/// Manages the UI for an audio output patch matrix, allowing users to configure routing between channels and devices.
/// </summary>
public partial class AudioOutputPatchMatrix : Control
{
    [Export] private AudioOutputPatch Patch { get; set; } // This is set in SettingsAudioOutputPatch when created. 
    
    [Export] private int PatchId { get; set; }
    
    private GlobalData _globalData;
    private GlobalSignals _globalSignals;
    private AudioDevices _audioDevices;
    
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
    
    private int _deviceCount;
    
    /// <summary>
    /// Initializes the node, loads required scenes, sets up UI elements, and connects signals.
    /// </summary>
    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals"); // Global
        _audioDevices = GetNode<AudioDevices>("/root/AudioDevices");
        
        
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
        GD.Print("AudioOutputPatchMatrix:_Ready - Patch matrix loaded with id: " + PatchId + " and name: " + Patch.Name);
        
        _patchName = GetNode<LineEdit>("%PatchName");
        _patchName.Text = Patch.Name;
        GD.Print("AudioOutputPatchMatrix:_Ready - Patch name: " + Patch.Name);
        _patchName.TextChanged += PatchNameOnTextChanged;
        
        _deletePatchButton = GetNode<Button>("%DeletePatchButton");
        _deletePatchButton.Pressed += DeletePatchButtonPressed;

        _addChannelButton = GetNode<Button>("%AddChannelButton");
        _addChannelButton.Pressed += AddChannelButtonPressed;
        
        // Signal from AudioDevices events
        _globalSignals.AudioDevicesChanged += SyncAudioDeviceDisplays;
        
        
        SyncAudioDeviceDisplays();
    }

    /// <summary>
    /// Handles the deletion of the current patch and removes the UI node.
    /// </summary>
    private void DeletePatchButtonPressed()
    {
        _globalData.Settings.DeletePatch(Patch.Id);
        QueueFree();
    }

    
    /// <summary>
    /// Synchronizes the displayed audio devices and channels with the current data, rebuilding the UI as needed.
    /// </summary>
    private async void SyncAudioDeviceDisplays()
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
        
        _availableDeviceList = _audioDevices.GetAvailableAudioDeviceNames();
        
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
                NewUsedButNotFoundDeviceColumn(device.Key, device.Value);
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Device used in audio patch but not found: {device.Key}", 3);
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

    
    /// <summary>
    /// Creates a new UI row for a channel, including delete button and editable label.
    /// </summary>
    /// <param name="channel">The channel key-value pair (ID and name).</param>
    private void NewChannelRow(KeyValuePair<int, string> channel)
    {
        HBoxContainer channelHBox = new HBoxContainer();
        channelHBox.Name = $"{channel.Key}HBox";
        _channelList.AddChild(channelHBox);
        int currentIndex = channelHBox.GetIndex();
        if (currentIndex > 0) _channelList.MoveChild(channelHBox, currentIndex - 1);
        Button deleteChannelButton = new Button();
        deleteChannelButton.CustomMinimumSize = new Vector2(32, 32);
        deleteChannelButton.SetMouseFilter(MouseFilterEnum.Pass);
        deleteChannelButton.TooltipText = "Delete this channel";
        deleteChannelButton.Icon = GetThemeIcon("DeleteBin", "AtlasIcons");
        deleteChannelButton.ExpandIcon = true;
        deleteChannelButton.FocusMode = FocusModeEnum.None;
        deleteChannelButton.AddThemeConstantOverride("icon_max_width", 13);
        deleteChannelButton.IconAlignment = HorizontalAlignment.Center;
        
        channelHBox.AddChild(deleteChannelButton);
        deleteChannelButton.Pressed += () =>
        {
            Patch.RemoveChannel(channel.Key);
            SyncAudioDeviceDisplays();
        };
        
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
        
        channelLabel.TextChanged += newText =>
        {
            try
            {
                Patch.RenameChannel(channel.Key, newText);
            }
            catch (Exception ex)
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to rename channel {channel.Key}: {ex.Message}", 2);
                Console.WriteLine(ex);
                throw;
            }
        };
    }
    
    private void NewUsedDeviceColumn(string deviceName, List<OutputChannel> outputChannels)
    {
        //Double check the device has been opened.
        _audioDevices.OpenAudioDevice(deviceName, out var _);
        
        
        var header = LoadDeviceOutputDeviceHeader(deviceName, true);
    
        var specs = _audioDevices.GetReadableAudioDeviceSpecs(deviceName);
        header.GetChild<Label>(1).TooltipText = deviceName;
        foreach (var spec in specs)
        {
            header.GetChild<Label>(1).TooltipText += "\n" + spec;
        }
        
        // Add device outputs
        AddDeviceOutputColumns(deviceName, outputChannels);
    }
    
    private void NewUsedButNotFoundDeviceColumn(string deviceName, List<OutputChannel> outputChannels)
    {
        var header = LoadDeviceOutputDeviceHeader(deviceName, true);
        var label = header.GetChild<Label>(1);
        label.TooltipText = $"{deviceName}: Is used in patch but is currently unavailable.";
        var style = new StyleBoxFlat();
        style.BgColor = new Color((float)1.0,(float)0.00,(float)0.00,(float)0.5);

        header.AddThemeStyleboxOverride("panel", style);
        
        //label.AddThemeColorOverride("font_color", GlobalStyles.Danger);
        
        var outputNodes = AddDeviceOutputColumns(deviceName, outputChannels);
        foreach (var outputNode in outputNodes)
        {
            outputNode.AddThemeStyleboxOverride("panel", style);
        }
    }
    
    private void NewUnusedDeviceColumn(string deviceName)
    {
        var header = LoadDeviceOutputDeviceHeader(deviceName);
        header.GetChild<Label>(1).TooltipText = $"{deviceName}: Currently disabled";
    }


    private List<Panel> AddDeviceOutputColumns(string deviceName, List<OutputChannel> outputChannels)
    {
        var deviceOutputNodes = new List<Panel>();
        for (int outputIndex = 0; outputIndex < outputChannels.Count; outputIndex++)
        {
            var outHeader = _deviceOutputHeaderScene.Instantiate<Panel>();
            _deviceContainer.AddChild(outHeader);
            
            deviceOutputNodes.Add(outHeader);
            
            var outputNameEdit = outHeader.GetNode<LineEdit>("OutputName");
            outputNameEdit.Text = outputChannels[outputIndex].Name;
            outHeader.Set("ParentDevice", deviceName);
            outHeader.Set("OutputIndex", outputIndex);
        
            outputNameEdit.TextChanged += newText =>
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

        return deviceOutputNodes;
    }
    
    private void AddChannelButtonPressed()
    {
        Patch.NewChannel("New Channel", out var error);
        if (error != null)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"{error}", 2);
            return;
        }
        SyncAudioDeviceDisplays();
    }


    private Panel LoadDeviceOutputDeviceHeader(string name, bool state = false)
    {
        Panel instance = _deviceHeaderScene.Instantiate<Panel>();
        instance.Set("DeviceName", name);
        _deviceContainer.AddChild(instance);
        instance.GetNode<Label>("Label").Text = name;
        instance.Name = name; 
        
        CheckButton toggleDeviceButton = instance.GetNode<CheckButton>("ToggleDeviceButton");
        toggleDeviceButton.SetPressed(state);
        
        // Connect functions to the use device check button. 
        toggleDeviceButton.Toggled += presssed =>
        {
            if (presssed)
            {
                AudioDevice enabledDevice = _audioDevices.OpenAudioDevice(name, out string error);
                GD.Print("AudioOutputPatchMatrix:NewUnusedDeviceColumn - When enabling audio device: " + error);
                if (enabledDevice == null)
                {
                    GD.Print($"AudioOutputPatchMatrix:NewUnusedDeviceColumn - Error enabling audio device. {error}");
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

    /// <summary>
    /// Builds the matrix of checkboxes for routing channels to device outputs.
    /// </summary>
    private async void BuildPatchMatrix()
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
            //GD.Print($"{channel.Key} : {channel.Value}");

            for (int col = 0; col < culumnCount; col++)
            {
                var header = deviceHeaders[col];

                // Determine if this is an output header (has "ParentDevice" property set)
                var parentDeviceVar = header.Get("ParentDevice");
                if (parentDeviceVar.VariantType != Variant.Type.Nil)
                {
                    string deviceName = parentDeviceVar.ToString();
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

                    checkBox.Toggled += pressed =>
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
                                        GD.Print($"AudioOutputPatchMatrix:BuildPatchMatrix - Routed channel {channelId} to {deviceName}:index {outputIndex}");
                                    }
                                }
                                else
                                {
                                    channelListInner.Remove(channelId);
                                    GD.Print($"AudioOutputPatchMatrix:BuildPatchMatrix -Unrouted channel {channelId} from {deviceName}:index {outputIndex}");
                                }
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
        
    
    /// <summary>
    /// Updates the patch name when the text in the LineEdit changes.
    /// </summary>
    /// <param name="newtext">The new text entered by the user.</param>
    private void PatchNameOnTextChanged(string newtext)
    {
        Patch.Name = newtext;
        _globalData.Settings.UpdatePatch(Patch);
    }
    
    /// <summary>
    /// Triggers a refresh of the audio device displays.
    /// </summary>
    private void _onRefreshButtonPressed()
    {
        SyncAudioDeviceDisplays();
    }
    
    

    
}
