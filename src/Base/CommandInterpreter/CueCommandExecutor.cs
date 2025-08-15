using System.Linq;
using System.Runtime.InteropServices;
using Cue2.Base.Classes;
using Cue2.Shared;
using Godot;
using Hardware.Info;

namespace Cue2.Base.CommandInterpreter;

public partial class CueCommandExectutor : CueCommandInterpreter
{
    private GlobalData _globalData;
    private GlobalSignals _globalSignals;
    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        GD.Print("Cue Command Executor Successfully added");
        
        _globalSignals.Go += GoCommand;
    }

    public void GoCommand()
    {
        if (!_globalData.ShellSelection.SelectedShells.Any())
        {
            GD.Print("No Shells Selected");
            return;
        }
        foreach (var cue1 in _globalData.ShellSelection.SelectedShells)
        {
            var cue = (Cue)cue1;
            _globalData.CueCommandInterpreter.CueCommandExectutor.ExecuteCommand(cue);
        } 
    }

    public void ExecuteCommand(Cue cue)
    {
        //_globalData.Playback.PlayMedia(cue.FilePath);
        GD.Print(cue.Name);

        // Check cue type to determine how to play
        // TODO: This is broken by cue refactor
        /*if (cue.Type == "")
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Nothing in the Cue.", 1);
        }
        
        else
        {
            _globalData.Playback.PlayMedia(cue);
            _globalSignals.EmitSignal(nameof(GlobalSignals.CueGo), cue.Id);
        }*/
        

        if (cue.ChildCues.Count() != 0)
        {
            foreach (var child in cue.ChildCues)
            {
                var childCue = CueList.FetchCueFromId(child);
                ExecuteCommand(childCue);

            }
        }
        
    }

}

