#nullable enable
using Godot;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.IO;
using System.Threading;
using Cue2.Shared;
using System.Timers;

namespace Cue2.Base.Classes.Connections;

/// <summary>
/// Manages a single ESP32-based cue light with persistent TCP connection.
/// Supports Ethernet/WiFi config, secure credential sending, and LED commands.
/// </summary>
public partial class CueLight : GodotObject, IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly string _encryptionKey = "f8237hr8hnfv3fH@#R"; // Matches SaveManager
    public bool IsIdentifying = false;
    private System.Timers.Timer? _heartbeatTimer; // For periodic handshakes
    private System.Collections.Generic.Dictionary<string, TaskCompletionSource<string>> _pendingResponses = new();
    private bool _disposed = false;
    private CancellationTokenSource? _connectCts; // For cancelling overlapping connects
    private readonly SemaphoreSlim _connectSemaphore = new(1, 1);

    public int Id { get; private set; }
    public string Name { get; set; }
    public string ConnectionType { get; private set; }
    public string IpAddress { get; private set; }
    public int Port { get; private set; }
    public string Subnet { get; private set; }
    public string Gateway { get; private set; }
    public string Ssid { get; private set; }
    public string Password { get; private set; }
    public bool CueLightIsConnected { get; private set; }
    public CancellationToken CancellationToken => _connectCts?.Token ?? CancellationToken.None;
    
    public CueLight(int id)
    {
        Id = id;
        Name = $"CueLight_{id}";
        ConnectionType = "Ethernet";
        IpAddress = "192.168.1.100"; // Default for testing
        Port = 80;
        Subnet = "255.255.255.0";
        Gateway = "192.168.1.1";
        Ssid = "";
        Password = "";
        CueLightIsConnected = false;
        IsIdentifying = false;
    }
    
    /// <summary>
    /// Deserializes cue light data from file loading.
    /// </summary>
    public CueLight(Godot.Collections.Dictionary data)
    {
        if (data.ContainsKey("Id")) Id = data["Id"].AsInt32();
        if (data.ContainsKey("Name")) Name = data["Name"].ToString();
        if (data.ContainsKey("ConnectionType")) ConnectionType = data["ConnectionType"].ToString();
        if (data.ContainsKey("IpAddress")) IpAddress = data["IpAddress"].ToString();
        if (data.ContainsKey("Port")) Port = data["Port"].AsInt32();
        if (data.ContainsKey("Subnet")) Subnet = data["Subnet"].ToString();
        if (data.ContainsKey("Gateway")) Gateway = data["Gateway"].ToString();
        if (data.ContainsKey("Ssid")) Ssid = data["Ssid"].ToString();
        if (data.ContainsKey("Password")) Password = data["Password"].ToString();
    }
    
    
    /// <summary>
    /// Establishes a persistent TCP connection and starts heartbeat.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (CueLightIsConnected) return; // Already connected
        if (_disposed) throw new ObjectDisposedException(nameof(CueLight));
        
        // Add timeout to prevent indefinite semaphore wait 
        if (!await _connectSemaphore.WaitAsync(TimeSpan.FromSeconds(2)))
        {
            GD.PrintErr($"CueLight:ConnectAsync - Semaphore timeout for {Name}");
            return;
        }
        var cts = _connectCts; // Capture for use in task
        
        try
        {
            // Cancel prior connect if active
            _connectCts?.Cancel();
            _connectCts?.Dispose();
            _connectCts = new CancellationTokenSource();

            // Create linked CTS with timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_connectCts.Token);
            
            
            GD.Print($"CueLight:ConnectAsync - Connecting to {Name} ({ConnectionType}) at {IpAddress}:{Port}");
            _client = new TcpClient();
            await _client.ConnectAsync(IpAddress, Port, linkedCts.Token);
            _stream = _client.GetStream();
            _stream.ReadTimeout = 1000; // 1sec response timeout
            CueLightIsConnected = true;

            // Start heartbeat timer
            _heartbeatTimer = new System.Timers.Timer(5000); // 5 seconds
            _heartbeatTimer.Elapsed += async (s, e) => await HeartbeatAsync();
            _heartbeatTimer.AutoReset = true;
            _heartbeatTimer.Start(); 
            
            // Start receive loop
            StartReceiveLoop();
            
            GD.Print($"CueLight:ConnectAsync - Connected to {Name} at {IpAddress}:{Port}");
        }
        catch (ArgumentException ex) // Catch CTS-related errors
        {
            GD.PrintErr($"CueLight:ConnectAsync - CTS initialization error for {Name}: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            GD.Print($"CueLight:ConnectAsync - Connect to {Name} cancelled (new attempt started)");
        }
        catch (Exception ex)
        {
            CueLightIsConnected = false;
            GD.Print($"CueLight:ConnectAsync - Unexpected error for {Name}: {ex.Message}");
        }
        finally
        {
            _connectCts?.Dispose();
            _connectCts = null;
            _connectSemaphore.Release();
        }
    }

    /// <summary>
    /// Gracefully disconnects and stops heartbeat.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (!CueLightIsConnected) return; // Already disconnected
        if (_disposed) throw new ObjectDisposedException(nameof(CueLight));
        
        try
        {
            _connectCts?.Cancel();
            _heartbeatTimer?.Stop();
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
            
            _stream?.Close();
            _client?.Close();
            CueLightIsConnected = false;
            GD.Print($"CueLight:DisconnectAsync - Disconnected from {Name}");
        }
        catch (Exception ex)
        {
            GD.Print($"CueLight:DisconnectAsync - Disconnect error for {Name}: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Sends a heartbeat PING and awaits PONG response with timeout.
    /// </summary>
    private async Task HeartbeatAsync()
    {
        if (_disposed || !CueLightIsConnected) return;

        try
        {
            if (_pendingResponses.ContainsKey("PING"))
            {
                // Previous heartbeat not completed - assume failure
                throw new Exception("Previous heartbeat timed out");
            }

            var tcs = new TaskCompletionSource<string>();
            _pendingResponses["PING"] = tcs;

            await SendCommandAsync("PING\n");

            // Timeout task
            _ = Task.Delay(4000).ContinueWith(_ =>
            {
                if (!tcs.Task.IsCompleted)
                {
                    tcs.TrySetResult("TIMEOUT");
                }
            });

            string response = await tcs.Task;
            if (response != "PONG")
            {
                throw new Exception($"Heartbeat failed: {response}");
            }

            GD.Print($"CueLight:HeartbeatAsync - Heartbeat OK for {Name}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"CueLight:HeartbeatAsync - Heartbeat failed for {Name}: {ex.Message}");
            CueLightIsConnected = false;
            _heartbeatTimer?.Stop();
        }
    }
    
    /// <summary>
    /// Starts the asynchronous receive loop to handle incoming messages.
    /// </summary>
    private void StartReceiveLoop()
    {
        if (_disposed) return;
        
        _ = Task.Run(async () =>
        {
            try
            {
                byte[] buffer = new byte[1024];
                while (CueLightIsConnected && _stream != null)
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        throw new Exception("Connection closed by remote host");
                    }
                    string response = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
                    // Log raw bytes for debugging
                    string rawBytes = BitConverter.ToString(buffer, 0, bytesRead).Replace("-", "");
                    GD.Print($"CueLight:StartReceiveLoop - Raw bytes received from {Name}: {rawBytes}");
                    GD.Print($"CueLight:StartReceiveLoop - Parsed response from {Name}: '{response}'");

                    // Normalize response to handle PONG variations
                    if (response.Equals("PONG", StringComparison.OrdinalIgnoreCase) || response.Contains("PONG"))
                    {
                        if (_pendingResponses.TryGetValue("PING", out var tcs))
                        {
                            tcs.TrySetResult("PONG");
                            _pendingResponses.Remove("PING");
                            GD.Print($"CueLight:StartReceiveLoop - Processed PONG for {Name}");
                        }
                    }
                    else if (response.Equals("ACK", StringComparison.OrdinalIgnoreCase) || response.Contains("ACK"))
                    {
                        GD.Print($"CueLight:StartReceiveLoop - Received ACK for {Name}");
                    }
                    else
                    {
                        GD.Print($"CueLight:StartReceiveLoop - Unexpected response from {Name}: '{response}'");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                GD.Print($"CueLight:StartReceiveLoop - Receive loop cancelled for {Name}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"CueLight:StartReceiveLoop - Receive error for {Name}: {ex.Message}");
                CueLightIsConnected = false;
                _heartbeatTimer?.Stop();
            }
        });
    }
    

    /// <summary>
    /// Sets IP address and reconnects if connected.
    /// </summary>
    public async void SetIpAddressAsync(string newIp)
    {
        if (IpAddress == newIp) return;
        if (CueLightIsConnected) await DisconnectAsync();
        IpAddress = newIp;
        await ConnectAsync();
    }
    
    /// <summary>
    /// Configures Ethernet settings and sends to ESP32.
    /// </summary>
    public async Task SetEthernetAsync(string ip, string subnet, string gateway)
    {
        try
        {
            ConnectionType = "Ethernet";
            IpAddress = ip;
            Subnet = subnet;
            Gateway = gateway;
            string command = $"SET_ETHERNET:{ip}:{subnet}:{gateway}\n";
            await SendCommandAsync(command);
            GD.Print(
                $"CueLight:SetEthernetAsync - Configured Ethernet for {Name}: IP={ip}, Subnet={subnet}, Gateway={gateway}", 0);

            await DisconnectAsync();
            await ConnectAsync(); // Reconnect with new config
        }
        catch (Exception ex)
        {
            GD.Print(
                $"CueLight:SetEthernetAsync - Ethernet config error for {Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Configures WiFi settings and sends encrypted credentials to ESP32.
    /// </summary>
    public async Task SetWiFiAsync(string ssid, string password)
    {
        try
        {
            ConnectionType = "WiFi";
            Ssid = ssid;
            Password = password;
            string encrypted = EncryptCredentials(ssid, password);
            string command = $"SET_WIFI:{encrypted}\n";
            await SendCommandAsync(command);
            GD.Print(
                $"CueLight:SetWiFiAsync - Sent WiFi config for {Name} (SSID={ssid})", 0);

            await DisconnectAsync();
            // IP may change post-reconnect; user must update manually or via discovery
        }
        catch (Exception ex)
        {
            GD.Print(
                $"CueLight:SetWiFiAsync - WiFi config error for {Name}: {ex.Message}", 2);
        }
    }
    
    /// <summary>
    /// Sends CONFIG_COLORS command with all cue light colors.
    /// </summary>
    public async Task ConfigureColorsAsync(Color idleColor, Color goColor, Color standbyColor, Color countInColor, byte brightness)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CueLight));
        
        try
        {
           
            int idleR = (int)(idleColor.R * 255);
            int idleG = (int)(idleColor.G * 255);
            int idleB = (int)(idleColor.B * 255);
            int goR = (int)(goColor.R * 255);
            int goG = (int)(goColor.G * 255);
            int goB = (int)(goColor.B * 255);
            int standbyR = (int)(standbyColor.R * 255);
            int standbyG = (int)(standbyColor.G * 255);
            int standbyB = (int)(standbyColor.B * 255);
            int countInR = (int)(countInColor.R * 255);
            int countInG = (int)(countInColor.G * 255);
            int countInB = (int)(countInColor.B * 255);
            
            string command = $"CONFIG_COLORS:{idleR},{idleG},{idleB},{goR},{goG},{goB}," +
                             $"{standbyR},{standbyG},{standbyB},{countInR},{countInG},{countInB},{brightness}\n";
            await SendCommandAsync(command);
            GD.Print($"CueLight:ConfigureColorsAsync - Sent CONFIG_COLORS with brightness {brightness} to {Name}");
        }
        catch (Exception ex)
        {
            CueLightIsConnected = false;
            GD.PrintErr($"CueLight:ConfigureColorsAsync - CONFIG_COLORS error for {Name}: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Sends GO command
    /// </summary>
    public async Task GoAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CueLight));
        
        try
        {
            await SendCommandAsync("GO\n");
            GD.Print(
                $"CueLight:GoAsync - Sent GO to {Name}");
        }
        catch (Exception ex)
        {
            CueLightIsConnected = false;
            GD.Print($"CueLight:GoAsync - GO error for {Name}: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Sends STANDBY command
    /// </summary>
    public async Task StandbyAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CueLight));
        
        try
        {
            await SendCommandAsync("STANDBY\n");
            GD.Print(
                $"CueLight:StandbyAsync - Sent STANDBY to {Name}");
        }
        catch (Exception ex)
        {
            CueLightIsConnected = false;
            GD.Print(
                $"CueLight:StandbyAsync - STANDBY error for {Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends COUNTIN command
    /// </summary>
    public async Task CountInAsync(float timeUntilGo)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CueLight));
        
        try
        {
            await SendCommandAsync($"COUNTIN:{timeUntilGo:F1}\n");
            GD.Print(
                $"CueLight:CountInAsync - Sent COUNTIN ({timeUntilGo}s) to {Name}");
        }
        catch (Exception ex)
        {
            CueLightIsConnected = false;
            GD.Print(
                $"CueLight:CountInAsync - COUNTIN error for {Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Toggles IDENTIFY (blue LED chase).
    /// </summary>
    public async Task IdentifyAsync(bool enable)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CueLight));
        
        try
        {
            IsIdentifying = enable;
            string state = enable ? "START" : "STOP"; ;
            await SendCommandAsync($"IDENTIFY:{state}\n");
            GD.Print($"CueLight:IdentifyAsync - IDENTIFY {state} for {Name}");
        }
        catch (Exception ex)
        {
            CueLightIsConnected = false;
            GD.Print($"CueLight:IdentifyAsync - IDENTIFY error for {Name}: {ex.Message}");
        }
    }

    private async Task SendCommandAsync(string command)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CueLight));
        if (!CueLightIsConnected || _stream == null)
            throw new InvalidOperationException("CueLight not connected");

        try
        {
            byte[] bytes = Encoding.ASCII.GetBytes(command);
            await _stream.WriteAsync(bytes, 0, bytes.Length);
            await _stream.FlushAsync();
            GD.Print($"CueLight:SendCommandAsync - Sent: {command.Trim()} to {Name}");
        }
        catch (Exception ex)
        {
            throw new Exception($"Send failed: {ex.Message}");
        }
    }

    private string EncryptCredentials(string ssid, string password)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CueLight));
        
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
            { "Name", Name },
            { "ConnectionType", ConnectionType },
            { "IpAddress", IpAddress },
            { "Port", Port },
            { "Subnet", Subnet },
            { "Gateway", Gateway },
            { "Ssid", Ssid },
            { "Password", Password }, // Encrypt in prod
            { "IsIdentifying", IsIdentifying }
        };
    }
    
    
    /// <summary>
    /// Disposes of resources (TCP client, stream, timer).
    /// </summary>
    public new void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _connectCts?.Cancel();
            _connectCts?.Dispose();
            _connectCts = null;
            
            if (CueLightIsConnected)
            {
                DisconnectAsync().GetAwaiter().GetResult(); // Synchronous to ensure cleanup
            }
            _heartbeatTimer?.Dispose();
            _stream?.Dispose();
            _client?.Dispose();
            GD.Print($"CueLight:Dispose - Disposed CueLight {Id} ({Name})");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"CueLight:Dispose - Error disposing CueLight {Id} ({Name}): {ex.Message}");
        }
    }



}