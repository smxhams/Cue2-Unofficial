using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;

namespace Cue2.Base.Classes;

/// <summary>
/// Categories and file-format helpers for exporting/importing show settings
/// independently of the session (.c2) showfile.
/// </summary>
/// <remarks>
/// Keyboard Input Map and Cue2 Preferences live in user:// and are intentionally
/// excluded — this format only covers show-scoped settings.
/// </remarks>
public static class SettingsExport
{
    /// <summary>Marker stored in the JSON root so loaders can reject unrelated files.</summary>
    public const string FormatId = "cue2-settings";

    /// <summary>Current on-disk format version.</summary>
    public const int FormatVersion = 1;

    /// <summary>File extension without the leading dot.</summary>
    public const string FileExtension = "c2settings";

    /// <summary>Godot FileDialog filter string.</summary>
    public const string FileDialogFilter = "*.c2settings ; Cue2 Settings";

    /// <summary>
    /// One user-facing filter category mapped to one or more <see cref="Settings.GetData"/> keys.
    /// </summary>
    public readonly struct Category
    {
        /// <summary>Stable id stored in export documents and used as UI checkbox metadata.</summary>
        public string Id { get; }

        /// <summary>Label shown in the filter dropdown.</summary>
        public string Label { get; }

        /// <summary>Settings serialization keys included when this category is selected.</summary>
        public string[] Keys { get; }

        /// <summary>
        /// Creates a filter category.
        /// </summary>
        /// <param name="id">Stable category id.</param>
        /// <param name="label">UI label.</param>
        /// <param name="keys">Settings keys to include.</param>
        public Category(string id, string label, params string[] keys)
        {
            Id = id;
            Label = label;
            Keys = keys ?? System.Array.Empty<string>();
        }
    }

    /// <summary>
    /// All selectable export/import categories (order matches the Settings tree roughly).
    /// </summary>
    public static readonly Category[] Categories =
    {
        new("General", "General",
            "UiScale", "GoScale", "WaveformResolution", "StopFadeDuration",
            "MediaBackupEnabled", "MultiEditEnabled", "SelectNewCues", "ShowTimelineWaveforms"),
        new("CueDefaults", "Cue Defaults",
            "CueDefaults", "AudioDefaults", "VideoDefaults", "TextDefaults"),
        new("AudioPatch", "Audio Output Patch", "AudioPatch", "AudioDevices"),
        new("Displays", "Canvas / Displays", "Displays"),
        new("CueLights", "Cue Lights",
            "CueLights", "CueLightIdleColour", "CueLightGoColour",
            "CueLightStandbyColour", "CueLightCountInColour", "CueLightBrightness"),
        new("OscConnections", "OSC Connections", "OscConnections"),
        new("OscListen", "OSC Listener", "OscListen"),
        new("OscInputMap", "OSC Input Map", "OscInputMap"),
        new("Midi", "MIDI", "Midi"),
        new("MidiInputMap", "MIDI Input Map", "MidiInputMap"),
    };

    /// <summary>
    /// Resolves selected category ids to a de-duplicated ordered list of settings keys.
    /// Unknown ids are ignored. Empty or null selection yields an empty array (caller should treat as no-op).
    /// </summary>
    /// <param name="categoryIds">Selected category ids (e.g. "AudioPatch"). Pass all category ids for a full export.</param>
    /// <returns>Settings keys suitable for <see cref="Settings.CaptureHistorySlice"/>.</returns>
    public static string[] ResolveKeys(IEnumerable<string> categoryIds)
    {
        if (categoryIds == null)
            return System.Array.Empty<string>();

        var selected = new HashSet<string>(categoryIds.Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.Ordinal);
        if (selected.Count == 0)
            return System.Array.Empty<string>();

        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cat in Categories)
        {
            if (!selected.Contains(cat.Id))
                continue;
            foreach (var key in cat.Keys)
            {
                if (seen.Add(key))
                    keys.Add(key);
            }
        }

