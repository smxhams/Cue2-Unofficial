using Godot;
using System;
using Cue2.Shared;
using Cue2.UI.Utilities;

namespace Cue2.UI.Scenes;

public partial class LogWindow : Window
{
    private EventLogger _eventLogger;
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;

    private VBoxContainer _logListContainer;
    public override void _Ready()
    {
            GD.Print("Log window intit");
            _eventLogger = GetNode<EventLogger>("/root/EventLogger");
            _globalData = GetNode<GlobalData>("/root/GlobalData");
            _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
            
            _logListContainer = GetNode<VBoxContainer>("%LogListContainer");
            
            UiUtilities.RescaleWindow(this, _globalData.BaseDisplayScale);
            UiUtilities.RescaleUi(this, _globalData.Settings.UiScale, _globalData.BaseDisplayScale);
            
            _globalSignals.UiScaleChanged += ScaleUi;
            _globalSignals.LogUpdated += _newLog;
            
            _syncLogs();
    }

    private void _newLog(string printout, int type)
    {
        var label = new Label();
        label.Text = printout;
        _logListContainer = GetNode<VBoxContainer>("%LogListContainer");
        _logListContainer.AddChild(label);
        _logListContainer.MoveChild(label, 0);
        if (type == 2) label.AddThemeColorOverride("font_color", GlobalStyles.Danger);
        if (type == 3) label.AddThemeColorOverride("font_color", GlobalStyles.Danger);
        if (type == 1) label.AddThemeColorOverride("font_color", GlobalStyles.Warning);
    }

    private void _syncLogs()
    {
        var logList = _eventLogger.GetLogList();
        for (int i = 0; i < logList.Count; i++)
        {
            var log = logList[i];
            GD.Print(log);
            var label = new Label();
            label.Text = log;
            if (log.Contains("Error")) label.AddThemeColorOverride("font_color", GlobalStyles.Danger);
            if (log.Contains("Alert")) label.AddThemeColorOverride("font_color", GlobalStyles.Danger);
            if (log.Contains("Warning")) label.AddThemeColorOverride("font_color", GlobalStyles.Warning);
            _logListContainer.AddChild(label);
            _logListContainer.MoveChild(label, 0);
        }
    }
    
    private void ScaleUi(float value)
    {
        try
        {
            float effectiveScale = value * _globalData.BaseDisplayScale;
            WrapControls = true;
            ContentScaleFactor = effectiveScale;
            ChildControlsChanged();
            GD.Print($"LogWindow:_scaleUI - Applied effective UI scale: {effectiveScale} (user: {value} * base: {_globalData.BaseDisplayScale})"); //!!! (Prefixed as per standards)
        } 
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Error applying UI scale: {ex.Message}", 2);
            GetWindow().ContentScaleFactor = value; // Fallback to original value without multiplier
        }
    }

    public override void _ExitTree()
    {
        _globalSignals.UiScaleChanged -= ScaleUi;
        _globalSignals.LogUpdated -= _newLog;
    }
}
