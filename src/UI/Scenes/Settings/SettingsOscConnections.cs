using Godot;
using System;
using Cue2.Base.Classes.Connections;

namespace Cue2.UI.Scenes.Settings;

public partial class SettingsOscConnections : ScrollContainer
{

    private Button _testButton;
    
    public override void _Ready()
    {
        _testButton = GetNode<Button>("%TestButton");
        
        _testButton.Pressed += OscConnections.SendTestMessage;
    }
}
