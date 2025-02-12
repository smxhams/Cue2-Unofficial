using Cue2.Base.Classes;
using Cue2.Shared;
using Godot;

namespace Cue2.Base.CommandInterpreter;

public partial class CueCommandExectutor : CueCommandInterpreter
{
    private GlobalData _globalData;
    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        GD.Print("Cue Command Executor Successfully added");
    }


    public void ExecuteCommand(Cue cue)
    {
        _globalData.Playback.PlayMedia(cue.FilePath);
        GD.Print(cue.Name);
    }

}