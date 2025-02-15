using Godot;
using System;
using Cue2.Shared;

namespace Cue2.Base;
public partial class SettingsWindow : Window
{
	private GlobalSignals _globalSignals;
	private Godot.Tree _setTree;
	private String _currentDisplay = "";
	//private Tree setTree;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		//Global Signals
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		
		_generateTree();
	}


	private void _on_close_pressed(){
		_globalSignals.EmitSignal(nameof(GlobalSignals.CloseSettingsWindow));
	}

	// On tree item pressed display each settings menu.
	private void _on_tree_item_selected(){
		if (_currentDisplay != "")
		{
			GetNode<ScrollContainer>("MarginContainer/HSplitContainer/Panel/RightSide/" + _currentDisplay).Hide();
		}
		var menuNode = GetSelectedMenu(_setTree.GetSelected().GetText(0));
		GetNode<ScrollContainer>("MarginContainer/HSplitContainer/Panel/RightSide/" + menuNode).Show();
		_currentDisplay = menuNode;
		

		

	}

	private string GetSelectedMenu(string action) =>
		action switch // Name corresponded to node name in UI.
		{
			"Audio Devices" => "AudioDevices",
			"Video Devices" => "VideoDevices",
			_ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
		};
	

	private void _generateTree()
	{
		// Settings Tree
		_setTree = GetNode<Godot.Tree>("MarginContainer/HSplitContainer/Container/ScrollContainer/Tree");
		TreeItem root = _setTree.CreateItem();
		_setTree.HideRoot = true;
		
		// Output Devices
		TreeItem tiOutputDevices = _setTree.CreateItem(root);
		tiOutputDevices.SetText(0, "Output Devices");
		TreeItem tiAudioDevice = _setTree.CreateItem(tiOutputDevices);
		tiAudioDevice.SetText(0, "Audio Devices");
		TreeItem tiVideoDevice = _setTree.CreateItem(tiOutputDevices);
		tiVideoDevice.SetText(0, "Video Devices");
		
		// Routing
		TreeItem tiRoutes = _setTree.CreateItem(root);
		tiRoutes.SetText(0, "Routes");
		TreeItem tiAudioRoutes = _setTree.CreateItem(tiRoutes);
		tiAudioRoutes.SetText(0, "Audio Routes");
		TreeItem tiVideoRoutes = _setTree.CreateItem(tiRoutes);
		tiVideoRoutes.SetText(0, "Video Routes");
		
		// Connections
		TreeItem tiConnections = _setTree.CreateItem(root);
		tiConnections.SetText(0, "Connections");
		TreeItem tiOSCConnection = _setTree.CreateItem(tiConnections);
		tiOSCConnection.SetText(0, "OSC Connection");
		TreeItem tiNetworkConnection = _setTree.CreateItem(tiConnections);
		tiNetworkConnection.SetText(0, "Network Connection");
		TreeItem tiArtNet = _setTree.CreateItem(tiConnections);
		tiArtNet.SetText(0, "Art-Net");
		
		// Cue defaults
		TreeItem tiDefaults = _setTree.CreateItem(root);
		tiDefaults.SetText(0, "Defaults");
		tiDefaults.SetTooltipText(0, "Set default behaviors and paramters acroll shells and cues universaly.");
		TreeItem tiAudioCueDafaults = _setTree.CreateItem(tiDefaults);
		tiDefaults.SetText(0, "Audio Cues");
		tiDefaults.SetTooltipText(0, "Set defaults for audio cues.");
		TreeItem tiVideoCueDefaults = _setTree.CreateItem(tiDefaults);
		tiDefaults.SetText(0, "Defaults");
		tiDefaults.SetTooltipText(0, "Set defaults for video cues.");
		
	}
}
