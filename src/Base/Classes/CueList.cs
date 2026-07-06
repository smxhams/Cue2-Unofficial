using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Godot;
using Godot.Collections;

using Cue2.Base.Classes.Connections;
using Cue2.Base.Classes.CueTypes;
using Cue2.Base.Minor;
using Cue2.Shared;

// This script is attached to the cuelist in main UI
// Originator
namespace Cue2.Base.Classes;

/// <summary>
/// Manages the main cue list UI, including creation, removal, drag-and-drop reordering
/// (with support for nesting/grouping), and save/load of cue hierarchy and order.
/// </summary>
/// <remarks>
/// Follows project MVVM-like separation: UI shells in ShellBar, data in Cue objects,
/// shared state via GlobalData/GlobalSignals. Reordering uses custom mouse tracking
/// (not native Godot drag/drop) to support above/below/into-child zones for multi-selection.
/// </remarks>
public partial class CueList : Control
{
	internal GlobalData _globalData;
	internal GlobalSignals _globalSignals;
	
	
	/// <summary>
	/// Global lookup of all cues by ID. Populated on creation and used for fast access.
	/// </summary>
	public static System.Collections.Generic.Dictionary<int, Cue> CueIndex; // <CueId, Cue>
	
	// Reordering is handled by a dedicated controller (see CueReorder.cs).
	private CueReorder _reorderController;

	// Reorder constants (avoid magic numbers; actual shell min size ~26 in ShellBar.tscn)
	private const int ShellHeight = 26;
	private const int ShellMarginDiv = 4;

	
	private PackedScene _shellBarPackedScene = SceneLoader.LoadPackedScene("uid://d207a67e3ebww", out _);

	// Ui
	internal VBoxContainer _cueContainer;
	private Button _addCueButton;
	private Button _expandAllButton;

	private Control _reorderCueControl;
	private Label _reorderLocationLabel;
	private VBoxContainer _reorderListContainer;
	private Panel _reorderIndicatorPanel;

	// Expand/collapse all state
	private bool _allExpanded = false;
	
	public CueList()
	{
		CueIndex = new System.Collections.Generic.Dictionary<int, Cue>();
	}

	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		_globalData.Cuelist = this;
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

		// Ui
		_cueContainer = GetNode<VBoxContainer>("%CueContainer");
		_addCueButton = GetNode<Button>("%AddCueButton");
		_expandAllButton = GetNode<Button>("%ExpandAllButton");
		
		_reorderCueControl = GetNode<Control>("%ReorderCueControl");
		_reorderLocationLabel = GetNode<Label>("%ReorderLocationLabel");
		_reorderListContainer = GetNode<VBoxContainer>("%ReorderListContainer");
		_reorderIndicatorPanel = GetNode<Panel>("%ReorderIndicatorPanel");

		_addCueButton.Icon = GetThemeIcon("PlusCircled", "AtlasIcons");
		_expandAllButton.Icon = GetThemeIcon("Right", "AtlasIcons");

		_reorderCueControl.Visible = false;

		_syncHotkeys();

		_reorderController = new CueReorder(this, _reorderCueControl, _reorderLocationLabel, _reorderListContainer, _reorderIndicatorPanel, _cueContainer);

