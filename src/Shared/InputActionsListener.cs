using Godot;

namespace Cue2.Shared;

public partial class InputActionsListener : Node
{
    public override void _Process(double delta)
    {
        if (Input.IsAnythingPressed())
        {
            Actions();
        }
    }

    private static void Actions()
    {
        if (Input.IsActionJustPressed("OpenSession"))
        {
            GD.Print("Open Session");
        }
        
    }
}

