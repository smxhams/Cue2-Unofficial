using Godot;
using System;

namespace Cue2.UI.Scenes.Inspectors;
public partial class InspectorOscConnectionCard : PanelContainer
{
	private Label _nameLabel;
	private TextEdit _commandTextEdit;
	private Button _deleteButton;
	public override void _Ready()
	{
		
		
		_nameLabel = GetNode<Label>("%NameLabel");
		_commandTextEdit = GetNode<TextEdit>("%CommandTextEdit");
		_deleteButton = GetNode<Button>("%DeleteButton");
		
		
	}
	
}
