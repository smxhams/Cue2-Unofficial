// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using Cue2.Domain.Connections;
using Cue2.Domain.Cues;
using Cue2.Services;
using Godot;
using Godot.Collections;

namespace Cue2.Domain.ShowSettings;

/// <summary>
/// Partial: CaptureHistorySlice / ApplyPartialFromHistory
/// </summary>
public partial class Settings
{

    public void ApplyPartialFromHistory(Dictionary settingsData)
    {
        if (settingsData == null || settingsData.Count == 0) return;

        GD.Print($"Settings:ApplyPartialFromHistory - Applying {settingsData.Count} key(s)");

        // Open devices first so patch UI / playback can resolve hardware after restore.
        // We intentionally do not close devices absent from the snapshot — another patch or
        // direct-output cue may still need them, and closing mid-session is disruptive.
        if (TryGetSettingsValue(settingsData, "AudioDevices", out var devices))
        {
            var deviceArray = devices.AsGodotArray();
            foreach (var device in deviceArray)
            {
                string deviceName = device.AsString();
                if (string.IsNullOrEmpty(deviceName)) continue;
                _audioDevices.OpenAudioDevice(deviceName, out var _);
            }
        }

        // Replace the entire patch table when AudioPatch is present. Old GodotObjects are freed
        // so no UI or cue may keep a dangling reference — callers must rebuild matrix UIs and
        // RelinkCueComponents after restore (HistoryManager does both).
        if (TryGetSettingsValue(settingsData, "AudioPatch", out var patchs)
            && patchs.VariantType == Variant.Type.Dictionary)
        {
            foreach (var patch in _audioOutputPatches.Values.ToList())
            {
                if (patch != null && GodotObject.IsInstanceValid(patch))
                    patch.Free();
            }
            _audioOutputPatches.Clear();

            var patchDict = patchs.AsGodotDictionary();
            foreach (var patchKey in patchDict.Keys)
            {
                var patchAsDict = patchDict[patchKey].AsGodotDictionary();
                var patchObj = AudioOutputPatch.FromData(patchAsDict);
                if (patchObj != null)
                    AddPatch(patchObj);
                else
                    GD.PrintErr($"Settings:ApplyPartialFromHistory - Failed to restore patch key '{patchKey}'");
            }

            GD.Print($"Settings:ApplyPartialFromHistory - Restored {_audioOutputPatches.Count} audio output patch(es)");
            GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.RecheckAllQuiet();
        }

        if (TryGetSettingsValue(settingsData, "Displays", out var displays)
            && displays.VariantType == Variant.Type.Dictionary)
        {
            _displaysManager.LoadFromData(displays.AsGodotDictionary());
            // DisplaysChanged is emitted by LoadFromData; MediaHealthService also listens there.
        }

        if (settingsData.TryGetValue("CueLights", out var cueLights))
        {
            var cueLightsAsDict = cueLights.AsGodotDictionary();
            _globalData.CueLightManager.LoadData(cueLightsAsDict);
        }

        if (TryGetSettingsValue(settingsData, "UiScale", out var value))
        {
            UiScale = value.AsSingle();
            _globalSignals.EmitSignal(nameof(GlobalSignals.UiScaleChanged), UiScale);
        }
        if (TryGetSettingsValue(settingsData, "GoScale", out value))
        {
            GoScale = value.AsSingle();
            _globalSignals.EmitSignal(nameof(GlobalSignals.GoScaleChanged), GoScale);
        }
        if (TryGetSettingsValue(settingsData, "CueListScale", out value))
        {
            CueListScale = value.AsSingle();
            _globalSignals.EmitSignal(nameof(GlobalSignals.CueListScaleChanged), CueListScale);
        }
        if (TryGetSettingsValue(settingsData, "WaveformResolution", out value))
            WaveformResolution = value.AsInt32();
        if (TryGetSettingsValue(settingsData, "StopFadeDuration", out value))
            StopFadeDuration = value.AsSingle();
        if (TryGetSettingsValue(settingsData, "MediaBackupEnabled", out value))
            MediaBackupEnabled = ReadBoolVariant(value);
        if (TryGetSettingsValue(settingsData, "MultiEditEnabled", out value))
            MultiEditEnabled = ReadBoolVariant(value);
        if (TryGetSettingsValue(settingsData, "SelectNewCues", out value))
            SelectNewCues = ReadBoolVariant(value);
        if (TryGetSettingsValue(settingsData, "ShowMode", out value))
        {
            ShowMode = ReadBoolVariant(value);
            NotifyShowModeChanged();
        }
        if (TryGetSettingsValue(settingsData, "ShowTimelineWaveforms", out value))
            ShowTimelineWaveforms = ReadBoolVariant(value);
        if (TryGetSettingsValue(settingsData, "OutputBackgroundColor", out value))
        {
            OutputBackgroundColor = Color.FromString(value.AsString(), DefaultOutputBackgroundColor);
            _displaysManager?.ApplyOutputBackgroundColor(OutputBackgroundColor);
        }
        if (TryGetSettingsValue(settingsData, "VideoQualityMode", out value))
            VideoQualityMode = ClampEnum(value.AsInt32(), VideoQualityMode.PreferQuality, VideoQualityMode.PreferPerformance, DefaultVideoQualityMode);
        if (TryGetSettingsValue(settingsData, "VideoPreviewQuality", out value))
            VideoPreviewQuality = ClampEnum(value.AsInt32(), VideoPreviewQuality.Full, VideoPreviewQuality.Quarter, DefaultVideoPreviewQuality);
        if (TryGetSettingsValue(settingsData, "OutputVSyncMode", out value))
        {
            OutputVSyncMode = ClampEnum(value.AsInt32(), OutputVSyncMode.PreferVSync, OutputVSyncMode.LowLatency, DefaultOutputVSyncMode);
            _displaysManager?.ApplyOutputVSyncPreference();
        }
        if (TryGetSettingsValue(settingsData, "AudioLatencyMode", out value))
            AudioLatencyMode = ClampEnum(value.AsInt32(), AudioLatencyMode.PreferLowLatency, AudioLatencyMode.PreferStability, DefaultAudioLatencyMode);
        if (TryGetSettingsValue(settingsData, "AudioDeclickMs", out value))
            AudioDeclickMs = Math.Clamp(value.AsInt32(), MinAudioDeclickMs, MaxAudioDeclickMs);
        if (TryGetSettingsValue(settingsData, "AudioMasterVolume", out value))
        {
            AudioMasterVolume = Math.Clamp(value.AsSingle(), 0f, 1f);
            _audioDevices?.SetSessionMasterVolume(AudioMasterVolume);
        }

        if (TryGetSettingsValue(settingsData, "CueDefaults", out value)
            && value.VariantType == Variant.Type.Dictionary)
        {
            ApplyCueDefaultsFromDict(value.AsGodotDictionary());
        }

        if (TryGetSettingsValue(settingsData, "AudioDefaults", out value)
            && value.VariantType == Variant.Type.Dictionary)
        {
            ApplyAudioDefaultsFromDict(value.AsGodotDictionary());
        }

        if (TryGetSettingsValue(settingsData, "VideoDefaults", out value)
            && value.VariantType == Variant.Type.Dictionary)
        {
            ApplyVideoDefaultsFromDict(value.AsGodotDictionary());
        }

        if (TryGetSettingsValue(settingsData, "TextDefaults", out value)
            && value.VariantType == Variant.Type.Dictionary)
        {
            ApplyTextDefaultsFromDict(value.AsGodotDictionary());
        }

        if (settingsData.TryGetValue("CueLightIdleColour", out value))
            CueLightIdleColour = Color.FromString(value.AsString(), CueLightIdleColour);
        if (settingsData.TryGetValue("CueLightGoColour", out value))
            CueLightGoColour = Color.FromString(value.AsString(), CueLightGoColour);
        if (settingsData.TryGetValue("CueLightStandbyColour", out value))
            CueLightStandbyColour = Color.FromString(value.AsString(), CueLightStandbyColour);
        if (settingsData.TryGetValue("CueLightCountInColour", out value))
            CueLightCountInColour = Color.FromString(value.AsString(), CueLightCountInColour);
        if (settingsData.TryGetValue("CueLightBrightness", out value))
            CueLightBrightness = (byte)value;

        if (settingsData.TryGetValue("OscListen", out var oscListen))
        {
            var oscListenAsDict = oscListen.AsGodotDictionary();
            GetNodeOrNull<OscListen>("/root/OscListen")?.LoadFromData(oscListenAsDict);
        }

        if (settingsData.TryGetValue("OscConnections", out var oscConnections))
        {
            var oscConnectionsAsDict = oscConnections.AsGodotDictionary();
            GetNodeOrNull<OscConnections>("/root/OscConnections")?.LoadFromData(oscConnectionsAsDict);
        }

        if (TryGetSettingsValue(settingsData, "OscInputMap", out var oscInputMapSlice)
            && oscInputMapSlice.VariantType == Variant.Type.Dictionary)
        {
            GetNodeOrNull<OscListen>("/root/OscListen")
                ?.LoadInputMapBindingsData(oscInputMapSlice.AsGodotDictionary());
        }

        if (TryGetSettingsValue(settingsData, "Midi", out var midiSlice)
            && midiSlice.VariantType == Variant.Type.Dictionary)
        {
            GetNodeOrNull<MidiManager>("/root/MidiManager")?.LoadFromData(midiSlice.AsGodotDictionary());
        }

        if (TryGetSettingsValue(settingsData, "MidiInputMap", out var midiInputMapSlice)
            && midiInputMapSlice.VariantType == Variant.Type.Dictionary)
        {
            GetNodeOrNull<MidiManager>("/root/MidiManager")
                ?.LoadInputMapBindingsData(midiInputMapSlice.AsGodotDictionary());
        }

        // InputMap is stored in user prefs (not the showfile) but still participates in
        // session undo/redo via HistoryManager with the "InputMap" slice key.
        if (TryGetSettingsValue(settingsData, "InputMap", out var inputMapData) && _globalData != null)
        {
            _globalData.ApplyInputBindings(inputMapData.AsGodotDictionary());
            // Keep user:// bindings aligned with the restored live map.
            _globalData.UserDataManager?.PersistLiveInputMap();
        }
    }

