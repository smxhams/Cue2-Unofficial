using Cue2.Base.Classes;
using Godot;

namespace Cue2.Base.CommandInterpreter;

public partial class CueCommandWriter : CueCommandInterpreter
{
    public override void _Ready()
    {
        GD.Print("Cue Command Writer Successfully added");
    }


    public void WriteCommand(ICue cue)
    {
        throw new System.NotImplementedException();
    }

}