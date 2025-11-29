using System;
using System.Collections.Generic;
using System.Net;
using Godot;
using Godot.Collections;
using Rug.Osc;

namespace Cue2.Base.Classes.Connections;

/// <summary>
/// OscConnections manages Osc clients and the transmission of OSC messages.
/// </summary>
public partial class OscConnections : Node
{
    private static OscSender _sender;
    private IPAddress _address;
    
    public static List<CueOscConnection> Connections { get; set; } = new List<CueOscConnection>();
    
    public override void _Ready()
    {
        _address = IPAddress.Loopback;
        _sender = new OscSender(_address, 7001);
        _sender.Connect();
        GD.Print("OscConnections: Sender initialized and connected to 127.0.0.1:8000");
    }

    private static void SendMessage(OscMessage message)
    {
        if (_sender == null)
        {
            GD.Print("OscConnections: Sender is null, cannot send");
            return;
        }
        try
        {
            _sender.Close();
            _sender.Connect();
            _sender.Send(message);
            GD.Print($"OscConnections: Sent {message}");
        }
        catch (Exception ex)
        {
            GD.Print($"OscConnections: Failed to send message: {ex.Message}");
        }
    }

    public static void SendTestMessage()
    {
        var message = new OscMessage("/cue/play", 1);
        SendMessage(message);
    }

    public override void _ExitTree()
    {
        _sender?.Close();
    }

    public static CueOscConnection CreateConnection(string name = "Osc", IPAddress address = null, int port = 7002, string networkInterface = "")
    {
        var connection = new CueOscConnection
        {
            Id = CueOscConnection._nextId++,
            Name = name ?? $"OSC {CueOscConnection._nextId}",
            Address = address ?? IPAddress.Loopback,
            Port = port,
            NetworkInterface = networkInterface
        };
        Connections.Add(connection);
        GD.Print($"OscConnections: Created connection '{connection.Name}' to {connection.Address}:{connection.Port}");
        return connection;
    }

    public static bool DeleteConnection(int id)
    {
        var connection = Connections.Find(c => c.Id == id);
        if (connection != null)
        {
            Connections.Remove(connection);
            GD.Print($"OscConnections: Deleted connection '{connection.Name}' (ID: {id})");
            return true;
        }
        GD.Print($"OscConnections: Connection with ID {id} not found");
        return false;
    }

    public Dictionary GetData()
    {
        var dict = new Dictionary();
        var connectionsArray = new Godot.Collections.Array();
        foreach (var connection in Connections)
        {
            connectionsArray.Add(connection.GetData());
        }
        dict["OscConnections"] = connectionsArray;
        return dict;
    }

    public void LoadFromData(Dictionary data)
    {
        if (data.TryGetValue("OscConnections", out var value) && value.As<Godot.Collections.Array>() is Godot.Collections.Array connectionsArray)
        {
            Connections.Clear();
            CueOscConnection._nextId = 0; // Reset ID counter
            foreach (var item in connectionsArray)
            {
                if (item.As<Dictionary>() is Dictionary dict)
                {
                    var connection = new CueOscConnection();
                    connection.LoadFromData(dict);
                    Connections.Add(connection);
                }
            }
            GD.Print($"OscConnections: Loaded {Connections.Count} connections");
        }
    }
    
    
}

public partial class CueOscConnection : GodotObject
{
    public static int _nextId = 0;
    public int Id { get; set; }
    public string Name = $"Osc";
    public string NetworkInterface { get; set; }
    public IPAddress Address { get; set; }
    public int Port { get; set; }

    public Dictionary GetData()
    {
        var dict = new Dictionary();
        dict["Id"] = Id;
        dict["Name"] = Name;
        dict["NetworkInterface"] = NetworkInterface;
        dict["Address"] = Address?.ToString() ?? "";
        dict["Port"] = Port;
        return dict;
    }

    public void LoadFromData(Dictionary data)
    {
        Id = data.TryGetValue("Id", out var value) ? (int)value : _nextId++;
        if (Id >= _nextId) _nextId = Id + 1;
        Name = data.TryGetValue("Name", out value) ? (string)value : Name;
        NetworkInterface = data.TryGetValue("NetworkInterface", out value) ? (string)value : NetworkInterface;
        Address = data.TryGetValue("Address", out value) && !string.IsNullOrEmpty((string)value) ? IPAddress.Parse((string)value) : IPAddress.Loopback;
        Port = data.TryGetValue("Port", out value) ? (int)value : Port;
    }
    
}