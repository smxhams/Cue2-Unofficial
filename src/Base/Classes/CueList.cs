using System.Collections.Generic;
using System.Linq;
using Cue2.Base.Classes.Connections;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Godot;
using Godot.Collections;

// This script is attached to the cuelist in main UI
// Originator
namespace Cue2.Base.Classes;


public partial class CueList : ScrollContainer
{
	[Signal] public delegate void CueDragStartedEventHandler(int cueId);
	[Signal] public delegate void CueDragEndedEventHandler(int cueId, Vector2 dropPosition);
	[Signal] public delegate void CuesReorderedEventHandler();

	private GlobalData _globalData;
	private GlobalSignals _globalSignals;
	
	
	public List<Cue> Cuelist { get; private set; }
	public static System.Collections.Generic.Dictionary<int, Cue> CueIndex;
	
	public int ShellBeingDragged = -1;
	public static int ShellDraggedOver = -1;

	private VBoxContainer _cueContainer;
	
	private PackedScene _shellBarPackedScene = SceneLoader.LoadPackedScene("uid://d207a67e3ebww", out _);
	
	public CueList()
	{
		Cuelist = new List<Cue>();
		CueIndex = new System.Collections.Generic.Dictionary<int, Cue>();
		
	}

	private int _childTally = -1;

	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		_globalData.Cuelist = this;
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

		_cueContainer = GetNode<VBoxContainer>("%CueContainer");
		
