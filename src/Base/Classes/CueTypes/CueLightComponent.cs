using System;
using Godot;
using System.Threading.Tasks;
using Cue2.Shared;
using Godot.Collections;

namespace Cue2.Base.Classes.CueTypes;

/// <summary>
/// Enum for cue light actions.
/// </summary>
public enum CueLightAction
{
    Go,
    Standby,
    CountIn,
    IdentifyStart,
    IdentifyStop
}

/// <summary>
/// ICueComponent for cue light actions in cues.
/// </summary>
public class CueLightComponent : ICueComponent 
{
    public string Type => "CueLight"; 
    public int CueLightId;
    private CueLightAction _action;
    private float _countInTime; // For CountIn

    public CueLightComponent(int cueLightId, CueLightAction action, float countInTime = 0f)
    {
        CueLightId = cueLightId;
        _action = action;
        _countInTime = countInTime;
    }

    /// <summary>
    /// Executes the action on the referenced cue light.
    /// </summary>
    public async Task ExecuteAsync(CueLightManager manager) 
    {
        var cueLight = manager.GetCueLight(CueLightId);
        if (cueLight == null)
        {
            GD.Print($"CueLightComponent:ExecuteAsync - CueLight {CueLightId} not found", 2);
            return;
        }

        try
        {
            switch (_action)
            {
                case CueLightAction.Go: await cueLight.GoAsync(); break;
                case CueLightAction.Standby: await cueLight.StandbyAsync(); break;
                case CueLightAction.CountIn: await cueLight.CountInAsync(_countInTime); break;
                case CueLightAction.IdentifyStart: await cueLight.IdentifyAsync(true); break;
                case CueLightAction.IdentifyStop: await cueLight.IdentifyAsync(false); break;
            }
            GD.Print( 
                $"CueLightComponent:ExecuteAsync - Executed {_action} on {CueLightId}", 0);
        }
        catch (Exception ex)
        {
            GD.Print( 
                $"CueLightComponent:ExecuteAsync - Execution failed for {_action} on {CueLightId}: {ex.Message}", 2);
        }
    }

    public Dictionary GetData() 
    {
        return new Dictionary
        {
            { "CueLightId", CueLightId },
            { "Action", (int)_action },
            { "CountInTime", _countInTime }
        };
    }

    public void LoadFromData(Dictionary data) 
    {
        if (data.ContainsKey("CueLightId")) CueLightId = data["CueLightId"].AsInt32();
        if (data.ContainsKey("Action")) _action = (CueLightAction)data["Action"].AsInt32();
        if (data.ContainsKey("CountInTime")) _countInTime = data["CountInTime"].AsSingle();
    }
}