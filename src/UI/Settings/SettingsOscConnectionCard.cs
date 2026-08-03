// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using Cue2.Domain.Connections;
using Cue2.Services;
using Cue2.UI.Utilities;
using Godot;

namespace Cue2.UI.Settings;

/// <summary>
/// Editable row for a single OSC send connection (name, interface, destination, port).
/// </summary>
public partial class SettingsOscConnectionCard : HBoxContainer
{
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;
    private HistoryManager _historyManager;

    public CueOscConnection CueOscConnection;

    private LineEdit _nameLineEdit;
    private OptionButton _transportOption;
    private OptionButton _interfaceOptionButton;
    private LineEdit _destinationLineEdit;
    private LineEdit _portLineEdit;
    private Button _deleteButton;

    private bool _nameEditing;
    private bool _destinationEditing;
    private bool _portEditing;
    private bool _isSyncingUi;

    /// <summary>
    /// Raised before a user mutation so the parent panel can record settings history.
    /// </summary>
    public event Action<string> HistoryRecordRequested;

    public override void _Ready()
    {
        _globalSignals = GetNodeOrNull<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNodeOrNull<GlobalData>("/root/GlobalData");
        _historyManager = _globalData?.HistoryManager;

        _nameLineEdit = GetNodeOrNull<LineEdit>("%NameLineEdit");
        _transportOption = GetNodeOrNull<OptionButton>("%TransportOption");
        _interfaceOptionButton = GetNodeOrNull<OptionButton>("%InterfaceOptionButton");
        _destinationLineEdit = GetNodeOrNull<LineEdit>("%DestinationLineEdit");
        _portLineEdit = GetNodeOrNull<LineEdit>("%PortLineEdit");
        _deleteButton = GetNodeOrNull<Button>("%DeleteButton");

        if (_transportOption != null)
        {
            _transportOption.Clear();
            _transportOption.AddItem("UDP", 0);
            _transportOption.AddItem("TCP", 1);
            _transportOption.ItemSelected += OnTransportSelected;
        }

        if (_nameLineEdit != null)
        {
            _nameLineEdit.EditingToggled += OnNameEditingToggled;
            _nameLineEdit.TextSubmitted += OnNameTextSubmitted;
        }
        if (_interfaceOptionButton != null)
        {
            _interfaceOptionButton.Pressed += LoadInterfaceOptions;
            _interfaceOptionButton.ItemSelected += OnInterfaceItemSelected;
        }
        if (_destinationLineEdit != null)
        {
            _destinationLineEdit.EditingToggled += OnDestinationEditingToggled;
            _destinationLineEdit.TextSubmitted += OnDestinationTextSubmitted;
        }
        if (_portLineEdit != null)
        {
            _portLineEdit.EditingToggled += OnPortEditingToggled;
            _portLineEdit.TextSubmitted += OnPortTextSubmitted;
        }
        if (_deleteButton != null)
            _deleteButton.Pressed += OnDeletePressed;

        try
        {
            if (_deleteButton != null)
                _deleteButton.Icon = GetThemeIcon("DeleteBin", "AtlasIcons");
        }
        catch { /* icons optional */ }
    }

    /// <summary>
    /// Binds this card to a live <see cref="CueOscConnection"/> and refreshes fields.
    /// </summary>
    public void SetCueOscConnection(CueOscConnection connection)
    {
        CueOscConnection = connection;
        _isSyncingUi = true;
        try
        {
            if (_nameLineEdit != null)
                _nameLineEdit.Text = connection?.Name ?? string.Empty;
            if (_transportOption != null)
                _transportOption.Select(connection?.Transport == OscTransport.Tcp ? 1 : 0);
            if (_destinationLineEdit != null)
                _destinationLineEdit.Text = connection?.Address?.ToString() ?? string.Empty;
            if (_portLineEdit != null)
                _portLineEdit.Text = (connection?.Port ?? OscConnections.DefaultSendPort).ToString();
            if (_interfaceOptionButton != null)
                _interfaceOptionButton.Disabled = connection?.Transport == OscTransport.Tcp;
            LoadInterfaceOptions();
        }
        finally
        {
            _isSyncingUi = false;
        }
    }

