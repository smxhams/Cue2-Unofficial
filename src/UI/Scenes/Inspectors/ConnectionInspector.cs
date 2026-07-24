using System;
using Cue2.Base.Classes;
using Cue2.Base.Classes.Connections;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Cue2.UI.Utilities;
using Godot;

namespace Cue2.UI.Scenes.Inspectors;

public partial class ConnectionInspector : Control
{
    private GlobalData _globalData;
    private GlobalSignals _globalSignals;

    private PackedScene _cueLightComponentCardScene = SceneLoader.LoadPackedScene("uid://cfl3cwoqby4lo", out string _);
    private PackedScene _oscComponentCardScene = SceneLoader.LoadPackedScene("uid://cst0ttvboq673", out string _);
    private PackedScene _midiOutputCardScene = SceneLoader.LoadPackedScene(
        "res://src/UI/Scenes/Inspectors/InspectorMidiOutputCard.tscn", out string _);

    private Cue _focusedCue;
    private MidiManager _midiManager;

    private Label _infoLabel;

    private FlowContainer _connectionCardContainer;
    private PanelContainer _blankConnectionCard;
    private OptionButton _availableConnectionsButton;
    
    
    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _midiManager = GetNodeOrNull<MidiManager>("/root/MidiManager");

        _globalSignals.ShellFocused += ShellSelected;
        if (_midiManager != null)
            _midiManager.MidiStateChanged += OnMidiStateChanged;

        _infoLabel = GetNode<Label>("InfoLabel");
        _infoLabel.AddThemeColorOverride("font_color", GlobalStyles.DisabledColor);
        
        _connectionCardContainer = GetNode<FlowContainer>("%ConnectionCardContainer");
        _connectionCardContainer.Visible = false;
        
        _blankConnectionCard = GetNode<PanelContainer>("%BlankConnectionCard");
        _availableConnectionsButton = GetNode<OptionButton>("%AvailableConnectionsButton");

        VisibilityChanged += LoadConnections;
        _availableConnectionsButton.ItemSelected += OnConnectionSelected;
        
