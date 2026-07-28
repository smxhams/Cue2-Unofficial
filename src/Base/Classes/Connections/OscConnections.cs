//==================================================================================//
// OscConnections.cs                                                                //
// This file is part of Cue2                                                        //
// http://cue2.live/                                                                //
//==================================================================================//

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Cue2.Shared;
using Godot;
using Godot.Collections;
using Rug.Osc;

namespace Cue2.Base.Classes.Connections;

/// <summary>
/// Manages named OSC send connections and transmits OSC messages for cue components.
/// Also exposes a send-side monitor log (mirrors MIDI monitor patterns).
/// </summary>
public partial class OscConnections : Node
{
    private GlobalSignals _globalSignals;

    /// <summary>Session OSC send connections (order preserved).</summary>
    public static System.Collections.Generic.List<CueOscConnection> Connections { get; set; } = new();

    private bool _monitorEnabled = true;
    private readonly ConcurrentQueue<string> _pendingLogLines = new();

    /// <summary>Maximum lines retained in the in-memory monitor buffer.</summary>
    public const int MaxMonitorLines = 500;

    /// <summary>When true, sent OSC messages are queued for the monitor log UI.</summary>
    public bool MonitorEnabled
    {
        get => _monitorEnabled;
        set
        {
            if (_monitorEnabled == value) return;
            _monitorEnabled = value;
            EmitSignal(SignalName.OscConnectionsStateChanged);
        }
    }

    /// <summary>Fired when connections are added/removed/reloaded or monitor toggles.</summary>
    [Signal]
    public delegate void OscConnectionsStateChangedEventHandler();

    /// <summary>
    /// Fired on the main thread for each send monitor line (timestamped summary).
    /// </summary>
    [Signal]
    public delegate void OscSendMonitorLineEventHandler(string line);

    public override void _Ready()
    {
        _globalSignals = GetNodeOrNull<GlobalSignals>("/root/GlobalSignals");
        GD.Print("OscConnections:_Ready - OSC send connections ready.");
    }

    public override void _Process(double delta)
    {
        int drained = 0;
        while (drained < 80 && _pendingLogLines.TryDequeue(out string line))
        {
            EmitSignal(SignalName.OscSendMonitorLine, line);
            drained++;
        }
    }

    public static CueOscConnection GetCueOscConnection(int id) =>
        Connections.Find(c => c.Id == id);

    public override void _ExitTree()
    {
        foreach (var connection in Connections.ToList())
        {
            try
            {
                connection.CloseConnection();
                if (GodotObject.IsInstanceValid(connection))
                    connection.Free();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"OscConnections:_ExitTree - {ex.Message}");
            }
        }
        Connections.Clear();
    }

    /// <summary>
    /// Creates a new OSC send connection, opens its sender, and notifies UI.
    /// </summary>
    public static CueOscConnection CreateConnection(
        string name = "Osc",
        IPAddress address = null,
        int port = 7002,
        string networkInterface = "")
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
        GD.Print($"OscConnections:CreateConnection - '{connection.Name}' → {connection.Address}:{connection.Port}");

        var node = Engine.GetMainLoop() is SceneTree tree
            ? tree.Root.GetNodeOrNull<OscConnections>("/root/OscConnections")
            : null;
        node?.NotifyConnectionsChanged($"— Connection created: {connection.Name} → {connection.Address}:{connection.Port}");
        return connection;
    }

    /// <summary>
    /// Removes and disposes a connection by id.
    /// </summary>
    public static bool DeleteConnection(int id)
    {
        var connection = Connections.Find(c => c.Id == id);
        if (connection == null)
        {
            GD.Print($"OscConnections:DeleteConnection - ID {id} not found");
            return false;
        }

        string label = connection.Name;
        Connections.Remove(connection);
        connection.CloseConnection();
        if (GodotObject.IsInstanceValid(connection))
            connection.Free();
        GD.Print($"OscConnections:DeleteConnection - '{label}' (ID: {id})");

        var node = Engine.GetMainLoop() is SceneTree tree
            ? tree.Root.GetNodeOrNull<OscConnections>("/root/OscConnections")
            : null;
        node?.NotifyConnectionsChanged($"— Connection deleted: {label}");
        return true;
    }

    /// <summary>Notifies listeners that the connection list changed (main thread).</summary>
    public void NotifyConnectionsChanged(string monitorNotice = null)
    {
        if (!string.IsNullOrEmpty(monitorNotice))
            EnqueueMonitorLine(monitorNotice);
        EmitSignal(SignalName.OscConnectionsStateChanged);
    }

    /// <summary>
    /// Called by <see cref="CueOscConnection"/> after a successful send for monitor logging.
    /// </summary>
    public void NotifyMessageSent(CueOscConnection connection, OscMessage message)
    {
        if (!_monitorEnabled || connection == null || message == null) return;
        string line = FormatSendLine(connection, message);
        _pendingLogLines.Enqueue(line);
    }

    /// <summary>
    /// Called by <see cref="CueOscConnection"/> when a send fails.
    /// </summary>
    public void NotifySendError(CueOscConnection connection, string error)
    {
        string name = connection?.Name ?? "?";
        EnqueueMonitorLine($"— Send failed [{name}]: {error}");
    }

    /// <summary>Clears any pending monitor log lines that have not yet been emitted.</summary>
    public void ClearPendingMonitorLines()
    {
        while (_pendingLogLines.TryDequeue(out _)) { }
    }

    private void EnqueueMonitorLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        string stamped = $"{DateTime.Now:HH:mm:ss.fff}  {line}";
        _pendingLogLines.Enqueue(stamped);
    }

    private static string FormatSendLine(CueOscConnection connection, OscMessage message)
    {
        var sb = new StringBuilder(128);
        sb.Append(DateTime.Now.ToString("HH:mm:ss.fff"));
        sb.Append("  [");
        sb.Append(connection.Name ?? "OSC");
        sb.Append(" → ");
        sb.Append(connection.Address);
        sb.Append(':');
        sb.Append(connection.Port);
        sb.Append("] ");
        sb.Append(message.Address);
        string args = OscListen.FormatArgs(message);
        if (!string.IsNullOrEmpty(args))
        {
            sb.Append("  ");
            sb.Append(args);
        }
        return sb.ToString();
    }

    public Dictionary GetData()
    {
        var dict = new Dictionary();
        var connectionsArray = new Godot.Collections.Array();
        foreach (var connection in Connections)
            connectionsArray.Add(connection.GetData());
        dict["OscConnections"] = connectionsArray;
        dict["MonitorEnabled"] = _monitorEnabled;
        return dict;
    }

    public void LoadFromData(Dictionary data)
    {
        if (data == null) return;

        if (data.TryGetValue("MonitorEnabled", out var monVar))
            _monitorEnabled = monVar.AsBool();

        if (data.TryGetValue("OscConnections", out var value)
            && value.As<Godot.Collections.Array>() is Godot.Collections.Array connectionsArray)
        {
            // Dispose existing
            foreach (var connection in Connections.ToList())
            {
                try
                {
                    connection.CloseConnection();
                    if (GodotObject.IsInstanceValid(connection))
                        connection.Free();
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"OscConnections:LoadFromData - dispose: {ex.Message}");
                }
            }
            Connections.Clear();
            CueOscConnection._nextId = 0;

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
            GD.Print($"OscConnections:LoadFromData - Loaded {Connections.Count} connection(s)");
        }

        EmitSignal(SignalName.OscConnectionsStateChanged);
    }

    /// <summary>
    /// Closes and removes all OSC send connections (New Session).
    /// </summary>
    public void ClearAll()
    {
        foreach (var connection in Connections.ToList())
        {
            try
            {
                connection.CloseConnection();
                if (GodotObject.IsInstanceValid(connection))
                    connection.Free();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"OscConnections:ClearAll - {ex.Message}");
            }
        }
        Connections.Clear();
        CueOscConnection._nextId = 0;
        _monitorEnabled = true;
        EmitSignal(SignalName.OscConnectionsStateChanged);
        GD.Print("OscConnections:ClearAll - All OSC connections cleared.");
    }
}

