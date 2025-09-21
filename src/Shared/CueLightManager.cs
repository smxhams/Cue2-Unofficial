using Godot;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Cue2.Base.Classes.Connections;
using Godot.Collections;

namespace Cue2.Shared;

/// <summary>
/// Manages a collection of CueLight instances, including creation, connection, and session integration.
/// </summary>
public partial class CueLightManager : Node
{
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;
    private System.Collections.Generic.Dictionary<int, CueLight> _cueLights = new();
    private int _nextId = 0;

    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        GD.Print("CueLightManager:_Ready - Initialized");

        TreeExiting += Clean;
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


    public void DeleteCueLight(CueLight cueLight)
    {
        if (_cueLights.Remove(cueLight.Id))
        {
            cueLight.Dispose();
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"CueLightManager:DeleteCueLight - Removed CueLight {cueLight.Id} ({cueLight.Name})", 0);
        }
    }

    /// <summary>
    /// Returns an array of all cuelights
    /// </summary>
    /// <returns></returns>
    public Array<CueLight> GetCueLights()
    {
        var result = new Array<CueLight>();
        foreach (var cueLight in _cueLights.Values)
        {
            result.Add(cueLight);
        }
        return result;
    }

    public async void AllGo(string cueNum = "")
    {
        foreach (var cueLight in _cueLights.Values)
        {
            await cueLight.GoAsync(cueNum);
        }
    }

    public async void AllStandby(string cueNum = "")
    {
        foreach (var cueLight in _cueLights.Values)
        {
            if (cueLight.CueLightIsConnected)
                await cueLight.StandbyAsync(cueNum);
        }
    }
    
    public async void AllCancel()
    {
        foreach (var cueLight in _cueLights.Values)
        {
            if (cueLight.CueLightIsConnected)
                await cueLight.CancelAsync();
        }
    }

    public async void AllCountIn(int timeUntilGo = 3, string cueNum = "")
    {
        foreach (var cueLight in _cueLights.Values)
        {
            if (cueLight.CueLightIsConnected)
                await cueLight.CountInAsync(timeUntilGo, cueNum);
        }
    }

    public async void AllIdentify(bool state)
    {
        foreach (var cueLight in _cueLights.Values)
        {
            if (cueLight.CueLightIsConnected)
                await cueLight.IdentifyAsync(state);
        }
    }
    
    

    private async void Clean()
    {
        foreach (var cueLight in _cueLights.Values)
        {
            if (_cueLights.Remove(cueLight.Id))
            {
                cueLight.Dispose();
            }
        }

    }
     
    

    public Dictionary GetData()
    {
        var data = new Dictionary();
        foreach (var kvp in _cueLights)
            data[kvp.Key] = kvp.Value.GetData();
        return data;
    }

    public async Task LoadData(Dictionary data)
    {
        foreach (var value in data.Values)
        {
            var cueLightDict = value.AsGodotDictionary();
            if (!cueLightDict.ContainsKey("Id"))
            {
                GD.PrintErr("CueLightManager:LoadData - Missing 'Id' key in data.");
                return;
            }
            if ((int)cueLightDict["Id"] >= _nextId) _nextId = (int)cueLightDict["Id"] + 1;
            var cueLight = new CueLight(cueLightDict);
            _cueLights[cueLight.Id] = cueLight;
        }
    }
}