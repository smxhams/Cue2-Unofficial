//==================================================================================//
// NativeLibPaths.cs                                                                //
// This file is part of Cue2                                                        //
// http://cue2.live/                                                                //
//==================================================================================//

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Godot;

namespace Cue2.Shared;

/// <summary>
/// Resolves on-disk directories for platform native libraries (FFmpeg, DryWetMidi, etc.).
/// </summary>
/// <remarks>
/// Editor builds load from <c>res://bin/{platform}/</c>. Exported Godot C# builds cannot
/// reliably <see cref="System.Runtime.InteropServices.NativeLibrary.Load"/> from a PCK, so
/// post-export packaging must place shared libraries as real files and this helper searches:
/// <list type="number">
/// <item><description><c>{exe_dir}/bin/{platform}/</c></description></item>
/// <item><description><c>{AppContext.BaseDirectory}/</c> (Godot C# <c>data_*</c> folder)</description></item>
/// <item><description><c>{AppContext.BaseDirectory}/bin/{platform}/</c></description></item>
/// <item><description><c>{exe_dir}/</c></description></item>
/// <item><description><c>res://bin/{platform}/</c> via <see cref="ProjectSettings.GlobalizePath"/></description></item>
/// </list>
/// See <c>docs/export-packaging.md</c>.
/// </remarks>
public static class NativeLibPaths
{
    /// <summary>
    /// Platform folder name under <c>bin/</c> for the current OS and process architecture
    /// (e.g. <c>win64</c>, <c>macos</c>, <c>linux64</c>).
    /// </summary>
    /// <param name="label">Human-readable platform label for logs.</param>
    /// <returns>Directory name under <c>bin/</c>.</returns>
    public static string GetPlatformDir(out string label)
    {
        Architecture arch = RuntimeInformation.ProcessArchitecture;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (arch == Architecture.Arm64)
            {
                label = "Windows ARM64";
                return "winarm64";
            }

            if (arch == Architecture.X64)
            {
                label = "Windows x64";
                return "win64";
            }

            label = $"Windows x64 (fallback for {arch})";
            GD.PrintErr($"NativeLibPaths:GetPlatformDir - Unsupported Windows arch {arch}; defaulting to win64.");
            return "win64";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            label = arch == Architecture.Arm64 ? "macOS ARM64" : "macOS x64";
            return "macos";
        }

        if (arch == Architecture.Arm64)
        {
            label = "Linux ARM64";
            return "linuxarm64";
        }

        if (arch == Architecture.X64)
        {
            label = "Linux x64";
            return "linux64";
        }

