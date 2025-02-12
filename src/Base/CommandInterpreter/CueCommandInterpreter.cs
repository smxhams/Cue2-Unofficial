using Cue2.Base.Classes;
using Godot;

namespace Cue2.Base.CommandInterpreter;

public partial class CueCommandInterpreter : Node
{
    public CueCommandExectutor CueCommandExectutor;
    public CueCommandWriter CueCommandWriter;
    
    public override void _Ready()
    {
        CueCommandExectutor = new CueCommandExectutor();
        AddChild(CueCommandExectutor);
        CueCommandWriter = new CueCommandWriter();
        AddChild(CueCommandWriter);
        GD.Print("Cue Command Interpreter Successfully added");
    }


    public static void InterpretCommand(ICue cue)
    {
        throw new System.NotImplementedException();
    }

}