#nullable enable
using Godot;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.IO;
using Cue2.Shared;

namespace Cue2.Base.Classes.Connections;

/// <summary>
/// Manages a single ESP32-based cue light with persistent TCP connection.
/// Supports Ethernet/WiFi config, secure credential sending, and LED commands.
/// </summary>
public partial class CueLight : GodotObject
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private bool _isConnected;
    private string _name;
    private string _connectionType; // "Ethernet" or "WiFi"
    private string _ipAddress;
    private int _port;
    private string _subnet;
    private string _gateway;
    private string _ssid;
    private string _password;
    private readonly string _encryptionKey = "f8237hr8hnfv3fH@#R"; // Matches SaveManager
    public bool IsIdentifying;
    private Timer? _heartbeatTimer; // For periodic handshakes

    public int Id { get; private set; }
    public string Name 
    { 
        get => _name;
        set 
        {
            _name = value;
            GD.Print($"CueLight:set_Name - Updated name to {_name} for CueLight ID {Id}"); //!!!
        } 
    }
    public string ConnectionType => _connectionType;
    public string IpAddress => _ipAddress;
    public int Port => _port;
    public string Subnet => _subnet;
    public string Gateway => _gateway;
    public string Ssid => _ssid;
    public string Password => _password; // Note: In production, avoid exposing; use secure storage
    public bool IsConnected => _isConnected;

    public CueLight()
    {
        // Blank constructor for Godot
    }
    
    public CueLight(int id)
    {
        Id = id;
        _name = $"CueLight_{id}";
        _connectionType = "Ethernet";
        _ipAddress = "192.168.1.100"; // Default for testing
        _port = 80;
        _subnet = "255.255.255.0";
        _gateway = "192.168.1.1";
        _ssid = "";
        _password = "";
        _isConnected = false;
        IsIdentifying = false;
    }

    /// <summary>
    /// Establishes a persistent TCP connection and starts heartbeat.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (_isConnected) return; // Already connected
        try
        {
            _client = new TcpClient();
            GD.Print($"CueLight:ConnectAsync - Connecting to {_name} ({_connectionType}) at {_ipAddress}:{_port}");
            await _client.ConnectAsync(_ipAddress, _port);
            _stream = _client.GetStream();
            _isConnected = true;

            // Start heartbeat timer (non-blocking)
            _heartbeatTimer = new Timer();
            _heartbeatTimer.Timeout += async () => await HandshakeAsync();
            _heartbeatTimer.WaitTime = 5.0; // 5s interval
            _heartbeatTimer.Autostart = true;
            //AddChild(_heartbeatTimer); // Add to scene tree for lifecycle

            GD.Print($"CueLight:ConnectAsync - Connected to {_name} at {_ipAddress}:{_port}", 0);
            await HandshakeAsync();
        }
        catch (SocketException ex)
        {
            _isConnected = false;
            GD.Print($"CueLight:ConnectAsync - Connection failed for {_name}: {ex.Message}", 2);
        }
        catch (Exception ex)
        {
            _isConnected = false;
            GD.Print($"CueLight:ConnectAsync - Unexpected error for {_name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Gracefully disconnects and stops heartbeat.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (!_isConnected) return;
        try
        {
            _isConnected = false;
            _stream?.Close();
            _client?.Close();
            _heartbeatTimer?.Stop();
            _heartbeatTimer?.QueueFree();
            GD.Print($"CueLight:DisconnectAsync - Disconnected from {_name}"); //!!!
            GD.Print($"CueLight:DisconnectAsync - Disconnected from {_name}", 0);
        }
        catch (Exception ex)
        {
            GD.Print($"CueLight:DisconnectAsync - Disconnect error for {_name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Configures Ethernet settings and sends to ESP32.
    /// </summary>
    public async Task SetEthernetAsync(string ip, string subnet, string gateway)
    {
        try
        {
            _connectionType = "Ethernet";
            _ipAddress = ip;
            _subnet = subnet;
            _gateway = gateway;
            string command = $"SET_ETHERNET:{ip}:{subnet}:{gateway}\n";
            await SendCommandAsync(command);
            GD.Print(
                $"CueLight:SetEthernetAsync - Configured Ethernet for {_name}: IP={ip}, Subnet={subnet}, Gateway={gateway}", 0);

            await DisconnectAsync();
            await ConnectAsync(); // Reconnect with new config
        }
        catch (Exception ex)
        {
            GD.Print(
                $"CueLight:SetEthernetAsync - Ethernet config error for {_name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Configures WiFi settings and sends encrypted credentials to ESP32.
    /// </summary>
    public async Task SetWiFiAsync(string ssid, string password)
    {
        try
        {
            _connectionType = "WiFi";
            _ssid = ssid;
            _password = password;
            string encrypted = EncryptCredentials(ssid, password);
            string command = $"SET_WIFI:{encrypted}\n";
            await SendCommandAsync(command);
            GD.Print(
                $"CueLight:SetWiFiAsync - Sent WiFi config for {_name} (SSID={ssid})", 0);

            await DisconnectAsync();
            // IP may change post-reconnect; user must update manually or via discovery
        }
        catch (Exception ex)
        {
            GD.Print(
                $"CueLight:SetWiFiAsync - WiFi config error for {_name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Sends handshake for connection validation/heartbeat.
    /// </summary>
    public async Task HandshakeAsync()
    {
        try
        {
            await SendCommandAsync("HANDSHAKE\n");
            if (IsIdentifying) await IdentifyAsync(true); // Restore if active
            GD.Print(
                $"CueLight:HandshakeAsync - Handshake OK for {_name}", 0);
        }
        catch (Exception ex)
        {
            _isConnected = false;
            GD.Print(
                $"CueLight:HandshakeAsync - Handshake failed for {_name}: {ex.Message}", 3); // Alert type
            await DisconnectAsync();
        }
    }

    /// <summary>
    /// Sends GO command (blink Color1, e.g., green).
    /// </summary>
    public async Task GoAsync()
    {
        try
        {
            await SendCommandAsync("GO\n");
            GD.Print(
                $"CueLight:GoAsync - Sent GO to {_name}", 0);
        }
        catch (Exception ex)
        {
            _isConnected = false;
            GD.Print(
                $"CueLight:GoAsync - GO error for {_name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Sends STANDBY command (hold Color2, e.g., yellow).
    /// </summary>
    public async Task StandbyAsync()
    {
        try
        {
            await SendCommandAsync("STANDBY\n");
            GD.Print(
                $"CueLight:StandbyAsync - Sent STANDBY to {_name}", 0);
        }
        catch (Exception ex)
        {
            _isConnected = false;
            GD.Print(
                $"CueLight:StandbyAsync - STANDBY error for {_name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Sends COUNTIN command (Color2 with countdown time).
    /// </summary>
    public async Task CountInAsync(float timeUntilGo)
    {
        try
        {
            await SendCommandAsync($"COUNTIN:{timeUntilGo:F1}\n");
            GD.Print(
                $"CueLight:CountInAsync - Sent COUNTIN ({timeUntilGo}s) to {_name}", 0);
        }
        catch (Exception ex)
        {
            _isConnected = false;
            GD.Print(
                $"CueLight:CountInAsync - COUNTIN error for {_name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Toggles IDENTIFY (blue LED chase).
    /// </summary>
    public async Task IdentifyAsync(bool enable)
    {
        try
        {
            IsIdentifying = enable;
            string state = enable ? "START" : "STOP";
            await SendCommandAsync($"IDENTIFY:{state}\n");
            GD.Print(
                $"CueLight:IdentifyAsync - IDENTIFY {state} for {_name}", 0);
        }
        catch (Exception ex)
        {
            _isConnected = false;
            GD.Print(
                $"CueLight:IdentifyAsync - IDENTIFY error for {_name}: {ex.Message}", 2);
        }
    }

    private async Task SendCommandAsync(string command)
    {
        if (!_isConnected || _stream == null)
            throw new InvalidOperationException("CueLight not connected");

        byte[] bytes = Encoding.ASCII.GetBytes(command);
        await _stream.WriteAsync(bytes, 0, bytes.Length);
        await _stream.FlushAsync();
    }

    private string EncryptCredentials(string ssid, string password)
    {
        try
        {
            using Aes aes = Aes.Create();
            byte[] key = Encoding.UTF8.GetBytes(_encryptionKey.PadRight(32, '\0').Substring(0, 32));
            aes.Key = key;
            aes.IV = new byte[16]; // Fixed IV for simplicity; randomize in prod
            using var encryptor = aes.CreateEncryptor();
            using var ms = new MemoryStream();
            using var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
            byte[] data = Encoding.UTF8.GetBytes($"{ssid}:{password}");
            cs.Write(data, 0, data.Length);
            cs.FlushFinalBlock();
            return Convert.ToBase64String(ms.ToArray());
        }
        catch (Exception ex)
        {
            GD.Print(
                $"CueLight:EncryptCredentials - Encryption failed: {ex.Message}", 2);
            return string.Empty;
        }
    }

    /// <summary>
    /// Serializes cue light data for saving.
    /// </summary>
    public Godot.Collections.Dictionary GetData()
    {
        return new Godot.Collections.Dictionary
        {
            { "Id", Id },
            { "Name", _name },
            { "ConnectionType", _connectionType },
            { "IpAddress", _ipAddress },
            { "Port", _port },
            { "Subnet", _subnet },
            { "Gateway", _gateway },
            { "Ssid", _ssid },
            { "Password", _password }, // Encrypt in prod if needed
            { "IsIdentifying", IsIdentifying }
        };
    }

    /// <summary>
    /// Deserializes cue light data for loading.
    /// </summary>
    public void SetData(Godot.Collections.Dictionary data)
    {
        if (data.ContainsKey("Id")) Id = data["Id"].AsInt32();
        if (data.ContainsKey("Name")) Name = data["Name"].ToString();
        if (data.ContainsKey("ConnectionType")) _connectionType = data["ConnectionType"].ToString();
        if (data.ContainsKey("IpAddress")) _ipAddress = data["IpAddress"].ToString();
        if (data.ContainsKey("Port")) _port = data["Port"].AsInt32();
        if (data.ContainsKey("Subnet")) _subnet = data["Subnet"].ToString();
        if (data.ContainsKey("Gateway")) _gateway = data["Gateway"].ToString();
        if (data.ContainsKey("Ssid")) _ssid = data["Ssid"].ToString();
        if (data.ContainsKey("Password")) _password = data["Password"].ToString();
        if (data.ContainsKey("IsIdentifying")) IsIdentifying = data["IsIdentifying"].AsBool();
    }
}