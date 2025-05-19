using System.Collections.Generic;
using Cue2.Shared;
using Godot;

namespace Cue2.Base.Classes;

public partial class Settings : Node
{
    private GlobalSignals _globalSignals;
    private static Dictionary<int, AudioOutputPatch> _audioOutputPatches = new Dictionary<int, AudioOutputPatch>();

    public float UiScale = 1.0f;
    public float GoScale = 1.0f;
    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        //Adds a default audio out patch at startup - this is only for debug
        //AudioOutputPatch newPatch = new AudioOutputPatch("Default patch");
        //_audioOutputPatches.Add(newPatch.Id, newPatch);
        
        
        
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


    public void CreatePatchFromData(string name, int id, Godot.Collections.Dictionary<int, Godot.Collections.Dictionary<string, bool>> channelData)
    {
        GD.Print("Creating Patch from data  " + name + " " + id);
        AudioOutputPatch newPatch = new AudioOutputPatch(name, id, channelData);
        _audioOutputPatches.Add(newPatch.Id, newPatch);
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