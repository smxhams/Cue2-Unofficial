using System.Linq;
using Cue2.Base.Classes.Connections;
using Cue2.Shared;
using Godot;
using Godot.Collections;

namespace Cue2.Base.Classes;

/// <summary>
/// Manages storing of settings data
/// Child of GlobalData
/// </summary>
public partial class Settings : Node
{
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;
    private AudioDevices _audioDevices;
    private static Dictionary<int, AudioOutputPatch> _audioOutputPatches = new Dictionary<int, AudioOutputPatch>();
    private DisplaysManager _displaysManager;

    /// <summary>System default UI scale (1.0 = 100%).</summary>
    public const float DefaultUiScale = 1.0f;

    /// <summary>System default Go button scale (1.0 = base).</summary>
    public const float DefaultGoScale = 1.0f;

    /// <summary>System default waveform peak bin count.</summary>
    public const int DefaultWaveformResolution = 4096;

    /// <summary>System default stop fade-out duration in seconds.</summary>
    public const float DefaultStopFadeDuration = 2.5f;

    /// <summary>Default for copying used media into the show folder (Audio/Video/Images).</summary>
    public const bool DefaultMediaBackupEnabled = true;

    public float UiScale = DefaultUiScale;
    public float GoScale = DefaultGoScale;
    public int WaveformResolution = DefaultWaveformResolution;

    /// <summary>
    /// Global stop fade-out duration in seconds (first Stop fades; second Stop hard-cuts).
    /// 0 = immediate stop. Persisted with the session.
    /// </summary>
    public float StopFadeDuration = DefaultStopFadeDuration;

    /// <summary>
    /// When true, used media files are copied into the show folder (Audio/Video/Images)
    /// so the show can be moved between machines with relative media paths.
    /// Persisted with the showfile.
    /// </summary>
    public bool MediaBackupEnabled = DefaultMediaBackupEnabled;

