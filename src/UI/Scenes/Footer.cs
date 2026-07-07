using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
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

    // Devices status
    private GlobalData _globalData;
    private AudioDevices _audioDevices;
    private DisplaysManager _displaysManager;
    private Button _devicesFooterButton;
    
    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

        _globalSignals.LogUpdated += _updateLog;
        
        _logCountButton = GetNode<Button>("%LogCountButton");
        
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _audioDevices = GetNode<AudioDevices>("/root/AudioDevices");
        _displaysManager = GetNode<DisplaysManager>("/root/DisplaysManager");
        _devicesFooterButton = GetNode<Button>("%DevicesFooterButton");
        _devicesFooterButton.TooltipText = "Devices";
        _devicesFooterButton.Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Test log", new Random().Next(0,5));
        _devicesFooterButton.MouseEntered += UpdateDevicesFooterTooltip;
        
        _globalSignals.AudioDevicesChanged += UpdateDevicesFooterTooltip;
        _globalSignals.DisplaysChanged += UpdateDevicesFooterTooltip;
        
        _logCountButton.Toggled += OnLogCountToggled;

        _globalSignals.ToggleLogWindow += ToggleLogWindow;

        _syncHotkeys();
        
        _processTimeLabel = GetNode<Label>("%ProcessTimeLabel");
        
        _updateTimer = new Timer();
        AddChild(_updateTimer);
        _updateTimer.WaitTime = 0.1;
        _updateTimer.Start();
        _updateTimer.Timeout += UpdateProcessTime;

        UpdateDevicesFooterTooltip(); // initial status
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

    /// <summary>
    /// Updates the DevicesFooterButton tooltip and color with the status of used audio devices and video outputs.
    /// Green (🟢) = connected/available.
    /// Red (🔴) = configured/used but target not currently available.
    /// Tints the button green (Success) when all are OK, red (Danger) if any problems.
    /// </summary>
    private void UpdateDevicesFooterTooltip()
    {
        if (_devicesFooterButton == null) return;

        var audioStatuses = _audioDevices?.GetUsedAudioDeviceStatuses() ?? new Dictionary<string, bool>();
        var videoStatuses = _displaysManager?.GetVideoOutputStatuses() ?? new Dictionary<string, bool>();

        if (audioStatuses.Count == 0 && videoStatuses.Count == 0)
        {
            _devicesFooterButton.TooltipText = "Devices\n\nNo audio or video devices are currently used or configured.";
            _devicesFooterButton.AddThemeColorOverride("font_color", GlobalStyles.Success);
            return;
        }

        bool hasProblem = false;
        string tooltip = "Devices:\n";

        if (audioStatuses.Count > 0)
        {
            tooltip += "\nAudio Devices:\n";
            foreach (var entry in audioStatuses.OrderBy(e => e.Key))
            {
                bool connected = entry.Value;
                if (!connected) hasProblem = true;

                string indicator = connected ? "🟢 " : "🔴 ";
                string status = connected ? "connected" : "being used but not connected";
                tooltip += $"{indicator}{entry.Key} ({status})\n";
            }
        }

        if (videoStatuses.Count > 0)
        {
            tooltip += "\nVideo Outputs:\n";
            foreach (var entry in videoStatuses.OrderBy(e => e.Key))
            {
                bool connected = entry.Value;
                if (!connected) hasProblem = true;

                string indicator = connected ? "🟢 " : "🔴 ";
                string status = connected ? "connected" : "target monitor unavailable";
                tooltip += $"{indicator}{entry.Key} ({status})\n";
            }
        }

        _devicesFooterButton.TooltipText = tooltip.TrimEnd('\n', '\r');

        if (hasProblem)
        {
            _devicesFooterButton.AddThemeColorOverride("font_color", GlobalStyles.Danger);
        }
        else
        {
            _devicesFooterButton.AddThemeColorOverride("font_color", GlobalStyles.Success);
        }
    }
}
