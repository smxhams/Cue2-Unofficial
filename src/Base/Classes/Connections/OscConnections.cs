using System.Net;
using Godot;
using Rug.Osc;

namespace Cue2.Base.Classes.Connections;

/// <summary>
/// OscConnections manages Osc clients and the transmission of OSC messages.
/// </summary>
public partial class OscConnections : Node
{
    private static OscSender _sender;
    private IPAddress _address;
    
    public override void _Ready()
    {
        _address = IPAddress.Loopback;
        _sender = new OscSender(_address, 8000);
        _sender.Connect();
        GD.Print("OscConnections: Sender initialized and connected to 127.0.0.1:8000");
    }

    public static void SendTestMessage()
    {
        if (_sender == null)
        {
            GD.Print("OscConnections: Sender is null, cannot send");
            return;
        }
        var message = new OscMessage("/cue/play", 1);
        _sender.Send(message);
        GD.Print("OscConnections:SendTestMessage - Sent /cue/play 1");
    }

    public override void _ExitTree()
    {
        _sender?.Close();
    }
}