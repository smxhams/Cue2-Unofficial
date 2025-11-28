using Godot;
using System;
using System.Net.NetworkInformation;
using Cue2.Base.Classes.Connections;
using Cue2.Shared;

namespace Cue2.UI.Scenes.Settings;

public partial class SettingsOscListen : ScrollContainer
{
    private GlobalSignals _globalSignals;


    private CheckButton _enabledCheckButton;
    private Label _ipAddressLabel;
    private LineEdit _sessionNameLineEdit;
    private LineEdit _portLineEdit;

    private bool _portEditing = false;
    private bool _sessionNameEditing = false;

    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

        // Define Ui properties
        _enabledCheckButton = GetNode<CheckButton>("%EnabledCheckButton");
        _ipAddressLabel = GetNode<Label>("%IpAddressLabel");
        _sessionNameLineEdit = GetNode<LineEdit>("%SessionNameLineEdit");
        _portLineEdit = GetNode<LineEdit>("%PortLineEdit");

        // Set ui feilds
        _enabledCheckButton.SetPressed(OscListen.OscListenEnabled);
        _portLineEdit.Text = OscListen.Port.ToString();
        _sessionNameLineEdit.Text = OscListen.SessionName;
        
        // Connect ui logic
        _enabledCheckButton.Toggled += OscListen.SetEnabled;
        _portLineEdit.EditingToggled += PortEditing;
        _sessionNameLineEdit.EditingToggled += SessionNameEditing;

        DisplayIpAddresses();

    }

    private void SessionNameEditing(bool toggledon)
    {
        if (toggledon == true && _sessionNameEditing == false)
        {
            _sessionNameEditing = true;
        }
        else if (toggledon == false && _sessionNameEditing == true)
        {
            // Submit session name
            _sessionNameEditing = false;
            _sessionNameLineEdit.ReleaseFocus();
            SubmitSessionName();
        }
    }

    private void SubmitSessionName()
    {
        string sessionName = _sessionNameLineEdit.Text.Trim();
        if (string.IsNullOrEmpty(sessionName))
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "SettingsOscListen: Session name cannot be empty.", 1);
            _sessionNameLineEdit.Text = OscListen.SessionName;
            return;
        }
        if (sessionName.Length > 20)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "SettingsOscListen: Session name too long. Max 20 characters.", 1);
            _sessionNameLineEdit.Text = OscListen.SessionName;
            return;
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(sessionName, @"^[a-zA-Z0-9_]+$"))
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "SettingsOscListen: Session name can only contain letters, numbers, and underscores.", 1);
            _sessionNameLineEdit.Text = OscListen.SessionName;
            return;
        }
        OscListen.SessionName = sessionName;
        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"SettingsOscListen: Session name set to '{sessionName}'", 0);
    }

    private void PortEditing(bool toggledon)
    {
        if (toggledon == true && _portEditing == false)
        {
            _portEditing = true;
        }
        else if (toggledon == false && _portEditing == true)
        {
            // Submit port number
            _portEditing = false;
            _portLineEdit.ReleaseFocus();
            SubmitPort();
        }
    }

    private void SubmitPort()
    {
        if (int.TryParse(_portLineEdit.Text, out int port))
        {
            if (port >= 1 && port <= 65535)
            {
                OscListen.SetPort(port);
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),$"SettingsOscListen: Port set to {port}", 0);
            }
            else
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),"SettingsOscListen: Invalid port number. Must be between 1 and 65535.", 1);
                _portLineEdit.Text = OscListen.Port.ToString();
            }
        }
        else
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"SettingsOscListen: Invalid port number. Please enter a valid integer.", 1);
            _portLineEdit.Text = OscListen.Port.ToString();
        }
    }


    private void DisplayIpAddresses()
    {
        var ipAddresses = new System.Collections.Generic.List<string>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus == OperationalStatus.Up)
            {
                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) // IPv4
                    {
                        ipAddresses.Add(ip.Address.ToString());
                    }
                }
            }
        }
        _ipAddressLabel.Text = string.Join("\n", ipAddresses);
    }
}
