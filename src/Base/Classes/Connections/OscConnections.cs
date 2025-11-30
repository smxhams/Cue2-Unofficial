using System;
using System.Collections.Generic;
using System.Linq;
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
    public static List<CueOscConnection> Connections { get; set; } = new List<CueOscConnection>();
    
    public override void _Ready()
    {
        GD.Print("OscConnections: Sender initialized and connected to 127.0.0.1:8000");
    }

    public static CueOscConnection GetCueOscConnection(int id) => Connections.Find(c => c.Id == id);
    

    public override void _ExitTree()
    {
        foreach (var connection in Connections)
        {
            connection.CloseConnection();
        }
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
        connection.InitialiseSender();
        GD.Print($"OscConnections: Created connection '{connection.Name}' to {connection.Address}:{connection.Port}");
        return connection;
    }

    public static bool DeleteConnection(int id)
    {
        var connection = Connections.Find(c => c.Id == id);
        if (connection != null)
        {
            Connections.Remove(connection);
            connection.CloseConnection();
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
                    connection.InitialiseSender();
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
    
    private OscSender _sender;

    public void InitialiseSender()
    {
        try
        {
            // Close existing sender if already connected
            if (_sender != null)
            {
                _sender.Close();
                _sender.Dispose();
                _sender = null;
            }

            IPAddress localAddress = IPAddress.Any;
            if (!string.IsNullOrEmpty(NetworkInterface))
            {
                var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                var ni = interfaces.FirstOrDefault(i => i.Name == NetworkInterface);
                if (ni != null)
                {
                    var ipProps = ni.GetIPProperties();
                    var ipv4 = ipProps.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    if (ipv4 != null)
                    {
                        localAddress = ipv4.Address;
                    }
                    else
                    {
                        GD.Print($"CueOscConnection: No IPv4 address found for interface '{NetworkInterface}'");
                    }
                }
                else
                {
                    GD.PrintErr($"CueOscConnection: Network interface '{NetworkInterface}' not found");
                }
            }
            if (!string.IsNullOrEmpty(NetworkInterface))
            {
                _sender = new OscSender(localAddress, Address, Port);
                GD.Print($"CueOscConnection:InitialiseSender - Connected via interface {NetworkInterface} to {Name}@{Address}:{Port} from {localAddress}");
            }
            else
            {
                _sender = new OscSender(Address, Port);
                GD.Print($"CueOscConnection:InitialiseSender - Connected via automatic to {Name}@{Address}:{Port}");
            }
            _sender.Connect();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"CueOscConnection: Failed to initialize sender for connection '{Name}': {ex.Message}");
            _sender = null;
        }
    }

    /// <summary>
    /// Reconnects the OSC sender with the current properties.
    /// </summary>
    public void Reconnect()
    {
        CloseConnection();
        InitialiseSender();
    }

    public void SendMessage(OscMessage message)
    {
        if (_sender != null)
        {
            try
            {
                _sender.Send(message);
                GD.Print($"CueOscConnection: Sent {message} from {NetworkInterface}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"CueOscConnection: Failed to send {message}: {ex.Message}");
            }
        }
        else
        {
            GD.PrintErr("CueOscConnection: Sender not initialized");
        }
    }

    public void CloseConnection()
    {
        _sender?.Close();
        _sender?.Dispose();
        _sender = null;
    }

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
        GD.Print($"CueOscConnection: Loaded {Id} connection: Name: {Name}, NetworkInterface: {NetworkInterface},  Address: {Address}, Port: {Port}");
    }
    
}