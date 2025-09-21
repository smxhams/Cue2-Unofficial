#nullable enable
using Godot;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Cue2.Shared;

namespace Cue2.Base.Classes.Connections;

/// <summary>
/// Manages a single ESP32-based cue light with persistent TCP connection.
/// Supports Ethernet/WiFi config, secure credential sending, and LED commands.
/// </summary>
public partial class CueLight : GodotObject, IDisposable
{
    private UdpClient? _udpClient;
    private bool _disposed = false;
    private readonly int _connectionTimeoutMs = 3000;

    public int Id { get; private set; }
    public string Name { get; set; }
    public string IpAddress { get; private set; }
    public int Port { get; private set; }
    public bool CueLightIsConnected { get; private set; }
    
    public CueLight(int id)
    {
        Id = id;
        Name = $"CueLight_{id}";
        IpAddress = "192.168.1.100"; // Default for testing
        Port = 80;
        CueLightIsConnected = false;
    }
    
    /// <summary>
    /// Deserializes cue light data from file loading.
    /// </summary>
    public CueLight(Godot.Collections.Dictionary data)
    {
        Id = data.ContainsKey("Id") ? data["Id"].AsInt32() : 0;
        Name = data.ContainsKey("Name") ? data["Name"].ToString() : $"CueLight_{Id}";
        IpAddress = data.ContainsKey("IpAddress") ? data["IpAddress"].ToString() : "192.168.1.100";
        Port = data.ContainsKey("Port") ? data["Port"].AsInt32() : 80;
        CueLightIsConnected = false;
    }
    