/// <summary>
/// A named OSC UDP sender (destination IP/port + optional local network interface).
/// </summary>
public partial class CueOscConnection : GodotObject
{
    public static int _nextId = 0;
    public int Id { get; set; }
    public string Name = "Osc";
    public string NetworkInterface { get; set; }
    public IPAddress Address { get; set; } = IPAddress.Loopback;
    public int Port { get; set; } = 7002;

    private OscSender _sender;

    /// <summary>True when the underlying UDP sender has been opened.</summary>
    public bool IsSenderOpen => _sender != null;

    public void InitialiseSender()
    {
        try
        {
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
                    var ipv4 = ipProps.UnicastAddresses.FirstOrDefault(a =>
                        a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    if (ipv4 != null)
                        localAddress = ipv4.Address;
                    else
                        GD.Print($"CueOscConnection:InitialiseSender - No IPv4 for interface '{NetworkInterface}'");
                }
                else
                {
                    GD.PrintErr($"CueOscConnection:InitialiseSender - Interface '{NetworkInterface}' not found");
                }
            }

            if (!string.IsNullOrEmpty(NetworkInterface))
            {
                _sender = new OscSender(localAddress, Address, Port);
                GD.Print($"CueOscConnection:InitialiseSender - Via {NetworkInterface} → {Name}@{Address}:{Port} from {localAddress}");
            }
            else
            {
                _sender = new OscSender(Address, Port);
                GD.Print($"CueOscConnection:InitialiseSender - Auto → {Name}@{Address}:{Port}");
            }
            _sender.Connect();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"CueOscConnection:InitialiseSender - '{Name}': {ex.Message}");
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
        var manager = Engine.GetMainLoop() is SceneTree tree
            ? tree.Root.GetNodeOrNull<OscConnections>("/root/OscConnections")
            : null;

        if (_sender == null)
        {
            GD.PrintErr("CueOscConnection:SendMessage - Sender not initialized");
            manager?.NotifySendError(this, "Sender not initialized");
            return;
        }

        try
        {
            _sender.Send(message);
            GD.Print($"CueOscConnection:SendMessage - {message} via {Name}");
            manager?.NotifyMessageSent(this, message);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"CueOscConnection:SendMessage - Failed {message}: {ex.Message}");
            manager?.NotifySendError(this, ex.Message);
        }
    }

    public void CloseConnection()
    {
        try
        {
            _sender?.Close();
            _sender?.Dispose();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"CueOscConnection:CloseConnection - {ex.Message}");
        }
        _sender = null;
    }

    public Dictionary GetData()
    {
        var dict = new Dictionary();
        dict["Id"] = Id;
        dict["Name"] = Name;
        dict["NetworkInterface"] = NetworkInterface ?? string.Empty;
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
        Address = data.TryGetValue("Address", out value) && !string.IsNullOrEmpty((string)value)
            ? IPAddress.Parse((string)value)
            : IPAddress.Loopback;
        Port = data.TryGetValue("Port", out value) ? (int)value : Port;
        GD.Print($"CueOscConnection:LoadFromData - {Id}: {Name} {Address}:{Port}");
    }
}
