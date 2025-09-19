using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cue2.Base.Classes.Connections;

namespace Cue2.Shared;

/// <summary>
/// Manages a collection of CueLight instances, including creation, connection, and session integration.
/// </summary>
public partial class CueLightManager : Node
{
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;
    private Dictionary<int, CueLight> _cueLights = new();
    private int _nextId = 0;

    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals.Save += SaveCueLights; // Connect to save signal
        _globalSignals.OpenSelectedSession += OnOpenSession; // Async load
        GD.Print("CueLightManager:_Ready - Initialized"); //!!!
    }

    /// <summary>
    /// Creates a new CueLight instance.
    /// </summary>
    public CueLight CreateCueLight()
    {
        var cueLight = new CueLight(_nextId++);
        _cueLights[cueLight.Id] = cueLight;
        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
            $"CueLightManager:CreateCueLight - Created {_nextId - 1}: {cueLight.Name}", 0);
        return cueLight;
    }

    /// <summary>
    /// Retrieves a CueLight by ID.
    /// </summary>
    public CueLight? GetCueLight(int id) => _cueLights.TryGetValue(id, out var cl) ? cl : null;

    /// <summary>
    /// Connects all cue lights asynchronously.
    /// </summary>
    public async Task ConnectAllAsync()
    {
        foreach (var cueLight in _cueLights.Values)
        {
            if (!cueLight.IsConnected)
                await cueLight.ConnectAsync();
        }
    }

    /// <summary>
    /// Disconnects all cue lights.
    /// </summary>
    public async Task DisconnectAllAsync()
    {
        foreach (var cueLight in _cueLights.Values)
            await cueLight.DisconnectAsync();
    }

    private void SaveCueLights()
    {
        var data = new Godot.Collections.Dictionary();
        foreach (var kvp in _cueLights)
            data[kvp.Key] = kvp.Value.GetData();
        _globalData.Settings.SaveCueLightData(data); // Delegate to Settings
        GD.Print("CueLightManager:SaveCueLights - Saved data"); //!!!
    }

    private async void OnOpenSession(string path)
    {
        // Need to find something else for this
        
        
        /*await DisconnectAllAsync();
        _cueLights.Clear();
        _nextId = 0;
        var loadedData = _globalData.Settings.LoadCueLightData();
        foreach (var entry in loadedData)
        {
            if (entry.Key is Variant keyVar && keyVar.TryToInt(out int id))
            {
                var cueLight = new CueLight(id);
                cueLight.SetData(entry.Value.AsGodotDictionary());
                _cueLights[id] = cueLight;
                if (id >= _nextId) _nextId = id + 1;
                if (_globalData.SessionPath == path) // Reconnect for loaded session
                    await cueLight.ConnectAsync();
                if (cueLight.IsIdentifying) // Restore state
                    await cueLight.IdentifyAsync(true);
            }
        }
        GD.Print($"CueLightManager:OnOpenSession - Loaded {_cueLights.Count} cue lights");*/
    }
}