using Godot;
using System;
using Cue2.Shared;

namespace Cue2.UI;
public partial class HeaderUI : Control
{
    private GlobalSignals _globalSignals;
    
    private Node _settingsWindow;
    private Button _goButton;

    private double _baseGoSize;
    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

        _goButton = GetNode<Button>("%GoButton");

        _baseGoSize = _goButton.GetSize().X;
        
        _goButton.Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.Go));
        
        
        
        _syncHotkeys();

        _globalSignals.Go += _goButtonFeedback;
        _globalSignals.GoScaleChanged += _goScaleChange;
    }

    private void _syncHotkeys()
    {
        _goButton.TooltipText = "Hotkey: " + GlobalData.ParseHotkey("Go");
    }

    private async void _goButtonFeedback()
    {
        var pressed = _goButton.GetThemeStylebox("pressed");
        var normal = _goButton.GetThemeStylebox("normal");
        _goButton.AddThemeStyleboxOverride("normal", pressed);
        await ToSignal(GetTree().CreateTimer(0.2), "timeout");
        _goButton.AddThemeStyleboxOverride("normal", normal);
    }

    private void _goScaleChange(float scale)
    {
        var newGoScale = (float)_baseGoSize * scale;
        //Go Button scale
        _goButton.SetCustomMinimumSize(new Vector2(newGoScale, newGoScale));
        
        // Header size
        if (newGoScale > 50) SetCustomMinimumSize(new Vector2(GetCustomMinimumSize().X, newGoScale));
        else SetCustomMinimumSize(new Vector2(GetCustomMinimumSize().X, 50.0f));
    }
}
