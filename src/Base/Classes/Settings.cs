using System.Collections.Generic;
using Cue2.Shared;
using Godot;

namespace Cue2.Base.Classes;

public partial class Settings : Node
{
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;
    private static Dictionary<int, AudioOutputPatch> _audioOutputPatches = new Dictionary<int, AudioOutputPatch>();

    public float UiScale = 1.0f;
    public float GoScale = 1.0f;
    
    public int WaveformResolution = 4096;
    
    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNode<GlobalData>("/root/GlobalData");
    }
    
    public Dictionary<int, AudioOutputPatch> GetAudioOutputPatches() => _audioOutputPatches;
    
    
    public void UpdatePatch(AudioOutputPatch patch)
    {
        _audioOutputPatches[patch.Id] = patch;

        GD.Print("Updated patch with id: " + patch.Id + " and name: " + patch.Name);
        
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
            GD.PrintErr("Settings:AddPatch - Patch ID already exists: " + patch.Id);
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Failed to add patch due to duplicate ID: " + patch.Id, 2);
            return;
        }
        _audioOutputPatches.Add(patch.Id, patch);
        GD.Print("Settings:AddPatch - Added patch with ID: " + patch.Id + " and name: " + patch.Name);
        
        // Double check audio devices in added patch are opened. 
        foreach (var device in patch.OutputDevices)
        {
            _globalData.AudioDevices.OpenAudioDevice(device.Key, out var _);
        }
    }

    private void PrintPatches()
    {
        foreach (var patch in _audioOutputPatches)
        {
            foreach (var channels in patch.Value.Channels)
            {
                GD.Print($"ID: {patch.Key} Name: {patch.Value.Name} Channel: {channels.Key} Name: {channels.Value}");

            }
        }
    }
    


    public void ResetSettings()
    {
        foreach (var patch in _audioOutputPatches)
        {
            patch.Value.Free();
        }
        _audioOutputPatches.Clear();
    }
    
}