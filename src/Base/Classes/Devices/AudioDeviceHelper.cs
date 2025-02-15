namespace Cue2.Base.Classes.Devices;

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi; // Requires NuGet: NAudio (only used on Windows)

public static class AudioDeviceHelper
{
    public static int GetAudioOutputChannels()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return GetWindowsAudioChannels();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return GetLinuxAudioChannels();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return GetMacAudioChannels();
        }
        else
        {
            Console.WriteLine("Unsupported OS");
            return -1;
        }
    }

    private static int GetWindowsAudioChannels()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.AudioClient.MixFormat.Channels;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving Windows audio channels: {ex.Message}");
            return -1;
        }
    }

    private static int GetLinuxAudioChannels()
    {
        try
        {
            string output = ExecuteBashCommand("cat /proc/asound/card*/codec#* | grep 'Channels:' | head -n 1");
            if (!string.IsNullOrEmpty(output) && int.TryParse(output.Split(':')[1].Trim(), out int channels))
            {
                return channels;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving Linux audio channels: {ex.Message}");
        }
        return -1;
    }

    private static int GetMacAudioChannels()
    {
        try
        {
            string output = ExecuteBashCommand("system_profiler SPAudioDataType | grep 'Output Channels' | head -n 1");
            if (!string.IsNullOrEmpty(output) && int.TryParse(output.Split(':')[1].Trim(), out int channels))
            {
                return channels;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving Mac audio channels: {ex.Message}");
        }
        return -1;
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
        catch (Exception ex)
        {
            Console.WriteLine($"Error executing Bash command: {ex.Message}");
            return string.Empty;
        }
    }
}
