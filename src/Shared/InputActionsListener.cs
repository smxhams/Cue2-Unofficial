using Godot;

namespace Cue2.Shared;


public partial class InputActionsListener : Node
{
    private GlobalSignals _globalSignals;
    
    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
    }
    public override void _Process(double delta)
    {
        if (Input.IsAnythingPressed())
        {
            Actions();
        }
    }

    private void Actions()
    {
        if (Input.IsActionJustPressed("OpenSession"))
        {
            GD.Print("Open Session");
        }
        
        if (Input.IsActionJustPressed("SaveSession"))
        {
            GD.Print("Save");
        }
        
        if (Input.IsActionJustPressed("Go"))
        {
            GD.Print("Go");
        }
        
        if (Input.IsActionJustPressed("StopAll")) 
        {
            GD.Print("Stop All");
            _globalSignals.EmitSignal(nameof(GlobalSignals.StopAll));
        }

        if (Input.IsActionJustPressed("CreateCue"))
        {
            GD.Print("Input Action: Create Cue");
            _globalSignals.EmitSignal(nameof(GlobalSignals.CreateCue));
        }
        
        
    }
}

