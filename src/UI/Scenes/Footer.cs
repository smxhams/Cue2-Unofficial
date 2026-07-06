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

    // Ui
    private Label _processTimeLabel;
    private Button _logCountButton;
    private string _logPrintoutBaseTooltip = "Log";
    
    private Timer _updateTimer;
    private double _lastDelta;
    
    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

        _globalSignals.LogUpdated += _updateLog;
        
        _logCountButton = GetNode<Button>("%LogCountButton");
        
        GetNode<Button>("%DevicesFooterButton").Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Test log", new Random().Next(0,5));
        _logCountButton.Toggled += OnLogCountToggled;

        _globalSignals.ToggleLogWindow += ToggleLogWindow;

        _syncHotkeys();
        
        _processTimeLabel = GetNode<Label>("%ProcessTimeLabel");
        
        _updateTimer = new Timer();
        AddChild(_updateTimer);
        _updateTimer.WaitTime = 0.1;
        _updateTimer.Start();
        _updateTimer.Timeout += UpdateProcessTime;
    }


    public override void _Process(double delta)
    {
        _lastDelta = delta;
    }

    private void _updateLog(String @printout, int @type)
    {
        var logPrintout = GetNode<Button>("%LogPrintout");
        logPrintout.Text = @printout;
        if (type == 0) logPrintout.RemoveThemeColorOverride("font_color");
        if (type == 1) logPrintout.AddThemeColorOverride("font_color", GlobalStyles.Warning);
        if (type == 2) logPrintout.AddThemeColorOverride("font_color", GlobalStyles.Danger);
        if (type == 3) logPrintout.AddThemeColorOverride("font_color", GlobalStyles.Danger);
        _logCountButton.Text = "Log " + EventLogger.GetLogCount().ToString();
        
        _last5Logs.Add(@printout);
        if (_last5Logs.Count > 5)
        {
            _last5Logs.RemoveAt(0);
        }
        
        //Update log tooltip to show last 5 logs
        logPrintout.TooltipText = _logPrintoutBaseTooltip + "\n\nLast 5 log messages:\n";
        foreach (var log in _last5Logs)
        {    
            logPrintout.TooltipText += log + "\n";
        }
    }

    private void UpdateProcessTime()
    {
        int ms = (int)(_lastDelta * 1000);
        _processTimeLabel.Text = $"{ms:0000}ms";

        double fps = 1.0 / _lastDelta;
        int microseconds = (int)(_lastDelta * 1000000);
        _processTimeLabel.TooltipText = $"{ms}ms\nFPS: {fps:F2}\nPrecise: {microseconds}μs";
    }
    
    
    private void OnLogCountToggled(Boolean @toggle)
    {
        if (@toggle == true){
            if (_logWindow == null)
            {
                GD.Print("Loading settings window scene");
                _logWindow = SceneLoader.LoadScene("uid://cg8mrxu40hjf", out string error); // Loads settings window
                _logWindow.TreeExiting += OnLogWindowClosed;
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

    private void OnLogWindowClosed()
    {
        GD.Print($"Footer:OnLogWindowClosed");
        _logWindow = null;
        _logCountButton.ButtonPressed = false;
    }

    private void ToggleLogWindow()
    {
        _logCountButton.ButtonPressed = !_logCountButton.ButtonPressed;
    }

    private void _syncHotkeys()
    {
        string logHotkey = GlobalData.ParseHotkey("ToggleLog");
        _logCountButton.TooltipText = "Log, click to open full log" + (!string.IsNullOrEmpty(logHotkey) ? "\nHotkey: " + logHotkey : "");

        var logPrintout = GetNode<Button>("%LogPrintout");
        _logPrintoutBaseTooltip = "Log";
        if (!string.IsNullOrEmpty(logHotkey))
            _logPrintoutBaseTooltip += "\nHotkey: " + logHotkey;
        logPrintout.TooltipText = _logPrintoutBaseTooltip;
    }
}
