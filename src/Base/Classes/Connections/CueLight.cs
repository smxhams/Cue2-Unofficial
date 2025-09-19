using Godot;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.IO;
using Cue2.Shared;

namespace Cue2.Base.Classes.Connections;


public partial class CueLight : Node
{
    private GlobalSignals _globalSignals;
    private TcpClient _client;
    private NetworkStream _stream;
    private bool _isConnected;
    private string _name;
    private string _connectionType;
    private string _ipAddress;
    private int _port;
    private string _subnet;
    private string _gateway;
    private string _ssid;
    private string _password;
    private readonly string _encryptionKey = "4837fh3f#@#$fj8493";
    private bool _isIdentifying;
    
    public int Id { get; private set; }
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            GD.Print($"CueLight:SetName - Updated name to {_name} for CueLight ID {Id}");
        }
    }
    
    public bool IsConnected => _isConnected;

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
        _isIdentifying = false;

        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
    }


    public async Task ConnectAsync()
    {
        try
        {
            _client = new TcpClient();
            GD.Print($"CueLight:ConnectAsync - Connecting to {_name} at {_ipAddress}:{_port}");
            await _client.ConnectAsync(_ipAddress, _port);
            _stream = _client.GetStream();
            _isConnected = true;

            StartHeartbeat();
            
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"CueLight:ConnectAsync - Connected to {_name} at {_ipAddress}:{_port}", 0);
            await HandshakeAsync();
        }
        catch (SocketException ex)
        {
            _isConnected = false;
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"CueLight:ConnectAsync - Failed to connect to {_name} at {_ipAddress}:{_port}: {ex.Message}", 2);
        }
        catch (Exception ex)
        {
            _isConnected = false;
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"CueLight:ConnectAsync - Unexpected error for {_name}: {ex.Message}", 2);
        }
    }

    public async Task DisconnectAsync()
    {
        if (!_isConnected) return;
        try
        {
            _isConnected = false;
            _stream?.Close();
            _client?.Close();
            GD.Print($"CueLight:DisconnectAsync - Disconnected from {_name}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"CueLight:DisconnectAsync - Disconnected from {_name}", 0);
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"CueLight:DisconnectAsync - Error disconnecting {_name}: {ex.Message}", 2);
        }
    }
    
    

    private async void StartHeartbeat()
    {
        while (!_isConnected)
        {
            await Task.Delay(5000);
            if (!_isConnected) return;
            await HandshakeAsync();
        }
    }

    private async Task HandshakeAsync()
    {
        try
        {
            await SendCommandAsync("HANDSHAKE\n");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"CueLight:HandshakeAsync - Handshake successful with {_name}", 0);
        }
        catch (Exception ex)
        {
            _isConnected = false;
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"CueLight:HandshakeAsync - Handshake failed for {_name}: {ex.Message}", 3); // Alert for disconnection
        }
    }

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
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"CueLight:SetEthernetAsync - Set Ethernet config for {_name}: IP={ip}, Subnet={subnet}, Gateway={gateway}", 0);

            // Reconnect with new settings
            await DisconnectAsync();
            await ConnectAsync();
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"CueLight:SetEthernetAsync - Error setting Ethernet for {_name}: {ex.Message}", 2);
        }
    }

    public async Task SetWiFiAsync(string ssid, string password)
    {
        try
        {
            _connectionType = "WiFi";
            _ssid = ssid;
            _password = password;
            string encryptedCredentials = EncryptCredentials(ssid, password);
            string command = $"SET_WIFI:{encryptedCredentials}\n";
            await SendCommandAsync(command);
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"CueLight:SetWiFiAsync - Sent WiFi config for {_name}: SSID={ssid}", 0);

            // Reconnect after WiFi config (ESP32 may change IP)
            await DisconnectAsync();
            // Note: IP may need to be updated after ESP32 reconnects to WiFi
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"CueLight:SetWiFiAsync - Error setting WiFi for {_name}: {ex.Message}", 2);
        }
    }
        
    public async Task GoAsync()
    {
        try
        {
            await SendCommandAsync("GO\n"); // Blink Color1
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"CueLight:GoAsync - Sent GO (blink Color1) to {_name}", 0);
        }
        catch (Exception ex)
        {
            _isConnected = false;
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"CueLight:GoAsync - Error sending GO to {_name}: {ex.Message}", 2);
        }
    }

    public async Task StandbyAsync()
    {
        try
        {
            await SendCommandAsync("STANDBY\n"); // Hold Color2
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"CueLight:StandbyAsync - Sent STANDBY (hold Color2) to {_name}", 0);
        }
        catch (Exception ex)
        {
            _isConnected = false;
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"CueLight:StandbyAsync - Error sending STANDBY to {_name}: {ex.Message}", 2);
        }
    }

    public async Task CountInAsync(float timeUntilGo)
    {
        try
        {
            await SendCommandAsync($"COUNTIN:{timeUntilGo}\n"); // Color2 with time
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"CueLight:CountInAsync - Sent COUNTIN ({timeUntilGo}s) to {_name}", 0);
        }
        catch (Exception ex)
        {
            _isConnected = false;
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"CueLight:CountInAsync - Error sending COUNTIN to {_name}: {ex.Message}", 2);
        }
    }

    public async Task IdentifyAsync(bool enable)
    {
        try
        {
            _isIdentifying = enable;
            await SendCommandAsync($"IDENTIFY:{(enable ? "START" : "STOP")}\n"); // Toggle blue chase
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"CueLight:IdentifyAsync - Sent IDENTIFY {(enable ? "START" : "STOP")} to {_name}", 0);
        }
        catch (Exception ex)
        {
            _isConnected = false;
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"CueLight:IdentifyAsync - Error sending IDENTIFY to {_name}: {ex.Message}", 2);
        }
    }

    private async Task SendCommandAsync(string command)
    {
        if (!_isConnected || _stream == null)
            throw new InvalidOperationException("Not connected to cue light");

        byte[] commandBytes = Encoding.ASCII.GetBytes(command);
        await _stream.WriteAsync(commandBytes, 0, commandBytes.Length);
        await _stream.FlushAsync();
    }

    private string EncryptCredentials(string ssid, string password)
    {
        try
        {
            using Aes aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(_encryptionKey.PadRight(32, '\0').Substring(0, 32));
            aes.IV = new byte[16]; // Zeroed IV for simplicity; consider random IV in production
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
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"CueLight:EncryptCredentials - Error encrypting credentials for {_name}: {ex.Message}", 2);
            return "";
        }
    }

    public Godot.Collections.Dictionary GetData()
    {
        var data = new Godot.Collections.Dictionary
        {
            { "Id", Id },
            { "Name", _name },
            { "ConnectionType", _connectionType },
            { "IpAddress", _ipAddress },
            { "Port", _port },
            { "Subnet", _subnet },
            { "Gateway", _gateway },
            { "Ssid", _ssid },
            { "Password", _password },
            { "IsIdentifying", _isIdentifying }
        };
        return data;
    }

    public void SetData(Godot.Collections.Dictionary data)
    {
        Id = data["Id"].AsInt32();
        Name = data["Name"].ToString();
        _connectionType = data["ConnectionType"].ToString();
        _ipAddress = data["IpAddress"].ToString();
        _port = data["Port"].AsInt32();
        _subnet = data["Subnet"].ToString();
        _gateway = data["Gateway"].ToString();
        _ssid = data["Ssid"].ToString();
        _password = data["Password"].ToString();
        _isIdentifying = data["IsIdentifying"].AsBool();
    }
}