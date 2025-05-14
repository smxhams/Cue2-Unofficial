using Godot;
using System;
using Cue2.Shared;

namespace Cue2.UI;
public partial class HeaderUI : Control
{
    private GlobalSignals _globalSignals;
    
    private Node _settingsWindow;
    private Button _goButton;
    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

        _goButton = GetNode<Button>("%GoButton");
        
        _goButton.Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.Go));
        GetNode<Button>("%StopAllButton").Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.StopAll));
        GetNode<Button>("%PauseAllButton").Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.PauseAll));
        
        _syncHotkeys();

        _globalSignals.Go += _goButtonFeedback;


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
    
    
}
