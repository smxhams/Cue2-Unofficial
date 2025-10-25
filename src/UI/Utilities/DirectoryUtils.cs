using Godot;
using System;

namespace Cue2.UI.Utilities;

/// <summary>
/// Utility class for handling directory and path operations.
/// </summary>
public static class DirectoryUtils
{
    
    /// <summary>
    /// Prepares the session directory structure for saving a file.
    /// Given a user-provided save path (e.g., "/path/to/MySession.c2"), this method:
    /// - Extracts the base directory and filename without extension.
    /// - Creates a session folder named after the filename (e.g., "/path/to/MySession/").
    /// - Ensures subfolders "Media" and "Waveforms" exist inside the session folder.
    /// - Returns the adjusted full path to save the file inside the session folder (e.g., "/path/to/MySession/MySession.c2").
    /// If the directories already exist, it verifies them without overwriting.
    /// </summary>
    /// <param name="userSavePath">The user-provided full path for saving the file (e.g., "/path/to/MySession.c2").</param>
    /// <returns>The adjusted full path to save the file inside the session folder, or an empty string if an error occurs.</returns>
    public static string PrepareSessionDirectory(string userSavePath, out string mediaPath, out string waveFormPath)
    {
        try
        {
            mediaPath = string.Empty;
            waveFormPath = string.Empty;
            
            if (string.IsNullOrEmpty(userSavePath))
            {
                GD.Print($"Invalid input: userSavePath is null or empty.");
                return string.Empty;
            }
            
            // Extract base directory, filename, and filename without extension using Godot's String extensions
            string baseDir = userSavePath.GetBaseDir();
            string fileName = userSavePath.GetFile();
            string fileNameWithoutExt = fileName.GetBaseName();
            if (string.IsNullOrEmpty(fileNameWithoutExt))
            {
                GD.Print($"DirectoryUtils:PrepareSessionDirectory - Could not extract filename without extension from: {userSavePath}");
                return string.Empty;
            }
            
            // Check if the base directory already ends with the session name (to avoid nesting)
            string baseDirLastSegment = baseDir.GetFile();
            string sessionDir;
            if (baseDirLastSegment == fileNameWithoutExt)
            {
                GD.Print($"Base directory '{baseDir}' already matches session name '{fileNameWithoutExt}'. Using existing directory without nesting.");
                sessionDir = baseDir;
            }
            else
            {
                sessionDir = baseDir + "/" + fileNameWithoutExt;
            }

            // Create or verify session directory
            Error err = DirAccess.MakeDirAbsolute(sessionDir);
            if (err != Error.Ok && err != Error.AlreadyInUse) // AlreadyInUse means it exists
            {
                GD.Print($"DirectoryUtils:PrepareSessionDirectory - Failed to create or verify session directory '{sessionDir}': {err}");
                return string.Empty;
            }
            
            // Create or verify subfolders
            string mediaDir = sessionDir + "/Media";
            err = DirAccess.MakeDirAbsolute(mediaDir);
            if (err != Error.Ok && err != Error.AlreadyInUse)
            {
                GD.Print($"DirectoryUtils:PrepareSessionDirectory - Failed to create or verify Media directory '{mediaDir}': {err}");
                return string.Empty;
            }
            mediaPath = mediaDir;

            string waveformsDir = sessionDir + "/Waveforms";
            err = DirAccess.MakeDirAbsolute(waveformsDir);
            if (err != Error.Ok && err != Error.AlreadyInUse)
            {
                GD.Print($"DirectoryUtils:PrepareSessionDirectory - Failed to create or verify Waveforms directory '{waveformsDir}': {err}");
                return string.Empty;
            }
            waveFormPath = waveformsDir;

            // Construct and return the adjusted save path (e.g., "/path/to/MySession/MySession.c2")
            string adjustedSavePath = sessionDir + "/" + fileName;

            GD.Print($"DirectoryUtils:PrepareSessionDirectory - Session directory prepared successfully. Adjusted save path: {adjustedSavePath}");

            return adjustedSavePath;
        }
        catch (Exception ex)
        {
            GD.Print($"DirectoryUtils:PrepareSessionDirectory - Unexpected error preparing session directory for '{userSavePath}': {ex.Message}");
            mediaPath = string.Empty;
            waveFormPath = string.Empty;
            return string.Empty;
        }
    }
}