using System.Collections.Generic;
using Cue2.Shared;
using Godot;

namespace Cue2.UI.Scenes.Settings;

public partial class SettingsAudioOutputPatch : ScrollContainer
{
	private GlobalData _globalData;

	private OptionButton _deviceOptionsDropMenu;

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

	public override void _ExitTree()
	{
		VisibilityChanged -= DisplayPatchMatrix;

		// Explicitly free any generated patch matrix UI nodes (the objects created
		// by instantiating AudioOutputPatchMatrix scenes and their dynamic children).
		// This ensures no orphaned nodes when the audio patch settings UI is torn down
		// (e.g. on quit, even if parent removal order is unusual).
		var patchMatrixContainer = GetNodeOrNull<VBoxContainer>("%PatchesVBoxContainer");
		if (patchMatrixContainer != null && IsInstanceValid(patchMatrixContainer))
		{
			foreach (Node child in patchMatrixContainer.GetChildren())
			{
				if (IsInstanceValid(child))
					child.QueueFree();
			}
		}
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
		
		if (patchMatrixContainer.GetChildCount() > 0) { GD.Print("SettingsAudioOutputPatch:DisplayPatchMatrix - Has child, let's see if it finds a match"); }

		var childList = patchMatrixContainer.GetChildren();
		var alreadyExistingPatches = new List<int>(); // List of patch ids that already have a patch matrix inst
		
		// Clean existing patch instances.
		foreach (Node child in childList)
		{
			var id = child.Get("PatchId").AsInt32();
			if (!patches.ContainsKey(id))
			{
				GD.Print($"Removing patch matrix {child.Name} as it does not exist in settings patch list");
				child.QueueFree();
			}
			else
			{
				alreadyExistingPatches.Add(id);
				// TODO: Tell patch instance to check its data and refresh if needed
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