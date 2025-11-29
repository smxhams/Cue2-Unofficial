using Godot;
using System;
using Cue2.Base.Classes.CueTypes;

namespace Cue2.UI.Scenes.Inspectors;
public partial class InspectorOscConnectionCard : PanelContainer
{
	private Label _nameLabel;
	private LineEdit _commandLineEdit;
	private Button _deleteButton;
	private Label _oscConnectionLabel;
	private OscComponent _oscComponent;
	private ConnectionInspector _connectionInspector;

	private bool _commandEditing = false;
	
	public override void _Ready()
	{
		
		
		_nameLabel = GetNode<Label>("%NameLabel");
		_commandLineEdit = GetNode<LineEdit>("%CommandTextEdit");
		_deleteButton = GetNode<Button>("%DeleteButton");
		_oscConnectionLabel = GetNode<Label>("%OscConnectionLabel");
		

		_commandLineEdit.EditingToggled += OnCommandEditing;
		_commandLineEdit.TextSubmitted += OnCommandTextSubmitted;
		_deleteButton.Pressed += RemoveComponent;

		_deleteButton.Icon = GetThemeIcon("DeleteBin", "AtlasIcons");
	}
	
	public void SetComponent(OscComponent component, ConnectionInspector inspector)
	{
		_oscComponent = component;
		_nameLabel.Text = component.OscConnection.Name;
		_commandLineEdit.Text = component.OscMessage;
		_oscConnectionLabel.Text = $"{component.OscConnection.Address}:{component.OscConnection.Port}";
		_connectionInspector = inspector;
	}

	private void OnCommandEditing(bool editing)
	{
		if (editing) _commandEditing = true;
		else
		{
			_commandEditing = false;
			OnCommandTextSubmitted(_commandLineEdit.Text);
		}
	}

	private void OnCommandTextSubmitted(string text)
	{
		_commandEditing = false;
		_commandLineEdit.ReleaseFocus();
		_oscComponent.OscMessage = text;
	}

	private void RemoveComponent()
	{
		_connectionInspector.RemoveComponent(_oscComponent);
		QueueFree();
	}
	
}
