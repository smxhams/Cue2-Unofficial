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
        if (Input.IsActionJustPressed("OpenSession"))
        {
            GD.Print("Input Action: Open Session");
        }
        
        if (Input.IsActionJustPressed("SaveSession"))
        {
            GD.Print("Input Action: Save");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Save));
        }
        
        if (Input.IsActionJustPressed("SaveAsSession"))
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

        if (Input.IsActionJustPressed("CreateCue"))
        {
            GD.Print("Input Action: Create Cue");
            _globalSignals.EmitSignal(nameof(GlobalSignals.CreateCue));
        }

        if (Input.IsActionJustPressed("CreateGroup"))
        {
            GD.Print("Input Action: Create Group");
            _globalSignals.EmitSignal(nameof(GlobalSignals.CreateGroup));
        }
        
        
    }
    
    private void SetListening(bool listening) => _listenForInput = listening;
    
    private void SetListeningTrue()
    {
        GD.Print("Listening True");
        SetListening(true);
    }

    private void SetListeningFalse()
    {
        GD.Print("Listening False");
        SetListening(false);
    }
}