    private void OnTransportSelected(long index)
    {
        if (_isSyncingUi || CueOscConnection == null) return;
        if (_historyManager?.IsRestoring == true) return;
        var next = index == 1 ? OscTransport.Tcp : OscTransport.Udp;
        if (CueOscConnection.Transport == next) return;

        // Update model + UI controls immediately — TCP connect runs async and must not freeze the dropdown.
        RequestHistory(next == OscTransport.Tcp ? "Set OSC transport TCP" : "Set OSC transport UDP");
        CueOscConnection.Transport = next;
        if (_interfaceOptionButton != null)
            _interfaceOptionButton.Disabled = next == OscTransport.Tcp;

        // Non-blocking: UDP opens inline; TCP connect is background with status in monitor/log.
        CueOscConnection.Reconnect();
    }

    private void RequestHistory(string description)
    {
        if (_historyManager?.IsRestoring == true) return;
        HistoryRecordRequested?.Invoke(description);
    }

    private void OnNameEditingToggled(bool editing)
    {
        if (editing) _nameEditing = true;
        else
        {
            _nameEditing = false;
            _nameLineEdit?.ReleaseFocus();
            OnNameTextSubmitted(_nameLineEdit?.Text ?? string.Empty);
        }
    }

    private void OnNameTextSubmitted(string newText)
    {
        _nameEditing = false;
        _nameLineEdit?.ReleaseFocus();
        if (_isSyncingUi || CueOscConnection == null) return;
        if (_historyManager?.IsRestoring == true) return;

        if (!string.IsNullOrWhiteSpace(newText))
        {
            string trimmed = newText.Trim();
            if (CueOscConnection.Name == trimmed) return;
            RequestHistory($"Rename OSC connection to '{trimmed}'");
            CueOscConnection.Name = trimmed;
        }
        else
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "OSC Connection: name cannot be empty.", (int)LogType.Warning);
            if (_nameLineEdit != null)
                _nameLineEdit.Text = CueOscConnection.Name;
        }
    }

    private void OnInterfaceItemSelected(long index)
    {
        if (_isSyncingUi || CueOscConnection == null) return;
        if (_historyManager?.IsRestoring == true) return;

        string previous = CueOscConnection.NetworkInterface ?? string.Empty;
        string next;

        if (index == 0)
        {
            next = string.Empty;
        }
        else
        {
            var interfaceNames = EnumerateInterfaceNames();
            if (CueOscConnection != null
                && !string.IsNullOrEmpty(CueOscConnection.NetworkInterface)
                && !interfaceNames.Contains(CueOscConnection.NetworkInterface))
            {
                interfaceNames.Add(CueOscConnection.NetworkInterface);
            }

            if (index - 1 < interfaceNames.Count)
                next = interfaceNames[(int)index - 1];
            else
                return;
        }

        if (previous == next) return;
        RequestHistory("Change OSC connection interface");
        CueOscConnection.NetworkInterface = next;
        CueOscConnection.Reconnect();
    }

    private void OnDestinationEditingToggled(bool editing)
    {
        if (editing) _destinationEditing = true;
        else
        {
            _destinationEditing = false;
            _destinationLineEdit?.ReleaseFocus();
            OnDestinationTextSubmitted(_destinationLineEdit?.Text ?? string.Empty);
        }
    }

    private void OnDestinationTextSubmitted(string newText)
    {
        _destinationEditing = false;
        _destinationLineEdit?.ReleaseFocus();
        if (_isSyncingUi || CueOscConnection == null) return;
        if (_historyManager?.IsRestoring == true) return;

        if (IPAddress.TryParse(newText, out var ip))
        {
            if (CueOscConnection.Address != null && CueOscConnection.Address.Equals(ip)) return;
            RequestHistory($"Set OSC destination to {ip}");
            CueOscConnection.Address = ip;
            CueOscConnection.Reconnect();
        }
        else if (_destinationLineEdit != null)
        {
            _destinationLineEdit.Text = CueOscConnection.Address?.ToString() ?? string.Empty;
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "OSC Connection: invalid IP address.", (int)LogType.Warning);
        }
    }

    private void OnPortEditingToggled(bool editing)
    {
        if (editing) _portEditing = true;
        else
        {
            _portEditing = false;
            _portLineEdit?.ReleaseFocus();
            OnPortTextSubmitted(_portLineEdit?.Text ?? string.Empty);
        }
    }

    private void OnPortTextSubmitted(string newText)
    {
        _portEditing = false;
        _portLineEdit?.ReleaseFocus();
        if (_isSyncingUi || CueOscConnection == null) return;
        if (_historyManager?.IsRestoring == true) return;

        var port = UiUtilities.ValidatePort(newText);
        if (port != -1)
        {
            if (CueOscConnection.Port == port) return;
            RequestHistory($"Set OSC port to {port}");
            CueOscConnection.Port = port;
            CueOscConnection.Reconnect();
        }
        else if (_portLineEdit != null)
        {
            _portLineEdit.Text = CueOscConnection.Port.ToString();
        }
    }

    private void OnDeletePressed()
    {
        if (CueOscConnection == null) return;
        if (_historyManager?.IsRestoring == true) return;

        string name = CueOscConnection.Name;
        RequestHistory($"Delete OSC connection '{name}'");
        OscConnections.DeleteConnection(CueOscConnection.Id);
        QueueFree();
    }

    /// <summary>
    /// Applies stretch ratios from parent column headers.
    /// </summary>
    public void UpdateRatios(Godot.Collections.Dictionary ratios)
    {
        if (ratios == null) return;
        foreach (var key in ratios.Keys)
        {
            string keyStr = key.ToString();
            float value = (float)ratios[key];
            switch (keyStr)
            {
                case "Name":
                    if (_nameLineEdit != null)
                        _nameLineEdit.SizeFlagsStretchRatio = value;
                    break;
                case "Interface":
                    if (_interfaceOptionButton != null)
                        _interfaceOptionButton.SizeFlagsStretchRatio = value;
                    break;
                case "Destination":
                    if (_destinationLineEdit != null)
                        _destinationLineEdit.SizeFlagsStretchRatio = value;
                    break;
                case "Port":
                    if (_portLineEdit != null)
                        _portLineEdit.SizeFlagsStretchRatio = value;
                    break;
            }
        }
    }

    private static List<string> EnumerateInterfaceNames()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => (ni.OperationalStatus == OperationalStatus.Up ||
                          ni.OperationalStatus == OperationalStatus.Down ||
                          ni.OperationalStatus == OperationalStatus.NotPresent) &&
                         (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                          ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                          ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                          ni.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet))
            .OrderBy(ni => ni.OperationalStatus == OperationalStatus.Up ? 0 :
                ni.OperationalStatus == OperationalStatus.Down ? 1 : 2)
            .Select(ni => ni.Name)
            .ToList();
    }

    private void LoadInterfaceOptions()
    {
        if (_interfaceOptionButton == null) return;

        _interfaceOptionButton.Clear();
        _interfaceOptionButton.AddItem("Automatic", 0);

        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => (ni.OperationalStatus == OperationalStatus.Up ||
                          ni.OperationalStatus == OperationalStatus.Down ||
                          ni.OperationalStatus == OperationalStatus.NotPresent) &&
                         (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                          ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                          ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                          ni.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet))
            .OrderBy(ni => ni.OperationalStatus == OperationalStatus.Up ? 0 :
                ni.OperationalStatus == OperationalStatus.Down ? 1 : 2)
            .ToList();

        var interfaceNames = new List<string>();
        foreach (var ni in interfaces)
        {
            string ipAddress = "";
            foreach (var ip in ni.GetIPProperties().UnicastAddresses)
            {
                if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    ipAddress = ip.Address.ToString();
                    break;
                }
            }
            string status = ni.OperationalStatus == OperationalStatus.Up ? "" : " (inactive)";
            string itemText = $"{ni.Name}: {ipAddress}{status}";
            _interfaceOptionButton.AddItem(itemText, interfaceNames.Count + 1);
            interfaceNames.Add(ni.Name);
        }

        if (CueOscConnection != null
            && !string.IsNullOrEmpty(CueOscConnection.NetworkInterface)
            && !interfaceNames.Contains(CueOscConnection.NetworkInterface))
        {
            string itemText = $"{CueOscConnection.NetworkInterface} (not found)";
            _interfaceOptionButton.AddItem(itemText, interfaceNames.Count + 1);
            interfaceNames.Add(CueOscConnection.NetworkInterface);
        }

        if (CueOscConnection != null && !string.IsNullOrEmpty(CueOscConnection.NetworkInterface))
        {
            int index = interfaceNames.IndexOf(CueOscConnection.NetworkInterface);
            if (index >= 0)
                _interfaceOptionButton.Select(index + 1);
        }
        else
        {
            _interfaceOptionButton.Select(0);
        }
    }
}
