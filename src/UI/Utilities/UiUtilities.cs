using System;
using System.Linq;
using System.Net;
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
    /// <param name="isValid">Out: True if the input was successfully parsed, false otherwise.</param>
    /// <returns>The formatted string (e.g., "01:02:03.000") or "00:00:00.000" on parse failure.</returns>
    /// <remarks>
    /// Supports flexible formats: colon-separated (h:m:s.ms), plain seconds (e.g., "3723" -> "01:02:03.000"), or partial (e.g., "1:2:3" -> "01:02:03.000").
    /// Plain numbers are treated as total seconds. Logs warnings on invalid input. Use in UI for time fields like cue start/end times.
    /// </remarks>
    public static string ParseAndFormatTime(string input, out double seconds, out bool isValid)
    {
        return ParseAndFormatTime(input, out seconds, out _, out isValid); // Overload to default without labeledFormat
    }
    
    public static string ParseAndFormatTime(string input, out double seconds)
    {
        return ParseAndFormatTime(input, out seconds, out _, out _); // Overload to default without labeledFormat
    }
    
    public static string ParseAndFormatTime(string input, out double seconds, out string labeledFormat)
    {
        return ParseAndFormatTime(input, out seconds, out labeledFormat, out _); // Overload to default without labeledFormat
    }
    
    
    
    /// <summary>
    /// Parses user input from a LineEdit (e.g., "2:02.000", "122", "2:2") into seconds and formats to "m:s.ms".
    /// </summary>
    /// <param name="input">The raw string from the LineEdit.</param>
    /// <param name="seconds">Out: The parsed time in seconds (double).</param>
    /// <param name="labeledFormat">Out: Optional labeled format (e.g., "01hr:02m:03s.000ms" or "02m:03s.000ms" if hours are 0).</param>
    /// <param name="isValid">Out: True if the input was successfully parsed, false otherwise.</param>
    /// <returns>The formatted string (e.g., "2:02.000") or "" on parse failure.</returns>
    /// <remarks>
    /// Supports flexible formats: colon-separated (m:s.ms), plain seconds (e.g., "122" -> "2:02.000"), or partial (e.g., "2:2" -> "2:02.000").
    /// Plain numbers are treated as total seconds.
    /// </remarks>
    public static string ParseAndFormatTime(string input, out double seconds, out string labeledFormat, out bool isValid)
    {
        seconds = 0.0;
        labeledFormat = "00m:00s.000ms";
        isValid = false;
        if (string.IsNullOrWhiteSpace(input))
        {
            GD.Print("UiUtilities:ParseAndFormatTime - Empty input, defaulting to 0.");
            return "";
        }

        try
        {
            // Check for invalid characters before processing
            if (!Regex.IsMatch(input, @"^[\d:.]+$"))
            {
                GD.PrintErr("Invalid time format: contains invalid characters");
                return "";
            }

            // Normalize input: remove any non-numeric/colon/dot characters, handle flexible formats
            input = Regex.Replace(input, @"[^0-9:.]", "");

            // minute:second.milisecond
            var regex = new Regex(@"^(?:(\d+):)?(?:(\d+):)?(?:(\d+)(?:\.(\d+))?)?$");
            var match = regex.Match(input);

            if (match.Success)
            {
                double hour = 0;
                double min = 0;
                if (match.Groups[2].Success)
                {
                    hour = double.Parse(match.Groups[1].Value);
                    min = double.Parse(match.Groups[2].Value);
                }
                else if (match.Groups[1].Success)
                {
                    min = double.Parse(match.Groups[1].Value);
                }
                string secStr = match.Groups[3].Value;
                string msStr = match.Groups[4].Value;

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
                isValid = true;
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
                if (min >= 60)
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
    /// Parses a wall-clock time of day for cue clock triggers.
    /// </summary>
    /// <param name="input">User text (e.g. "14:30", "14:30:00", "2:30 PM", "2:30:00 pm").</param>
    /// <param name="timeOfDay">Out: parsed local time of day.</param>
    /// <param name="display">Out: normalized <c>HH:mm:ss</c> display, or empty on failure / empty input.</param>
    /// <returns>
    /// <c>true</c> when input is empty (means clear) or successfully parsed;
    /// <c>false</c> when input is non-empty but invalid.
    /// </returns>
    /// <remarks>
    /// Empty/whitespace input is treated as "clear" (success with <see cref="TimeSpan.Zero"/> and empty display).
    /// 12-hour times without AM/PM are rejected when the hour is 1–12 ambiguous? No — bare 1–12 without meridiem
    /// is treated as 24h if hour ≤ 23 (so "2:30" = 02:30). Prefer AM/PM when using 12h.
    /// </remarks>
    public static bool TryParseClockTime(string input, out TimeSpan timeOfDay, out string display)
    {
        timeOfDay = TimeSpan.Zero;
        display = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
            return true; // clear

        string raw = input.Trim();
        bool pm = false;
        bool am = false;
        string upper = raw.ToUpperInvariant();
        if (upper.EndsWith("PM") || upper.EndsWith("P.M.") || upper.EndsWith("P.M"))
        {
            pm = true;
            raw = Regex.Replace(raw, @"\s*[Pp]\.?[Mm]\.?\s*$", "").Trim();
        }
        else if (upper.EndsWith("AM") || upper.EndsWith("A.M.") || upper.EndsWith("A.M"))
        {
            am = true;
            raw = Regex.Replace(raw, @"\s*[Aa]\.?[Mm]\.?\s*$", "").Trim();
        }

        // Allow H:M, H:M:S, H:M:S.ms — also H.M via dots as separators is not supported; use colons.
        if (!Regex.IsMatch(raw, @"^\d{1,2}:\d{1,2}(:\d{1,2}(\.\d+)?)?$"))
        {
            // Also accept plain "HHMM" or "HHMMSS"? Keep strict for clarity.
            GD.Print($"UiUtilities:TryParseClockTime - Invalid clock format '{input}'");
            return false;
        }

        var parts = raw.Split(':');
        if (!int.TryParse(parts[0], out int hour) ||
            !int.TryParse(parts[1], out int minute))
            return false;

        int second = 0;
        if (parts.Length >= 3)
        {
            // Allow fractional seconds but store whole seconds for display.
            if (!double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double secFrac))
                return false;
            second = (int)Math.Floor(secFrac);
            if (second < 0 || second > 59) return false;
        }

        if (minute < 0 || minute > 59) return false;

        if (am || pm)
        {
            if (hour < 1 || hour > 12) return false;
            if (hour == 12) hour = 0;
            if (pm) hour += 12;
        }
        else
        {
            if (hour < 0 || hour > 23) return false;
        }

        timeOfDay = new TimeSpan(hour, minute, second);
        display = Cue.FormatClockTimeOfDay(timeOfDay);
        return true;
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

            return DbToLinear(db);
        }
        catch (Exception ex)
        {
            GD.Print($"UiUtilities:DbToLinear - Invalid input '{dbInput}': {ex.Message}; returning 0.");
            return -1f;
        }
    }

    /// <summary>
    /// Converts a dB value to linear volume (0…1). Clamps to the practical −60…0 dB UI range.
    /// </summary>
    /// <param name="db">Decibels (0 = full, −60 or lower = silence).</param>
    /// <returns>Linear volume in 0…1.</returns>
    public static float DbToLinear(float db)
    {
        if (db <= -60f)
            return 0f;
        if (db > 0f)
            db = 0f;
        return Mathf.Pow(10f, db / 20f);
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

    public static IPAddress ValidateIpAddress(string input)
    {
        return IPAddress.None;
    }

    public static int ValidatePort(string input)
    {
        if (int.TryParse(input, out int port))
        {
            if (port >= 1 && port <= 65535)
            {
                return port;
            }
            else
            {
                GD.Print($"SettingsOscListen: Invalid port number. Must be between 1 and 65535.");
                return -1;
            }
        }
        else
        {
            GD.Print($"SettingsOscListen: Invalid port number. Please enter a valid integer.");
            return -1;
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
        GD.Print($"UiUtilities:RescaleUi - Scale: {scale}, Display Scale: {baseDisplayScale}");
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

    /// <summary>
    /// True when the window fills the screen via maximize or any fullscreen mode.
    /// </summary>
    /// <param name="window">Window to inspect.</param>
    /// <returns>True if maximized or fullscreen; false if null/invalid or windowed.</returns>
    public static bool IsWindowFillScreen(Window window)
    {
        if (window == null || !GodotObject.IsInstanceValid(window))
            return false;

        return window.Mode is Window.ModeEnum.Maximized
            or Window.ModeEnum.Fullscreen
            or Window.ModeEnum.ExclusiveFullscreen;
    }

    /// <summary>
    /// Leaves maximized/fullscreen before interactive drag or edge-resize.
    /// </summary>
    /// <remarks>
    /// Borderless windows that remain maximized while the OS changes their frame size
    /// end up in a hybrid state: still "maximized" while no longer full-screen, which
    /// breaks layout/content scaling until the next full mode toggle. Always call this
    /// before <see cref="DisplayServer.WindowStartResize"/> or <see cref="DisplayServer.WindowStartDrag"/>.
    /// Prefer applying a known normal size/position so restore is not full-monitor sized.
    /// </remarks>
    /// <param name="window">Target window.</param>
    /// <param name="restoreSize">Optional normal size to apply after leaving fill-screen mode.</param>
    /// <param name="restorePosition">Optional global position to apply after leaving fill-screen mode.</param>
    /// <returns>True if the mode was changed to windowed.</returns>
    public static bool EnsureWindowedForInteraction(
        Window window,
        Vector2I? restoreSize = null,
        Vector2I? restorePosition = null)
    {
        if (window == null || !GodotObject.IsInstanceValid(window))
            return false;

        if (!IsWindowFillScreen(window))
            return false;

        // Leave fill-screen first so subsequent Size/Position apply as true windowed geometry.
        window.Mode = Window.ModeEnum.Windowed;

        if (restoreSize is Vector2I size && size.X > 0 && size.Y > 0)
            window.Size = size;

        if (restorePosition is Vector2I pos)
            window.Position = pos;

        return true;
    }

    /// <summary>
    /// Toggles between maximized and windowed. Uses <see cref="Window.ModeEnum.Maximized"/>
    /// (not Fullscreen) so borderless chrome and a normal restore rect are preserved.
    /// </summary>
    /// <param name="window">Target window.</param>
    public static void ToggleMaximize(Window window)
    {
        if (window == null || !GodotObject.IsInstanceValid(window))
            return;

        if (IsWindowFillScreen(window))
            window.Mode = Window.ModeEnum.Windowed;
        else
            window.Mode = Window.ModeEnum.Maximized;
    }

    /// <summary>
    /// Computes absolute window position from a display-relative position on the given screen,
    /// clamped so the window stays at least partially visible.
    /// </summary>
    /// <param name="screenIndex">DisplayServer screen index.</param>
    /// <param name="relativePosition">Position relative to that screen's top-left.</param>
    /// <param name="minVisibleWidth">Minimum horizontal overlap kept on-screen.</param>
    /// <param name="minVisibleHeight">Minimum vertical overlap kept on-screen.</param>
    /// <returns>Clamped global position.</returns>
    public static Vector2I ClampWindowPositionToScreen(
        int screenIndex,
        Vector2I relativePosition,
        int minVisibleWidth = 200,
        int minVisibleHeight = 80)
    {
        int count = DisplayServer.GetScreenCount();
        if (count <= 0)
            return relativePosition;

        screenIndex = Mathf.Clamp(screenIndex, 0, count - 1);
        Vector2I screenPos = DisplayServer.ScreenGetPosition(screenIndex);
        Vector2I screenSize = DisplayServer.ScreenGetSize(screenIndex);
        Vector2I absPos = screenPos + relativePosition;

        absPos.X = Mathf.Clamp(absPos.X, screenPos.X, screenPos.X + screenSize.X - minVisibleWidth);
        absPos.Y = Mathf.Clamp(absPos.Y, screenPos.Y, screenPos.Y + screenSize.Y - minVisibleHeight);
        return absPos;
    }

    /// <summary>
    /// Finds the screen index whose bounds contain <paramref name="point"/>, or 0 if none.
    /// </summary>
    public static int FindScreenAtPoint(Vector2I point)
    {
        int count = DisplayServer.GetScreenCount();
        for (int i = 0; i < count; i++)
        {
            Vector2I sPos = DisplayServer.ScreenGetPosition(i);
            Vector2I sSize = DisplayServer.ScreenGetSize(i);
            if (new Rect2I(sPos, sSize).HasPoint(point))
                return i;
        }

        return 0;
    }

    /// <summary>
    /// Converts a global window position to coordinates relative to the screen that contains
    /// the top-left (or the window center as fallback).
    /// </summary>
    public static Vector2I ToScreenRelativePosition(Vector2I globalPos, Vector2I windowSize)
    {
        int count = DisplayServer.GetScreenCount();
        for (int i = 0; i < count; i++)
        {
            Vector2I sPos = DisplayServer.ScreenGetPosition(i);
            Vector2I sSize = DisplayServer.ScreenGetSize(i);
            Rect2I screenRect = new Rect2I(sPos, sSize);

            if (screenRect.HasPoint(globalPos))
                return globalPos - sPos;

            Vector2I center = globalPos + (windowSize / 2);
            if (screenRect.HasPoint(center))
                return globalPos - sPos;
        }

        return globalPos;
    }
}