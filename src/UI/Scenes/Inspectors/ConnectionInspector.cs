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

    private PackedScene _cueLightComponentCardScene;
    
    private Cue _focusedCue;

    private Label _infoLabel;

    private FlowContainer _connectionCardContainer;
    private PanelContainer _blankConnectionCard;
    private OptionButton _availableConnectionsButton;
    
    
    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

        _globalSignals.ShellFocused += ShellSelected;
        
        _cueLightComponentCardScene = SceneLoader.LoadPackedScene("uid://cfl3cwoqby4lo", out string _);
        
        
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


    private void LoadConnections()
    {
        if (!Visible || !_connectionCardContainer.Visible) return;

        // Clean out existing cards
        foreach (var child in _connectionCardContainer.GetChildren())
        {
            if (child == _blankConnectionCard) continue;
            child.QueueFree();
        }
        
        // Load options button in Blank Connection Card
        _availableConnectionsButton.Clear();
        
        var availableConnections = _globalData.GetAvailableConnections();
        
        if (availableConnections.Count == 0)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "No available connections found.", 1); // Warning log
            // Optionally show blank card or disable button
            _availableConnectionsButton.AddItem("No available connections");
            _availableConnectionsButton.Disabled = true;
            return;
        }

        _availableConnectionsButton.Disabled = false;
        _availableConnectionsButton.AddItem("Select Connection");

        int index = 1;
        foreach (var kvp in availableConnections)
        {
            var connectionType = (string)kvp.Key;
            var connectionObj = kvp.Value;

            if (connectionObj.Obj is CueLight cueLight)
            {
                string displayText = $"{connectionType} - {cueLight.Name}";
                _availableConnectionsButton.AddItem(displayText, index);
                _availableConnectionsButton.SetItemMetadata(index, cueLight); // Associate the object with the item for later retrieval
                index++;
            }
            else
            {
                GD.Print($"Unsupported connection type: {connectionObj.VariantType}", 2); // Error log
                continue;
            }
        }
        
        
        
        // Load Cueliught connection cards
        if (_focusedCue == null) return;
        foreach (var component in _focusedCue.Components)
        {
            if (component is CueLightComponent)
            {
                LoadCueLightComponentCard(component as CueLightComponent);
            }
        }
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
            cueLightComp.CountInTime = (float)seconds; 
            countInLineEdit.Text = time; 
            countInLineEdit.ReleaseFocus();
        };
        
        deleteButton.Icon = GetThemeIcon("DeleteBin", "AtlasIcons"); 
        deleteButton.Pressed += () => 
        { 
            try 
            { 
                _focusedCue.Components.Remove(cueLightComp); 
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Removed CueLightComponent from Cue {_focusedCue.Id}", 0); 
                LoadConnections(); 
            } 
            catch (Exception ex) 
            { 
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to remove CueLightComponent: {ex.Message}", 2); 
            } 
        };
    }

    private void OnConnectionSelected(long selectedIndex)
    {
        var selectedMetadata = _availableConnectionsButton.GetItemMetadata((int)selectedIndex);
        var selectedObj = selectedMetadata.Obj;
        if (selectedObj is CueLight selectedCueLight)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Selected connection: Cue Light - {selectedCueLight.Name}", 0);
            var cueLightComponent = new CueLightComponent { CueLight = selectedCueLight, CueLightId = selectedCueLight.Id };
            _focusedCue.AddCueLightComponent(cueLightComponent);
            LoadConnections();
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