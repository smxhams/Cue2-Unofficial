using Godot;
using Rug.Osc;
using System;
using System.Threading;
using Cue2.Shared;
using Godot.Collections;

namespace Cue2.Base.Classes.Connections;

/// <summary>
/// OscListen manages the receiving of Osc messages, parses and manages actions related to messages. 
/// </summary>
public partial class OscListen : Node
{
    private GlobalSignals _globalSignals;
    
    private static OscReceiver _receiver;
    private static Thread _thread;
    private static bool _running = false;
    public static int Port = 7001;
    public static string SessionName = "";


    public static bool OscListenEnabled = false;

    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        
        if (OscListenEnabled)
        {
            StartListening();
        }
    }


    private static void StartListening()
    {
        if (_running) return;
        try
        {
            _receiver = new OscReceiver(Port);
            _receiver.Connect();
            GD.Print($"OscListener: Receiver connected successfully on port {Port}");
        }
        catch (Exception ex)
        {
            GD.Print($"OscListen: Failed to connect receiver: {ex.Message}");
            return;
        }
        _running = true;
        _thread = new Thread(() =>
        {
            GD.Print("OscListen: Thread started, waiting for messages");
            while (_running)
            {
                try
                {
                    OscPacket packet = _receiver.Receive();
                    GD.Print("OscListen: Received a packet");
                    if (packet is OscMessage oscMessage)
                    {
                        GD.Print("OscListen: Packet is OscMessage, deferring");
                        // Since static, need to defer on an instance, but for now, just log
                        GD.Print($"OscListen: Message: {oscMessage}");
                    }
                    else
                    {
                        GD.Print("OscListen: Packet is not OscMessage");
                    }
                }
                catch (Exception ex)
                {
                    GD.Print($"OscListen: Exception in receive: {ex.Message}");
                    break;
                }
            }
            GD.Print("OscListen: Thread exiting");
        });
        _thread.Start();
    }

    private static void StopListening()
    {
        if (!_running) return;
        _running = false;
        _receiver?.Close();
        _thread?.Join();
        _receiver = null;
        _thread = null;
        GD.Print("OscListen: Stopped listening");
    }

    public static void SetEnabled(bool enabled)
    {
        OscListenEnabled = enabled;
        if (enabled)
        {
            StartListening();
        }
        else
        {
            StopListening();
        }
    }

    public static void SetPort(int port)
    {
        Port = port;
        if (OscListenEnabled)
        {
            StopListening();
            StartListening();
        }
    }


    public Dictionary GetData()
    {
        var saveDict = new Dictionary();
        
        saveDict.Add("OscListenEnabled", OscListenEnabled);
        saveDict.Add("Port", Port);
        saveDict.Add("SessionName", SessionName);
        
        return saveDict;
    }

    public void LoadFromData(Dictionary OscListenData)
    {
        OscListenEnabled = OscListenData.TryGetValue("OscListenEnabled", out var value) ? (bool)value : OscListenEnabled;
        SetEnabled(OscListenEnabled);
        Port = OscListenData.TryGetValue("Port", out value) ? (int)value : Port;
        SessionName = OscListenData.TryGetValue("SessionName", out value) ? (string)value : SessionName;
    }

    /// <summary>
    /// Restores OSC listen defaults for a new empty session.
    /// </summary>
    public void ResetToDefaults()
    {
        SetEnabled(false);
        OscListenEnabled = false;
        Port = 7001;
        SessionName = string.Empty;
        GD.Print("OscListen:ResetToDefaults - OSC listen disabled, port 7001.");
    }
    
    public override void _ExitTree()
    {
        StopListening();
    }
}