        label = $"Linux x64 (fallback for {arch})";
        GD.PrintErr($"NativeLibPaths:GetPlatformDir - Unsupported Linux arch {arch}; defaulting to linux64.");
        return "linux64";
    }

    /// <summary>
    /// Builds the on-disk shared library file name for an FFmpeg library on the current platform.
    /// </summary>
    /// <param name="name">Base name without prefix/suffix (e.g. <c>avutil</c>, <c>avcodec</c>).</param>
    /// <param name="major">ABI major version (e.g. <c>60</c>, <c>62</c>).</param>
    /// <returns>File name such as <c>avutil-60.dll</c>, <c>libavutil.60.dylib</c>, or <c>libavutil.so.60</c>.</returns>
    public static string GetFFmpegLibraryFileName(string name, string major)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return $"{name}-{major}.dll";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return $"lib{name}.{major}.dylib";

        return $"lib{name}.so.{major}";
    }

    /// <summary>
    /// File name of the Melanchall DryWetMidi native library for the current process, or empty if unsupported.
    /// </summary>
    /// <param name="platformLabel">Human-readable platform label for logs.</param>
    /// <returns>Native library file name, or empty string when Linux/unsupported.</returns>
    public static string GetDryWetMidiNativeFileName(out string platformLabel)
    {
        Architecture arch = RuntimeInformation.ProcessArchitecture;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            platformLabel = arch == Architecture.Arm64 ? "Windows ARM64" : "Windows x64";
            return IntPtr.Size == 4
                ? "Melanchall_DryWetMidi_Native32.dll"
                : "Melanchall_DryWetMidi_Native64.dll";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            platformLabel = arch == Architecture.Arm64 ? "macOS ARM64" : "macOS x64";
            return "Melanchall_DryWetMidi_Native64.dylib";
        }

        platformLabel = "Linux (unsupported by DryWetMidi natives)";
        return string.Empty;
    }

    /// <summary>
    /// Candidate directories that may contain platform natives, in search order.
    /// </summary>
    /// <param name="platformDir">Folder under <c>bin/</c> (from <see cref="GetPlatformDir"/>).</param>
    /// <returns>Absolute directory paths (may not all exist).</returns>
    public static IReadOnlyList<string> GetCandidateDirectories(string platformDir)
    {
        var dirs = new List<string>(8);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
                return;

            string full;
            try
            {
                full = Path.GetFullPath(dir);
            }
            catch
            {
                return;
            }

            // Normalize trailing separator for consistent combine/exists checks
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (seen.Add(full))
                dirs.Add(full);
        }

        string exePath = OS.GetExecutablePath();
        string exeDir = string.IsNullOrEmpty(exePath)
            ? string.Empty
            : Path.GetDirectoryName(exePath) ?? string.Empty;

        if (!string.IsNullOrEmpty(exeDir))
        {
            Add(Path.Combine(exeDir, "bin", platformDir));
            Add(exeDir);
        }

        string baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            Add(baseDir);
            Add(Path.Combine(baseDir, "bin", platformDir));
        }

        // Editor / project checkout: real files under res://bin/{platform}/
        try
        {
            string resBin = ProjectSettings.GlobalizePath($"res://bin/{platformDir}/");
            Add(resBin);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"NativeLibPaths:GetCandidateDirectories - GlobalizePath failed: {ex.Message}");
        }

        return dirs;
    }

    /// <summary>
    /// Finds the first candidate directory that contains <paramref name="fileName"/>.
    /// </summary>
    /// <param name="fileName">Library file name (not a path).</param>
    /// <param name="platformDir">Optional platform dir; resolved via <see cref="GetPlatformDir"/> when null.</param>
    /// <param name="directory">Directory that contains the file when found; otherwise empty.</param>
    /// <param name="tried">Directories that were searched (for error messages).</param>
    /// <returns>Full path to the library file, or empty if not found.</returns>
    public static string FindLibraryFile(
        string fileName,
        string platformDir,
        out string directory,
        out IReadOnlyList<string> tried)
    {
        directory = string.Empty;
        if (string.IsNullOrEmpty(fileName))
        {
            tried = Array.Empty<string>();
            return string.Empty;
        }

        if (string.IsNullOrEmpty(platformDir))
            platformDir = GetPlatformDir(out _);

        var candidates = GetCandidateDirectories(platformDir);
        tried = candidates;

        foreach (string dir in candidates)
        {
            string fullPath = Path.Combine(dir, fileName);
            if (File.Exists(fullPath))
            {
                directory = dir;
                return fullPath;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Finds a directory that contains all of the given library file names (same folder).
    /// </summary>
    /// <param name="fileNames">Required file names in one directory.</param>
    /// <param name="platformDir">Platform folder under <c>bin/</c>.</param>
    /// <param name="tried">Directories searched when not found.</param>
    /// <returns>Directory path, or empty if no single directory has every file.</returns>
    public static string FindDirectoryContainingAll(
        IReadOnlyList<string> fileNames,
        string platformDir,
        out IReadOnlyList<string> tried)
    {
        if (fileNames == null || fileNames.Count == 0)
        {
            tried = Array.Empty<string>();
            return string.Empty;
        }

        if (string.IsNullOrEmpty(platformDir))
            platformDir = GetPlatformDir(out _);

        var candidates = GetCandidateDirectories(platformDir);
        tried = candidates;

        foreach (string dir in candidates)
        {
            bool allPresent = true;
            foreach (string name in fileNames)
            {
                if (!File.Exists(Path.Combine(dir, name)))
                {
                    allPresent = false;
                    break;
                }
            }

            if (allPresent)
                return dir;
        }

        return string.Empty;
    }
}