        return keys.ToArray();
    }

    /// <summary>
    /// Builds a root document dictionary for writing a .c2settings file.
    /// </summary>
    /// <param name="categoryIds">Categories the user chose to include.</param>
    /// <param name="settingsSlice">Payload from <see cref="Settings.CaptureHistorySlice"/>.</param>
    /// <returns>Dictionary ready for <see cref="Json.Stringify"/>.</returns>
    public static Dictionary BuildDocument(IEnumerable<string> categoryIds, Dictionary settingsSlice)
    {
        var cats = new Godot.Collections.Array();
        if (categoryIds != null)
        {
            foreach (var id in categoryIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
                cats.Add(id);
        }

        return new Dictionary
        {
            ["format"] = FormatId,
            ["version"] = FormatVersion,
            ["categories"] = cats,
            ["settings"] = settingsSlice ?? new Dictionary()
        };
    }

    /// <summary>
    /// Parses and validates a settings export document.
    /// </summary>
    /// <param name="root">Parsed JSON root.</param>
    /// <param name="settings">On success, the nested settings dictionary.</param>
    /// <param name="categoriesInFile">Category ids recorded when the file was saved (may be empty for older/hand-edited files).</param>
    /// <param name="error">Human-readable failure reason when false is returned.</param>
    /// <returns>True when the document is a usable Cue2 settings export.</returns>
    public static bool TryParseDocument(Dictionary root, out Dictionary settings,
        out string[] categoriesInFile, out string error)
    {
        settings = null;
        categoriesInFile = System.Array.Empty<string>();
        error = null;

        if (root == null || root.Count == 0)
        {
            error = "File is empty or not a valid JSON object.";
            return false;
        }

        // Accept either the dedicated export format or a bare settings table (advanced / hand-made).
        bool hasFormat = TryGetString(root, "format", out var format);
        if (hasFormat)
        {
            if (!string.Equals(format, FormatId, StringComparison.Ordinal))
            {
                error = $"Unsupported settings file format '{format}'.";
                return false;
            }

            if (TryGetVariant(root, "settings", out var settingsVar) &&
                settingsVar.VariantType == Variant.Type.Dictionary)
            {
                settings = settingsVar.AsGodotDictionary();
            }
            else
            {
                error = "Settings file is missing the 'settings' object.";
                return false;
            }

            if (TryGetVariant(root, "categories", out var catsVar) &&
                catsVar.VariantType == Variant.Type.Array)
            {
                var list = new List<string>();
                foreach (var item in catsVar.AsGodotArray())
                {
                    string id = item.AsString();
                    if (!string.IsNullOrWhiteSpace(id))
                        list.Add(id);
                }
                categoriesInFile = list.ToArray();
            }
        }
        else
        {
            // Bare settings dictionary (e.g. the "settings" block copied from a showfile dump).
            settings = root;
        }

        if (settings == null || settings.Count == 0)
        {
            error = "Settings file contains no settings data.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Filters a loaded settings dictionary to only the keys belonging to the selected categories.
    /// Keys present in the file but not selected are dropped; selected keys missing from the file are ignored.
    /// </summary>
    /// <param name="fileSettings">Settings payload from the file.</param>
    /// <param name="categoryIds">User-selected import categories.</param>
    /// <returns>Subset dictionary safe to pass to <see cref="Settings.ApplyPartialFromHistory"/>.</returns>
    public static Dictionary FilterSettingsByCategories(Dictionary fileSettings, IEnumerable<string> categoryIds)
    {
        var result = new Dictionary();
        if (fileSettings == null || fileSettings.Count == 0)
            return result;

        var keys = ResolveKeys(categoryIds);
        if (keys.Length == 0)
            return result;

        var keySet = new HashSet<string>(keys, StringComparer.Ordinal);
        foreach (var kvp in fileSettings)
        {
            string key = kvp.Key.AsString();
            if (keySet.Contains(key))
                result[key] = kvp.Value;
        }

        return result;
    }

    private static bool TryGetString(Dictionary data, string key, out string value)
    {
        value = null;
        if (!TryGetVariant(data, key, out var v))
            return false;
        value = v.AsString();
        return !string.IsNullOrEmpty(value);
    }

    private static bool TryGetVariant(Dictionary data, string key, out Variant value)
    {
        value = default;
        if (data == null || string.IsNullOrEmpty(key))
            return false;
        if (data.TryGetValue(key, out value))
            return true;

        foreach (var k in data.Keys)
        {
            if (k.AsString() == key)
            {
                value = data[k];
                return true;
            }
        }

        return false;
    }
}
