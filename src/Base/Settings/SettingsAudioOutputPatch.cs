using System.Collections.Generic;
using System.Linq;
using Cue2.Base.Classes;
using Cue2.Base.Classes.Devices;
using Cue2.Shared;
using Godot;
using LibVLCSharp.Shared;

namespace Cue2.Base.Settings;

public partial class SettingsAudioOutputPatch : ScrollContainer
{
	private GlobalData _globalData;

	private OptionButton _deviceOptionsDropMenu;
	private AudioOutputPatchMatrix _audioOutputPatchMatrix;

	private Label _deviceQuantityLabel;

	private Button _newPatchButton;
	
	
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		
		_deviceQuantityLabel = GetNode<Label>("%DeviceQuantityLabel");

		//_audioOutputPatchMatrix = GetNode<AudioOutputPatchMatrix>("%AudioOutputPatchMatrix");

		_newPatchButton = GetNode<Button>("%NewPatchButton");
		_newPatchButton.Pressed += NewPatchButtonPressed;
		
		
		DisplayPatchMatrix();
		VisibilityChanged += DisplayExistingDevices;
		VisibilityChanged += DisplayPatchMatrix;
		
		
		
	}

	private void NewPatchButtonPressed()
	{
		_globalData.Settings.CreateNewPatch();
		DisplayPatchMatrix();
	}

	private void DisplayPatchMatrix()
	{
		if (!Visible) return;
		// Get stored patch data from settings
		var patches = _globalData.Settings.GetAudioOutputPatches();

		VBoxContainer patchMatrixContainer = GetNode<VBoxContainer>("%PatchesVBoxContainer");
		PackedScene patchMatrixScene = GD.Load<PackedScene>("uid://dgy2bmmm4rjpt");
		if (patchMatrixContainer.GetChildCount() > 0) {GD.Print("Has child, lets see if it finds a match"); }

		var childList = patchMatrixContainer.GetChildren();
		
		// add patch scenes if in patch list
		foreach (var patch in patches)
		{
			bool matchedPatch = false;
			foreach (var child in childList)
			{

				if (child.Get("PatchId").AsInt16() == patch.Key)
				{
					GD.Print("Found existing patch matrix with id: " + patch.Key + " and name: " + patch.Value.Name);
					matchedPatch = true;
				}

			}
			if (!matchedPatch)
			{
				GD.Print("No displayed patch matrix with id: " + patch.Key + " and name: " + patch.Value.Name);
				Node instance = patchMatrixScene.Instantiate();
				instance.Set("Patch", patch.Value);
				instance.Set("PatchId", patch.Key);
				patchMatrixContainer.AddChild(instance);
			}
			
		}
		
		// Removes patch scenes if not in patch list
		foreach (var child in childList)
		{
			bool matchChild = false;
			foreach (var patch in patches)
			{
				if (child.Get("PatchId").AsInt16() == patch.Key)
				{
					GD.Print("Found existing patch matrix with id: " + patch.Key + " and name: " + patch.Value.Name);
					matchChild = true;
				}
			}
			if (!matchChild)
			{
				GD.Print("Invoked");
				child.QueueFree();
			}
		}
	}
	
	
	private void DisplayExistingDevices()
	{
		var visible = Visible;
		if (!Visible) return;  


		List<AudioDevice> deviceList = _globalData.Devices.GetAudioDevices();
		if (deviceList == null || !deviceList.Any())
		{
			_deviceQuantityLabel.Text = "There are currently no audio devices";
			return;
		}
		
		_deviceQuantityLabel.Text = "There are " + deviceList.Count + " devices in use";



	}
	
}