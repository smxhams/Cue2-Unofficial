//==================================================================================//
// OscActionBinding.cs                                                              //
// This file is part of Cue2                                                        //
// http://cue2.live/                                                                //
//==================================================================================//

using System;
using Godot;
using Godot.Collections;

namespace Cue2.Shared;

/// <summary>
/// OSC pattern bound to a project InputMap action (e.g. Go, SaveSession).
/// Default is unbound (no OSC control).
/// </summary>
public sealed class OscActionBinding
{
    /// <summary>True when an OSC address pattern is assigned.</summary>
    public bool HasBinding { get; set; }

    /// <summary>OSC address path to match (e.g. "/go", "/cue/1/start"). Case-sensitive.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// When true, also require <see cref="ArgsDisplay"/> to match the received argument string.
    /// When false, only the address is matched (any args).
    /// </summary>
    public bool MatchArgs { get; set; }

    /// <summary>
    /// Serialized argument list used when <see cref="MatchArgs"/> is true
    /// (same format as the monitor log, e.g. "1" or "1, \"hello\"").
    /// </summary>
    public string ArgsDisplay { get; set; } = string.Empty;

    /// <summary>Factory default: unbound.</summary>
    public static OscActionBinding Unbound() => new();

    /// <summary>True when this differs from unbound default.</summary>
    public bool IsNonDefault => HasBinding;

    /// <summary>
    /// Human-readable summary (e.g. "/go" or "/level 0.5").
    /// </summary>
    public string GetDisplay()
    {
        if (!HasBinding || string.IsNullOrEmpty(Address)) return string.Empty;
        if (MatchArgs && !string.IsNullOrEmpty(ArgsDisplay))
            return $"{Address} {ArgsDisplay}";
        return Address;
    }

    /// <summary>
    /// Returns true when the received message matches this binding pattern.
    /// </summary>
    public bool Matches(string address, string argsDisplay)
    {
        if (!HasBinding || string.IsNullOrEmpty(Address)) return false;
        if (!string.Equals(Address, address, StringComparison.Ordinal)) return false;
        if (!MatchArgs) return true;
        return string.Equals(
            ArgsDisplay ?? string.Empty,
            argsDisplay ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Sets the binding from a captured OSC message.
    /// </summary>
    /// <param name="address">OSC address path.</param>
    /// <param name="argsDisplay">Formatted args (may be empty).</param>
    /// <param name="matchArgs">When true, require exact args match at runtime.</param>
    public void SetFromMessage(string address, string argsDisplay, bool matchArgs)
    {
        HasBinding = !string.IsNullOrWhiteSpace(address);
        Address = address?.Trim() ?? string.Empty;
        ArgsDisplay = argsDisplay ?? string.Empty;
        // Only match args when the message actually carried arguments.
        MatchArgs = matchArgs && !string.IsNullOrEmpty(ArgsDisplay);
    }

    /// <summary>Clears to unbound.</summary>
    public void Clear()
    {
        HasBinding = false;
        Address = string.Empty;
        MatchArgs = false;
        ArgsDisplay = string.Empty;
    }

    /// <summary>Serializes for showfile / history.</summary>
    public Dictionary ToDict()
    {
        var d = new Dictionary();
        d["HasBinding"] = HasBinding;
        d["Address"] = Address ?? string.Empty;
        d["MatchArgs"] = MatchArgs;
        d["ArgsDisplay"] = ArgsDisplay ?? string.Empty;
        return d;
    }

    /// <summary>Deserializes from showfile / history.</summary>
    public static OscActionBinding FromDict(Dictionary data)
    {
        var b = new OscActionBinding();
        if (data == null) return b;
        b.HasBinding = data.TryGetValue("HasBinding", out var h) && h.AsBool();
        b.Address = data.TryGetValue("Address", out var a) ? a.AsString() : string.Empty;
        b.MatchArgs = data.TryGetValue("MatchArgs", out var ma) && ma.AsBool();
        b.ArgsDisplay = data.TryGetValue("ArgsDisplay", out var ad) ? ad.AsString() : string.Empty;
        if (!b.HasBinding || string.IsNullOrEmpty(b.Address))
            b.Clear();
        return b;
    }

    /// <summary>Deep copy.</summary>
    public OscActionBinding Clone()
    {
        return new OscActionBinding
        {
            HasBinding = HasBinding,
            Address = Address,
            MatchArgs = MatchArgs,
            ArgsDisplay = ArgsDisplay
        };
    }
}
