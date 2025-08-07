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

		_newPatchButton = GetNode<Button>("%NewPatchButton");
		_newPatchButton.Pressed += NewPatchButtonPressed;
		
		DisplayPatchMatrix();
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
		
		// Below loads 'AudioOutputPatchMatrix' instanced scene.
		PackedScene patchMatrixScene = SceneLoader.LoadPackedScene("uid://dgy2bmmm4rjpt", out _);
		
		if (patchMatrixContainer.GetChildCount() > 0) {GD.Print("Has child, lets see if it finds a match"); }

		var childList = patchMatrixContainer.GetChildren();
		var alreadyExistingPatches = new List<int>(); // List of patch ids that already have a patch matrix inst
		
		// Clean existing patch instances.
		foreach (Node child in childList)
		{
			var id = child.Get("PatchId").AsInt16();
			if (!patches.ContainsKey(id))
			{
				GD.Print($"Removing patch matrix {child.Name} as it does not exist in settings patch list");
				child.QueueFree();
			}
			else
			{
				alreadyExistingPatches.Add(id);
				// TODO: Tell patch instance to check it's data
			}
		}
		
		// Each patch stored in settings patches.
		foreach (var patch in patches)
		{
			if (alreadyExistingPatches.Contains(patch.Key)) continue; // Already existing and checked (look up)
			
			GD.Print($"Creating patch matrix with id: {patch.Key} and name: {patch.Value.Name}");
			Node instance = patchMatrixScene.Instantiate();
			instance.Set("Patch", patch.Value);
			instance.Set("PatchId", patch.Key);
			patchMatrixContainer.AddChild(instance);
		}
		
	}
	
	
}