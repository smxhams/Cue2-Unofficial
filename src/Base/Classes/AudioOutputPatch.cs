using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cue2.Base.Classes.Devices;
using Godot;

namespace Cue2.Base.Classes;

// Class representing a patch, stored inside is each audio output channel - Channel as class. Which contains informtation of where each channel should output. 

public class OutputChannel
{
    public string Name { get; set; }
    public List<int> RoutedChannels { get; set; }
}

public partial class AudioOutputPatch : Godot.GodotObject
{
    private static int _nextId = 0;
    
    public int Id { get; set; }
    public string Name { get; set; }
    public Dictionary<string, List<OutputChannel>> OutputDevices { get; set; }
    // <Device name, device output channnel, list of audio channel ID's>>
    public Dictionary<int, string> Channels { get; set; } // List of audio channels>
    private int _channelId { get; set; } = 0;



    public AudioOutputPatch()
    {
        Id = _nextId++;
        Name = "Unnamed";
        OutputDevices = new Dictionary<string, List<OutputChannel>>();
        Channels = new Dictionary<int, string>();
        
        // Add some default blank channels.
        Channels.Add(_channelId++, "Left");
        Channels.Add(_channelId++, "Right");
    }

    public AudioOutputPatch(string name) 
    {
        Id = _nextId++;
        Name = name;
        OutputDevices = new Dictionary<string, List<OutputChannel>>();
        Channels = new Dictionary<int, string>();
        
        // Add some default blank channels.
        Channels.Add(_channelId++, "Left");
        Channels.Add(_channelId++, "Right");
    }
    
    public static AudioOutputPatch FromData(Godot.Collections.Dictionary dataDict)
    {
        GD.Print("Attempting to create patch from save data:");
        try
        {
            int id = dataDict["Id"].AsInt32();
            string name = dataDict["Name"].AsString();
            GD.Print("Got ID and Name");

            var channels = new Dictionary<int, string>();
            var channelsDict = dataDict["Channels"].AsGodotDictionary();
            GD.Print("Created variables for channels");
            foreach (var channelKey in channelsDict.Keys)
            {
                int key = channelKey.AsInt32();
                string value = channelsDict[channelKey].AsString();
                channels.Add(key, value);
            }

            GD.Print("Added channel data");

            var outputDevices = new Dictionary<string, List<OutputChannel>>();
            var outputDevicesDict = dataDict["OutputDevices"].AsGodotDictionary();
            foreach (var deviceKey in outputDevicesDict.Keys)
            {
                string deviceName = deviceKey.AsString();
                var outputsList = new List<OutputChannel>();
                var outputsArray = outputDevicesDict[deviceKey].AsGodotArray();
                foreach (var outputVar in outputsArray)
                {
                    var outputDict = outputVar.AsGodotDictionary();
                    var output = new OutputChannel
                    {
                        Name = outputDict["Name"].AsString(),
                        RoutedChannels = new List<int>()
                    };
                    var routedArray = outputDict["RoutedChannels"].AsGodotArray();
                    foreach (var routedVar in routedArray)
                    {
                        output.RoutedChannels.Add(routedVar.AsInt32());
                    }
                    outputsList.Add(output);
                }
                outputDevices.Add(deviceName, outputsList);
            }

            var patch = new AudioOutputPatch(name);
            patch.Id = id;
            if (id >= _nextId) _nextId = id + 1;
            patch.Channels = channels;
            patch.OutputDevices = outputDevices;
            patch._channelId = channels.Any() ? channels.Keys.Max() + 1 : 0;

            return patch;
        }
        catch (Exception ex)
        {
            GD.PrintErr("AudioOutputPatch:FromData - Error loading patch data: " + ex.Message);
            // Assuming globalSignals is accessible or handle logging appropriately
            return null;
        }
    }
    
    public void RenameChannel(int id, string newName)
    {
        if (Channels.ContainsKey(id))
        {
            Channels[id] = newName;
            GD.Print("Successfuly renamed channel " + id + " to " + newName);
        }
    }

    public void NewChannel(string name)
    {
        Channels.Add(_channelId++, name);
    }

    public void AddDeviceOutputs(string deviceName, int outputCount)
    {
        if (OutputDevices.ContainsKey(deviceName))
        {
            GD.PrintErr($"AudioOutputPatch:AddOutputDevice - Device '{deviceName}' already exists in patch '{Name}'.");
            return;
        }

        var outputs = new List<OutputChannel>();
        for (int i = 0; i < outputCount; i++)
        {
            outputs.Add(new OutputChannel { Name = $"Output {i+1}", RoutedChannels = new List<int>() });
        }
        OutputDevices.Add(deviceName, outputs);
    }
    public void RemoveOutputDevice(string deviceName)
    {
        if (OutputDevices.ContainsKey(deviceName))
        {
            OutputDevices.Remove(deviceName);
            return;
        }
        GD.Print($"Could not remove device: {deviceName} from patch: {Name}");
    }
    
    public string GetDeviceOutputName(string deviceName, int outputIndex)
    {
        if (OutputDevices.TryGetValue(deviceName, out var outputs) && 
            outputIndex >= 0 && outputIndex < outputs.Count)
        {
            return outputs[outputIndex].Name;
        }
        GD.PrintErr($"AudioOutputPatch:GetOutputName - Failed to get name for device '{deviceName}' at index {outputIndex} in patch '{Name}'.");
        return null;
    }


    public bool RenameDeviceChannel(string deviceName, int outputIndex, string newOutputName)
    {
        if (!OutputDevices.TryGetValue(deviceName, out var outputs))
        {
            GD.PrintErr($"AudioOutputPatch:RenameOutput - Device '{deviceName}' not found in patch '{Name}'.");
            return false;
        }

        if (outputIndex < 0 || outputIndex >= outputs.Count)
        {
            GD.PrintErr($"AudioOutputPatch:RenameOutput - Invalid output index {outputIndex} for device '{deviceName}' in patch '{Name}'.");
            return false;
        }

        string oldName = outputs[outputIndex].Name;
        if (newOutputName == oldName)
        {
            GD.Print("AudioOutputPatch:RenameOutput - No name change needed.");
            return true; // No change needed
        }

        if (outputs.Any(o => o.Name == newOutputName))
        {
            GD.Print($"AudioOutputPatch:RenameOutput - Output name '{newOutputName}' already exists for device '{deviceName}' in patch '{Name}'.");
            return false;
        }

        outputs[outputIndex].Name = newOutputName;
        GD.Print($"AudioOutputPatch:RenameOutput - Successfully renamed output '{oldName}' to '{newOutputName}' for device '{deviceName}' at index {outputIndex} in patch '{Name}'.");
        return true;
    }


    public Hashtable GetData()
    {
        var data = new Hashtable();
        data.Add("Id", Id);
        data.Add("Name", Name);

        var channelsData = new Hashtable();
        foreach (var channel in Channels)
        {
            channelsData.Add(channel.Key, channel.Value);
        }
        data.Add("Channels", channelsData);

        var outputDevicesData = new Hashtable();
        foreach (var device in OutputDevices)
        {
            var outputsArray = new ArrayList();
            foreach (var output in device.Value)
            {
                var outputData = new Hashtable();
                outputData.Add("Name", output.Name);
                var routedChannelsArray = new ArrayList(output.RoutedChannels);
                outputData.Add("RoutedChannels", routedChannelsArray);
                outputsArray.Add(outputData);
            }
            outputDevicesData.Add(device.Key, outputsArray);
        }
        data.Add("OutputDevices", outputDevicesData);

        return data;
    }
}