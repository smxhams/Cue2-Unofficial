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
/// Partial: GetData / LoadSettings and related clamp helpers
/// </summary>
public partial class Settings
{

    public Dictionary GetData()
    {
        var saveTable = new Dictionary();
        var patchTable = new Dictionary();

        foreach (var patch in _audioOutputPatches)
        {
            patchTable.Add(patch.Key, patch.Value.GetData());
        }

        var devices = _audioDevices.GetOpenAudioDevicesNames();

        saveTable.Add("AudioPatch", patchTable);
        saveTable.Add("AudioDevices", devices);
        saveTable.Add("Displays", _displaysManager.GetData());
        saveTable.Add("CueLights", _globalData.CueLightManager.GetData());
        
        saveTable.Add("UiScale", UiScale);
        saveTable.Add("GoScale", GoScale);
        saveTable.Add("CueListScale", CueListScale);
        saveTable.Add("WaveformResolution", WaveformResolution);
        saveTable.Add("StopFadeDuration", StopFadeDuration);
        saveTable.Add("MediaBackupEnabled", MediaBackupEnabled);
        saveTable.Add("MultiEditEnabled", MultiEditEnabled);
        saveTable.Add("SelectNewCues", SelectNewCues);
        saveTable.Add("ShowMode", ShowMode);
        saveTable.Add("ShowTimelineWaveforms", ShowTimelineWaveforms);
        saveTable.Add("OutputBackgroundColor", OutputBackgroundColor.ToHtml(true));
        saveTable.Add("VideoQualityMode", (int)VideoQualityMode);
        saveTable.Add("VideoPreviewQuality", (int)VideoPreviewQuality);
        saveTable.Add("OutputVSyncMode", (int)OutputVSyncMode);
        saveTable.Add("AudioLatencyMode", (int)AudioLatencyMode);
        saveTable.Add("AudioDeclickMs", AudioDeclickMs);
        saveTable.Add("AudioMasterVolume", AudioMasterVolume);

        // Cue shell defaults (show-scoped)
        saveTable.Add("CueDefaults", CaptureCueDefaultsDict());
        saveTable.Add("AudioDefaults", CaptureAudioDefaultsDict());
        saveTable.Add("VideoDefaults", CaptureVideoDefaultsDict());
        saveTable.Add("TextDefaults", CaptureTextDefaultsDict());
        
        // Cuelights
        saveTable.Add("CueLightIdleColour", CueLightIdleColour.ToHtml());
        saveTable.Add("CueLightGoColour", CueLightGoColour.ToHtml());
        saveTable.Add("CueLightStandbyColour", CueLightStandbyColour.ToHtml());
        saveTable.Add("CueLightCountInColour", CueLightCountInColour.ToHtml());
        saveTable.Add("CueLightBrightness", CueLightBrightness);
        
        // Osc Listen
        var oscListen = GetNodeOrNull<OscListen>("/root/OscListen");
        if (oscListen != null)
        {
            saveTable.Add("OscListen", oscListen.GetData());
            // Project-action OSC bindings (Go, Save, …) — show-scoped, separate undo slice.
            saveTable.Add("OscInputMap", oscListen.GetInputMapBindingsData());
        }
        
        // Osc Connections
        saveTable.Add("OscConnections", GetNode<OscConnections>("/root/OscConnections").GetData());

        // MIDI session (enabled + session input device list)
        var midi = GetNodeOrNull<MidiManager>("/root/MidiManager");
        if (midi != null)
        {
            saveTable.Add("Midi", midi.GetData());
            // Project-action MIDI bindings (Go, Save, …) — show-scoped, separate undo slice.
            saveTable.Add("MidiInputMap", midi.GetInputMapBindingsData());
        }

        // Keyboard Input Map is stored in user:// via UserDataManager (not in the showfile).
        
        return saveTable;
    }

