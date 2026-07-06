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
	private GlobalData _globalData;
	private GlobalSignals _globalSignals;
	
	
	/// <summary>
	/// Global lookup of all cues by ID. Populated on creation and used for fast access.
	/// </summary>
	public static System.Collections.Generic.Dictionary<int, Cue> CueIndex; // <CueId, Cue>
	
	// Cue list reordering properties (internal state for drag gesture)
	private bool _isReordering;
	private ShellBar _mouseOverShellBar;
	private bool _insertAbove;
	private bool _insertBelow;
	private bool _insertMakeChild;
	private bool _dropAtEndAsTopLevel;
	
	/// <summary>
	/// The cue ID currently being dragged for reorder (set during StartReorder).
	/// </summary>
	private int ShellBeingDragged = -1;

	// ShellDraggedOver is declared but unused; retained for possible future extension / legacy.

	// Reorder constants (avoid magic numbers; actual shell min size ~26 in ShellBar.tscn)
	private const int ShellHeight = 26;
	private const int ShellMarginDiv = 4;

	
	private PackedScene _shellBarPackedScene = SceneLoader.LoadPackedScene("uid://d207a67e3ebww", out _);

	// Ui
	private VBoxContainer _cueContainer;
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

	private int _childTally = -1;

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

		_globalSignals.CreateCue += CreateCue;
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
		_mouseOverShellBar = shellbar;
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
		if (_isReordering) return;
		if (!shellbar.Selected)
		{
			_globalData.ShellSelection.SelectIndividualShell(FetchCueFromId(shellbar.CueId));
		}

		if (_reorderListContainer.GetChildCount() > 0)
		{
			foreach (var child in _reorderListContainer.GetChildren())
			{
				child.QueueFree();
			}
		}
		
		foreach (var selectedCue in ShellSelection.SelectedCues)
		{
			var label = new Label();
			label.Text = selectedCue.Name;
			label.AddThemeFontSizeOverride("font_size", 9);
			label.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f, 0.5f));
			_reorderListContainer.AddChild(label);
		}

		_reorderCueControl.Visible = true;
		_isReordering = true;
		ShellBeingDragged = shellbar.CueId;
		_insertAbove = false;
		_insertBelow = false;
		_insertMakeChild = false;
		_dropAtEndAsTopLevel = false;

		var cue = FetchCueFromId(shellbar.CueId);
		var shell = cue?.ShellBar;
	}
	
	public override void _Input(InputEvent @event)
	{
		if (!_isReordering) return;

		if (@event is InputEventMouseMotion eventMouseMotion)
		{
			_reorderCueControl.GlobalPosition = new Vector2(eventMouseMotion.Position.X, eventMouseMotion.Position.Y);
			var mouseY = eventMouseMotion.GlobalPosition.Y;
			var check = IsValidDropTarget();

			bool handledAsEnd = false;

			// Detect blank space below the *entire* list (after the visually last cue).
			// This should always result in a top-level append, even if the last cue is nested.
			var lastShell = GetLastVisibleShellBar();
			if (lastShell != null)
			{
				float lastBottom = lastShell.GetGlobalPosition().Y + lastShell.Size.Y;
				if (mouseY > lastBottom + 8)  // tolerance for "blank area below everything"
				{
					_insertAbove = false;
					_insertBelow = true;
					_insertMakeChild = false;
					_dropAtEndAsTopLevel = true;

					_reorderLocationLabel.Text = "Reorder at end (top level)";
					_reorderIndicatorPanel.GlobalPosition = new Vector2(_cueContainer.GetGlobalPosition().X, lastBottom);
					_reorderIndicatorPanel.Size = new Vector2(_cueContainer.Size.X, 2);
					_reorderIndicatorPanel.Visible = true;

					handledAsEnd = true;
					check = true; // blank end is always a valid drop target
				}
			}

			if (!handledAsEnd)
			{
				_dropAtEndAsTopLevel = false;

				if (check && _mouseOverShellBar != null)
				{
					var targetCueId = _mouseOverShellBar.CueId;
					var shellPosY = _mouseOverShellBar.GetGlobalPosition().Y;
					var shellSizeY = ShellHeight;
					var margin = shellSizeY / ShellMarginDiv;

					_insertAbove = mouseY < shellPosY + margin;
					_insertBelow = mouseY > shellPosY + margin * 3;
					var targetCue = FetchCueFromId(targetCueId);
					_insertMakeChild = targetCue != null && targetCue.ParentId != -1;

					string targetName = targetCue?.Name ?? "?";
					string parentName = "";
					if (_insertMakeChild && targetCue != null)
					{
						var p = FetchCueFromId(targetCue.ParentId);
						parentName = p?.Name ?? "?";
					}

					if (_insertBelow)
					{
						_reorderLocationLabel.Text = _insertMakeChild
							? $"Reorder below: {targetName} and child of: {parentName}"
							: $"Reorder below: {targetName}";
						_reorderIndicatorPanel.GlobalPosition = new Vector2(_mouseOverShellBar.GetGlobalPosition().X, _mouseOverShellBar.GetGlobalPosition().Y + _mouseOverShellBar.Size.Y);
						_reorderIndicatorPanel.Size = new Vector2(_mouseOverShellBar.Size.X, 1);
						_reorderIndicatorPanel.Visible = true;
					}
					else if (_insertAbove)
					{
						_reorderLocationLabel.Text = _insertMakeChild
							? $"Reorder above: {targetName} and child of: {parentName}"
							: $"Reorder above: {targetName}";
						_reorderIndicatorPanel.GlobalPosition = _mouseOverShellBar.GetGlobalPosition();
						_reorderIndicatorPanel.Size = new Vector2(_mouseOverShellBar.Size.X, 1);
						_reorderIndicatorPanel.Visible = true;
					}
					else
					{
						_reorderLocationLabel.Text = $"Make child of: {targetName}";
						_reorderIndicatorPanel.GlobalPosition = _mouseOverShellBar.GetGlobalPosition();
						_reorderIndicatorPanel.Size = _mouseOverShellBar.Size;
						_reorderIndicatorPanel.Visible = true;
					}
				}
				else
				{
					_reorderLocationLabel.Text = "Cannot reorder here";
					_reorderIndicatorPanel.Visible = false;
				}
			}
		}

		// Left release = commit
		if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && !mb.Pressed)
		{
			EndReorder();
		}

		// Cancel support (ESC or right-click)
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
		{
			CleanupReorder(keepChanges: false);
		}
		if (@event is InputEventMouseButton rmb && rmb.ButtonIndex == MouseButton.Right && rmb.Pressed)
		{
			CleanupReorder(keepChanges: false);
		}
	}

	private void EndReorder()
	{
		// Validate location. Blank end-of-list is always allowed.
		bool isEndDrop = _dropAtEndAsTopLevel;
		if (!isEndDrop && !IsValidDropTarget())
		{
			CleanupReorder(keepChanges: false);
			return;
		}

		var targetCue = isEndDrop ? null : FetchCueFromId(_mouseOverShellBar?.CueId ?? -1);
		if (!isEndDrop && targetCue == null)
		{
			CleanupReorder(keepChanges: false);
			return;
		}

		// Check for cycles for any of the moved items (skip for end-of-list top level drop)
		if (!isEndDrop)
		{
			foreach (var sc in ShellSelection.SelectedCues)
			{
				int prospective = (!_insertAbove && !_insertBelow) ? targetCue.Id : targetCue.ParentId;
				if (WouldCreateCycle(sc, prospective))
				{
					_globalSignals?.EmitSignal(nameof(GlobalSignals.Log), $"CueList:EndReorder - Cycle would be created; aborting reorder for {sc.Name}", (int)LogType.Warning);
					CleanupReorder(keepChanges: false);
					return;
				}
			}
		}

		// Snapshot the shells we will move, trying to preserve their relative visual order.
		// Fall back to SelectedCues order if we cannot obtain a full ordered list.
		var toMove = new List<ShellBar>();
		try
		{
			// ShellSelection.GetAllShellBarsInOrder is private; collect selected in encounter order
			// and rely on later insert index + sequential MoveChild for relative order.
			foreach (var c in ShellSelection.SelectedCues)
			{
				if (c?.ShellBar != null) toMove.Add(c.ShellBar);
			}
		}
		catch
		{
			foreach (var c in ShellSelection.SelectedCues)
				if (c?.ShellBar != null) toMove.Add(c.ShellBar);
		}

		if (toMove.Count == 0)
		{
			CleanupReorder(keepChanges: false);
			return;
		}

		// Snapshot child counts before any structural changes (used to detect cues that just became parents)
		var childCountBefore = new System.Collections.Generic.Dictionary<Cue, int>();
		foreach (var c in CueIndex.Values)
		{
			childCountBefore[c] = c.ChildCues.Count;
		}

		// Track parents that will lose or gain children so we can refresh their collapse/expand UI
		var affectedParents = new System.Collections.Generic.HashSet<Cue>();
		foreach (var mc in ShellSelection.SelectedCues)
		{
			if (mc != null && mc.ParentId != -1)
			{
				var op = FetchCueFromId(mc.ParentId);
				if (op != null) affectedParents.Add(op);
			}
		}

		// Compute final target using helper (after possible removals we will adjust)
		var (targetContainer, rawInsertIndex, newParentId, isMakeChild) = DetermineReorderTarget();

		// First, detach all to-move shells from their current parents (UI + data lists)
		// This prevents index shifting problems and stale parent links.
		foreach (var sb in toMove)
		{
			var cue = FetchCueFromId(sb.CueId);
			if (cue == null) continue;

			if (cue.ParentId != -1)
			{
				var oldP = FetchCueFromId(cue.ParentId);
				oldP?.ChildCues.Remove(cue.Id);
			}
			// Remove from whatever container it currently lives in (safe detach)
			sb.GetParent()?.RemoveChild(sb);
			cue.ParentId = -1; // temporary; will be set via sync or below
		}

		// Re-compute insert index in the (now smaller) target container
		int insertIndex = Math.Clamp(rawInsertIndex, 0, Math.Max(0, targetContainer.GetChildCount()));

		if (_dropAtEndAsTopLevel)
		{
			insertIndex = targetContainer.GetChildCount();
		}

		// Insert the moved items (preserve relative order from toMove snapshot)
		foreach (var sb in toMove)
		{
			targetContainer.AddChild(sb); // append first
			if (!_dropAtEndAsTopLevel && !isMakeChild && (_insertAbove || _insertBelow))
			{
				// For sibling inserts, place at desired spot (subsequent moves will push later ones)
				targetContainer.MoveChild(sb, insertIndex);
				// Advance insertIndex so next sibling of the block goes after this one
				insertIndex++;
			}
			// For _dropAtEndAsTopLevel we just AddChild successively → they go at the true end as top level.
			// else: make-child or non-sibling: leave appended at end of target container
		}

		// Now sync the data model ChildCues from the actual UI containers for affected areas
		SyncChildListsFromContainers();

		// Apply ParentId from the synced lists + call relationship UI updates
		foreach (var sb in toMove)
		{
			var cue = FetchCueFromId(sb.CueId);
			if (cue == null) continue;

			// The sync will have put the id into the correct parent's ChildCues.
			// If now a child of someone, set ParentId by searching who lists it (or use newParentId heuristic).
			bool foundParent = false;
			foreach (var other in CueIndex.Values)
			{
				if (other.ChildCues.Contains(cue.Id))
				{
					cue.ParentId = other.Id;
					foundParent = true;
					break;
				}
			}
			if (!foundParent)
				cue.ParentId = -1;

			sb.RelationshipChanged();
		}

		// Add any new parents (the containers that received the moved cues)
		foreach (var sb in toMove)
		{
			var mc = FetchCueFromId(sb.CueId);
			if (mc != null && mc.ParentId != -1)
			{
				var np = FetchCueFromId(mc.ParentId);
				if (np != null) affectedParents.Add(np);
			}
		}

		// Refresh collapse/expand UI on all affected parents.
		// For cues that just acquired their first child(ren) in this reorder, default to expanded.
		foreach (var parent in affectedParents)
		{
			if (parent.ShellBar != null)
			{
				int before = childCountBefore.TryGetValue(parent, out var b) ? b : 0;
				if (parent.ChildCues.Count > 0 && before == 0)
				{
					parent.Expanded = true;
				}
				parent.ShellBar.RelationshipChanged();
			}
		}

		CleanupReorder(keepChanges: true);
	}

	/// <summary>
	/// Common cleanup for ending or cancelling a reorder drag.
	/// </summary>
	private void CleanupReorder(bool keepChanges)
	{
		// clean preview labels
		foreach (var child in _reorderListContainer.GetChildren())
		{
			child.QueueFree();
		}
		_isReordering = false;
		_reorderCueControl.Visible = false;
		_mouseOverShellBar = null;
		_insertAbove = false;
		_insertBelow = false;
		_insertMakeChild = false;
		_dropAtEndAsTopLevel = false;
		ShellBeingDragged = -1;

		if (!keepChanges)
		{
			// Nothing else; shells were not moved.
		}
	}

	/// <summary>
	/// Rebuilds every Cue's ChildCues list from the current order of ShellBars inside
	/// the corresponding UI containers. Use after reorders to keep data model in sync with UI.
	/// Top-level has no owning list (order lives in _cueContainer children + GetCueOrder on save).
	/// </summary>
	private void SyncChildListsFromContainers()
	{
		foreach (var cueEntry in CueIndex)
		{
			var cue = cueEntry.Value;
			if (cue.ChildCues.Count == 0 && cue.ShellBar?.ShellChildContainer?.GetChildCount() == 0)
				continue;

			var container = cue.ShellBar?.ShellChildContainer;
			if (container == null) continue;

			var ordered = new List<int>();
			foreach (var child in container.GetChildren())
			{
				if (child is ShellBar sb)
				{
					int id = sb.CueId;
					if (id >= 0) ordered.Add(id);
				}
			}
			cue.ChildCues = ordered;
		}
	}

	/// <summary>
	/// Determines the target parent container, insertion index, and new parent cue id
	/// based on current _insert* flags and mouse-over target. Centralizes the three cases
	/// (top-level sibling, nested sibling, make-child).
	/// </summary>
	private (VBoxContainer targetContainer, int insertIndex, int newParentId, bool isMakeChild) DetermineReorderTarget()
	{
		if (_dropAtEndAsTopLevel)
		{
			// Special case: blank space below the entire list → always append as top-level
			return (_cueContainer, _cueContainer.GetChildCount(), -1, false);
		}

		var targetShell = _mouseOverShellBar;
		if (targetShell == null)
			return (_cueContainer, 0, -1, false);

		VBoxContainer container = _cueContainer;
		int newPid = -1;
		bool makeChild = false;

		if (!_insertAbove && !_insertBelow)
		{
			// Case 3: make direct child of target
			container = targetShell.ShellChildContainer ?? _cueContainer;
			newPid = targetShell.CueId;
			makeChild = true;
		}
		else if (FetchCueFromId(targetShell.CueId)?.ParentId != -1)
		{
			// Case 2: sibling inside the same parent group as target
			var targetParent = FetchCueFromId(FetchCueFromId(targetShell.CueId).ParentId);
			container = targetParent?.ShellBar?.ShellChildContainer ?? _cueContainer;
			newPid = targetParent?.Id ?? -1;
		}
		// else Case 1: top-level sibling, container stays _cueContainer, newPid stays -1

		int idx = targetShell.GetIndex();
		return (container, idx, newPid, makeChild);
	}

	/// <summary>
	/// Returns true if moving the given cue under the prospective new parent would
	/// create a cycle in the parent/child hierarchy.
	/// </summary>
	private bool WouldCreateCycle(Cue movingCue, int prospectiveParentId)
	{
		if (movingCue == null || prospectiveParentId == -1) return false;
		if (movingCue.Id == prospectiveParentId) return true;

		var current = FetchCueFromId(prospectiveParentId);
		while (current != null)
		{
			if (current.Id == movingCue.Id) return true;
			if (current.ParentId == -1) break;
			current = FetchCueFromId(current.ParentId);
		}
		return false;
	}

	/// <summary>
	/// Returns whether the current mouse-over shell is a valid drop target for the active reorder.
	/// Prevents dropping onto any of the currently selected (being-dragged) cues.
	/// </summary>
	private bool IsValidDropTarget()
	{
		if (_mouseOverShellBar == null) return false;
		var targetCue = FetchCueFromId(_mouseOverShellBar.CueId);
		if (targetCue == null) return false;
		return !ShellSelection.SelectedCues.Contains(targetCue);
	}

	/// <summary>
	/// Returns the visually last (bottom-most) ShellBar in the current list,
	/// walking into expanded child containers. Used to detect "blank space below everything".
	/// </summary>
	private ShellBar GetLastVisibleShellBar()
	{
		return FindLastShell(_cueContainer);
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

}