    /// <summary>
    /// Captures a settings subset for undo/redo. Scalar general-settings keys are read directly
    /// (no full GetData) so history does not depend on displays/OSC/etc. serialization.
    /// When <paramref name="keys"/> is null or empty, returns a full <see cref="GetData"/> snapshot.
    /// </summary>
    /// <param name="keys">Optional key filter (e.g. "StopFadeDuration", "InputMap", "AudioPatch").</param>
    /// <returns>Dictionary suitable for history storage (caller should deep-clone if needed).</returns>
    public Dictionary CaptureHistorySlice(params string[] keys)
    {
        if (keys == null || keys.Length == 0)
            return GetData();

        var slice = new Dictionary();
        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key)) continue;
            if (TryCaptureScalarHistoryKey(key, out var value))
                slice[key] = value;
            else if (key == "InputMap" && _globalData != null)
            {
                // Live InputMap snapshot for undo (persisted to user prefs, not the showfile).
                slice[key] = _globalData.GetCustomInputBindings();
            }
            else if (key == "AudioPatch")
            {
                var patchTable = new Dictionary();
                foreach (var patch in _audioOutputPatches)
                    patchTable.Add(patch.Key, patch.Value.GetData());
                slice[key] = patchTable;
            }
            else if (key == "AudioDevices")
            {
                slice[key] = _audioDevices != null
                    ? _audioDevices.GetOpenAudioDevicesNames()
                    : new Array<string>();
            }
            else if (key == "Displays")
            {
                // Canvas size + screens + target layers — avoid full GetData (patches, OSC, …).
                slice[key] = _displaysManager != null
                    ? _displaysManager.GetData()
                    : new Dictionary();
            }
            else if (key == "CueDefaults")
            {
                slice[key] = CaptureCueDefaultsDict();
            }
            else if (key == "AudioDefaults")
            {
                slice[key] = CaptureAudioDefaultsDict();
            }
            else if (key == "VideoDefaults")
            {
                slice[key] = CaptureVideoDefaultsDict();
            }
            else if (key == "TextDefaults")
            {
                slice[key] = CaptureTextDefaultsDict();
            }
            else if (key == "Midi")
            {
                var midi = GetNodeOrNull<MidiManager>("/root/MidiManager");
                slice[key] = midi != null ? midi.GetData() : new Dictionary();
            }
            else if (key == "MidiInputMap")
            {
                var midi = GetNodeOrNull<MidiManager>("/root/MidiManager");
                slice[key] = midi != null ? midi.GetInputMapBindingsData() : new Dictionary();
            }
            else if (key == "OscListen")
            {
                var osc = GetNodeOrNull<OscListen>("/root/OscListen");
                slice[key] = osc != null ? osc.GetData() : new Dictionary();
            }
            else if (key == "OscInputMap")
            {
                var osc = GetNodeOrNull<OscListen>("/root/OscListen");
                slice[key] = osc != null ? osc.GetInputMapBindingsData() : new Dictionary();
            }
            else if (key == "OscConnections")
            {
                var oscConn = GetNodeOrNull<OscConnections>("/root/OscConnections");
                slice[key] = oscConn != null ? oscConn.GetData() : new Dictionary();
            }
            else
            {
                // Fallback for other complex keys (CueLights, …)
                var full = GetData();
                if (full.ContainsKey(key))
                    slice[key] = full[key];
            }
        }
        return slice;
    }

    /// <summary>
    /// Reads known general-settings scalars without building a full session settings dump.
    /// </summary>
    private bool TryCaptureScalarHistoryKey(string key, out Variant value)
    {
        switch (key)
        {
            case "UiScale":
                value = UiScale;
                return true;
            case "GoScale":
                value = GoScale;
                return true;
            case "CueListScale":
                value = CueListScale;
                return true;
            case "WaveformResolution":
                value = WaveformResolution;
                return true;
            case "StopFadeDuration":
                value = StopFadeDuration;
                return true;
            case "MediaBackupEnabled":
                // Store as int for stable JSON round-trip across Godot versions.
                value = MediaBackupEnabled ? 1 : 0;
                return true;
            case "MultiEditEnabled":
                value = MultiEditEnabled ? 1 : 0;
                return true;
            case "SelectNewCues":
                value = SelectNewCues ? 1 : 0;
                return true;
            case "ShowMode":
                value = ShowMode ? 1 : 0;
                return true;
            case "ShowTimelineWaveforms":
                value = ShowTimelineWaveforms ? 1 : 0;
                return true;
            case "OutputBackgroundColor":
                value = OutputBackgroundColor.ToHtml(true);
                return true;
            case "VideoQualityMode":
                value = (int)VideoQualityMode;
                return true;
            case "VideoPreviewQuality":
                value = (int)VideoPreviewQuality;
                return true;
            case "OutputVSyncMode":
                value = (int)OutputVSyncMode;
                return true;
            case "AudioLatencyMode":
                value = (int)AudioLatencyMode;
                return true;
            case "AudioDeclickMs":
                value = AudioDeclickMs;
                return true;
            case "AudioMasterVolume":
                value = AudioMasterVolume;
                return true;
            default:
                value = default;
                return false;
        }
    }

    /// <summary>
    /// Clamps a raw int into an enum range; returns <paramref name="fallback"/> if out of range.
    /// </summary>
    private static TEnum ClampEnum<TEnum>(int raw, TEnum min, TEnum max, TEnum fallback)
        where TEnum : struct, Enum
    {
        int minI = Convert.ToInt32(min);
        int maxI = Convert.ToInt32(max);
        if (raw < minI || raw > maxI)
            return fallback;
        return (TEnum)Enum.ToObject(typeof(TEnum), raw);
    }

    /// <summary>
    /// Resets all cue shell defaults to system factory values.
    /// </summary>
}