    public void LoadSettings(Dictionary settingsData)
    {
        GD.Print($"Settings:LoadSettings - Loading Settings");

        // Names from the showfile open-device list (may include devices opened for direct-output
        // that are not currently in a patch). Unioned with patch keys before reconcile.
        var showfileDeviceNames = new System.Collections.Generic.List<string>();
        bool loadedAudioDevicesOrPatches = false;

        if (settingsData.TryGetValue("AudioDevices", out var devices))
        {
            GD.Print($"Settings:LoadSettings - Loading AudioDevices");
            loadedAudioDevicesOrPatches = true;
            // Soft convert — hard cast (Array<string>) throws when JSON yields Array or Variant mix.
            var deviceArray = devices.AsGodotArray();
            foreach (var device in deviceArray)
            {
                string deviceName = device.AsString();
                if (string.IsNullOrEmpty(deviceName)) continue;
                showfileDeviceNames.Add(deviceName);
                _audioDevices.OpenAudioDevice(deviceName, out var _);
            }
        }

        if (settingsData.TryGetValue("AudioPatch", out var patchs))
        {
            GD.Print($"Settings:LoadSettings - Loading AudioPatches");
            loadedAudioDevicesOrPatches = true;
            // Replace any session-seeded Default Patch from ResetSettings so open/load is authoritative.
            foreach (var existing in _audioOutputPatches.Values.ToList())
            {
                if (existing != null && GodotObject.IsInstanceValid(existing))
                    existing.Free();
            }
            _audioOutputPatches.Clear();

            if (patchs.VariantType == Variant.Type.Dictionary)
            {
                var patchDict = patchs.AsGodotDictionary();
                foreach (var patchKey in patchDict.Keys)
                {
                    var patchAsDict = patchDict[patchKey].AsGodotDictionary();
                    var patchObj = AudioOutputPatch.FromData(patchAsDict);
                    if (patchObj != null)
                        AddPatch(patchObj);
                    else
                        GD.PrintErr("Settings:LoadSettings - Failed to deserialize an audio output patch.");
                }
            }
            else
            {
                GD.PrintErr($"Settings:LoadSettings - AudioPatch is not a Dictionary (got {patchs.VariantType}).");
            }

            // Older showfiles with an empty patch table still get a usable Default Patch.
            EnsureDefaultAudioPatch();
        }

        // After open + patch apply: close SDL devices left over from the previous session that
        // are not in the showfile open list and not referenced by any loaded patch.
        if (loadedAudioDevicesOrPatches)
            ReconcileOpenAudioDevices(showfileDeviceNames);
        
        if (settingsData.TryGetValue("Displays", out var displays))
        {
            GD.Print($"Settings:LoadSettings - Loading Displays");
            var displaysAsDict = displays.AsGodotDictionary();
            _displaysManager.LoadFromData(displaysAsDict);
        }

        if (settingsData.TryGetValue("CueLights", out var cueLights))
        {
            GD.Print($"Settings:LoadSettings - Loading CueLights");
            var cueLightsAsDict = cueLights.AsGodotDictionary();
            _globalData.CueLightManager.LoadData(cueLightsAsDict);
        }
        
        UiScale = settingsData.TryGetValue("UiScale", out var value) ? (float)value : UiScale;
        _globalSignals.EmitSignal(nameof(GlobalSignals.UiScaleChanged), UiScale);
        GoScale = settingsData.TryGetValue("GoScale", out value) ? (float)value : GoScale;
        _globalSignals.EmitSignal(nameof(GlobalSignals.GoScaleChanged), GoScale);
        CueListScale = settingsData.TryGetValue("CueListScale", out value)
            ? (float)value
            : DefaultCueListScale;
        _globalSignals.EmitSignal(nameof(GlobalSignals.CueListScaleChanged), CueListScale);
        WaveformResolution = settingsData.TryGetValue("WaveformResolution", out value) ? (int)value : WaveformResolution;
        StopFadeDuration = settingsData.TryGetValue("StopFadeDuration", out value) ? (float)value : StopFadeDuration;
        // Default true for older shows that predate this setting
        MediaBackupEnabled = settingsData.TryGetValue("MediaBackupEnabled", out value)
            ? value.AsBool()
            : DefaultMediaBackupEnabled;
        MultiEditEnabled = settingsData.TryGetValue("MultiEditEnabled", out value)
            ? value.AsBool()
            : DefaultMultiEditEnabled;
        // Default true for older shows that predate this setting
        SelectNewCues = settingsData.TryGetValue("SelectNewCues", out value)
            ? value.AsBool()
            : DefaultSelectNewCues;
        // Default false (edit mode) for older shows that predate this setting
        ShowMode = settingsData.TryGetValue("ShowMode", out value)
            ? value.AsBool()
            : DefaultShowMode;
        ShowTimelineWaveforms = settingsData.TryGetValue("ShowTimelineWaveforms", out value)
            ? value.AsBool()
            : DefaultShowTimelineWaveforms;
        OutputBackgroundColor = settingsData.TryGetValue("OutputBackgroundColor", out value)
            ? Color.FromString(value.AsString(), DefaultOutputBackgroundColor)
            : DefaultOutputBackgroundColor;
        VideoQualityMode = settingsData.TryGetValue("VideoQualityMode", out value)
            ? ClampEnum(value.AsInt32(), VideoQualityMode.PreferQuality, VideoQualityMode.PreferPerformance, DefaultVideoQualityMode)
            : DefaultVideoQualityMode;
        VideoPreviewQuality = settingsData.TryGetValue("VideoPreviewQuality", out value)
            ? ClampEnum(value.AsInt32(), VideoPreviewQuality.Full, VideoPreviewQuality.Quarter, DefaultVideoPreviewQuality)
            : DefaultVideoPreviewQuality;
        OutputVSyncMode = settingsData.TryGetValue("OutputVSyncMode", out value)
            ? ClampEnum(value.AsInt32(), OutputVSyncMode.PreferVSync, OutputVSyncMode.LowLatency, DefaultOutputVSyncMode)
            : DefaultOutputVSyncMode;
        AudioLatencyMode = settingsData.TryGetValue("AudioLatencyMode", out value)
            ? ClampEnum(value.AsInt32(), AudioLatencyMode.PreferLowLatency, AudioLatencyMode.PreferStability, DefaultAudioLatencyMode)
            : DefaultAudioLatencyMode;
        AudioDeclickMs = settingsData.TryGetValue("AudioDeclickMs", out value)
            ? Math.Clamp(value.AsInt32(), MinAudioDeclickMs, MaxAudioDeclickMs)
            : DefaultAudioDeclickMs;
        AudioMasterVolume = settingsData.TryGetValue("AudioMasterVolume", out value)
            ? Math.Clamp(value.AsSingle(), 0f, 1f)
            : DefaultAudioMasterVolume;

        // Loading a showfile should not keep the previous show's emergency disable/blackout.
        _displaysManager?.ClearRuntimeOutputControls();
        _displaysManager?.ApplyOutputBackgroundColor(OutputBackgroundColor);
        _displaysManager?.ApplyOutputVSyncPreference();
        _audioDevices?.SyncSessionMasterFromSettings();

        // Cue shell defaults (older shows without this key keep system defaults)
        if (settingsData.TryGetValue("CueDefaults", out value) && value.VariantType == Variant.Type.Dictionary)
            ApplyCueDefaultsFromDict(value.AsGodotDictionary());
        else
            ResetCueDefaultsToSystem();

        if (settingsData.TryGetValue("AudioDefaults", out value) && value.VariantType == Variant.Type.Dictionary)
            ApplyAudioDefaultsFromDict(value.AsGodotDictionary());
        else
            ResetAudioDefaultsToSystem();

        if (settingsData.TryGetValue("VideoDefaults", out value) && value.VariantType == Variant.Type.Dictionary)
            ApplyVideoDefaultsFromDict(value.AsGodotDictionary());
        else
            ResetVideoDefaultsToSystem();

        if (settingsData.TryGetValue("TextDefaults", out value) && value.VariantType == Variant.Type.Dictionary)
            ApplyTextDefaultsFromDict(value.AsGodotDictionary());
        else
            ResetTextDefaultsToSystem();
        
        CueLightIdleColour = settingsData.TryGetValue("CueLightIdleColour", out value) ? Color.FromString(value.AsString(), CueLightIdleColour) : CueLightIdleColour;
        CueLightGoColour = settingsData.TryGetValue("CueLightGoColour", out value) ? Color.FromString(value.AsString(), CueLightGoColour) : CueLightGoColour;
        CueLightStandbyColour = settingsData.TryGetValue("CueLightStandbyColour", out value) ? Color.FromString(value.AsString(), CueLightStandbyColour) : CueLightStandbyColour;
        CueLightCountInColour = settingsData.TryGetValue("CueLightCountInColour", out value) ? Color.FromString(value.AsString(), CueLightCountInColour) : CueLightCountInColour;
        CueLightBrightness = settingsData.TryGetValue("CueLightBrightness", out value) ? (byte)value : CueLightBrightness;
        
        // Osc Listen
        if (settingsData.TryGetValue("OscListen", out var oscListen))
        {
            GD.Print($"Settings:LoadSettings - Loading OscListen");
            var oscListenAsDict = oscListen.AsGodotDictionary();
            GetNodeOrNull<OscListen>("/root/OscListen")?.LoadFromData(oscListenAsDict);
        }

        if (settingsData.TryGetValue("OscInputMap", out var oscInputMap) &&
            oscInputMap.VariantType == Variant.Type.Dictionary)
        {
            GD.Print("Settings:LoadSettings - Loading OscInputMap");
            GetNodeOrNull<OscListen>("/root/OscListen")
                ?.LoadInputMapBindingsData(oscInputMap.AsGodotDictionary());
        }
        
        // Osc Connections
        if (settingsData.TryGetValue("OscConnections", out var oscConnections))
        {
            GD.Print($"Settings:LoadSettings - Loading OscConnections");
            var oscConnectionsAsDict = oscConnections.AsGodotDictionary();
            GetNode<OscConnections>("/root/OscConnections").LoadFromData(oscConnectionsAsDict);
        }

        if (settingsData.TryGetValue("Midi", out var midiData) && midiData.VariantType == Variant.Type.Dictionary)
        {
            GD.Print("Settings:LoadSettings - Loading Midi");
            GetNodeOrNull<MidiManager>("/root/MidiManager")?.LoadFromData(midiData.AsGodotDictionary());
        }

        if (settingsData.TryGetValue("MidiInputMap", out var midiInputMap) &&
            midiInputMap.VariantType == Variant.Type.Dictionary)
        {
            GD.Print("Settings:LoadSettings - Loading MidiInputMap");
            GetNodeOrNull<MidiManager>("/root/MidiManager")
                ?.LoadInputMapBindingsData(midiInputMap.AsGodotDictionary());
        }

        // Legacy showfiles may still contain "InputMap" — ignore; bindings are user preferences.
        if (settingsData.ContainsKey("InputMap"))
        {
            GD.Print("Settings:LoadSettings - Ignoring showfile InputMap (now stored in user preferences).");
        }

        // Sync show/edit mode UI after load (always notify so chrome matches the loaded value).
        NotifyShowModeChanged();
    }

    /// <summary>
    /// Applies a partial settings dictionary for scoped undo/redo without a full session reset.
    /// Only keys present in <paramref name="settingsData"/> are touched (e.g. StopFadeDuration alone
    /// will not rebuild displays).
    /// </summary>
    /// <param name="settingsData">Subset of <see cref="GetData"/> keys to restore.</param>
}
