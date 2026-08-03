// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Rug.Osc;

namespace Cue2.Domain.Connections;

/// <summary>
/// Shared OSC argument parsing / formatting helpers for send components and built-in commands.
/// </summary>
public static class OscMessageUtil
{
    /// <summary>
    /// Parses a free-form args string into typed objects for <see cref="OscMessage"/>.
    /// Supports: integers, floats, quoted strings, true/false, and bare tokens as strings.
    /// Separators: whitespace or commas.
    /// </summary>
    public static object[] ParseArgsText(string argsText)
    {
        if (string.IsNullOrWhiteSpace(argsText))
            return Array.Empty<object>();

        var result = new List<object>();
        string s = argsText.Trim();
        int i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ','))
                i++;
            if (i >= s.Length) break;

            if (s[i] == '"' || s[i] == '\'')
            {
                char quote = s[i++];
                var sb = new StringBuilder();
                while (i < s.Length && s[i] != quote)
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        i++;
                        sb.Append(s[i++]);
                    }
                    else
                        sb.Append(s[i++]);
                }
                if (i < s.Length) i++; // closing quote
                result.Add(sb.ToString());
                continue;
            }

            int start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != ',')
                i++;
            string token = s.Substring(start, i - start);
            result.Add(CoerceToken(token));
        }

        return result.ToArray();
    }

    /// <summary>Coerces a bare token to int, float, bool, or string.</summary>
    public static object CoerceToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return string.Empty;
        if (string.Equals(token, "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(token, "false", StringComparison.OrdinalIgnoreCase)) return false;
        if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)
            && !token.Contains('.'))
            return i;
        if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
            return f;
        return token;
    }

    /// <summary>Formats args for display / monitor lines.</summary>
    public static string FormatArgs(IReadOnlyList<object> args)
    {
        if (args == null || args.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        for (int i = 0; i < args.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(FormatArg(args[i]));
        }
        return sb.ToString();
    }

    /// <summary>Formats args from a Rug.Osc message.</summary>
    public static string FormatArgs(OscMessage message)
    {
        if (message == null || message.Count == 0) return string.Empty;
        var list = new List<object>(message.Count);
        for (int i = 0; i < message.Count; i++)
            list.Add(message[i]);
        return FormatArgs(list);
    }

    public static string FormatArg(object arg)
    {
        if (arg == null) return "null";
        return arg switch
        {
            string s => $"\"{s}\"",
            float f => f.ToString("G", CultureInfo.InvariantCulture),
            double d => d.ToString("G", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => Convert.ToString(arg, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    /// <summary>Builds an <see cref="OscMessage"/> from path + optional args text.</summary>
    public static OscMessage BuildMessage(string address, string argsText)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("OSC address is empty.", nameof(address));
        string path = address.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;
        object[] args = ParseArgsText(argsText);
        return args.Length == 0 ? new OscMessage(path) : new OscMessage(path, args);
    }

    /// <summary>Tries to read argument <paramref name="index"/> as double.</summary>
    public static bool TryGetFloat(IReadOnlyList<object> args, int index, out double value)
    {
        value = 0;
        if (args == null || index < 0 || index >= args.Count) return false;
        object a = args[index];
        switch (a)
        {
            case float f: value = f; return true;
            case double d: value = d; return true;
            case int i: value = i; return true;
            case long l: value = l; return true;
            case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v):
                value = v; return true;
            default: return false;
        }
    }

    /// <summary>Tries to read argument <paramref name="index"/> as int.</summary>
    public static bool TryGetInt(IReadOnlyList<object> args, int index, out int value)
    {
        value = 0;
        if (!TryGetFloat(args, index, out double d)) return false;
        value = (int)Math.Round(d);
        return true;
    }

    /// <summary>Tries to read argument <paramref name="index"/> as string.</summary>
    public static bool TryGetString(IReadOnlyList<object> args, int index, out string value)
    {
        value = null;
        if (args == null || index < 0 || index >= args.Count) return false;
        value = args[index]?.ToString() ?? string.Empty;
        return true;
    }
}
