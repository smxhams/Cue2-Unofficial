using Godot;
using Rug.Osc;
using System.Threading;

namespace Cue2.Base.Classes.Connections;

/// <summary>
/// OscListener manages the receiving of Osc messages, parses and manages actions related to messages. 
/// </summary>
public partial class OscListener : Node
{
    private OscReceiver _receiver;
    private Thread _thread;
    private bool _running = true;

    public override void _Ready()
    {
        _receiver = new OscReceiver(8000);
        _receiver.Connect();
        _thread = new Thread(() =>
        {
            while (_running)
            {
                try
                {
                    OscPacket packet = _receiver.Receive();
                    if (packet is OscMessage oscMessage)
                    {
                        CallDeferred("OnMessageReceived", oscMessage.ToString());
                    }
                }
                catch
                {
                    break;
                }
            }
        });
        _thread.Start();
    }

    private void OnMessageReceived(string message)
    {
        GD.Print($"TestOscReceiver: Received {message}");
        // Handle message, e.g., if (message.Contains("/cue/play")) { /* trigger cue */ }
    }

    public override void _ExitTree()
    {
        _running = false;
        _receiver?.Close();
        _thread?.Join();
    }
}