		_globalSignals.CreateCue += CreateCue;
		
	}

	public Cue CreateCue(Dictionary data) // Create a cue from data
	{
		var newCue = new Cue(data);
		AddCue(newCue);
		return newCue;
	}
	public void CreateCue()
	{
		var newCue = new Cue(); // Create a cue with default values
		AddCue(newCue);
	}
	
	private void AddCue(Cue cue)
	{
		CreateNewShell(cue);
		
		Cuelist.Add(cue);
		CueIndex.Add(cue.Id, cue);
		// Will make new cues focused
		//FocusCue(cue); Readd select shell when finished
		
	}
	// This instantiates the shell scene which creates the UI elements to represent the cue in the scene
	private void CreateNewShell(Cue newCue)
	{
		var shellBar = _shellBarPackedScene.Instantiate<ShellBar>();
		var container = GetNode<VBoxContainer>("%CueContainer");
		container.AddChild(shellBar);
		shellBar.SetCue(newCue);
		newCue.ShellBar = shellBar; // Adds shellbar scene to the cue object.
		shellBar.Set("CueId", newCue.Id); // Sets shell_bar property CueId
	}
	
	public void RemoveCue(Cue cue)
	{
		cue.ShellBar.QueueFree();
		Cuelist.Remove(cue);
	}

	public static Cue FetchCueFromId(int id)
	{
		try
		{
			CueIndex.TryGetValue(id, out Cue cue);
			return cue;
		}
		catch (KeyNotFoundException)
		{
			return null;
		}

	}

	public void ReorderCue(int cueId, int newIndex)
	{
		var cue = FetchCueFromId(cueId);
		if (cue == null || cue.ParentId != -1) return; // Only reorder top-level cues for now

		// Remove from current position
		Cuelist.Remove(cue);

		// Insert at new index
		Cuelist.Insert(newIndex, cue);

		// Update UI positions
		UpdateShellPositions();

		EmitSignal(SignalName.CuesReordered);
	}

	private void UpdateShellPositions()
	{
		for (int i = 0; i < Cuelist.Count; i++)
		{
			var cue = Cuelist[i];
			if (cue.ParentId == -1) // Top-level
			{
				_cueContainer.MoveChild(cue.ShellBar, i);
			}
		}
	}
	

	public void ShellMouseOverByDraggedShellBottomHalf(int cueId)
	{
		GD.Print("Cue: " + ShellBeingDragged + " has moused over: " + cueId);
		var targetCue = CueIndex[cueId];
		var targetIndex = targetCue.ShellBar.GetIndex();
		var draggedIndex = CueIndex[ShellBeingDragged].ShellBar.GetIndex();
		//MoveCueWithItsChildren(CueIndex[ShellBeingDragged].Id, targetIndex);
		MoveCue(CueIndex[ShellBeingDragged].Id, targetIndex);
		
		CueIndex[ShellBeingDragged].ShellBar.GetNode<Container>("%OffSetWithLine").Visible = false;
		
		if (targetCue.ChildCues.Count > 0)
		{
			GD.Print("Has child");
			CueIndex[ShellBeingDragged].ShellBar.GetNode<Container>("%OffSetWithLine").Visible = true;
		}
	}
	
	public void ShellMouseOverByDraggedShellTopHalf(int cueId)
	{
		GD.Print("Cue: " + ShellBeingDragged + " has moused over: " + cueId);
		var targetIndex = CueIndex[cueId].ShellBar.GetIndex();
		//_cueContainer.MoveChild(CueIndex[ShellBeingDragged].ShellBar, targetIndex+1);
		MoveCue(CueIndex[ShellBeingDragged].Id, targetIndex);
		CueIndex[ShellBeingDragged].ShellBar.GetNode<Container>("%OffSetWithLine").Visible = true;
	}

	// Move cue and childen in cue container index
	private void MoveCue(int cueId, int targetIndex)
	{
		_childTally = 1;
		if (CueIndex[cueId].ChildCues.Any())
		{
			TallyChildren(cueId);
		}
		
		var cueIndex = CueIndex[cueId].ShellBar.GetIndex();
		var indexDif = (targetIndex - cueIndex);
		if (indexDif > 0) indexDif -= 1;
		for (int i = 0; i < _childTally; i++)
		{
			var shellBar = (ShellBar)_cueContainer.GetChild(cueIndex + i);
			if (indexDif > 0)
			{
				shellBar = (ShellBar)_cueContainer.GetChild(cueIndex);
			}
			var destinationIndex = shellBar.GetIndex() + indexDif + 1;
			_cueContainer.MoveChild(shellBar, destinationIndex);
		}
		//MoveChildren(cueId, targetIndex+1, startIndex);
	}

	private void MoveChildren(int cueId, int targetIndex, int startIndex)
	{
		for (int i = 0; i < CueIndex[cueId].ChildCues.Count(); i++)
		{
			var childToMove = _cueContainer.GetChild(startIndex+i);
			_cueContainer.MoveChild(childToMove, targetIndex + i + 1);
		}
		
	}

	private void TallyChildren(int cueId)
	// Recursive tally to get # of cues in a family tree. 
	{
		foreach (var cue in CueIndex[cueId].ChildCues)
		{
			_childTally += 1;
			if (CueIndex[cue].ChildCues.Any()) TallyChildren(cue);
		}
	}
	
	// Moves cues on X axis when being dragged
	public void MoveCueWithItsChildren(int cueId)
	{
		if (CueIndex[cueId].ChildCues.Any())
		{
			foreach (var childCueId in CueIndex[cueId].ChildCues)
			{
				ShellBar shell = (ShellBar)CueIndex[childCueId].ShellBar;
				shell.SetGlobalPosition(new Vector2(GetGlobalMousePosition().X + 5, shell.GetGlobalPosition().Y));
				MoveCueWithItsChildren(childCueId);
			}
		}

	}

	// If cue with children dragged, sets them back to correct Y position
	public void ResetCuePositionXWithChildren(int cueId)
	{
		if (CueIndex[cueId].ChildCues.Any())
		{
			foreach (var childCueId in CueIndex[cueId].ChildCues)
			{
				ShellBar shell = (ShellBar)CueIndex[childCueId].ShellBar;
				shell.SetPosition(new Vector2(0, shell.Position.Y));
				ResetCuePositionXWithChildren(childCueId);
			}
		}
	}

	public void AddCueToGroup(int childCueId, int parentCueId = -1)
	{
		var childCue = CueIndex[childCueId];
		if (parentCueId == -1)
		{
			parentCueId = childCue.ShellBar.GetIndex() - 1; // Cue above is parent
		}
		var parentShellBar = (ShellBar)_cueContainer.GetChildren()[parentCueId];
		var parentCue = CueIndex[parentShellBar.Get("CueId").AsInt32()];
		GD.Print(parentCue.Name);
		parentCue.AddChildCue(childCueId);
		childCue.SetParent(parentCue.Id);

		parentShellBar.GetNode<Container>("%Expanded").Visible = true;
		childCue.ShellBar.GetNode<Container>("%OffSetWithLine").Visible = true;
		ShellBar shellBar = (ShellBar)childCue.ShellBar;
		//shellBar.SetShellOffset(parentShellBar.ShellOffset + 1);
		if (childCue.ChildCues.Count != 0) CheckChildOffsets(childCueId);

	}

	private void CheckChildOffsets(int cueId)
	{
		GD.Print("Checking Child Offsets");
		var parent = CueIndex[cueId];
		foreach (var child in parent.ChildCues)
		{
			var childShellBar = (ShellBar)CueIndex[child].ShellBar;
			var parentShellBar = (ShellBar)parent.ShellBar;
			//childShellBar.SetShellOffset(parentShellBar.ShellOffset + 1);
			if (CueIndex[child].ChildCues.Count != 0) CheckChildOffsets(child);
		}
	}

	public void CheckCuesNewPosition(int cueId)
	{
		var cue = CueIndex[cueId];
		var shellAbove = (ShellBar)_cueContainer.GetChildren()[cue.ShellBar.GetIndex() - 1];
		var cueAbove = CueIndex[shellAbove.CueId];
		var shellBelow = (ShellBar)_cueContainer.GetChildren()[cue.ShellBar.GetIndex() + 1];
		var cueBelow = CueIndex[shellBelow.CueId];

		
		// Check if inserted in group - add to group
		if (cueAbove.ChildCues.Any() && shellAbove.GetNode<Container>("%Expanded").Visible == true)
		{
			AddCueToGroup(cueId, cueAbove.Id);
		}
		else if (cueBelow.ParentId != -1)
		{
			AddCueToGroup(cueId, cueBelow.ParentId);
		}
		
		// Check if being removed from group
		if (cue.ParentId == -1) return;
		if (cueAbove.Id != cue.ParentId || cueAbove.ParentId != cue.ParentId)
		{
			RemoveCueFromGroup(cue.Id);
		}
	}

	private void RemoveCueFromGroup(int cueId)
	{
		var childCue = CueIndex[cueId];
		var parentCue = CueIndex[childCue.ParentId];
		var parentShellBar = (ShellBar)parentCue.ShellBar;
		
		parentCue.RemoveChildCue(cueId);
		childCue.SetParent(-1);
		
		if (parentCue.ChildCues.Count == 0) parentShellBar.GetNode<Container>("%Expanded").Visible = true;
		childCue.ShellBar.GetNode<Container>("%OffSetWithLine").Visible = false;
		ShellBar shellBar = (ShellBar)childCue.ShellBar;
		//shellBar.SetShellOffset(0);
		if (childCue.ChildCues.Count != 0) CheckChildOffsets(cueId);
			
	}
	

	public void ExpandGroup(int cueId)
	{
		foreach (var child in CueIndex[cueId].ChildCues)
		{
			var shell = (ShellBar)CueIndex[child].ShellBar;
			shell.Visible = true;
			if (CueIndex[child].ChildCues.Count != 0 && shell.GetNode<Container>("%Expanded").Visible) ExpandGroup(child);

		}
	}
	
	public void CollapseGroup(int cueId)
	{
		foreach (var child in CueIndex[cueId].ChildCues)
		{
			var shell = (ShellBar)CueIndex[child].ShellBar;
			shell.Visible = false;
			if (CueIndex[child].ChildCues.Count != 0) CollapseGroup(child);

		}
		
	}


	



	public Godot.Collections.Dictionary<int, int> GetCueOrder()
	{
		var cueOrder = new Godot.Collections.Dictionary<int, int>();
		for (int i = 0; i < _cueContainer.GetChildren().Count; i++)
		{
			var cueId = _cueContainer.GetChild(i).Get("CueId");
			cueOrder.Add(i, (int)cueId);
		}

		return cueOrder;
	}
	
	
	// Save and load
	
	public void ResetCuelist()
	{
		var removalList = Cuelist;
		// Removes shellbars from ui
		foreach (ICue cue in removalList)
		{
			cue.ShellBar.Free();
		}
		// Resets 
		Cuelist = new List<Cue>();
		CueIndex = new System.Collections.Generic.Dictionary<int, Cue>();
		_globalData.ShellSelection.SelectedCues = new List<ICue>();
	}
	
	public Dictionary GetData()
	{
		var saveTable = new Dictionary();
		var cues = new Dictionary();
		var cueOrder = GetCueOrder();
		saveTable.Add("CueOrder", cueOrder);
		foreach (var cue1 in Cuelist)
		{
			var cue = (Cue)cue1;
			var cueData = cue.GetData();
			
			cues.Add(cue.Id, cueData);
		}
		saveTable.Add("Cues", cues);
		return saveTable;
	}

	public void LoadData(Dictionary cueData)
	{
		GD.Print($"CueList:LoadData - Loading Cues");

		if (cueData.TryGetValue("Cues", out var cues))
		{
			foreach (var cue in (Dictionary)cues)
			{
				var asDict = cue.Value.AsGodotDictionary();
				var cueDict = new Dictionary();
				foreach (var key in asDict.Keys)
				{
					var value = asDict[key];
					string keyStr = key.ToString();
						
					cueDict[keyStr] = value;
				} 
				Cue newCue = CreateCue(cueDict);
				
				
				// Link component reference objects
				var newCueAudioComponent = newCue.GetAudioComponent();
				if (newCueAudioComponent != null)
				{
					var patches = _globalData.Settings.GetAudioOutputPatches();
					patches.TryGetValue(newCueAudioComponent.PatchId, out var patch);
					if (patch != null)
					{
						newCueAudioComponent.Patch = patch;
					}
				}
				
				var newCueVideoComponent = newCue.GetVideoComponent();
				if (newCueVideoComponent != null)
				{
					var patches = _globalData.Settings.GetAudioOutputPatches();
					patches.TryGetValue(newCueVideoComponent.PatchId, out var patch);
					if (patch != null)
					{
						newCueVideoComponent.Patch = patch;
					}
				}

				var cueLightComps = newCue.GetCueLightComponents();
				if (cueLightComps != null)
				{
					foreach (var cueLightComp in cueLightComps)
					{
						var cuelight = _globalData.CueLightManager.GetCueLight(cueLightComp.CueLightId);
						cueLightComp.CueLight = cuelight;
					}
				}
				
				var oscComponents = newCue.GetOscComponents();
				if (oscComponents != null)
				{
					foreach (var oscComp in oscComponents)
					{
						var oscConnection = OscConnections.GetCueOscConnection(oscComp.OscConnectionId);
						oscComp.OscConnection = oscConnection;
					}
				}


			}
		}

		if (cueData.TryGetValue("CueOrder", out var order))
		{
			var cueOrder = new Godot.Collections.Dictionary<int, int>();
			foreach (var cue in (Godot.Collections.Dictionary)order)
			{
				cueOrder.Add((int)cue.Key, (int)cue.Value);
				//GD.Print(cue.Key + " <-order cue -> " + (int)cue.Value);
			}
			StructureCuelist(cueOrder);	
		}
	}
	
	private void StructureCuelist(Godot.Collections.Dictionary<int, int> cueOrder)
	{
		// Key is child order, value is cueId
		foreach (Cue cue in Cuelist)
		{
			// Assign child shellbars to parents
			if (cue.ParentId != -1)
			{
				GD.Print($"CueList:StructureCuelist - REPARENTING {cue.Name}");
				var parentShell = FetchCueFromId(cue.ParentId).ShellBar;
				cue.ShellBar.Reparent(parentShell.GetNode<VBoxContainer>("%ShellChildContainer"));
				//_cueContainer.RemoveChild(cue.ShellBar);
				//parentShell.GetNode<VBoxContainer>("%ShellChildContainer").AddChild(cue.ShellBar);
				//parentShell.AssignChild(cue.ShellBar);
			}
		}
		
		for (int i = 0; i < cueOrder.Count; i++)
		{
			var cue = CueIndex[cueOrder[i]];
			var shell = (ShellBar)cue.ShellBar;
			if (cue.ParentId != -1) continue;
			_cueContainer.CallDeferred("move_child", shell, i);
		}
	}

}

