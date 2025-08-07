using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cue2.Base.Classes.Devices;
using Godot;

namespace Cue2.Base.Classes;

// Class representing a patch, stored inside is each audio output channel - Channel as class. Which contains informtation of where each channel should output. 
public partial class AudioOutputPatch : Godot.GodotObject
{
    private static int _nextId = 0;
    
    public int Id { get; set; }
    public string Name { get; set; }
    public Dictionary<string, Dictionary<int, List<int>>> OutputDevices { get; set; }
    // <Device name, <device output channnel, list of audio channel ID's>>
    public Dictionary<int, string> Channels { get; set; } // List of audio channels>
    private int _channelId { get; set; } = 0;
    
    

    public AudioOutputPatch()
    {
        Id = _nextId++;
        Name = "Unnamed";
        OutputDevices = new Dictionary<string, Dictionary<int, List<int>>>();
        Channels = new Dictionary<int, string>();
        
        // Add some default blank channels.
        Channels.Add(_channelId++, "Left");
        Channels.Add(_channelId++, "Right");
    }

    public AudioOutputPatch(string name) 
    {
        Id = _nextId++;
        Name = name;
        OutputDevices = new Dictionary<string, Dictionary<int, List<int>>>();
        Channels = new Dictionary<int, string>();
        
        // Add some default blank channels.
        Channels.Add(_channelId++, "Left");
        Channels.Add(_channelId++, "Right");
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


    /*

    public AudioOutputPatch(string name, int id, Godot.Collections.Dictionary<int, Godot.Collections.Dictionary<string, bool>> channelData)
    {
        GD.Print("Patch name is: " + name);
        Id = id;
        if (id >= _nextId) _nextId = id + 1;
        Name = name;
        Channels = new Dictionary<int, Channel>(); // Initialises with 6 channels by default
        foreach (var channel in channelData)
        {
            Channels[channel.Key] = new Channel(channel.Value);
            //Channels[i] = new Channel(); // Default "unassigned" state
        }
    }

    public class DeviceChannelState
    {
        public int DeviceId { get; set; }
        public int DeviceChannel { get; set; }
        public bool IsActive { get; set; }

        // TOMORROW SAM, changing data structure to use this.
    }

    public void SetChannel(int channelIndex, string key, bool value)
    {
        if (!Channels.ContainsKey(channelIndex))
        {
            Channels.Add(channelIndex, new Channel());
        }
        if (!Channels[channelIndex].Outputs.ContainsKey(key))
        {
            Channels[channelIndex].Outputs.Add(key, value);
            //GD.Print("Created new patch on Ch: "  + channelIndex + " at: " + key);
            return;
        }
        Channels[channelIndex].Outputs[key] = value;
        //GD.Print("Updated patch on Ch: "  + channelIndex + " at: " + key);

    }

    public Channel GetChannel(int channelNumber)
    {
        return Channels[channelNumber];
    }

    public Hashtable GetData()
    {
        var dict = new Hashtable();
        dict.Add("Id", Id.ToString());
        dict.Add("Name", Name);
        var formattedChannels = new Hashtable();
        foreach (var channel in Channels)
        {
            formattedChannels.Add(channel.Key, channel.Value.Outputs);
        }
        dict.Add("Channels", formattedChannels);
        return dict;

    }*/

}