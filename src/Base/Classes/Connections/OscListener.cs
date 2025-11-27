using Godot;
using Rug.Osc;
using System;
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
        GD.Print($"OscListener:_Ready - Initializing OscReceiver");
        _receiver = new OscReceiver(8000);
        _receiver.Connect();
        _thread = new Thread(() =>
        {
            GD.Print("OscListener: Thread started, waiting for messages");
            while (_running)
            {
                try
                {
                    OscPacket packet = _receiver.Receive();
                    GD.Print("OscListener: Received a packet");
                    if (packet is OscMessage oscMessage)
                    {
                        GD.Print("OscListener: Packet is OscMessage, deferring");
                        CallDeferred("OnMessageReceived", oscMessage.ToString());
                    }
                    else
                    {
                        GD.Print("OscListener: Packet is not OscMessage");
                    }
                }
                catch (Exception ex)
                {
                    GD.Print($"OscListener: Exception in receive: {ex.Message}");
                    break;
                }
            }
            GD.Print("OscListener: Thread exiting");
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