    /// <summary>
    /// Sends a PING command and awaits acknowledgment.
    /// </summary>
    public async Task<bool> PingAsync()
    {
        if (_disposed) return false;
        try
        {
            if (_udpClient == null)
            {
                _udpClient = new UdpClient();
                _udpClient.Client.ReceiveTimeout = _connectionTimeoutMs;
                CueLightIsConnected = false;
            }

            string pingCommand = "PING";
            byte[] sendBytes = Encoding.UTF8.GetBytes(pingCommand);
            await _udpClient.SendAsync(sendBytes, sendBytes.Length, IpAddress, Port);
            GD.Print($"CueLight:PingAsync - Sent {pingCommand} to {Name} at {IpAddress}:{Port}");

            var result = await _udpClient.ReceiveAsync();
            string response = Encoding.UTF8.GetString(result.Buffer).Trim();
            if (response == "PONG")
            {
                CueLightIsConnected = true;
                GD.Print($"CueLight:PingAsync - Received PONG:{Id} from {Name}");
                return true;
            }
            else
            {
                CueLightIsConnected = false;
                GD.PrintErr($"CueLight:PingAsync - Invalid response from {Name}: {response}");
                return false;
            }
        }
        catch (Exception ex)
        {
            CueLightIsConnected = false;
            GD.PrintErr($"CueLight:PingAsync - Error pinging {Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sends a command and awaits acknowledgment.
    /// </summary>
    public async Task<bool> SendCommandAsync(string command)
    {
        if (_disposed) return false;
        try
        {
            if (_udpClient == null)
            {
                _udpClient = new UdpClient();
                _udpClient.Client.ReceiveTimeout = _connectionTimeoutMs;
                CueLightIsConnected = false;
            }

            byte[] sendBytes = Encoding.UTF8.GetBytes(command);
            await _udpClient.SendAsync(sendBytes, sendBytes.Length, IpAddress, Port);
            GD.Print($"CueLight:SendCommandAsync - Sent {command} to {Name} at {IpAddress}:{Port}");

            var result = await _udpClient.ReceiveAsync();
            string response = Encoding.UTF8.GetString(result.Buffer).Trim();
            if (response == "OK" || response == "ACK") 
            {
                CueLightIsConnected = true;
                GD.Print($"CueLight:SendCommandAsync - Received OK for {command} from {Name}");
                return true;
            }
            else
            {
                CueLightIsConnected = false;
                GD.PrintErr($"CueLight:SendCommandAsync - Error response for {command} from {Name}: {response}");
                return false;
            }
        }
        catch (Exception ex)
        {
            CueLightIsConnected = false;
            GD.PrintErr($"CueLight:SendCommandAsync - Error sending {command} to {Name}: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Sets IP address
    /// </summary>
    public async void SetIpAddressAsync(string newIp)
    {
        if (IpAddress == newIp) return;
        IpAddress = newIp;
    }
    
    /// <summary>
    /// Disposes of resources.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _udpClient?.Close();
            _udpClient?.Dispose();
            _udpClient = null;
            CueLightIsConnected = false;
            GD.Print($"CueLight:DisposeAsync - Disposed CueLight {Id} ({Name})");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"CueLight:DisposeAsync - Error disposing CueLight {Id} ({Name}): {ex.Message}");
        }
    }

    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }
    

    /// <summary>
    /// Serializes cue light data for saving.
    /// </summary>
    public Godot.Collections.Dictionary GetData()
    {
        return new Godot.Collections.Dictionary
        {
            { "Id", Id },
            { "Name", Name },
            { "IpAddress", IpAddress },
            { "Port", Port },
        };
    }
    
    
    // ~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-~-
    // Commands
    

    /// <summary>
    /// Configures colors and brightness for the CueLight.
    /// </summary>
    /// <param name="idleColor">Idle color (RGB).</param>
    /// <param name="goColor">Go color (RGB).</param>
    /// <param name="standbyColor">Standby color (RGB).</param>
    /// <param name="countInColor">Count-in color (RGB).</param>
    /// <param name="brightness">Brightness (0-255).</param>
    public async Task<bool> ConfigureAsync(Color idleColor, Color goColor, Color standbyColor, Color countInColor, byte brightness)
    {
        string command = $"CONFIG:{Name}:" + 
                         $"{(byte)(idleColor.R * 255)},{(byte)(idleColor.G * 255)},{(byte)(idleColor.B * 255)}:" + 
                         $"{(byte)(goColor.R * 255)},{(byte)(goColor.G * 255)},{(byte)(goColor.B * 255)}:" + 
                         $"{(byte)(standbyColor.R * 255)},{(byte)(standbyColor.G * 255)},{(byte)(standbyColor.B * 255)}:" + 
                         $"{(byte)(countInColor.R * 255)},{(byte)(countInColor.G * 255)},{(byte)(countInColor.B * 255)}:" + 
                         $"{brightness}";
        return await SendCommandAsync(command);
    }
    
    /// <summary>
    /// Triggers GO state with optional display text (max 5 chars).
    /// </summary>
    /// <param name="text">Optional TFT text (defaults to empty).</param>
    public async Task<bool> GoAsync(string text = "")
    {
        if (text.Length > 5) text = text.Substring(0, 5); //!!! Ensure max 5 chars
        return await SendCommandAsync($"GO:{text}");
    }

    /// <summary>
    /// Triggers STANDBY state with optional display text (max 5 chars).
    /// </summary>
    /// <param name="text">Optional TFT text (defaults to empty).</param>
    public async Task<bool> StandbyAsync(string text = "")
    {
        if (text.Length > 5) text = text.Substring(0, 5); //!!! Ensure max 5 chars
        return await SendCommandAsync($"STANDBY:{text}");
    }
    
    /// <summary>
    /// Cancels STANDBY state.
    /// </summary>
    public async Task<bool> CancelAsync() 
    {
        return await SendCommandAsync("CANCEL");
    }

    /// <summary>
    /// Triggers COUNTIN state with duration and optional display text.
    /// </summary>
    /// <param name="timeUntilGo">Duration in seconds.</param>
    /// <param name="text">Optional TFT text (defaults to empty).</param>
    public async Task<bool> CountInAsync(float timeUntilGo, string text = "")
    {
        if (timeUntilGo <= 0)
        {
            GD.PrintErr($"CueLight:CountInAsync - Invalid duration for {Name}: {timeUntilGo}");
            return false;
        }
        if (text.Length > 5) text = text.Substring(0, 5); //!!! Ensure max 5 chars
        return await SendCommandAsync($"COUNTIN:{timeUntilGo}:{text}"); //!!! Updated to COUNTIN
    }

    /// <summary>
    /// Toggles identification mode on the cue light via UDP.
    /// </summary>
    public async Task<bool> IdentifyAsync(bool on)
    {
        return await SendCommandAsync($"IDENTIFY:{(on ? "START" : "STOP")}"); //!!! Aligned with Arduino
    }
}