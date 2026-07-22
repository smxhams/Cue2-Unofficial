using System;
using System.Linq;
using Cue2.Base.Classes.Connections;
using Cue2.Base.Classes.CueTypes;
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

    /// <summary>Default for multi-edit of shell properties when multiple cues are selected.</summary>
    public const bool DefaultMultiEditEnabled = true;

    /// <summary>Default for selecting a newly created cue after Add / drop.</summary>
    public const bool DefaultSelectNewCues = true;

    // ── Cue shell defaults (system factory values) ─────────────────────────

    /// <summary>System default pre-wait in seconds for newly created cues.</summary>
    public const double SystemDefaultCuePreWait = 0.0;

    /// <summary>System default post-wait in seconds for newly created cues.</summary>
    public const double SystemDefaultCuePostWait = 0.0;

    /// <summary>System default continue mode for newly created cues.</summary>
    public const FollowType SystemDefaultCueFollow = FollowType.None;

    /// <summary>System default shell colour for newly created cues.</summary>
    public static readonly Color SystemDefaultCueColor = new Color(0f, 0f, 0f, 1.0f);

    /// <summary>System default armed state for newly created cues.</summary>
    public const bool SystemDefaultCueArmed = true;

    /// <summary>System default skip-if-disarmed for newly created cues.</summary>
    public const bool SystemDefaultCueSkipIfDisarmed = false;

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

    /// <summary>
    /// When true and multiple cues are selected, the Shell Inspector enters multi-edit mode
    /// (blank fields; edits apply to every selected cue). When false, only the last-focused
    /// cue is shown/edited. Persisted with the showfile.
    /// </summary>
    public bool MultiEditEnabled = DefaultMultiEditEnabled;

    /// <summary>
    /// When true, newly created cues (Add Cue, media drop) become the selection/focus.
    /// When false, selection is left unchanged. Persisted with the showfile.
    /// </summary>
    public bool SelectNewCues = DefaultSelectNewCues;

    // ── Cue shell defaults (show-scoped; applied to newly created cues) ─────

    /// <summary>Default pre-wait (seconds) applied when a new cue is created.</summary>
    public double CueDefaultPreWait = SystemDefaultCuePreWait;

    /// <summary>Default post-wait (seconds) applied when a new cue is created.</summary>
    public double CueDefaultPostWait = SystemDefaultCuePostWait;

    /// <summary>Default continue mode applied when a new cue is created.</summary>
    public FollowType CueDefaultFollow = SystemDefaultCueFollow;

    /// <summary>Default shell colour applied when a new cue is created.</summary>
    public Color CueDefaultColor = SystemDefaultCueColor;

    /// <summary>Default armed state applied when a new cue is created.</summary>
    public bool CueDefaultArmed = SystemDefaultCueArmed;

    /// <summary>Default skip-if-disarmed applied when a new cue is created.</summary>
    public bool CueDefaultSkipIfDisarmed = SystemDefaultCueSkipIfDisarmed;

    public bool VerbosePrint = true;
    
    
    // Cuelight settings
    public Color CueLightIdleColour = new Color(0f, 0f, 0.1f, 1f);
    public Color CueLightGoColour = new Color(0f, 1f, 0f, 1f);
    public Color CueLightStandbyColour = new Color(1f, 0.4f, 0f, 1f);
    public Color CueLightCountInColour = new Color(1f, 0f, 0f, 1f);
    public byte CueLightBrightness = 50;
    
    
    /// <summary>Factory name for the audio output patch created on new sessions.</summary>
    public const string DefaultAudioPatchName = "Default Patch";

    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _audioDevices = GetNode<AudioDevices>("/root/AudioDevices");
        _displaysManager = GetNode<DisplaysManager>("/root/DisplaysManager");

        // First launch / empty show: ensure a Default Patch exists (same as DisplaysManager defaults).
        // Deferred so AudioDevices autoload _Ready and SDL device enumeration are fully available.
        CallDeferred(nameof(EnsureDefaultAudioPatch));
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
        // Shell ✕ / media health depends on patch existence
        GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.RecheckAllQuiet();
    } 
    
    public AudioOutputPatch CreateNewPatch()
    {
        var newPatch = new AudioOutputPatch();
        _audioOutputPatches.Add(newPatch.Id, newPatch);
        return newPatch;
    }

    /// <summary>
    /// Creates the session "Default Patch", optionally routing the current system default playback device.
    /// </summary>
    /// <returns>The newly created patch.</returns>
    /// <remarks>
    /// Stereo patch channels Left/Right are 1:1 mapped to device outputs 0/1 when available.
    /// On mono devices both channels route to output 0. If the system default cannot be resolved
    /// or opened, the patch is still created empty so the user can assign a device later.
    /// </remarks>
    public AudioOutputPatch CreateDefaultAudioPatch()
    {
        var patch = new AudioOutputPatch(DefaultAudioPatchName);
        _audioOutputPatches.Add(patch.Id, patch);

        TryRouteSystemDefaultDevice(patch);

        GD.Print($"Settings:CreateDefaultAudioPatch - Created '{patch.Name}' (id={patch.Id}), " +
                 $"devices={patch.OutputDevices.Count}");
        return patch;
    }

    /// <summary>
    /// Ensures at least one audio output patch exists by creating <see cref="DefaultAudioPatchName"/> when empty.
    /// </summary>
    /// <remarks>
    /// Used on first launch and after resets. Does nothing when patches already exist (loaded show, etc.).
    /// </remarks>
    public void EnsureDefaultAudioPatch()
    {
        if (_audioOutputPatches.Count > 0)
            return;

        CreateDefaultAudioPatch();
        GD.Print("Settings:EnsureDefaultAudioPatch - No patches present; created Default Patch.");
    }

    /// <summary>
    /// Returns the preferred audio output patch for newly created media cues.
    /// </summary>
    /// <returns>
    /// Patch named <see cref="DefaultAudioPatchName"/> when present; otherwise the lowest-id patch;
    /// or <c>null</c> when no patches exist.
    /// </returns>
    public AudioOutputPatch GetPreferredAudioOutputPatch()
    {
        if (_audioOutputPatches.Count == 0)
            return null;

        foreach (var patch in _audioOutputPatches.Values)
        {
            if (patch != null && GodotObject.IsInstanceValid(patch) &&
                string.Equals(patch.Name, DefaultAudioPatchName, StringComparison.Ordinal))
            {
                return patch;
            }
        }

        return _audioOutputPatches
            .OrderBy(kv => kv.Key)
            .Select(kv => kv.Value)
            .FirstOrDefault(p => p != null && GodotObject.IsInstanceValid(p));
    }

    /// <summary>
    /// Opens the system default playback device (if any) and wires it into <paramref name="patch"/>
    /// with a simple stereo default route.
    /// </summary>
    private void TryRouteSystemDefaultDevice(AudioOutputPatch patch)
    {
        if (patch == null || _audioDevices == null)
            return;

        string deviceName = _audioDevices.GetSystemDefaultPlaybackDeviceName();
        if (string.IsNullOrEmpty(deviceName))
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "Default Patch created without a system output device (none detected).", 1);
            return;
        }

        // Prefer a name that appears in the current playback enumeration so open-by-name succeeds.
        var available = _audioDevices.GetAvailableAudioDeviceNames();
        if (available != null && available.Count > 0 &&
            !available.Contains(deviceName, StringComparer.Ordinal))
        {
            // Case-insensitive match (Windows device names can vary slightly).
            var match = available.FirstOrDefault(n =>
                string.Equals(n, deviceName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                deviceName = match;
            else
            {
                GD.Print($"Settings:TryRouteSystemDefaultDevice - Default name '{deviceName}' " +
                         "not in available list; attempting open anyway.");
            }
        }

        var device = _audioDevices.OpenAudioDevice(deviceName, out string error);
        if (device == null)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"Default Patch: could not open system output '{deviceName}': {error}", 1);
            return;
        }

        int outputCount = Math.Max(1, device.Channels);
        patch.AddDeviceOutputs(deviceName, outputCount);

        // Default stereo channels (constructor order: Left=0, Right=1).
        var channelIds = patch.Channels.Keys.OrderBy(k => k).ToList();
        if (channelIds.Count > 0 && outputCount > 0)
            patch.SetRouting(deviceName, 0, channelIds[0], true);

        if (channelIds.Count > 1)
        {
            // Stereo device: Right → output 1. Mono: fold Right onto output 0.
            int rightOut = outputCount > 1 ? 1 : 0;
            patch.SetRouting(deviceName, rightOut, channelIds[1], true);
        }

        _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
            $"Default Patch routed to system output: {deviceName}", 0);
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
        // Note: callers that bulk-add patches (load/history) should recheck once afterward;
        // single interactive deletes recheck in DeletePatch.
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
    /// <summary>
    /// Resets all show/session settings to factory defaults (New Session / before Open).
    /// </summary>
    /// <remarks>
    /// Clears audio patches then seeds a <see cref="DefaultAudioPatchName"/> (system playback
    /// device when available). Also resets displays (canvas/layers/screens), cue lights,
    /// OSC listen/connections, and general scalars. Does <b>not</b> reset Input Map — that
    /// lives in user preferences. Emits scale/display signals so live UI can resync.
    /// When used as the wipe step before Open, <see cref="LoadSettings"/> replaces the seeded
    /// Default Patch with the showfile's patch table.
    /// </remarks>
    public void ResetSettings()
    {
        // Audio output patches
        foreach (var patch in _audioOutputPatches.Values.ToList())
        {
            if (patch != null && GodotObject.IsInstanceValid(patch))
                patch.Free();
        }
        _audioOutputPatches.Clear();

        // Seed a Default Patch (system playback device when available) so new cues can play out.
        CreateDefaultAudioPatch();

        // General show scalars
        UiScale = DefaultUiScale;
        GoScale = DefaultGoScale;
        WaveformResolution = DefaultWaveformResolution;
        StopFadeDuration = DefaultStopFadeDuration;
        MediaBackupEnabled = DefaultMediaBackupEnabled;
        MultiEditEnabled = DefaultMultiEditEnabled;
        SelectNewCues = DefaultSelectNewCues;
        VerbosePrint = true;

        // Cue shell defaults for newly created cues
        ResetCueDefaultsToSystem();

        // Cue light appearance defaults
        CueLightIdleColour = new Color(0f, 0f, 0.1f, 1f);
        CueLightGoColour = new Color(0f, 1f, 0f, 1f);
        CueLightStandbyColour = new Color(1f, 0.4f, 0f, 1f);
        CueLightCountInColour = new Color(1f, 0f, 0f, 1f);
        CueLightBrightness = 50;

        // Input Map is user-scoped (UserDataManager) — leave live bindings alone on New Session.

        // Displays / canvas editor model
        _displaysManager?.ResetToDefaults();

        // Cue lights registry
        _globalData?.CueLightManager?.Reset();

        // OSC
        GetNodeOrNull<OscListen>("/root/OscListen")?.ResetToDefaults();
        GetNodeOrNull<OscConnections>("/root/OscConnections")?.ClearAll();

        // Notify live UI (settings general, GO scale, etc.)
        _globalSignals?.EmitSignal(nameof(GlobalSignals.UiScaleChanged), UiScale);
        _globalSignals?.EmitSignal(nameof(GlobalSignals.GoScaleChanged), GoScale);

        GD.Print("Settings:ResetSettings - Show settings restored to defaults.");
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
        saveTable.Add("MultiEditEnabled", MultiEditEnabled);
        saveTable.Add("SelectNewCues", SelectNewCues);

        // Cue shell defaults (show-scoped)
        saveTable.Add("CueDefaults", CaptureCueDefaultsDict());
        
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

        // Input Map is stored in user:// via UserDataManager (not in the showfile).
        
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
            // Replace any session-seeded Default Patch from ResetSettings so open/load is authoritative.
            foreach (var existing in _audioOutputPatches.Values.ToList())
            {
                if (existing != null && GodotObject.IsInstanceValid(existing))
                    existing.Free();
            }
            _audioOutputPatches.Clear();

            foreach (var patch in (Dictionary)patchs)
            {
                var patchAsDict = patch.Value.AsGodotDictionary();
                var patchObj = AudioOutputPatch.FromData(patchAsDict);
                if (patchObj != null)
                    AddPatch(patchObj);
                else
                    GD.PrintErr("Settings:LoadSettings - Failed to deserialize an audio output patch.");
            }

            // Older showfiles with an empty patch table still get a usable Default Patch.
            EnsureDefaultAudioPatch();
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
        MultiEditEnabled = settingsData.TryGetValue("MultiEditEnabled", out value)
            ? value.AsBool()
            : DefaultMultiEditEnabled;
        // Default true for older shows that predate this setting
        SelectNewCues = settingsData.TryGetValue("SelectNewCues", out value)
            ? value.AsBool()
            : DefaultSelectNewCues;

        // Cue shell defaults (older shows without this key keep system defaults)
        if (settingsData.TryGetValue("CueDefaults", out value) && value.VariantType == Variant.Type.Dictionary)
            ApplyCueDefaultsFromDict(value.AsGodotDictionary());
        else
            ResetCueDefaultsToSystem();
        
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

        // Legacy showfiles may still contain "InputMap" — ignore; bindings are user preferences.
        if (settingsData.ContainsKey("InputMap"))
        {
            GD.Print("Settings:LoadSettings - Ignoring showfile InputMap (now stored in user preferences).");
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

        if (TryGetSettingsValue(settingsData, "CueDefaults", out value)
            && value.VariantType == Variant.Type.Dictionary)
        {
            ApplyCueDefaultsFromDict(value.AsGodotDictionary());
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
            GetNode<OscListen>("/root/OscListen").LoadFromData(oscListenAsDict);
        }

        if (settingsData.TryGetValue("OscConnections", out var oscConnections))
        {
            var oscConnectionsAsDict = oscConnections.AsGodotDictionary();
            GetNode<OscConnections>("/root/OscConnections").LoadFromData(oscConnectionsAsDict);
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
            case "MultiEditEnabled":
                value = MultiEditEnabled ? 1 : 0;
                return true;
            case "SelectNewCues":
                value = SelectNewCues ? 1 : 0;
                return true;
            default:
                value = default;
                return false;
        }
    }

    /// <summary>
    /// Resets all cue shell defaults to system factory values.
    /// </summary>
    public void ResetCueDefaultsToSystem()
    {
        CueDefaultPreWait = SystemDefaultCuePreWait;
        CueDefaultPostWait = SystemDefaultCuePostWait;
        CueDefaultFollow = SystemDefaultCueFollow;
        CueDefaultColor = SystemDefaultCueColor;
        CueDefaultArmed = SystemDefaultCueArmed;
        CueDefaultSkipIfDisarmed = SystemDefaultCueSkipIfDisarmed;
    }

    /// <summary>
    /// Applies the show's cue shell defaults to a newly constructed cue.
    /// Does not change id, name, number, parent/children, or components.
    /// </summary>
    /// <param name="cue">Cue instance to configure (typically just constructed).</param>
    public void ApplyShellDefaults(Cue cue)
    {
        if (cue == null) return;
        cue.PreWait = CueDefaultPreWait;
        cue.PostWait = CueDefaultPostWait;
        cue.Follow = CueDefaultFollow;
        cue.Color = CueDefaultColor;
        cue.Armed = CueDefaultArmed;
        cue.SkipIfDisarmed = CueDefaultSkipIfDisarmed;
    }

    /// <summary>
    /// Serializes the cue shell defaults block for showfile / history.
    /// </summary>
    public Dictionary CaptureCueDefaultsDict()
    {
        return new Dictionary
        {
            ["PreWait"] = CueDefaultPreWait,
            ["PostWait"] = CueDefaultPostWait,
            ["Follow"] = (int)CueDefaultFollow,
            ["Color"] = CueDefaultColor.ToHtml(true),
            ["Armed"] = CueDefaultArmed ? 1 : 0,
            ["SkipIfDisarmed"] = CueDefaultSkipIfDisarmed ? 1 : 0
        };
    }

    /// <summary>
    /// Loads cue shell defaults from a dictionary (showfile or history slice).
    /// Missing keys keep their current values.
    /// </summary>
    /// <param name="data">Dictionary with PreWait, PostWait, Follow, Color, Armed, SkipIfDisarmed.</param>
    public void ApplyCueDefaultsFromDict(Dictionary data)
    {
        if (data == null) return;

        if (TryGetSettingsValue(data, "PreWait", out var v))
            CueDefaultPreWait = v.AsDouble();
        if (TryGetSettingsValue(data, "PostWait", out v))
            CueDefaultPostWait = v.AsDouble();
        if (TryGetSettingsValue(data, "Follow", out v))
        {
            int followInt = v.AsInt32();
            if (followInt is >= 0 and <= 2)
                CueDefaultFollow = (FollowType)followInt;
        }
        if (TryGetSettingsValue(data, "Color", out v))
            CueDefaultColor = Color.FromString(v.AsString(), CueDefaultColor);
        if (TryGetSettingsValue(data, "Armed", out v))
            CueDefaultArmed = ReadBoolVariant(v);
        if (TryGetSettingsValue(data, "SkipIfDisarmed", out v))
            CueDefaultSkipIfDisarmed = ReadBoolVariant(v);
    }

    /// <summary>
    /// Returns true when every cue shell default matches the system factory value.
    /// </summary>
    public bool AreCueDefaultsAtSystem()
    {
        return Mathf.IsEqualApprox((float)CueDefaultPreWait, (float)SystemDefaultCuePreWait)
               && Mathf.IsEqualApprox((float)CueDefaultPostWait, (float)SystemDefaultCuePostWait)
               && CueDefaultFollow == SystemDefaultCueFollow
               && CueDefaultColor.IsEqualApprox(SystemDefaultCueColor)
               && CueDefaultArmed == SystemDefaultCueArmed
               && CueDefaultSkipIfDisarmed == SystemDefaultCueSkipIfDisarmed;
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