    public bool VerbosePrint = true;
    
    
    // Cuelight settings
    public Color CueLightIdleColour = new Color(0f, 0f, 0.1f, 1f);
    public Color CueLightGoColour = new Color(0f, 1f, 0f, 1f);
    public Color CueLightStandbyColour = new Color(1f, 0.4f, 0f, 1f);
    public Color CueLightCountInColour = new Color(1f, 0f, 0f, 1f);
    public byte CueLightBrightness = 50;
    
    
    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _audioDevices = GetNode<AudioDevices>("/root/AudioDevices");
        _displaysManager = GetNode<DisplaysManager>("/root/DisplaysManager");
    }

    public override void _ExitTree()
    {
        // Ensure all AudioOutputPatch GodotObjects (which are never added to the scene tree)
        // are explicitly Freed on shutdown to prevent leaks. They are owned by this static
        // collection and the audio patch settings UI.
        foreach (var patch in _audioOutputPatches.Values.ToList())
        {
            if (patch != null && GodotObject.IsInstanceValid(patch))
            {
                patch.Free();
            }
        }
        _audioOutputPatches.Clear();
    }
    
    public Dictionary<int, AudioOutputPatch> GetAudioOutputPatches() => _audioOutputPatches;
    
    
    public void UpdatePatch(AudioOutputPatch patch)
    {
        _audioOutputPatches[patch.Id] = patch;
        GD.Print($"Settings:UpdatePatch - Updated patch with id: {patch.Id} and name: {patch.Name}");
    }

    public void DeletePatch(int patchId)
    {
        _audioOutputPatches[patchId].Free();
        _audioOutputPatches.Remove(patchId);
    } 
    
    public AudioOutputPatch CreateNewPatch()
    {
        var newPatch = new AudioOutputPatch();
        _audioOutputPatches.Add(newPatch.Id, newPatch);
        return newPatch;
    }
    
    public AudioOutputPatch GetPatch(int patchId) => _audioOutputPatches[patchId];

    public void AddPatch(AudioOutputPatch patch)
    {
        if (_audioOutputPatches.ContainsKey(patch.Id))
        {
            GD.PrintErr($"Settings:AddPatch - Patch ID already exists: {patch.Id}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"Settings:AddPatch - Failed to add patch due to duplicate ID: {patch.Id}", 2);
            return;
        }
        _audioOutputPatches.Add(patch.Id, patch);
        GD.Print($"Settings:AddPatch - Added patch with ID: {patch.Id} and name: {patch.Name}");
        
        // Double check audio devices in added patch are opened. 
        foreach (var device in patch.OutputDevices)
        {
            _audioDevices.OpenAudioDevice(device.Key, out var _);
        }
    }

    private void PrintPatches()
    {
        foreach (var patch in _audioOutputPatches)
        {
            foreach (var channels in patch.Value.Channels)
            {
                GD.Print($"Settings:PrintPatches - ID: {patch.Key} Name: {patch.Value.Name} Channel: {channels.Key} Name: {channels.Value}");
            }
        }
    }
    
    
    // Save and loads
    public void ResetSettings()
    {
        foreach (var patch in _audioOutputPatches)
        {
            patch.Value.Free();
        }
        _audioOutputPatches.Clear();
        
        UiScale = DefaultUiScale;
        GoScale = DefaultGoScale;
        WaveformResolution = DefaultWaveformResolution;
        StopFadeDuration = DefaultStopFadeDuration;
        MediaBackupEnabled = DefaultMediaBackupEnabled;
        CueLightIdleColour = new Color(0f, 0f, 0.1f, 1f);
        CueLightGoColour = new Color(0f, 1f, 0f, 1f);
        CueLightStandbyColour = new Color(1f, 0.4f, 0f, 1f);
        CueLightCountInColour = new Color(1f, 0f, 0f, 1f);

        // Restore input map bindings to project defaults (e.g. on New Session)
        _globalData?.ResetInputBindingsToDefaults();
    }

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
        saveTable.Add("WaveformResolution", WaveformResolution);
        saveTable.Add("StopFadeDuration", StopFadeDuration);
        saveTable.Add("MediaBackupEnabled", MediaBackupEnabled);
        
        // Cuelights
        saveTable.Add("CueLightIdleColour", CueLightIdleColour.ToHtml());
        saveTable.Add("CueLightGoColour", CueLightGoColour.ToHtml());
        saveTable.Add("CueLightStandbyColour", CueLightStandbyColour.ToHtml());
        saveTable.Add("CueLightCountInColour", CueLightCountInColour.ToHtml());
        saveTable.Add("CueLightBrightness", CueLightBrightness);
        
        // Osc Listen
        saveTable.Add("OscListen", GetNode<OscListen>("/root/OscListen").GetData());
        
        // Osc Connections
        saveTable.Add("OscConnections", GetNode<OscConnections>("/root/OscConnections").GetData());

        // Per-session custom input bindings (from live InputMap)
        if (_globalData != null)
            saveTable.Add("InputMap", _globalData.GetCustomInputBindings());
        else
            saveTable.Add("InputMap", new Dictionary());
        
        return saveTable;
    }

    public void LoadSettings(Dictionary settingsData)
    {
        GD.Print($"Settings:LoadSettings - Loading Settings");

        if (settingsData.TryGetValue("AudioDevices", out var devices))
        {
            GD.Print($"Settings:LoadSettings - Loading AudioDevices");
            var deviceArray = (Array<string>)devices;
            foreach (var device in deviceArray)
            {
                _audioDevices.OpenAudioDevice(device, out var _);
            }
        }

        if (settingsData.TryGetValue("AudioPatch", out var patchs))
        {
            GD.Print($"Settings:LoadSettings - Loading AudioPatches");
            foreach (var patch in (Dictionary)patchs)
            {
                var patchAsDict = patch.Value.AsGodotDictionary();
                var patchObj = AudioOutputPatch.FromData(patchAsDict);
                _globalData.Settings.AddPatch(patchObj);
            }
        }
        
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
        WaveformResolution = settingsData.TryGetValue("WaveformResolution", out value) ? (int)value : WaveformResolution;
        StopFadeDuration = settingsData.TryGetValue("StopFadeDuration", out value) ? (float)value : StopFadeDuration;
        // Default true for older shows that predate this setting
        MediaBackupEnabled = settingsData.TryGetValue("MediaBackupEnabled", out value)
            ? value.AsBool()
            : DefaultMediaBackupEnabled;
        
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
            GetNode<OscListen>("/root/OscListen").LoadFromData(oscListenAsDict);
        }
        
        // Osc Connections
        if (settingsData.TryGetValue("OscConnections", out var oscConnections))
        {
            GD.Print($"Settings:LoadSettings - Loading OscConnections");
            var oscConnectionsAsDict = oscConnections.AsGodotDictionary();
            GetNode<OscConnections>("/root/OscConnections").LoadFromData(oscConnectionsAsDict);
        }

        // Custom input map bindings saved with the session
        if (settingsData.TryGetValue("InputMap", out var inputMapData) && _globalData != null)
        {
            GD.Print($"Settings:LoadSettings - Loading custom InputMap bindings");
            _globalData.ApplyInputBindings(inputMapData.AsGodotDictionary());
        }
    }

    /// <summary>
    /// Applies a partial settings dictionary for scoped undo/redo without a full session reset.
    /// Only keys present in <paramref name="settingsData"/> are touched (e.g. StopFadeDuration alone
    /// will not rebuild displays).
    /// </summary>
    /// <param name="settingsData">Subset of <see cref="GetData"/> keys to restore.</param>
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
        }

        if (TryGetSettingsValue(settingsData, "Displays", out var displays)
            && displays.VariantType == Variant.Type.Dictionary)
        {
            _displaysManager.LoadFromData(displays.AsGodotDictionary());
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
        if (TryGetSettingsValue(settingsData, "WaveformResolution", out value))
            WaveformResolution = value.AsInt32();
        if (TryGetSettingsValue(settingsData, "StopFadeDuration", out value))
            StopFadeDuration = value.AsSingle();
        if (TryGetSettingsValue(settingsData, "MediaBackupEnabled", out value))
            MediaBackupEnabled = ReadBoolVariant(value);

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
            GetNode<OscListen>("/root/OscListen").LoadFromData(oscListenAsDict);
        }

        if (settingsData.TryGetValue("OscConnections", out var oscConnections))
        {
            var oscConnectionsAsDict = oscConnections.AsGodotDictionary();
            GetNode<OscConnections>("/root/OscConnections").LoadFromData(oscConnectionsAsDict);
        }

        if (TryGetSettingsValue(settingsData, "InputMap", out var inputMapData) && _globalData != null)
            _globalData.ApplyInputBindings(inputMapData.AsGodotDictionary());
    }

    /// <summary>
    /// Captures a settings subset for undo/redo. Scalar general-settings keys are read directly
    /// (no full GetData) so history does not depend on displays/OSC/etc. serialization.
    /// When <paramref name="keys"/> is null or empty, returns a full <see cref="GetData"/> snapshot.
    /// </summary>
    /// <param name="keys">Optional key filter (e.g. "StopFadeDuration", "InputMap").</param>
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
                slice[key] = _globalData.GetCustomInputBindings();
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
            else
            {
                // Fallback for other complex keys (CueLights, OscListen, …)
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
            default:
                value = default;
                return false;
        }
    }

    /// <summary>
    /// TryGetValue that tolerates string / StringName keys after JSON history clones.
    /// </summary>
    private static bool TryGetSettingsValue(Dictionary data, string key, out Variant value)
    {
        value = default;
        if (data == null || string.IsNullOrEmpty(key)) return false;
        if (data.TryGetValue(key, out value)) return true;

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

    private static bool ReadBoolVariant(Variant value)
    {
        return value.VariantType switch
        {
            Variant.Type.Bool => value.AsBool(),
            Variant.Type.Int => value.AsInt32() != 0,
            Variant.Type.Float => !Mathf.IsZeroApprox(value.AsSingle()),
            Variant.Type.String => value.AsString() is "1" or "true" or "True",
            _ => value.AsBool()
        };
    }
    
}