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

    public float UiScale = DefaultUiScale;
    public float GoScale = DefaultGoScale;
    public int WaveformResolution = DefaultWaveformResolution;

    /// <summary>
    /// Global stop fade-out duration in seconds (first Stop fades; second Stop hard-cuts).
    /// 0 = immediate stop. Persisted with the session.
    /// </summary>
    public float StopFadeDuration = DefaultStopFadeDuration;
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
    
}