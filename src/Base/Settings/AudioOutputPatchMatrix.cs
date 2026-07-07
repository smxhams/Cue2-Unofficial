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
    
    private bool _isRebuilding;
    private bool _isDisposed;
    
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
        GD.Print($"AudioOutputPatchMatrix:_Ready - Patch matrix loaded with id: {PatchId} and name: {Patch?.Name}");

        _patchName = GetNode<LineEdit>("%PatchName");
        _patchName.Text = Patch?.Name ?? "Unnamed";
        _patchName.TextChanged += PatchNameOnTextChanged;
        
        _deletePatchButton = GetNode<Button>("%DeletePatchButton");
        _deletePatchButton.Pressed += DeletePatchButtonPressed;

        _addChannelButton = GetNode<Button>("%AddChannelButton");
        _addChannelButton.Pressed += AddChannelButtonPressed;
        
        // Signal from AudioDevices events (hotplug etc). We unsubscribe in _ExitTree.
        _globalSignals.AudioDevicesChanged += SyncAudioDeviceDisplays;

        SyncAudioDeviceDisplays();
    }

    /// <summary>
    /// Handles the deletion of the current patch and removes the UI node.
    /// </summary>
    private void DeletePatchButtonPressed()
    {
        if (_isDisposed || !GodotObject.IsInstanceValid(this) || Patch == null || !GodotObject.IsInstanceValid(Patch))
            return;
        _globalData.Settings.DeletePatch(Patch.Id);
        QueueFree();
    }

    
    /// <summary>
    /// Synchronizes the displayed audio devices and channels with the current data, rebuilding the UI as needed.
    /// </summary>
    private async void SyncAudioDeviceDisplays()
    {
        if (_isDisposed || !GodotObject.IsInstanceValid(this) || Patch == null || !GodotObject.IsInstanceValid(Patch))
            return;
        if (_isRebuilding)
            return;

        _isRebuilding = true;
        try
        {
            GD.Print("AudioOutputPatchMatrix:SyncAudioDeviceDisplays - Syncing devices in audio output patch matrix");
            // For now we remove devices and start fresh while developing, in future match against info instead.

            var deviceHeaders = _deviceContainer.GetChildren();
            foreach (var deviceHeader in deviceHeaders)
            {
                deviceHeader.QueueFree();
            }
            var channelRows = _channelList.GetChildren();
            foreach (var channelRow in channelRows)
            {
                if (channelRow.Name == "AddChannelButton") continue; // Exempt add channel button from being deleted.
                channelRow.QueueFree();
            }

            await ToSignal(GetTree(), "process_frame");

            var available = _audioDevices.GetAvailableAudioDeviceNames() ?? new List<string>();
            _availableDeviceList = available;

            // CHANNELS (ROWS)
            var sortedChannels = Patch.Channels.OrderBy(kv => kv.Key).ToList();
            foreach (var channel in sortedChannels)
            {
                NewChannelRow(channel);
            }

            // DEVICES (COLUMNS)
            // Copy to avoid mutating the original available list when removing used devices.
            var unusedDeviceList = new List<string>(_availableDeviceList);

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

            // Then build all checkboxes between cue output channels and devices/device channels.
            BuildPatchMatrix();
        }
        finally
        {
            _isRebuilding = false;
        }
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
            if (_isDisposed || !GodotObject.IsInstanceValid(this) || Patch == null || !GodotObject.IsInstanceValid(Patch))
                return;
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
            if (_isDisposed || !GodotObject.IsInstanceValid(this) || Patch == null || !GodotObject.IsInstanceValid(Patch))
                return;
            try
            {
                Patch.RenameChannel(channel.Key, newText);
            }
            catch (Exception ex)
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to rename channel {channel.Key}: {ex.Message}", 2);
                GD.PrintErr($"AudioOutputPatchMatrix:NewChannelRow - Rename exception: {ex}");
            }
        };
    }
    
    private void NewUsedDeviceColumn(string deviceName, List<OutputChannel> outputChannels)
    {
        // Double-check the device is open (no-op + no emit if already open).
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
        style.BgColor = new Color(1.0f, 0.0f, 0.0f, 0.5f);

        header.AddThemeStyleboxOverride("panel", style);

        var outputNodes = AddDeviceOutputColumns(deviceName, outputChannels);
        foreach (var outputNode in outputNodes)
        {
            outputNode.AddThemeStyleboxOverride("panel", style);
        }
    }
    
    /// <summary>
    /// Creates header for a device that is available but not enabled in this patch.
    /// </summary>
    private void NewUnusedDeviceColumn(string deviceName)
    {
        var header = LoadDeviceOutputDeviceHeader(deviceName);
        header.GetChild<Label>(1).TooltipText = $"{deviceName}: Currently disabled (enable to use in patch)";
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
        
            // Capture locals for the closure
            int capturedIndex = outputIndex;
            outputNameEdit.TextChanged += newText =>
            {
                if (_isDisposed || !GodotObject.IsInstanceValid(this) || Patch == null || !GodotObject.IsInstanceValid(Patch))
                    return;
                if (!Patch.RenameDeviceChannel(deviceName, capturedIndex, newText))
                {
                    // Revert to current (unchanged) name on failure
                    string currentName = Patch.GetDeviceOutputName(deviceName, capturedIndex);
                    if (currentName != null)
                    {
                        outputNameEdit.Text = currentName;
                    }
                    else
                    {
                        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to revert output name for device '{deviceName}' at index {capturedIndex}", 2);
                    }
                }
            };
        }

        return deviceOutputNodes;
    }
    
    /// <summary>
    /// Adds a new default channel to the patch and refreshes the matrix.
    /// </summary>
    private void AddChannelButtonPressed()
    {
        if (_isDisposed || !GodotObject.IsInstanceValid(this) || Patch == null || !GodotObject.IsInstanceValid(Patch))
            return;
        Patch.NewChannel("New Channel", out var error);
        if (error != null)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), error, 2);
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
        // Temporarily unsubscribe from AudioDevicesChanged during Open+Sync to avoid the
        // double-rebuild: OpenAudioDevice emits the signal, and we explicitly rebuild after model change.
        toggleDeviceButton.Toggled += pressed =>
        {
            if (_isDisposed || !GodotObject.IsInstanceValid(this) || Patch == null || !GodotObject.IsInstanceValid(Patch))
            {
                return;
            }
            _globalSignals.AudioDevicesChanged -= SyncAudioDeviceDisplays;
            try
            {
                if (pressed)
                {
                    AudioDevice enabledDevice = _audioDevices.OpenAudioDevice(name, out string error);
                    if (enabledDevice == null)
                    {
                        // Revert visual state without re-triggering Toggled
                        toggleDeviceButton.SetPressedNoSignal(false);
                        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to enable audio device '{name}': {error}", 2);
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
            }
            finally
            {
                if (!_isDisposed && GodotObject.IsInstanceValid(this))
                {
                    _globalSignals.AudioDevicesChanged += SyncAudioDeviceDisplays;
                }
            }
        };
        return instance;
    }

    /// <summary>
    /// Builds the matrix of checkboxes for routing channels to device outputs.
    /// </summary>
    private async void BuildPatchMatrix()
    {
        if (_isDisposed || !GodotObject.IsInstanceValid(this) || Patch == null || !GodotObject.IsInstanceValid(Patch))
            return;

        // For now remove everything and start over on each build - eventually should build once and update
        var children = _patchMatrix.GetChildren();
        foreach (var child in children)
        {
            child.QueueFree();
        }

        await ToSignal(GetTree(), "process_frame");

        var deviceHeaders = _deviceContainer.GetChildren();

        // Calculate column count
        var columnCount = deviceHeaders.Count;
        _patchMatrix.Columns = columnCount;

        var sortedChannels = Patch.Channels.OrderBy(kv => kv.Key).ToList();

        foreach (var channel in sortedChannels)
        {
            int channelId = channel.Key;

            for (int col = 0; col < columnCount; col++)
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

                    // Use helper for initial state
                    checkBox.ButtonPressed = Patch.IsChannelRouted(deviceName, outputIndex, channelId);

                    checkBox.Toggled += pressed =>
                    {
                        if (_isDisposed || !GodotObject.IsInstanceValid(this) || Patch == null || !GodotObject.IsInstanceValid(Patch))
                            return;
                        try
                        {
                            Patch.SetRouting(deviceName, outputIndex, channelId, pressed);
                            GD.Print($"AudioOutputPatchMatrix:BuildPatchMatrix - {(pressed ? "Routed" : "Unrouted")} channel {channelId} to {deviceName}:index {outputIndex}");
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
        if (_isDisposed || !GodotObject.IsInstanceValid(this) || Patch == null || !GodotObject.IsInstanceValid(Patch))
            return;
        Patch.Name = newtext;
        _globalData.Settings.UpdatePatch(Patch);
    }
    
    /// <summary>
    /// Triggers a refresh of the audio device displays.
    /// </summary>
    private void _onRefreshButtonPressed()
    {
        if (_isDisposed || !GodotObject.IsInstanceValid(this) || Patch == null || !GodotObject.IsInstanceValid(Patch))
            return;
        SyncAudioDeviceDisplays();
    }
    
    

    /// <summary>
    /// Unsubscribes from global signals, disconnects handlers, and explicitly frees
    /// dynamically generated child nodes (channel rows, device headers/outputs, checkboxes,
    /// filler controls etc.) created during patch matrix building. This prevents leaks
    /// of Godot objects that were instantiated for the UI but might not be freed if
    /// parents are removed without cascading properly (especially on app quit).
    /// </summary>
    public override void _ExitTree()
    {
        _isDisposed = true;

        if (_globalSignals != null && GodotObject.IsInstanceValid(_globalSignals))
        {
            _globalSignals.AudioDevicesChanged -= SyncAudioDeviceDisplays;
        }

        // Disconnect direct child signal handlers (release any captured references in method groups).
        if (_patchName != null && GodotObject.IsInstanceValid(_patchName))
            _patchName.TextChanged -= PatchNameOnTextChanged;
        if (_deletePatchButton != null && GodotObject.IsInstanceValid(_deletePatchButton))
            _deletePatchButton.Pressed -= DeletePatchButtonPressed;
        if (_addChannelButton != null && GodotObject.IsInstanceValid(_addChannelButton))
            _addChannelButton.Pressed -= AddChannelButtonPressed;

        // Proactively QueueFree all objects we generated while building the matrix UI.
        // These include: channel HBoxes + their Buttons/LineEdits, device header Panels,
        // output header Panels, CheckBoxes for the matrix, and empty filler Controls.
        FreeDynamicChildren(_deviceContainer);
        FreeDynamicChildren(_channelList, skipName: "AddChannelButton");
        FreeDynamicChildren(_patchMatrix);
    }

    private void FreeDynamicChildren(Node parent, string skipName = null)
    {
        if (parent == null || !GodotObject.IsInstanceValid(parent)) return;

        var children = parent.GetChildren();
        foreach (Node child in children)
        {
            if (skipName != null && child.Name == skipName) continue;
            if (GodotObject.IsInstanceValid(child))
                child.QueueFree();
        }
    }
}
