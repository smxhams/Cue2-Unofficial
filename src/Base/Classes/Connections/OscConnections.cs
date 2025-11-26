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
    }

    public static void SendTestMessage()
    {
        var message = new OscMessage("/cue/play", 1);
        _sender.Send(message);
        GD.Print("TestOscSender: Sent /cue/play 1");
    }

    public override void _ExitTree()
    {
        _sender?.Close();
    }
}