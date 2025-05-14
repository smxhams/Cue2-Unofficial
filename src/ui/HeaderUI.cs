using Godot;
using System;
using Cue2.Shared;

namespace Cue2.UI;
public partial class HeaderUI : Control
{
    private GlobalSignals _globalSignals;
    
    private Node _settingsWindow;
    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        
        GetNode<Button>("%GoButton").Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.Go));
        GetNode<Button>("%StopAllButton").Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.StopAll));
        GetNode<Button>("%PauseAllButton").Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.PauseAll));
    }
    
}
