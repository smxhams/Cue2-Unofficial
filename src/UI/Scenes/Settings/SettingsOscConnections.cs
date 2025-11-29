using System;
using Godot;
using Cue2.Base.Classes.Connections;
using Cue2.Shared;
using Godot.Collections;
using Array = System.Array;

namespace Cue2.UI.Scenes.Settings;

public partial class SettingsOscConnections : ScrollContainer
{

    // Ui Properties
    private Button _newOscButton;
    private VBoxContainer _connectionsContainer;

    private Label _nameLabel;
    private Label _interfaceLabel;
    private Label _destinationLabel;
    private Label _portLabel;
    
    private PackedScene _oscConnectionCardScene = SceneLoader.LoadPackedScene("uid://coxpcw6hyfn4p", out _);
    
    public override void _Ready()
    {
        
        // Assign Ui properties
        _newOscButton = GetNode<Button>("%NewOscButton");
        _connectionsContainer = GetNode<VBoxContainer>("%ConnectionsContainer");
        
        _nameLabel = GetNode<Label>("%NameLabel");
        _interfaceLabel = GetNode<Label>("%InterfaceLabel");
        _destinationLabel = GetNode<Label>("%DestinationLabel");
        _portLabel = GetNode<Label>("%PortLabel");
        
        _newOscButton.Pressed += NewConnection;
        _nameLabel.Resized += UpdateUiColumns;
        _interfaceLabel.Resized += UpdateUiColumns;
        _destinationLabel.Resized += UpdateUiColumns;
        _portLabel.Resized += UpdateUiColumns;

        VisibilityChanged += SyncConnections;
    }


    public void SyncConnections()
    {
        GD.Print($"SettingsOscConnections:SyncConnections - Syncing {OscConnections.Connections.Count} OSC connections");
        if (Visible)
        {
            // Clear existing cards
            foreach (var child in _connectionsContainer.GetChildren())
            {
                _connectionsContainer.RemoveChild(child);
                child.QueueFree();
            }

            // Add cards for each connection
            var ratios = GetColumnRatios();
            foreach (var connection in OscConnections.Connections)
            {
                var connectionCard = _oscConnectionCardScene.Instantiate<SettingsOscConnectionCard>();
                _connectionsContainer.AddChild(connectionCard);
                connectionCard.SetCueOscConnection(connection);
                connectionCard.UpdateRatios(ratios);
            }
        }
    }

    private void NewConnection()
    {
        var connection = OscConnections.CreateConnection();
        var connectionCard = _oscConnectionCardScene.Instantiate<SettingsOscConnectionCard>();
        _connectionsContainer.AddChild(connectionCard);
        connectionCard.SetCueOscConnection(connection);

        var ratios = GetColumnRatios();
        connectionCard.UpdateRatios(ratios);
    }

    private Godot.Collections.Dictionary GetColumnRatios()
    {
        float totalWidth = _nameLabel.Size.X + _interfaceLabel.Size.X + _destinationLabel.Size.X + _portLabel.Size.X;
        if (totalWidth == 0) return new Godot.Collections.Dictionary();
        var ratios = new Godot.Collections.Dictionary();
        ratios["Name"] = _nameLabel.Size.X / totalWidth;
        ratios["Interface"] = _interfaceLabel.Size.X / totalWidth;
        ratios["Destination"] = _destinationLabel.Size.X / totalWidth;
        ratios["Port"] = _portLabel.Size.X / totalWidth;
        return ratios;
    }

    private void UpdateUiColumns()
    {
        var ratios = GetColumnRatios();
        UpdateAllCardRatios(ratios);
    }

    private void UpdateAllCardRatios(Godot.Collections.Dictionary ratios)
    {
        foreach (var child in _connectionsContainer.GetChildren())
        {
            if (child is SettingsOscConnectionCard card)
            {
                card.UpdateRatios(ratios);
            }
        }
    }
}
