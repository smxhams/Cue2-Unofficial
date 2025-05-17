using Godot;
using System;
using System.Collections.Generic;
using Cue2.Shared;

namespace Cue2.UI.Scenes;


public partial class Footer : Control
{
    private GlobalSignals _globalSignals;
    
    private List<string> _last5Logs = new List<string>();
    private Node _logWindow;
    
    
    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

        _globalSignals.LogUpdated += _updateLog;
        
        GetNode<Button>("%DevicesFooterButton").Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Test log", new Random().Next(0,5));
        GetNode<Button>("%LogCount").Toggled += _onLogCountToggled;
    } 

    private void _updateLog(String @printout, int @type)
    {
        var logPrintout = GetNode<Button>("%LogPrintout");
        logPrintout.Text = @printout;
        if (type == 0) logPrintout.RemoveThemeColorOverride("font_color");
        if (type == 1) logPrintout.AddThemeColorOverride("font_color", GlobalStyles.Warning);
        if (type == 2) logPrintout.AddThemeColorOverride("font_color", GlobalStyles.Danger);
        if (type == 3) logPrintout.AddThemeColorOverride("font_color", GlobalStyles.Danger);
        GetNode<Button>("%LogCount").Text = "Log " + EventLogger.GetLogCount().ToString();
        
        _last5Logs.Add(@printout);
        if (_last5Logs.Count > 5)
        {
            _last5Logs.RemoveAt(0);
        }
        
        //Update log tooltip to show last 5 logs
        logPrintout.TooltipText = "Last 5 logs:\n";
        foreach (var log in _last5Logs)
        {    
            logPrintout.TooltipText += log + "\n";
        }
    }
    
    
    private void _onLogCountToggled(Boolean @toggle)
    {
        if (@toggle == true){
            if (_logWindow == null)
            {
                GD.Print("Loading settings window scene");
                _logWindow = SceneLoader.LoadScene("uid://cg8mrxu40hjf", out string error); // Loads settings window
                _logWindow.TreeExiting += _onLogWindowClosed;
                AddChild(_logWindow);
            }
            else {
                _logWindow.GetWindow().Show();
            }
        }
        if (@toggle == false)
        {
            _logWindow?.QueueFree();
        }
    }

    private void _onLogWindowClosed()
    {
        _logWindow = null;
        GetNode<Button>("%LogCount").ButtonPressed = false;
        GD.Print("Captured it's closing");
    }
}
