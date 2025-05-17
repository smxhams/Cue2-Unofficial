using Godot;
using System;
using Cue2.Shared;

namespace Cue2.UI.Scenes;

public partial class LogWindow : Window
{
    private EventLogger _eventLogger;
    private GlobalSignals _globalSignals;

    private VBoxContainer _logListContainer;
    public override void _Ready()
    {
            GD.Print("Log window intit");
            _eventLogger = GetNode<EventLogger>("/root/EventLogger");
            _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
            
            _logListContainer = GetNode<VBoxContainer>("%LogListContainer");

            _globalSignals.LogUpdated += _newLog;
            
            _syncLogs();
    }

    private void _newLog(string printout, int type)
    {
        var label = new Label();
        label.Text = printout;
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
}
