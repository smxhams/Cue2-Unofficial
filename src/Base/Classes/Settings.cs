using Cue2.Shared;
using Godot;
using Godot.Collections;

namespace Cue2.Base.Classes;

public partial class Settings : Node
{
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;
    private AudioDevices _audioDevices;
    private static Dictionary<int, AudioOutputPatch> _audioOutputPatches = new Dictionary<int, AudioOutputPatch>();
    private Godot.Collections.Dictionary _cueLightData = new Godot.Collections.Dictionary();

    public float UiScale = 1.0f;
    public float GoScale = 1.0f;
    public int WaveformResolution = 4096;
    public float StopFadeDuration = 2.0f;
    
    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _audioDevices = GetNode<AudioDevices>("/root/AudioDevices");
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
    
    
    
    /// <summary>
    /// Saves cue light data.
    /// </summary>
    public void SaveCueLightData(Dictionary data)
    {
        _cueLightData = data;
        GD.Print("Settings:SaveCueLightData - Stored data");
    }

    /// <summary>
    /// Loads cue light data.
    /// </summary>
    public Dictionary LoadCueLightData()
    {
        return _cueLightData;
    }
    
    // Save and loads
    public void ResetSettings()
    {
        foreach (var patch in _audioOutputPatches)
        {
            patch.Value.Free();
        }
        _audioOutputPatches.Clear();
        _cueLightData.Clear();
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
        saveTable.Add("CueLights", _cueLightData);
        
        saveTable.Add("UiScale", UiScale);
        saveTable.Add("GoScale", GoScale);
        saveTable.Add("WaveformResolution", WaveformResolution);
        saveTable.Add("StopFadeDuration", StopFadeDuration);
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

        if (settingsData.TryGetValue("CueLights", out var cueLights))
        {
            GD.Print($"Settings:LoadSettings - Loading CueLights");
        }
        
        UiScale = settingsData.TryGetValue("UiScale", out var value) ? (float)value : UiScale;
        GoScale = settingsData.TryGetValue("GoScale", out value) ? (float)value : GoScale;
        WaveformResolution = settingsData.TryGetValue("WaveformResolution", out value) ? (int)value : WaveformResolution;
        StopFadeDuration = settingsData.TryGetValue("StopFadeDuration", out value) ? (float)value : StopFadeDuration;
        
    }
    
}