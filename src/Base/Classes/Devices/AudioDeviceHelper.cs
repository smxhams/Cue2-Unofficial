using System.Linq;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

using NAudio.CoreAudioApi; // Requires NuGet: NAudio (only used on Windows)

namespace Cue2.Base.Classes.Devices;

public static class AudioDeviceHelper
{
    
    public static AudioDevice? GetAudioDevice(string deviceName, string VlcIdentifier)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return GetWindowsAudioDevice(deviceName, VlcIdentifier);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return GetLinuxAudioDevice(deviceName, VlcIdentifier);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return GetMacAudioDevice(deviceName, VlcIdentifier);
        }
        else
        {
            Console.WriteLine("Unsupported OS");
            return null;
        }
    }

    private static AudioDevice? GetWindowsAudioDevice(string deviceName, string VlcIdentifier)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .FirstOrDefault(d => d.FriendlyName.Contains(deviceName, StringComparison.OrdinalIgnoreCase));

            if (device == null) return null;

            AudioDevice newDevice = new AudioDevice(device.FriendlyName, device.AudioClient.MixFormat.Channels, VlcIdentifier)
                {
                    SampleRate = device.AudioClient.MixFormat.SampleRate,
                    BitDepth = device.AudioClient.MixFormat.BitsPerSample
                };

            return newDevice;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static AudioDevice? GetLinuxAudioDevice(string deviceName, string VlcIdentifier)
    {
        try
        {
            string name = ExecuteBashCommand($"aplay -l | grep '{deviceName}' | head -n 1");
            string channelsOutput = ExecuteBashCommand("cat /proc/asound/card*/codec#* | grep 'Channels:' | head -n 1");
            string sampleRateOutput = ExecuteBashCommand("cat /proc/asound/card*/codec#* | grep 'Rates:' | head -n 1");

            int.TryParse(channelsOutput.Split(':')[1].Trim(), out int channels);
            int.TryParse(sampleRateOutput.Split(':')[1].Trim().Split(' ')[0], out int sampleRate);

            return new AudioDevice(name, channels, VlcIdentifier);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static AudioDevice? GetMacAudioDevice(string deviceName, string VlcIdentifier)
    {
        try
        {
            string name = ExecuteBashCommand($"system_profiler SPAudioDataType | grep '{deviceName}' | head -n 1");
            string channelsOutput =
                ExecuteBashCommand("system_profiler SPAudioDataType | grep 'Output Channels' | head -n 1");
            string sampleRateOutput =
                ExecuteBashCommand("system_profiler SPAudioDataType | grep 'Sample Rate' | head -n 1");

            int.TryParse(channelsOutput.Split(':')[1].Trim(), out int channels);
            int.TryParse(sampleRateOutput.Split(':')[1].Trim().Replace(" Hz", ""), out int sampleRate);

            return new AudioDevice(name, channels, VlcIdentifier);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ExecuteBashCommand(string command)
    {
        try
        {
            using Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bash",
                    Arguments = $"-c \"{command}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output.Trim();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}