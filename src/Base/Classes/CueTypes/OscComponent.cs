using System;
using System.Threading.Tasks;
using Cue2.Base.Classes.Connections;
using Godot;
using Godot.Collections;
using Rug.Osc;

namespace Cue2.Base.Classes.CueTypes;


public class OscComponent : ICueComponent
{
    public string Type => "OscComponent";
    public int OscConnectionId;
    public string OscMessage;
    public CueOscConnection OscConnection;
    
    public async Task Execute()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(OscMessage))
            {
                GD.Print($"OscComponent: Invalid OSC message path: '{OscMessage}'");
                return;
            }
            if (!OscMessage.StartsWith("/"))
            {
                GD.Print($"OscComponent: OSC message path must start with '/': '{OscMessage}'");
                return;
            }
            var oscMes = new OscMessage(OscMessage, 1);
            OscConnection.SendMessage(oscMes);
        }
        catch (Exception ex)
        {
            GD.Print($"OscComponent: Failed to execute OSC component: {ex.Message}");
        }
        await Task.Delay(1); // Show UI for 1 second
    }
    
    public Dictionary GetData() 
    {
        return new Dictionary()
        {
            { "Command", OscMessage },
            { "OscConnectionId", OscConnectionId },
        };
    }

    public void LoadFromData(Dictionary data)
    {
        OscMessage = data.TryGetValue("Command", out var value) ? (string)value : OscMessage;
        OscConnectionId = data.TryGetValue("OscConnectionId", out value) ? (int)value : OscConnectionId;
    }
}