		_globalSignals.CreateCue += CreateCue;
		_globalSignals.GroupSelectedCues += GroupSelectedCues;
		_globalSignals.CuelistExpandOneLayer += ExpandOneLayer;
		_globalSignals.CuelistCollapseOneLayer += CollapseOneLayer;
		_globalSignals.ToggleExpandAll += OnExpandAllPressed;
		_addCueButton.Pressed += CreateCue;
		_expandAllButton.Pressed += OnExpandAllPressed;
	}
	
	/// <summary>
	/// Creates a new cue from the provided data dictionary and adds it to the list.
	/// </summary>
	/// <param name="data">Dictionary of cue properties (from save or defaults).</param>
	/// <returns>The newly created and added Cue.</returns>
	public Cue CreateCue(Dictionary data) // Create a cue from data
	{
		var newCue = new Cue(data);
		AddCue(newCue);
		return newCue;
	}

	/// <summary>
	/// Creates a default new cue and adds it to the cuelist (wired to Add button / signal).
	/// </summary>
	public void CreateCue()
	{
		var newCue = new Cue(); // Create a cue with default values
		AddCue(newCue);

	}

	private void _syncHotkeys()
	{
		string createHotkey = GlobalData.ParseHotkey("CreateCue");
		string expandHotkey = GlobalData.ParseHotkey("ToggleExpandAll");

		string createTip = "Add a new cue.\nInserts below selection.";
		if (!string.IsNullOrEmpty(createHotkey))
			createTip += "\nHotkey: " + createHotkey;
		_addCueButton.TooltipText = createTip;

		string expandTip = "Expand/Collapse all groups.";
		if (!string.IsNullOrEmpty(expandHotkey))
			expandTip += "\nHotkey: " + expandHotkey;
		_expandAllButton.TooltipText = expandTip;
	}

	/// <summary>
	/// Groups the currently selected cues under a newly created group cue.
	/// If no cues are selected, emits an error log.
	/// The new group is inserted at the position of the first selected cue (preserving its nesting level),
	/// and all selected cues are moved to become direct children of the group.
	/// </summary>
	public void GroupSelectedCues()
	{
		var selected = ShellSelection.SelectedCues?.ToList() ?? new List<Cue>();
		if (selected.Count == 0)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
				"CueList:GroupSelectedCues - No cues selected. Select one or more cues then press the Group shortcut.", (int)LogType.Error);
			return;
		}

		// Work on a snapshot to avoid issues if selection changes during the operation
		var toGroup = selected;

		// Determine insertion point from the first selected cue's current location.
		// The group will be inserted as a sibling at the same level as the anchor.
		Cue anchor = toGroup[0];
		VBoxContainer targetContainer = _cueContainer;
		int insertIndex = _cueContainer.GetChildCount();
		int newGroupParentId = -1;

		if (anchor.ShellBar != null)
		{
			var currentParentNode = anchor.ShellBar.GetParent();
			if (currentParentNode is VBoxContainer vc)
			{
				targetContainer = vc;
				insertIndex = anchor.ShellBar.GetIndex();
			}

			if (anchor.ParentId != -1)
			{
				newGroupParentId = anchor.ParentId;
			}
		}

		// Create the wrapping group cue
		var groupCue = new Cue();
		groupCue.Name = $"Group ({toGroup.Count} cues)";
		groupCue.CueNum = groupCue.Id.ToString();

		// Insert the group shell at the anchor's former position (sibling level)
		var groupShellBar = CreateShellAndInsert(groupCue, targetContainer, insertIndex, newGroupParentId);
		groupCue.Expanded = true;

		var groupChildContainer = groupCue.ShellBar?.ShellChildContainer ?? _cueContainer;

		var oldParentsToRefresh = new HashSet<Cue>();

		// Only move the "top level" of the selection (cues whose direct parent is not also selected).
		// Their descendants (if any were selected) will travel with them because child ShellBars live inside the parent's ShellChildContainer.
		var selectedIds = new HashSet<int>(toGroup.Select(c => c.Id));
		var topLevelToMove = toGroup
			.Where(c => c.ParentId == -1 || !selectedIds.Contains(c.ParentId))
			.ToList();

		// Detach the top-level selected shells and reparent them (with their subtrees) under the group
		foreach (var cue in topLevelToMove)
		{
			if (cue?.ShellBar == null) continue;

			// Record old parent for later UI refresh
			if (cue.ParentId != -1)
			{
				var oldParent = FetchCueFromId(cue.ParentId);
				if (oldParent != null)
				{
					oldParent.ChildCues.Remove(cue.Id);
					oldParentsToRefresh.Add(oldParent);
				}
			}

			// Remove the shell (and any contained child shells) from its current location
			var currentParent = cue.ShellBar.GetParent();
			currentParent?.RemoveChild(cue.ShellBar);

			// Place inside the new group's child area
			groupChildContainer.AddChild(cue.ShellBar);

			// Update data model for this subtree root
			cue.ParentId = groupCue.Id;
			if (!groupCue.ChildCues.Contains(cue.Id))
			{
				groupCue.ChildCues.Add(cue.Id);
			}
		}

		// Refresh relationship UI on former parents (they may have lost children)
		foreach (var oldP in oldParentsToRefresh)
		{
			oldP.ShellBar?.RelationshipChanged();
		}

		// Update the new group
		groupCue.ShellBar?.RelationshipChanged();

		// Select the group cue (replacing the previous multi-selection)
		_globalData.ShellSelection.SelectIndividualShell(groupCue);

		// Recalculate durations (children first conceptually, then group)
		groupCue.CalculateTotalDuration();

		_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
			$"CueList:GroupSelectedCues - Created group containing {toGroup.Count} cue(s).", (int)LogType.Info);

		GD.Print($"CueList:GroupSelectedCues - Group cue {groupCue.Id} now contains {groupCue.ChildCues.Count} children.");
	}

	/// <summary>
	/// Creates one or more cues from dropped media files and inserts them according to the given parameters.
	/// This is the main entry point for file drag-and-drop cue creation.
	/// Supports single/multiple files, audio/video/images, group wrapping, and precise insert positions.
	/// </summary>
	/// <param name="files">Full paths to valid media files.</param>
	/// <param name="targetCueId">If dropping relative to a specific cue/shell, its ID; otherwise -1.</param>
	/// <param name="insertMode">Desired position relative to target (ignored or treated as AtEnd if no target).</param>
	/// <param name="asGroup">When true and multiple files, wrap the created cues inside a new parent group cue.</param>
	public void CreateCuesFromDroppedFiles(string[] files, int targetCueId, DropInsertMode insertMode, bool asGroup)
	{
		if (files == null || files.Length == 0) return;

		var mediaEngine = GetNodeOrNull<MediaEngine>("/root/MediaEngine");
		var newCues = new List<Cue>();
		Cue groupCue = null;

		// Determine insertion base location once
		var (targetContainer, baseInsertIndex, parentIdForNew) = ResolveInsertLocation(targetCueId, insertMode);

		if (asGroup && files.Length > 1)
		{
			// Create a wrapper group cue first
			groupCue = new Cue();
			groupCue.Name = $"Group ({files.Length} files)";
			groupCue.CueNum = groupCue.Id.ToString();

			var groupShell = CreateShellAndInsert(groupCue, targetContainer, baseInsertIndex, parentIdForNew);
			newCues.Add(groupCue);

			// Subsequent children go into the group's child container, at end of it
			targetContainer = groupCue.ShellBar?.ShellChildContainer ?? targetContainer;
			baseInsertIndex = targetContainer.GetChildCount(); // append children
			parentIdForNew = groupCue.Id;
			// Expand the new group
			groupCue.Expanded = true;
		}

		int currentIndex = baseInsertIndex;

		foreach (string filePath in files)
		{
			if (!File.Exists(filePath)) continue;

			var cue = new Cue();
			string baseName = Path.GetFileNameWithoutExtension(filePath);
			cue.Name = string.IsNullOrWhiteSpace(baseName) ? $"Cue {cue.Id}" : baseName;
			cue.CueNum = cue.Id.ToString();

			// Add the appropriate component
			string ext = Path.GetExtension(filePath).ToLowerInvariant();
			bool isAudio = GlobalData.AudioFileFilters.Any(e => e.TrimStart('*').Equals(ext, StringComparison.OrdinalIgnoreCase));
			bool isVideoOrImage = GlobalData.VideoFileFilters.Any(e => e.TrimStart('*').Equals(ext, StringComparison.OrdinalIgnoreCase)) ||
			                       GlobalData.ImageFileFilters.Any(e => e.TrimStart('*').Equals(ext, StringComparison.OrdinalIgnoreCase));

			if (isAudio)
			{
				cue.AddAudioComponent(filePath);
			}
			else if (isVideoOrImage)
			{
				var vcomp = cue.AddVideoComponent(filePath);
				// Video may contain audio - we will discover on metadata
			}
			else
			{
				continue; // should have been filtered
			}

			// For children inside a just-created group, always append to keep order simple
			int useIndex = (groupCue != null && parentIdForNew == groupCue.Id)
				? targetContainer.GetChildCount()
				: currentIndex;

			var shell = CreateShellAndInsert(cue, targetContainer, useIndex, parentIdForNew);

			// Advance only for non-group-child sequential inserts
			if (!(groupCue != null && parentIdForNew == groupCue.Id))
			{
				currentIndex = targetContainer.GetChildCount();
			}

			newCues.Add(cue);

			// Kick off async metadata + waveform (fire and forget with logging)
			_ = ApplyMetadataToNewCueAsync(cue, filePath, mediaEngine);
		}

		if (newCues.Count == 0)
		{
			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log), "CueList:CreateCuesFromDroppedFiles - No cues were created from the provided files.", (int)LogType.Warning);
			return;
		}

		// Select the first newly created cue (or the group if we made one)
		var cueToFocus = groupCue ?? newCues.FirstOrDefault();
		if (cueToFocus != null)
		{
			_globalData?.ShellSelection?.SelectIndividualShell(cueToFocus);
			_globalSignals?.EmitSignal(nameof(GlobalSignals.ShellFocused), cueToFocus.Id);
		}

		// Recalculate durations for affected area (simple: recalc the new ones + parents)
		foreach (var c in newCues)
		{
			c.CalculateTotalDuration();
		}
		if (groupCue != null) groupCue.CalculateTotalDuration();

		_globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
			$"CueList: Created {newCues.Count} cue(s) from drop" + (groupCue != null ? " (grouped)" : ""), (int)LogType.Info);

		GD.Print($"CueList:CreateCuesFromDroppedFiles - Created {newCues.Count} cue(s).");
	}

	/// <summary>
	/// Resolves the target UI container, insert index, and logical parent ID for a drop insertion.
	/// </summary>
	private (VBoxContainer container, int index, int parentId) ResolveInsertLocation(int targetCueId, DropInsertMode mode)
	{
		if (targetCueId < 0 || mode == DropInsertMode.AtEnd)
		{
			// End of top level
			return (_cueContainer, _cueContainer.GetChildCount(), -1);
		}

		var targetCue = FetchCueFromId(targetCueId);
		if (targetCue == null || targetCue.ShellBar == null)
		{
			return (_cueContainer, _cueContainer.GetChildCount(), -1);
		}

		var targetShell = targetCue.ShellBar;

		switch (mode)
		{
			case DropInsertMode.Above:
				if (targetCue.ParentId != -1)
				{
					var p = FetchCueFromId(targetCue.ParentId);
					var cont = p?.ShellBar?.ShellChildContainer ?? _cueContainer;
					int idx = targetShell.GetIndex();
					return (cont, idx, targetCue.ParentId);
				}
				else
				{
					int idx = targetShell.GetIndex();
					return (_cueContainer, idx, -1);
				}

			case DropInsertMode.Below:
				if (targetCue.ParentId != -1)
				{
					var p = FetchCueFromId(targetCue.ParentId);
					var cont = p?.ShellBar?.ShellChildContainer ?? _cueContainer;
					int idx = targetShell.GetIndex() + 1;
					return (cont, idx, targetCue.ParentId);
				}
				else
				{
					int idx = targetShell.GetIndex() + 1;
					return (_cueContainer, idx, -1);
				}

			case DropInsertMode.AsChild:
				var childCont = targetShell.ShellChildContainer ?? _cueContainer;
				return (childCont, childCont.GetChildCount(), targetCue.Id);

			default:
				return (_cueContainer, _cueContainer.GetChildCount(), -1);
		}
	}

	/// <summary>
	/// Creates the ShellBar UI, wires it, inserts it into the given container at index, updates data model.
	/// </summary>
	private ShellBar CreateShellAndInsert(Cue cue, VBoxContainer container, int insertIndex, int parentId)
	{
		var shellBar = _shellBarPackedScene.Instantiate<ShellBar>();

		int countBefore = container.GetChildCount();
		container.AddChild(shellBar);

		int desired = insertIndex;
		if (desired < 0 || desired > countBefore)
			desired = countBefore;

		container.MoveChild(shellBar, desired);

		shellBar.MouseEntered += () => OnMouseEntered(shellBar);
		shellBar.SetCue(cue);
		cue.ShellBar = shellBar;
		shellBar.Set("CueId", cue.Id);

		if (!CueIndex.ContainsKey(cue.Id))
			CueIndex.Add(cue.Id, cue);
		else
			CueIndex[cue.Id] = cue;

		cue.ParentId = parentId;
		if (parentId != -1)
		{
			var parent = FetchCueFromId(parentId);
			if (parent != null && !parent.ChildCues.Contains(cue.Id))
			{
				parent.ChildCues.Add(cue.Id);
				bool wasEmpty = parent.ChildCues.Count == 1;
				if (wasEmpty) parent.Expanded = true;
				parent.ShellBar?.RelationshipChanged();
			}
		}

		return shellBar;
	}

	/// <summary>
	/// Asynchronously fetches metadata (and waveform for audio), attaches it to the component, and updates cue duration.
	/// Safe to fire-and-forget.
	/// </summary>
	private async Task ApplyMetadataToNewCueAsync(Cue cue, string filePath, MediaEngine mediaEngine)
	{
		if (cue == null || string.IsNullOrEmpty(filePath) || mediaEngine == null) return;

		try
		{
			var audioComp = cue.GetAudioComponent();
			var videoComp = cue.GetVideoComponent();

			if (audioComp != null)
			{
				var meta = await mediaEngine.GetAudioFileMetadataAsync(filePath);
				audioComp.Metadata = meta;
				audioComp.RecalculateDuration();

				// Optional waveform (best effort)
				try
				{
					audioComp.WaveformData = await mediaEngine.GenerateWaveformAsync(filePath);
				}
				catch { /* non-fatal */ }
			}
			else if (videoComp != null)
			{
				var meta = await mediaEngine.GetVideoFileMetadataAsync(filePath);
				videoComp.Metadata = meta;
				videoComp.HasAudio = meta.AudioChannels > 0;
				videoComp.UseAudio = videoComp.HasAudio;
				videoComp.ScaledWidth = meta.Width;
				videoComp.ScaledHeight = meta.Height;

				// For video we don't auto-gen full waveform here (inspector does when opened)
			}

			cue.CalculateTotalDuration();

			// Notify UI that a shell may need refresh (duration etc.)
			_globalSignals?.EmitSignal(nameof(GlobalSignals.UpdateShellBar), cue.Id);
		}
		catch (Exception ex)
		{
			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
				$"CueList: Failed to load metadata for dropped file '{Path.GetFileName(filePath)}': {ex.Message}", (int)LogType.Warning);
		}
	}

	private void AddCue(Cue cue)
	{
		CreateNewShell(cue);
		CueIndex.Add(cue.Id, cue);
		// Will make new cues focused
		//FocusCue(cue); //Read select shell when finished

	}
	// This instantiates the shell scene which creates the UI elements to represent the cue in the scene
	private void CreateNewShell(Cue newCue)
	{
		var shellBar = _shellBarPackedScene.Instantiate<ShellBar>();
		if (ShellSelection.SelectedCues.Count == 0) // No selection, add cue at end of cuelist
		{
			_cueContainer.AddChild(shellBar);
		}
		else
		{
			var selectedCue = ShellSelection.SelectedCues.Last();
			if (selectedCue.ParentId == -1) // Cue selected in main cuelist, add after
			{
				var newIndex = selectedCue.ShellBar.GetIndex() + 1;
				_cueContainer.AddChild(shellBar);
				_cueContainer.MoveChild(shellBar, newIndex);
			}
			else // Selected cue has parent, add as child of that parent
			{
				var parent = FetchCueFromId(selectedCue.ParentId);
				var newIndex = selectedCue.ShellBar.GetIndex() + 1;
				parent.ShellBar.ShellChildContainer.AddChild(shellBar);
				parent.ShellBar.ShellChildContainer.MoveChild(shellBar, newIndex);
				newCue.ParentId = selectedCue.ParentId;
				bool wasNewParent = parent.ChildCues.Count == 0;
				parent.ChildCues.Add(newCue.Id);
				if (wasNewParent)
				{
					parent.Expanded = true;
				}
				parent.ShellBar.RelationshipChanged();
			}
		}

		shellBar.MouseEntered += () => OnMouseEntered(shellBar);
		shellBar.SetCue(newCue);
		newCue.ShellBar = shellBar; // Adds shellbar scene to the cue object.
		shellBar.Set("CueId", newCue.Id); // Sets shell_bar property CueId
	}
	
	/// <summary>
	/// Removes the cue from the index and queues its ShellBar for deletion.
	/// Prunes from parent's ChildCues (if any) and refreshes the parent's collapse/expand UI.
	/// </summary>
	/// <param name="cue">The cue to remove.</param>
	public void RemoveCue(Cue cue)
	{
		if (cue.ParentId != -1)
		{
			var p = FetchCueFromId(cue.ParentId);
			if (p != null)
			{
				p.ChildCues.Remove(cue.Id);
				p.ShellBar?.RelationshipChanged();
			}
		}
		cue.ShellBar?.QueueFree();
		CueIndex.Remove(cue.Id);
	}

	/// <summary>
	/// Retrieves a Cue by its ID using the static index.
	/// </summary>
	/// <param name="id">The unique cue identifier.</param>
	/// <returns>The Cue if found; otherwise null.</returns>
	public static Cue FetchCueFromId(int id)
	{
		if (CueIndex == null) return null;
		CueIndex.TryGetValue(id, out Cue cue);
		return cue;
	}
	

	private void OnMouseEntered(ShellBar shellbar)
	{
		if (_reorderController != null && _reorderController.IsActive)
		{
			_reorderController.SetMouseOver(shellbar);
		}
	}
	
	//==========================//
	//--- Cuelist reordering ---//
	//==========================//

	/// <summary>
	/// Begins a drag-reorder operation for the given shell (and its current multi-selection).
	/// Shows the floating reorder preview and enables mouse tracking in _Input.
	/// </summary>
	/// <param name="shellbar">The ShellBar that initiated the drag (via its drag button).</param>
	public void StartReorder(ShellBar shellbar)
	{
		_reorderController.Start(shellbar);
	}
	
	public override void _Input(InputEvent @event)
	{
		_reorderController?.ProcessInput(@event);
	}

	private void EndReorder()
	{
		_reorderController?.Commit();
	}

	/// <summary>
	/// Common cleanup for ending or cancelling a reorder drag.
	/// </summary>
	private void CleanupReorder(bool keepChanges)
	{
		_reorderController?.Cancel();
	}

	/// <summary>
	/// Returns the visually last (bottom-most) ShellBar in the current list,
	/// walking into expanded child containers. Used to detect "blank space below everything".
	/// </summary>
	internal ShellBar GetLastVisibleShellBar()
	{
		return FindLastShell(_cueContainer);
	}

	internal void EmitLog(string message, int type)
	{
		_globalSignals?.EmitSignal(nameof(GlobalSignals.Log), message, type);
	}

	internal void SelectIndividualForReorder(int cueId)
	{
		var cue = FetchCueFromId(cueId);
		if (cue != null)
			_globalData.ShellSelection.SelectIndividualShell(cue);
	}

	private ShellBar FindLastShell(VBoxContainer container)
	{
		ShellBar last = null;
		foreach (var child in container.GetChildren())
		{
			if (child is ShellBar sb)
			{
				last = sb;
				var subContainer = sb.GetNodeOrNull<VBoxContainer>("%ShellChildContainer");
				if (subContainer != null && subContainer.GetChildCount() > 0 && subContainer.Visible)
				{
					var subLast = FindLastShell(subContainer);
					if (subLast != null)
						last = subLast;
				}
			}
		}
		return last;
	}

	/// <summary>
	/// Handler for the Expand/Collapse All button. Toggles the expanded state of all groups
	/// (recursively) and updates the button icon.
	/// </summary>
	private void OnExpandAllPressed()
	{
		_allExpanded = !_allExpanded;
		SetAllExpanded(_allExpanded);
		_expandAllButton.Icon = GetThemeIcon(_allExpanded ? "Down" : "Right", "AtlasIcons");
	}

	private void SetAllExpanded(bool expanded)
	{
		SetExpandedRecursive(_cueContainer, expanded);
	}

	private void SetExpandedRecursive(VBoxContainer container, bool expanded)
	{
		foreach (var child in container.GetChildren())
		{
			if (child is ShellBar shellBar)
			{
				shellBar.SetExpanded(expanded);
				var childCont = shellBar.ShellChildContainer;
				if (childCont != null)
				{
					SetExpandedRecursive(childCont, expanded);
				}
			}
		}
	}

	/// <summary>
	/// Expands only one layer of nesting (top-level groups only, does not recurse into subgroups).
	/// </summary>
	public void ExpandOneLayer()
	{
		SetExpandedOneLayer(true);
	}

	/// <summary>
	/// Collapses only one layer of nesting (top-level groups only).
	/// </summary>
	public void CollapseOneLayer()
	{
		SetExpandedOneLayer(false);
	}

	private void SetExpandedOneLayer(bool expanded)
	{
		foreach (var child in _cueContainer.GetChildren())
		{
			if (child is ShellBar shellBar)
			{
				shellBar.SetExpanded(expanded);
				// Intentionally do not recurse into child containers - this is "one layer"
			}
		}
	}

	//--- Save and load ---//
	
	public void ResetCuelist()
	{
		// Removes shellbars from ui
		foreach (var cue in CueIndex)
		{
			cue.Value.ShellBar?.QueueFree();
		}
		// Resets 
		CueIndex = new System.Collections.Generic.Dictionary<int, Cue>();
		ShellSelection.SelectedCues = new List<Cue>();
	}
	
	/// <summary>
	/// Serializes the entire cuelist (cues + top-level order) for session save.
	/// </summary>
	/// <returns>Dictionary containing "Cues" and "CueOrder".</returns>
	public Dictionary GetData()
	{
		var saveTable = new Dictionary();
		var cues = new Dictionary();
		var cueOrder = GetCueOrder();
		saveTable.Add("CueOrder", cueOrder);
		foreach (var cue in CueIndex.Values)
		{
			var cueData = cue.GetData();
			
			cues.Add(cue.Id, cueData);
		}
		saveTable.Add("Cues", cues);
		return saveTable;
	}

	/// <summary>
	/// Returns a position-to-cueId map for the top-level cues only (used for save/load order).
	/// Child ordering is maintained via each Cue's ChildCues list.
	/// </summary>
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

	/// <summary>
	/// Loads cues from serialized data, creates shells, links components (patches, OSC, cue lights),
	/// then applies saved cue order via StructureCuelist.
	/// </summary>
	/// <param name="cueData">The "cues" sub-dictionary from session save.</param>
	public void LoadData(Dictionary cueData)
	{
		_globalSignals?.EmitSignal(nameof(GlobalSignals.Log), "CueList:LoadData - Loading Cues", (int)LogType.Info);

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
				
				// Patches are instantiated in load sequence seperate form cues. Once patchs and cues are created they
				// need to be linked.
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
		foreach (Cue cue in CueIndex.Values)
		{
			// Assign child shellbars to parents (reparent only; ordering done after)
			if (cue.ParentId != -1)
			{
				_globalSignals?.EmitSignal(nameof(GlobalSignals.Log), $"CueList:StructureCuelist - REPARENTING {cue.Name}", (int)LogType.Info);
				var parentShell = FetchCueFromId(cue.ParentId)?.ShellBar;
				var childContainer = parentShell?.GetNode<VBoxContainer>("%ShellChildContainer");
				if (childContainer != null && cue.ShellBar != null && cue.ShellBar.GetParent() != childContainer)
				{
					cue.ShellBar.Reparent(childContainer);
				}
			}
		}

		// Reorder children inside each group container to exactly match the persisted ChildCues order.
		foreach (Cue cue in CueIndex.Values)
		{
			if (cue.ChildCues.Count == 0) continue;
			var container = cue.ShellBar?.ShellChildContainer;
			if (container == null) continue;

			// Build lookup of current child shells by id for fast placement
			var shellsById = new System.Collections.Generic.Dictionary<int, ShellBar>();
			foreach (var ch in container.GetChildren())
			{
				if (ch is ShellBar s) shellsById[s.CueId] = s;
			}

			int pos = 0;
			foreach (int childId in cue.ChildCues)
			{
				if (shellsById.TryGetValue(childId, out var sb))
				{
					container.MoveChild(sb, pos);
					pos++;
				}
			}
		}

		// Order top-level shells
		for (int i = 0; i < cueOrder.Count; i++)
		{
			if (!CueIndex.TryGetValue(cueOrder[i], out var cue)) continue;
			var shell = cue.ShellBar as ShellBar;
			if (shell == null || cue.ParentId != -1) continue;
			// Use direct call; defer only if needed for init timing (kept simple here)
			_cueContainer.MoveChild(shell, i);
		}

		// Refresh collapse/expand buttons and visibility for any groups after load structure
		// (SetCue ran early; child shells have now been moved into containers)
		foreach (var cue in CueIndex.Values)
		{
			if (cue.ChildCues.Count > 0 && cue.ShellBar != null)
			{
				cue.ShellBar.RelationshipChanged();
			}
		}
	}

	/// <summary>
	/// Encapsulates the complex state machine and mouse-driven logic for reordering cues
	/// (including support for inserting above/below or as child of a group).
	/// This keeps the main CueList class focused and improves readability.
	/// </summary>
}

