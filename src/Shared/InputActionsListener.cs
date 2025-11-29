using Godot;

namespace Cue2.Shared;


public partial class InputActionsListener : Node
{
    private GlobalSignals _globalSignals;

    private bool _listenForInput = true;
    
    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        
        // Signals sent from all text edit feilds when focused. This is used to toggle input actions.
        _globalSignals.TextEditFocusEntered += SetListeningFalse;
        _globalSignals.TextEditFocusExited += SetListeningTrue;
        
    }
    public override void _Process(double delta)
    {
        if (!_listenForInput) return;
        
        if (Input.IsAnythingPressed())
        {
            Actions();
        }
    }
    
    private void Actions()
    {
        if (Input.IsActionJustPressed("NewSession", true))
        {
            GD.Print("InputActionsListener:Actions - Input Action: New Session");
            _globalSignals.EmitSignal(nameof(GlobalSignals.NewSession));
        }
        
        if (Input.IsActionJustPressed("OpenSession", true))
        {
            GD.Print("Input Action: Open Session");
            _globalSignals.EmitSignal(nameof(GlobalSignals.OpenSession));
        }
        
        if (Input.IsActionJustPressed("SaveSession", true))
        {
            GD.Print("Input Action: Save");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Save));
        }
        
        if (Input.IsActionJustPressed("SaveAsSession", true))
        {
            GD.Print("Input Action: Save As");
            _globalSignals.EmitSignal(nameof(GlobalSignals.SaveAs));
        }
        
        if (Input.IsActionJustPressed("Go"))
        {
            GD.Print("Input Action: Go");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Go));
        }
        
        if (Input.IsActionJustPressed("StopAll")) 
        {
            GD.Print("Input Action: Stop All");
            _globalSignals.EmitSignal(nameof(GlobalSignals.StopAll));
        }

        if (Input.IsActionJustPressed("CreateCue", true))
        {
            GD.Print("Input Action: Create Cue");
            _globalSignals.EmitSignal(nameof(GlobalSignals.CreateCue));
        }

        if (Input.IsActionJustPressed("CreateGroup", true))
        {
            GD.Print("Input Action: Create Group");
            _globalSignals.EmitSignal(nameof(GlobalSignals.CreateGroup));
        }
        
        
    }
    
    private void SetListening(bool listening) => _listenForInput = listening;
    
    private void SetListeningTrue()
    {
        SetListening(true);
    }

    private void SetListeningFalse()
    {
        SetListening(false);
    }
}

