using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using Cue2.Base.Classes.Connections;
using Cue2.Shared;
using Cue2.UI.Utilities;
using Godot;
using Godot.Collections;

namespace Cue2.UI.Scenes.Settings;

public partial class SettingsOscConnectionCard : HBoxContainer
{
    private GlobalSignals _globalSignals;
    
    public CueOscConnection CueOscConnection;
    
    // Ui Properties
    private Button _rearrangeButton;
    private LineEdit _nameLineEdit;
    private OptionButton _interfaceOptionButton;
    private LineEdit _destinationLineEdit;
    private LineEdit _portLineEdit;
    private Button _deleteButton;

    // Previous values for validation and revert
    private bool _nameEditing;
    private bool _destinationEditing;
    private bool _portEditing;

    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

        // Assign Ui nodes
        _rearrangeButton = GetNode<Button>("%RearrangeButton");
        _nameLineEdit = GetNode<LineEdit>("%NameLineEdit");
        _interfaceOptionButton = GetNode<OptionButton>("%InterfaceOptionButton");
        _destinationLineEdit = GetNode<LineEdit>("%DestinationLineEdit");
        _portLineEdit = GetNode<LineEdit>("%PortLineEdit");
        _deleteButton = GetNode<Button>("%DeleteButton");

        // Connect signals for user input handling
        _nameLineEdit.EditingToggled += OnNameEditingToggled;
        _nameLineEdit.TextSubmitted += OnNameTextSubmitted;
        _interfaceOptionButton.Pressed += LoadInterfaceOptions;
        _interfaceOptionButton.ItemSelected += OnInterfaceItemSelected;
        _destinationLineEdit.EditingToggled += OnDestinationEditingToggled;
        _destinationLineEdit.TextSubmitted += OnDestinationTextSubmitted;
        _portLineEdit.EditingToggled += OnPortEditingToggled;
        _portLineEdit.TextSubmitted += OnPortTextSubmitted;
        _deleteButton.Pressed += OnDeletePressed;
        
        
        _rearrangeButton.Icon = GetThemeIcon("Rearrange", "AtlasIcons");
        _deleteButton.Icon = GetThemeIcon("DeleteBin", "AtlasIcons");

    }

    public void SetCueOscConnection(CueOscConnection connection)
    {
        CueOscConnection = connection;
        _nameLineEdit.Text = connection.Name;
        _destinationLineEdit.Text = connection.Address.ToString();
        _portLineEdit.Text = connection.Port.ToString();
        LoadInterfaceOptions();

    }

    

    private void OnNameEditingToggled(bool editing)
    {
        if (editing) _nameEditing = true;
        else
        {
            _nameEditing = false;
            _nameLineEdit.ReleaseFocus();
            OnNameTextSubmitted(_nameLineEdit.Text);
        }
    }

    private void OnNameTextSubmitted(string newText)
    {
        if (CueOscConnection != null && !string.IsNullOrWhiteSpace(newText))
        {
            CueOscConnection.Name = newText;
        }
        else
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Failed to submit new name for Osc Connection", 1);
        }
    }

    private void OnInterfaceItemSelected(long index)
    {
        if (CueOscConnection != null)
        {
            if (index == 0)
            {
                CueOscConnection.NetworkInterface = "";
            }
            else
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                var interfaceNames = new List<string>();
                foreach (var ni in interfaces)
                {
                    interfaceNames.Add(ni.Name);
                }
                // Include stored if not found
                if (CueOscConnection != null && !string.IsNullOrEmpty(CueOscConnection.NetworkInterface) && !interfaceNames.Contains(CueOscConnection.NetworkInterface))
                {
                    interfaceNames.Add(CueOscConnection.NetworkInterface);
                }
                if (index - 1 < interfaceNames.Count)
                {
                    CueOscConnection.NetworkInterface = interfaceNames[(int)index - 1];
                }
            }
        }
    }

    private void OnDestinationEditingToggled(bool editing)
    {
        if (editing) _destinationEditing = true;
        else
        {
            _destinationEditing = false;
            _destinationLineEdit.ReleaseFocus();
            OnDestinationTextSubmitted(_destinationLineEdit.Text);
        }
    }

    private void OnDestinationTextSubmitted(string newText)
    {

        if (CueOscConnection != null)
        {
            if (IPAddress.TryParse(newText, out var ip))
            {
                CueOscConnection.Address = ip;
            }
            else
            {
                _destinationLineEdit.Text = CueOscConnection.Address.ToString();
            }
        }
    }

    private void OnPortEditingToggled(bool editing)
    {
        if (editing) _portEditing = true;
        else
        {
            _portEditing = false;
            _portLineEdit.ReleaseFocus();
            OnPortTextSubmitted(_portLineEdit.Text);
        }
    }

    private void OnPortTextSubmitted(string newText)
    {
        if (CueOscConnection != null)
        {
            var port = UiUtilities.ValidatePort(newText);
            if (port != -1)
            {
                CueOscConnection.Port = port;
            }
            else _portLineEdit.Text = CueOscConnection.Port.ToString();
        }
    }

    private void OnDeletePressed()
    {
        if (CueOscConnection != null)
        {
            OscConnections.DeleteConnection(CueOscConnection.Id);
            QueueFree();
        }
    }

    public void UpdateRatios(Godot.Collections.Dictionary ratios)
    {
        foreach (var key in ratios.Keys)
        {
            string keyStr = key.ToString();
            float value = (float)ratios[key];
            switch (keyStr)
            {
                case "Name":
                    _nameLineEdit.SizeFlagsStretchRatio = value;
                    break;
                case "Interface":
                    _interfaceOptionButton.SizeFlagsStretchRatio = value;
                    break;
                case "Destination":
                    _destinationLineEdit.SizeFlagsStretchRatio = value;
                    break;
                case "Port":
                    _portLineEdit.SizeFlagsStretchRatio = value;
                    break;
                default:
                    GD.Print($"Unknown ratio key: {keyStr}");
                    break;
            }
        }
    }
    
    private void LoadInterfaceOptions()
    {
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
                if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) // IPv4
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

        // If stored interface not found, add it as not found
        if (CueOscConnection != null && !string.IsNullOrEmpty(CueOscConnection.NetworkInterface) && !interfaceNames.Contains(CueOscConnection.NetworkInterface))
        {
            string itemText = $"{CueOscConnection.NetworkInterface} (not found)";
            _interfaceOptionButton.AddItem(itemText, interfaceNames.Count + 1);
            interfaceNames.Add(CueOscConnection.NetworkInterface);
        }

        // Set selected based on connection's NetworkInterface
        if (CueOscConnection != null && !string.IsNullOrEmpty(CueOscConnection.NetworkInterface))
        {
            int index = interfaceNames.IndexOf(CueOscConnection.NetworkInterface);
            if (index >= 0)
            {
                _interfaceOptionButton.Select(index + 1); // +1 because 0 is Automatic
            }
        }
        else
        {
            _interfaceOptionButton.Select(0); // Default to Automatic
        }
    }
}