using Godot;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Cue2.Shared;

public partial class TestCueLightButton : Button
{
    private GlobalSignals _globalSignals;
    private const string Esp32Ip = "192.168.1.47"; // Replace with your ESP32 IP
    private const int Esp32Port = 80;
    private const string Command = "BLINK_GREEN\n";

    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        Pressed += async () => await SendCueLightCommandAsync();
        GD.Print("TestCueLightButton:_Ready - Button ready to send UDP command."); //!!!
    }

    private async Task SendCueLightCommandAsync()
    {
        try
        {
            using var client = new UdpClient();
            byte[] commandBytes = Encoding.ASCII.GetBytes(Command);
            GD.Print($"TestCueLightButton:SendCueLightCommandAsync - Sending UDP command to {Esp32Ip}:{Esp32Port}"); //!!!

            await client.SendAsync(commandBytes, commandBytes.Length, Esp32Ip, Esp32Port);

            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"TestCueLightButton:SendCueLightCommandAsync - Sent '{Command.Trim()}' to ESP32 at {Esp32Ip}:{Esp32Port} via UDP", 0);
        }
        catch (SocketException ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"TestCueLightButton:SendCueLightCommandAsync - Socket error: {ex.Message}", 2);
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"TestCueLightButton:SendCueLightCommandAsync - Unexpected error: {ex.Message}", 2);
        }
    }
}