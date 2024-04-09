using Godot;
using System;

public partial class settings : Window
{
	private GlobalSignals _globalSignals;
	private Godot.Tree setTree;
	private String currentDisplay;
	//private Tree setTree;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		//Global Signals
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

		// Settings Tree
		setTree = GetNode<Godot.Tree>("MarginContainer/HSplitContainer/Container/ScrollContainer/Tree");
		TreeItem root = setTree.CreateItem();
		setTree.HideRoot = true;

		// Connections
		TreeItem tiConnections = setTree.CreateItem(root);
		tiConnections.SetText(0, "Connections");
		TreeItem tiAudioOutputs = setTree.CreateItem(tiConnections);
		tiAudioOutputs.SetText(0, "Audio Outputs");
		TreeItem tiAudioInputs = setTree.CreateItem(tiConnections);
		tiAudioInputs.SetText(0, "Audio Inputs");
		TreeItem tiVideoOutputs = setTree.CreateItem(tiConnections);
		tiVideoOutputs.SetText(0, "Video Outputs");
		TreeItem tiArtNet = setTree.CreateItem(tiConnections);
		tiArtNet.SetText(0, "Art-Net");

		TreeItem tiAudio = setTree.CreateItem(root);
		TreeItem tiAudio1 = setTree.CreateItem(root);
		TreeItem tiAudio2 = setTree.CreateItem(root);
		TreeItem tiAudio3 = setTree.CreateItem(root);
		TreeItem tiAudio4 = setTree.CreateItem(root);
		TreeItem tiAudio5 = setTree.CreateItem(root);
		TreeItem tiAudio6 = setTree.CreateItem(root);
		TreeItem tiAudio7 = setTree.CreateItem(root);
		TreeItem tiAudio8 = setTree.CreateItem(root);
		TreeItem tiAudio9 = setTree.CreateItem(root);
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
	private void _on_close_pressed(){
		_globalSignals.EmitSignal(nameof(GlobalSignals.CloseSettingsWindow));
	}

	private void _on_tree_item_selected(){
		GD.Print(setTree.GetSelected().GetText(0));
		if (currentDisplay == null){
			if (setTree.GetSelected().GetText(0) == "Audio Outputs"){
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
