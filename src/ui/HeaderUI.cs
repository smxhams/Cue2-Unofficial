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
        GetNode<Button>("%SettingsButton").Toggled += _onSettingsToggled;
        _globalSignals.CloseSettingsWindow += close_settings_window;

    }

	
    // Settings windows toggle
    private void _onSettingsToggled(Boolean @toggle){
        if (@toggle == true){
            if (_settingsWindow == null)
            {
                GD.Print("Loading settings window scene");
                _settingsWindow = SceneLoader.LoadScene("uid://cfw3syjm11bd6", out string error); // Loads settings window
                AddChild(_settingsWindow);
            }
            else {
                _settingsWindow.GetWindow().Show();
            }
        }
        if (@toggle == false){
            _settingsWindow.GetWindow().Hide();
        }
    }
    private void close_settings_window(){ //From global signal, emitted by close button of settings window.
        GetNode<Button>("%SettingsButton").ButtonPressed = false;
    }
}