        LoadConnections();
    }

    public override void _ExitTree()
    {
        if (_globalSignals != null)
            _globalSignals.ShellFocused -= ShellSelected;
        if (_midiManager != null)
            _midiManager.MidiStateChanged -= OnMidiStateChanged;
        if (_availableConnectionsButton != null)
            _availableConnectionsButton.ItemSelected -= OnConnectionSelected;
        base._ExitTree();
    }

    /// <summary>
    /// Refresh when session MIDI outputs are added/removed in Settings.
    /// </summary>
    private void OnMidiStateChanged()
    {
        if (Visible && _connectionCardContainer != null && _connectionCardContainer.Visible)
            LoadConnections();
    }


    private void LoadConnections()
    {
        if (!Visible || !_connectionCardContainer.Visible) return;

        // Clean out existing cards
        foreach (var child in _connectionCardContainer.GetChildren())
        {
            if (child == _blankConnectionCard) continue;
            child.QueueFree();
        }
        
        // Load options button in Blank Connection Card (OSC / cue lights / session MIDI outputs).
        _availableConnectionsButton.Clear();
        _availableConnectionsButton.Disabled = false;
        _availableConnectionsButton.AddItem("Select Connection");

        int index = 1;
        var availableConnections = _globalData.GetAvailableConnections();
        foreach (var kvp in availableConnections)
        {
            var connectionType = (string)kvp.Value;
            var connectionObj = kvp.Key;

            if (connectionObj.Obj is CueLight cueLight)
            {
                string displayText = $"{connectionType} - {cueLight.Name}";
                _availableConnectionsButton.AddItem(displayText, index);
                _availableConnectionsButton.SetItemMetadata(index, cueLight);
                index++;
            }
            else if (connectionObj.Obj is CueOscConnection cueOscConnection)
            {
                string displayText = $"{connectionType} - {cueOscConnection.Name}";
                _availableConnectionsButton.AddItem(displayText, index);
                _availableConnectionsButton.SetItemMetadata(index, cueOscConnection);
                index++;
            }
            else
            {
                GD.Print($"ConnectionInspector:LoadConnections - Unsupported connection type: {connectionObj.VariantType}");
            }
        }

        // Session MIDI outputs (string metadata "midi:DeviceName").
        if (_midiManager != null)
        {
            foreach (string outName in _midiManager.SessionOutputNames)
            {
                if (string.IsNullOrEmpty(outName)) continue;
                _availableConnectionsButton.AddItem($"MIDI Output - {outName}", index);
                _availableConnectionsButton.SetItemMetadata(index, "midi:" + outName);
                index++;
            }
        }

        if (index == 1)
        {
            // Only the placeholder was added.
            _availableConnectionsButton.Clear();
            _availableConnectionsButton.AddItem("No available connections");
            _availableConnectionsButton.Disabled = true;
        }
        else
        {
            _availableConnectionsButton.Select(0);
        }
        
        // Load existing connection cards on the focused cue.
        if (_focusedCue == null) return;
        foreach (var component in _focusedCue.Components)
        {
            if (component is CueLightComponent cueLightComp)
                LoadCueLightComponentCard(cueLightComp);
            else if (component is OscComponent oscComp)
                LoadOscComponentCard(oscComp);
            else if (component is MidiOutputComponent midiOut)
                LoadMidiOutputComponentCard(midiOut);
        }
    }

    private void LoadOscComponentCard(OscComponent component)
    {
        var oscCard = _oscComponentCardScene.Instantiate<InspectorOscConnectionCard>();
        _connectionCardContainer.AddChild(oscCard);
        oscCard.SetComponent(component, this);
            
    }

    private void LoadMidiOutputComponentCard(MidiOutputComponent component)
    {
        if (_midiOutputCardScene == null)
        {
            GD.PrintErr("ConnectionInspector:LoadMidiOutputComponentCard - card scene missing");
            return;
        }
        var card = _midiOutputCardScene.Instantiate<InspectorMidiOutputCard>();
        _connectionCardContainer.AddChild(card);
        card.SetComponent(component, this);
    }

    public void RemoveComponent(ICueComponent component)
    {
        if (_focusedCue != null)
            _globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Remove connection component");
        _focusedCue.RemoveICueComponent(component);
        // Refresh tab content indicators (dot on Connection tab).
        _globalSignals?.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
    }

    private void LoadCueLightComponentCard(CueLightComponent cueLightComp)
    {
        //GD.Print($"ConnectionInspector:LoadConnection - CUEEEEELIGHT COMPONENTTTTTTT");
        var cueLightCard = _cueLightComponentCardScene.Instantiate<PanelContainer>();
        _connectionCardContainer.AddChild(cueLightCard);
        var position = cueLightCard.GetIndex();
        if (position > 0)
        {
            _connectionCardContainer.MoveChild(cueLightCard, position - 1);
        }
        var actionOptionButton = cueLightCard.GetNode<OptionButton>("%ConnectionActionButton");
        var countInLineEdit = cueLightCard.GetNode<LineEdit>("%CountInLineEdit");
        var nameLabel = cueLightCard.GetNode<Label>("%NameLabel");
        var deleteButton = cueLightCard.GetNode<Button>("%DeleteButton");
        nameLabel.Text = cueLightComp.CueLight.Name;
        if (cueLightComp.CountInTime > 0)
        {
            countInLineEdit.Text = UiUtilities.FormatTime((double)cueLightComp.CountInTime);
        }
        else
        {
            countInLineEdit.Text = "";
            countInLineEdit.PlaceholderText = "(Pre-Wait)";
        }
        
        
        // Populate actionOptionButton with CueLightAction enum values 
        actionOptionButton.Clear(); 
        var actions = Enum.GetValues(typeof(CueLightAction)); 
        for (int i = 0; i < actions.Length; i++) 
        { 
            actionOptionButton.AddItem(actions.GetValue(i)?.ToString(), i); 
        } 
        actionOptionButton.Selected = (int)cueLightComp.Action; 

        // Handle action selection to update component 
        actionOptionButton.ItemSelected += (long index) => 
        { 
            try 
            {
                if ((int)cueLightComp.Action == (int)index) return;
                if (_focusedCue != null)
                    _globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit cue light action");
                cueLightComp.Action = (CueLightAction)index; 
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Updated action for CueLightComponent in Cue {_focusedCue.Id} to {cueLightComp.Action}", 0); 
            } 
            catch (Exception ex) 
            { 
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to update CueLightComponent action: {ex.Message}", 2); 
            } 
        };
        
        // Handle countInLineEdit changes (optional, but for completeness) 
        countInLineEdit.TextSubmitted += (string newText) =>
        {
            var time = UiUtilities.ParseAndFormatTime(newText, out var seconds);
            if (_focusedCue != null)
                _globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit cue light count-in");
            cueLightComp.CountInTime = (float)seconds; 
            countInLineEdit.Text = time; 
            countInLineEdit.ReleaseFocus();
        };
        
        deleteButton.Icon = GetThemeIcon("DeleteBin", "AtlasIcons"); 
        deleteButton.Pressed += () => 
        { 
            try 
            {
                if (_focusedCue != null)
                    _globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Remove cue light component");
                _focusedCue.Components.Remove(cueLightComp); 
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Removed CueLightComponent from Cue {_focusedCue.Id}", 0); 
                LoadConnections();
                _globalSignals.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
            } 
            catch (Exception ex) 
            { 
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to remove CueLightComponent: {ex.Message}", 2); 
            } 
        };
    }

    private void OnConnectionSelected(long selectedIndex)
    {
        if (selectedIndex < 0 || _availableConnectionsButton == null) return;
        var selectedMetadata = _availableConnectionsButton.GetItemMetadata((int)selectedIndex);

        // MIDI outputs use string metadata "midi:DeviceName".
        if (selectedMetadata.VariantType == Variant.Type.String)
        {
            string meta = selectedMetadata.AsString();
            if (meta != null && meta.StartsWith("midi:", StringComparison.Ordinal))
            {
                string deviceName = meta.Substring("midi:".Length);
                if (_focusedCue != null && !string.IsNullOrEmpty(deviceName))
                {
                    _globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Add MIDI output component");
                    var midiComp = new MidiOutputComponent
                    {
                        OutputDeviceName = deviceName,
                        MessageType = MidiTriggerMessageType.NoteOn,
                        Channel = 1,
                        Data1 = 60,
                        Data2 = 100,
                        NoteDurationSeconds = 0.5
                    };
                    _focusedCue.AddICueComponent(midiComp);
                    _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                        $"Selected connection: MIDI Output - {deviceName}", 0);
                    LoadConnections();
                    _globalSignals.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
                }
                return;
            }
        }

        var selectedObj = selectedMetadata.Obj;
        if (selectedObj is CueLight selectedCueLight)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Selected connection: Cue Light - {selectedCueLight.Name}", 0);
            if (_focusedCue != null)
                _globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Add cue light component");
            var cueLightComponent = new CueLightComponent { CueLight = selectedCueLight, CueLightId = selectedCueLight.Id };
            _focusedCue.AddICueComponent(cueLightComponent);
            LoadConnections();
            _globalSignals.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
        }
        else if (selectedObj is CueOscConnection selectedOscConnection)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Selected connection: OSC Connection - {selectedOscConnection.Name}", 0);
            if (_focusedCue != null)
                _globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Add OSC component");
            var oscComponent = new OscComponent { OscConnection = selectedOscConnection, OscConnectionId = selectedOscConnection.Id };
            _focusedCue.AddICueComponent(oscComponent);
            LoadConnections();
            _globalSignals.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
        }
        else
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Failed to retrieve selected connection object.", 2);
        }
    }

    private void ShellSelected(int cueId)
    {
        _focusedCue = CueList.FetchCueFromId(cueId);

        if (_focusedCue == null)
        {
            GD.Print($"ConnectionInspector:ShellSelected - No Shell selected");
            _infoLabel.Visible = true;
            _connectionCardContainer.Visible = false;
            return;
        }
        
        _infoLabel.Visible = false;
        _connectionCardContainer.Visible = true;
        
        LoadConnections();
        
        
    }
    

}