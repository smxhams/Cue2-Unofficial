using Godot;
using System;

namespace Cue2.Base;
public partial class SettingsWindow : Window
{
	private GlobalSignals _globalSignals;
	private Godot.Tree _setTree;
	private String _currentDisplay;
	//private Tree setTree;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		//Global Signals
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

		// Settings Tree
		_setTree = GetNode<Godot.Tree>("MarginContainer/HSplitContainer/Container/ScrollContainer/Tree");
		TreeItem root = _setTree.CreateItem();
		_setTree.HideRoot = true;

		// Connections
		TreeItem tiConnections = _setTree.CreateItem(root);
		tiConnections.SetText(0, "Connections");
		TreeItem tiAudioOutputs = _setTree.CreateItem(tiConnections);
		tiAudioOutputs.SetText(0, "Audio Outputs");
		TreeItem tiAudioInputs = _setTree.CreateItem(tiConnections);
		tiAudioInputs.SetText(0, "Audio Inputs");
		TreeItem tiVideoOutputs = _setTree.CreateItem(tiConnections);
		tiVideoOutputs.SetText(0, "Video Outputs");
		TreeItem tiArtNet = _setTree.CreateItem(tiConnections);
		tiArtNet.SetText(0, "Art-Net");

		TreeItem tiAudio = _setTree.CreateItem(root);
		TreeItem tiAudio1 = _setTree.CreateItem(root);
		TreeItem tiAudio2 = _setTree.CreateItem(root);
		TreeItem tiAudio3 = _setTree.CreateItem(root);
		TreeItem tiAudio4 = _setTree.CreateItem(root);
		TreeItem tiAudio5 = _setTree.CreateItem(root);
		TreeItem tiAudio6 = _setTree.CreateItem(root);
		TreeItem tiAudio7 = _setTree.CreateItem(root);
		TreeItem tiAudio8 = _setTree.CreateItem(root);
		TreeItem tiAudio9 = _setTree.CreateItem(root);
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
	private void _on_close_pressed(){
		_globalSignals.EmitSignal(nameof(GlobalSignals.CloseSettingsWindow));
	}

	private void _on_tree_item_selected(){
		GD.Print(_setTree.GetSelected().GetText(0));
		if (_currentDisplay == null){
			if (_setTree.GetSelected().GetText(0) == "Audio Outputs"){
				GetNode<ScrollContainer>("MarginContainer/HSplitContainer/RightSide/AudioDevices").Show();
				_checkAudioOutputDevices();
			}
		}
	}

	private void _checkAudioOutputDevices(){
		var audioOutputOptions = GetNode<OptionButton>("MarginContainer/HSplitContainer/RightSide/AudioDevices/VBoxContainer/VBoxContainer/AudioOutputOptions");
		audioOutputOptions.Clear();
		foreach (Object device in AudioServer.GetOutputDeviceList()){
			audioOutputOptions.AddItem(device.ToString());
		};
	}
}
