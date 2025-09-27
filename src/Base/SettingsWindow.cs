using Godot;
using System;
using System.Collections.Generic;
using Cue2.Shared;

namespace Cue2.Base;
public partial class SettingsWindow : Window
{
	private GlobalSignals _globalSignals;
	private GlobalData _globalData;
	private Godot.Tree _setTree;
	private String _currentDisplay = "";

	//private Tree setTree;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		//Global Signals
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		
		
		_scaleUI(_globalData.Settings.UiScale);
		GD.Print("UI Scale: " + _globalData.Settings.UiScale);
		
		GetNode<Button>("%SaveWithShow").Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.SettingsSaveUserDir), _getFilters());
		
		_generateTree();
		_connectSignals();
	}

	private void _connectSignals()
	{
		_globalSignals.UiScaleChanged += _scaleUI;
		GetNode<Button>("%SaveFilterOptionButton").Pressed += () =>
		{
			GetNode<PanelContainer>("%DropMenuFilter").Visible = true;
			GetNode<Button>("%SaveFilterOptionButton").Disabled = true;
		};
		GetNode<PanelContainer>("%DropMenuFilter").MouseExited += () =>
		{
			GetNode<PanelContainer>("%DropMenuFilter").Visible = false;
			GetNode<Button>("%SaveFilterOptionButton").Disabled = false;
		};
		
		TreeExiting += () => _globalSignals.UiScaleChanged -= _scaleUI; //TODO: This needs to be done to all signals that expect to be Freed.
	}

	private string _getFilters()
	{
		return "";
	}
	
	private void _scaleUI(float value)
	{
		GetWindow().WrapControls = true;
		GetWindow().ContentScaleFactor = value;
		GetWindow().ChildControlsChanged();
	}


	private void _on_close_pressed(){
		_globalSignals.EmitSignal(nameof(GlobalSignals.CloseSettingsWindow));
	}

	// On tree item pressed display each settings menu.
	private void _on_tree_item_selected(){
		if (_currentDisplay != "")
		{
			GetNode<ScrollContainer>("%" + _currentDisplay).Hide();
			
		}
		else
		{
			// Checks all settings displays incase one is already open
			foreach (var node in GetNode<MarginContainer>("%RightSide")
				         .GetChildren())
			{
				var child = (ScrollContainer)node;
				if (child.IsVisible()) child.Hide();
			}			
		}
		
		var menuNode = GetSelectedMenu(_setTree.GetSelected().GetText(0));
		GetNode<ScrollContainer>("%" + menuNode).Show();
		_currentDisplay = menuNode;
		

		

	}

	private string GetSelectedMenu(string action) =>
		action switch // Name corresponded to node name in UI.
		{
			"Audio Output Patch" => "AudioOutputPatch",
			"Canvas Editor" => "CanvasEditor",
			"General" => "SettingsGeneral",
			"Cue Lights" => "CueLights",
			_ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
		};
	

	private void _generateTree()
	{
		// Settings Tree
		_setTree = GetNode<Godot.Tree>("%SettingsTree");
		TreeItem root = _setTree.CreateItem();
		_setTree.HideRoot = true;
		
		//General
		TreeItem tiGeneral = _setTree.CreateItem(root);
		tiGeneral.SetText(0, "General");
		TreeItem tiInputMap = _setTree.CreateItem(tiGeneral);
		tiInputMap.SetText(0, "Input Map");
		
		// Audio
		TreeItem tiAudio = _setTree.CreateItem(root);
		tiAudio.SetText(0, "Audio");
		TreeItem tiAudioOutputPatch = _setTree.CreateItem(tiAudio);
		tiAudioOutputPatch.SetText(0, "Audio Output Patch");

		
		// Output Devices
		TreeItem tiOutputDevices = _setTree.CreateItem(root);
		tiOutputDevices.SetText(0, "Video/Image");
		TreeItem tiVideoDevice = _setTree.CreateItem(tiOutputDevices);
		tiVideoDevice.SetText(0, "Canvas Editor");
		
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
		TreeItem tiCueLights = _setTree.CreateItem(tiConnections);
		tiCueLights.SetText(0, "Cue Lights");
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
		tiAudioCueDafaults.SetText(0, "Audio Cues");
		tiAudioCueDafaults.SetTooltipText(0, "Set defaults for audio cues.");
		TreeItem tiVideoCueDefaults = _setTree.CreateItem(tiDefaults);
		tiAudioCueDafaults.SetText(0, "Defaults");
		tiAudioCueDafaults.SetTooltipText(0, "Set defaults for video cues.");
		
		
	}
}
