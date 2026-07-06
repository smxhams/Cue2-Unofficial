using System;
using System.Collections.Generic;
using System.Linq;
using Cue2.Shared;
using Godot;

namespace Cue2.UI.Scenes.Settings;

/// <summary>
/// Settings panel for viewing and editing the project's InputMap actions.
/// Displays action cards inside a FlowContainer and supports rebinding.
/// </summary>
public partial class SettingsInputMap : ScrollContainer
{
    private GlobalSignals _globalSignals;

    private FlowContainer _inputsContainer;
    private PackedScene _inputActionCardScene;
    private GlobalData _globalData;

    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNode<GlobalData>("/root/GlobalData");

        _inputsContainer = GetNode<FlowContainer>("%InputsContainer");

        // Load by path for now (uid will be assigned by editor on first save of the scene).
        _inputActionCardScene = SceneLoader.LoadPackedScene(
            "res://src/UI/Scenes/Settings/InputActionCard.tscn", out string err);
        if (_inputActionCardScene == null)
        {
            GD.PrintErr($"SettingsInputMap:_Ready - Failed to load InputActionCard.tscn: {err}");
        }

        VisibilityChanged += OnVisibilityChanged;

        // Initial populate if already visible (e.g. opened directly)
        if (IsVisibleInTree())
        {
            PopulateActions();
        }
    }

    private void OnVisibilityChanged()
    {
        if (Visible)
        {
            PopulateActions();
        }
        else
        {
            // Optional: clear to reduce node count when hidden
            ClearCards();
        }
    }

    /// <summary>
    /// Clears existing action cards from the container.
    /// </summary>
    private void ClearCards()
    {
        if (_inputsContainer == null) return;
        foreach (Node child in _inputsContainer.GetChildren())
        {
            _inputsContainer.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>
    /// Reads actions from the project InputMap and creates a card for each relevant action.
    /// </summary>
    private void PopulateActions()
    {
        if (_inputsContainer == null || _inputActionCardScene == null) return;

        ClearCards();

        // Use the centralized list of mappable actions defined in GlobalData.
        var managed = GlobalData.MappableInputActions;

        // Prefer our curated list so order and visibility is controlled.
        // Fall back to discovering from InputMap if an action is missing from the list.
        var actionsToShow = new List<string>();

        foreach (var action in managed)
        {
            if (InputMap.HasAction(action))
            {
                actionsToShow.Add(action);
            }
        }

        // Also discover any other non-ui_ actions that exist at runtime but are not in our list yet.
        var allActions = InputMap.GetActions();
        foreach (StringName actionName in allActions)
        {
            string name = actionName.ToString();
            if (name.StartsWith("ui_")) continue;
            if (actionsToShow.Contains(name)) continue;
            if (managed.Contains(name)) continue;
            actionsToShow.Add(name);
        }

        GD.Print($"SettingsInputMap:PopulateActions - Populating {actionsToShow.Count} input action cards");

        foreach (var actionName in actionsToShow)
        {
            var card = _inputActionCardScene.Instantiate<InputActionCard>();
            _inputsContainer.AddChild(card);
            card.SetAction(actionName);
        }
    }

    public override void _ExitTree()
    {
        VisibilityChanged -= OnVisibilityChanged;
    }
}
