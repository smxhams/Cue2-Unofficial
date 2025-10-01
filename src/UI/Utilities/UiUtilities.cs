using System;
using System.Linq;
using System.Text.RegularExpressions;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Godot;

namespace Cue2.UI.Utilities;

/// <summary>
/// A utility class for UI elements that need to inspect Cue components.
/// </summary>
public partial class UiUtilities : Node
{
    private static readonly Regex IpRegex = new Regex(@"^(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$");
    private static readonly Regex CleanRegex = new Regex(@"[^\d.]"); // Removes anything that's not digit or dot
    
    /// <summary>
    /// Checks if the given Cue contains a component of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of ICueComponent to check for (e.g., AudioComponent).</typeparam>
    /// <param name="cue">The Cue instance to inspect.</param>
    /// <returns>True if at least one component of type T is present; otherwise, false.</returns>
    public static bool HasComponent<T>(Cue cue) where T : ICueComponent
    {
        if (cue == null)
        {
            GD.Print("UiUtilities:HasComponent - Attempted to check component on null Cue.");
            return false;
        }

        try
        {
            return cue.Components.OfType<T>().Any();
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"UiUtilities:HasComponent - Error checking component: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Parses user input from a LineEdit (e.g., "1:02:03.000", "3723", "1:2:3") into seconds and formats to "h:m:s.ms".
    /// </summary>
    /// <param name="input">The raw string from the LineEdit.</param>
    /// <param name="seconds">Out: The parsed time in seconds (double).</param>
    /// <returns>The formatted string (e.g., "01:02:03.000") or "00:00:00.000" on parse failure.</returns>
    /// <remarks>
    /// Supports flexible formats: colon-separated (h:m:s.ms), plain seconds (e.g., "3723" -> "01:02:03.000"), or partial (e.g., "1:2:3" -> "01:02:03.000").
    /// Plain numbers are treated as total seconds. Logs warnings on invalid input. Use in UI for time fields like cue start/end times.
    /// </remarks>
    public static string ParseAndFormatTime(string input, out double seconds)
    {
        string _; // Dummy for labeledFormat //!!!
        return ParseAndFormatTime(input, out seconds, out _); // Overload to default without labeledFormat
    }
    
    
    
    /// <summary>
    /// Parses user input from a LineEdit (e.g., "2:02.000", "122", "2:2") into seconds and formats to "m:s.ms".
    /// </summary>
    /// <param name="input">The raw string from the LineEdit.</param>
    /// <param name="seconds">Out: The parsed time in seconds (double).</param>
    /// /// <param name="labeledFormat">Out: Optional labeled format (e.g., "01hr:02m:03s.000ms" or "02m:03s.000ms" if hours are 0).</param>
    /// <returns>The formatted string (e.g., "2:02.000") or "" on parse failure.</returns>
    /// <remarks>
    /// Supports flexible formats: colon-separated (m:s.ms), plain seconds (e.g., "122" -> "2:02.000"), or partial (e.g., "2:2" -> "2:02.000").
    /// Plain numbers are treated as total seconds.
    /// </remarks>
    public static string ParseAndFormatTime(string input, out double seconds, out string labeledFormat)
    {
        seconds = 0.0;
        labeledFormat = "00m:00s.000ms";
        if (string.IsNullOrWhiteSpace(input))
        {
            GD.Print("UiUtilities:ParseAndFormatTime - Empty input, defaulting to 0.");
            return "";
        }

        try
        {
            // Normalize input: remove any non-numeric/colon/dot characters, handle flexible formats
            input = Regex.Replace(input, @"[^0-9:.]", "");

            // minute:second.milisecond
            var regex = new Regex(@"^(?:(\d+):)?(?:(\d+):)?(?:(\d+)(?:\.(\d+))?)?$");
            var match = regex.Match(input);

            if (match.Success)
            {
                double hour = match.Groups[1].Success ? double.Parse(match.Groups[1].Value) : 0;
                double min = match.Groups[1].Success ? double.Parse(match.Groups[1].Value) : 0;
                string secStr = match.Groups[2].Value;
                string msStr = match.Groups[3].Value;

                double sec = string.IsNullOrEmpty(secStr) ? 0 : double.Parse(secStr);
                double fracSec = 0.0;
                if (!string.IsNullOrEmpty(msStr))
                {
                    msStr = msStr.Substring(0, Math.Min(msStr.Length, 3)); // Truncate to at most 3 digits, ignoring extra
                    fracSec = double.Parse("0." + msStr);
                }

                // If no colon (plain number), treat entire input as seconds
                if (!input.Contains(":") && double.TryParse(input, out double totalSec))
                {
                    hour = Math.Floor(totalSec / 3600);
                    min = Math.Floor((totalSec % 3600) / 60);
                    sec = Math.Floor(totalSec % 60);
                    fracSec = totalSec - Math.Floor(totalSec); // Fractional as seconds
                }

                seconds = (hour * 3600) + (min * 60) + sec + fracSec;
                labeledFormat = FormatLabeledTime(seconds); // Compute labeled format
                return FormatTime(seconds);
            }
            else
            {
                GD.PrintErr("Invalid time format");
            }


        }
        catch (Exception ex)
        {
            GD.Print($"UiUtilities:ParseAndFormatTime - Invalid input '{input}': {ex.Message}");
            return "";
        }
        return null;
    }

    public static string FormatTime(double seconds)
    {
        var hour = (int)Math.Floor(seconds / 3600);
        var min = (int)Math.Floor((seconds % 3600) / 60);
        var sec = (int)Math.Floor(seconds % 60);
        var fracSec = seconds - Math.Floor(seconds);
        var ms = (int)Math.Round(fracSec * 1000); // Round to nearest ms
        if (ms >= 1000) // Carry over if rounding causes overflow
        {
            ms -= 1000;
            sec += 1;
            if (sec >= 60)
            {
                sec -= 60;
                min += 1;
                if (min >= 60) //!!!
                {
                    min -= 60;
                    hour += 1;
                }
            }
        }

        var time = $"{min:D2}:{sec:D2}.{ms:D3}";
        if (hour > 0)
        {
            time = $"{hour:D2}:" + time;
        }
        return time;
    }

    private static string FormatLabeledTime(double seconds)
    {
        var hour = (int)Math.Floor(seconds / 3600);
        var min = (int)Math.Floor((seconds % 3600) / 60);
        var sec = (int)Math.Floor(seconds % 60);
        var fracSec = seconds - Math.Floor(seconds);
        var ms = (int)Math.Round(fracSec * 1000); // Round to nearest ms
        if (ms >= 1000) // Carry over if rounding causes overflow
        {
            ms -= 1000;
            sec += 1;
            if (sec >= 60)
            {
                sec -= 60;
                min += 1;
                if (min >= 60)
                {
                    min -= 60;
                    hour += 1;
                }
            }
        }

        string labeled = $"{min:D2}m:{sec:D2}s.{ms:D3}ms";
        if (hour > 0)
        {
            labeled = $"{hour:D2}hr:" + labeled;
        }
        return labeled;
    }
    
    /// <summary>
    /// Converts a linear volume (0.0f to 1.0f) to decibels (dB).
    /// </summary>
    /// <param name="linear">The linear volume value (0.0f = off, 1.0f = full).</param>
    /// <returns>The dB value rouinded to one decimal place (e.g., 0dB for 1.0f, -60dB for 0.0f to avoid -inf).</returns>
    /// <remarks>
    /// Formula: 20 * log10(linear). Clamps below -60dB for practicality in UI sliders.
    /// Logs warnings for invalid input (outside 0-1 range).
    /// </remarks>
    public static float LinearToDb(float linear)
    {
        if (linear < 0f || linear > 1f)
        {
            
            GD.Print($"UiUtilities:LinearToDb - Invalid linear value {linear}; clamping to 0-1.");
            linear = Mathf.Clamp(linear, 0f, 1f);
        }

        if (Mathf.IsZeroApprox(linear)) return -60f; // Avoid -inf.
        float db = 20f * MathF.Log10(linear);
        float dbRounded = MathF.Round(db, 1);
        return dbRounded;
    }
    
    /// <summary>
    /// Converts decibels (dB) to a linear volume (0.0f to 1.0f).
    /// </summary>
    /// <param name="db">The dB value (e.g., 0dB = full, -60dB or lower = off).</param>
    /// <returns>The linear volume (0.0f to 1.0f). Returns -1f on failure</returns>
    /// <remarks>
    /// Formula: 10^(db/20). Handles -inf/off as 0.0f. Logs warnings for extreme values.
    /// Use in UI for volume controls syncing dB display with internal linear values.
    /// </remarks>
    public static float DbToLinear(string dbInput)
    {
        if (string.IsNullOrWhiteSpace(dbInput))
        {
            GD.Print("UiUtilities:DbToLinear - Empty input; returning 0.");
            return -1f;
        }

        try
        {
            // Clean: remove 'dB' case-insensitively, trim
            string cleaned = dbInput.ToLower().Replace("db", "").Trim();

            if (!float.TryParse(cleaned, out float db))
            {
                throw new FormatException("Invalid numeric format after parsing.");
            }

            if (db <= -60f)
            {
                GD.Print($"UiUtilities:DbToLinear - Parsed dB {db} from '{dbInput}' is below threshold; returning 0.");
                return 0f;
            }

            GD.Print(Mathf.Pow(10f, db / 20f));
            return Mathf.Pow(10f, db / 20f);
        }
        catch (Exception ex)
        {
            GD.Print($"UiUtilities:DbToLinear - Invalid input '{dbInput}': {ex.Message}; returning 0.");
            return -1f;
        }
    }
    
    /// <summary>
    /// Recursively sets the colour of all label children of provided root
    /// </summary>
    /// <param name="root">Parent node</param>
    /// <param name="colour">Colour to set labels to</param>
    public static void FormatLabelsColours(Node root, Color colour)
    {

        if (root is Label label)
        {
            label.AddThemeColorOverride("font_color", colour);
        }
        foreach (var child in root.GetChildren())
        {
            FormatLabelsColours(child, colour);
        }
    }

    
    /// <summary>
    /// Cleans and verifies an IP address string.
    /// - Removes invalid characters (non-digits/dots).
    /// - Trims leading/trailing dots.
    /// - Validates as a proper IPv4 address (four octets, each 0-255).
    /// - If invalid, logs an error via GlobalSignals and returns null.
    /// - If valid, returns the cleaned IP string.
    /// </summary>
    /// <param name="input">The raw user input string.</param>
    /// <param name="globalSignals">Reference to GlobalSignals for logging errors. If null, falls back to GD.PrintErr.</param>
    /// <returns>Cleaned valid IP string, or null if invalid.</returns>
    public static string VerifyIpInput(string input, GlobalSignals globalSignals = null)
    {
        try
        {
            string cleaned = CleanRegex.Replace(input ?? "", "").Trim('.');
            
            if (string.IsNullOrEmpty(cleaned) || cleaned.Count(c => c == '.') != 3)
            {
                LogError("Invalid IP format: Must have exactly three dots and non-empty octets.", globalSignals);
                return null;
            }
            
            string[] octets = cleaned.Split('.');
            if (octets.Length != 4 || octets.Any(o => !int.TryParse(o, out int val) || val < 0 || val > 255))
            {
                LogError("Invalid IP: Each octet must be an integer between 0-255.", globalSignals);
                return null;
            }
            
            if (!IpRegex.IsMatch(cleaned))
            {
                LogError("Invalid IP format: Leading zeros not allowed except for octet value 0.", globalSignals);
                return null;
            }

            return cleaned;
        }
        catch (Exception ex)
        {
            LogError($"Unexpected error validating IP: {ex.Message}", globalSignals, 2);
            return null;
        }
    }
    
    
    /// <summary>
    /// Helper to log errors via GlobalSignals if available, else GD.PrintErr.
    /// Uses log level 1 (Warning) by default, or specified level.
    /// </summary>
    private static void LogError(string message, GlobalSignals globalSignals, int logLevel = 1)
    {
        if (globalSignals != null)
        {
            globalSignals.EmitSignal(nameof(GlobalSignals.Log), message, logLevel);
        }
        else
        {
            GD.PrintErr(message);
        }
    }


    public static void RescaleUi(Window window, double scale, double baseDisplayScale = 1.0)
    {
        try
        {
            var effectiveScale = scale * baseDisplayScale;
            window.WrapControls = true;
            window.ContentScaleFactor = (float)effectiveScale;
            window.ChildControlsChanged();
        } 
        catch (Exception ex)
        {
            GD.PrintErr($"UiUtilities:RescaleUI - Error applying UI scale: {ex.Message}");
            window.ContentScaleFactor = (float)scale; // Fallback to original value without multiplier
        }
    }

    public static void RescaleWindow(Window window, double scale)
    {
        var oldSize = window.Size;
        var newSize = new Vector2I((int)(window.Size.X * scale), (int)(window.Size.Y * scale));
        window.Size = newSize;
        var offsetX = window.Position.X + ((oldSize.X - newSize.X)/2);
        var offsetY = window.Position.Y + ((oldSize.Y - newSize.Y)/2);
        window.Position = new Vector2I((int)offsetX, (int)offsetY);
    }



}