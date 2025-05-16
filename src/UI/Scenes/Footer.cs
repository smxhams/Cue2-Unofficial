using Godot;
using System;
using System.Collections.Generic;
using Cue2.Shared;

namespace Cue2.UI.Scenes;


public partial class Footer : Control
{
    private GlobalSignals _globalSignals;
    private List<string> _last5Logs = new List<string>();
    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

        _globalSignals.LogUpdated += _updateLog;
        
        GetNode<Button>("%DevicesFooterButton").Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Test log", 0);
    } 

    private void _updateLog(String @printout, int @type)
    {
        GetNode<Label>("%LogPrintout").Text = @printout;
        GetNode<Label>("%LogCount").Text = "Log " + EventLogger.GetLogCount().ToString();
        
        _last5Logs.Add(@printout);
        if (_last5Logs.Count > 5)
        {
            _last5Logs.RemoveAt(0);
        }
        
        GetNode<Label>("%LogPrintout2").Text = _last5Logs[0];
        GetNode<Label>("%LogPrintout3").Text = _last5Logs[1];
        GetNode<Label>("%LogPrintout4").Text = _last5Logs[2];
        GetNode<Label>("%LogPrintout5").Text = _last5Logs[3];
        GetNode<Label>("%LogPrintout6").Text = _last5Logs[4];
    }
}
