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

	/// <summary>
	/// Total number of cues in the show (all levels, including group children).
	/// </summary>
	/// <value>Count of entries in <see cref="CueIndex"/>.</value>
	public int TotalCueCount => CueIndex?.Count ?? 0;
	
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

	private Control _headerColorPad;
	private Control _headerIssuePad;
	private Label _numberHeaderLabel;
	private Control _numberNameResizeGrip;
	private Label _nameHeaderLabel;
	private Label _preWaitHeaderLabel;
	private Label _durationHeaderLabel;
	private Label _postWaitHeaderLabel;
	private Label _followHeaderLabel;

	private Control _reorderCueControl;
	private Label _reorderLocationLabel;
	private VBoxContainer _reorderListContainer;
	private Panel _reorderIndicatorPanel;

	// Expand/collapse all state
	private bool _allExpanded = false;

	/// <summary>True while the user is dragging the Number/Name column grip.</summary>
	private bool _isDraggingNumberColumn;
	
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

		_headerColorPad = GetNodeOrNull<Control>("%HeaderColorPad");
		_headerIssuePad = GetNodeOrNull<Control>("%HeaderIssuePad");
		_numberHeaderLabel = GetNodeOrNull<Label>("%NumberHeaderLabel");
		_numberNameResizeGrip = GetNodeOrNull<Control>("%NumberNameResizeGrip");
		_nameHeaderLabel = GetNodeOrNull<Label>("%NameHeaderLabel");
		_preWaitHeaderLabel = GetNodeOrNull<Label>("%PreWaitHeaderLabel");
		_durationHeaderLabel = GetNodeOrNull<Label>("%DurationHeaderLabel");
		_postWaitHeaderLabel = GetNodeOrNull<Label>("%PostWaitHeaderLabel");
		_followHeaderLabel = GetNodeOrNull<Label>("%FollowHeaderLabel");
		
		_reorderCueControl = GetNode<Control>("%ReorderCueControl");
		_reorderLocationLabel = GetNode<Label>("%ReorderLocationLabel");
		_reorderListContainer = GetNode<VBoxContainer>("%ReorderListContainer");
		_reorderIndicatorPanel = GetNode<Panel>("%ReorderIndicatorPanel");

		_addCueButton.Icon = GetThemeIcon("PlusCircled", "AtlasIcons");
		_expandAllButton.Icon = GetThemeIcon("Right", "AtlasIcons");

		_reorderCueControl.Visible = false;

		LoadShellColumnPrefs();
		SetupShellColumnHeader();
		ShellColumnLayout.Changed += OnShellColumnLayoutChanged;

		_syncHotkeys();

		_reorderController = new CueReorder(this, _reorderCueControl, _reorderLocationLabel, _reorderListContainer, _reorderIndicatorPanel, _cueContainer);

		_globalSignals.CreateCue += CreateCue;
		_globalSignals.DeleteSelectedCues += DeleteSelectedCues;
		_globalSignals.DuplicateSelectedCues += DuplicateSelectedCues;
		_globalSignals.GroupSelectedCues += GroupSelectedCues;
		_globalSignals.CuelistExpandOneLayer += ExpandOneLayer;
		_globalSignals.CuelistCollapseOneLayer += CollapseOneLayer;
		_globalSignals.ToggleExpandAll += OnExpandAllPressed;
		_addCueButton.Pressed += CreateCue;
		_expandAllButton.Pressed += OnExpandAllPressed;
	}

	public override void _ExitTree()
	{
		ShellColumnLayout.Changed -= OnShellColumnLayoutChanged;
		if (_numberNameResizeGrip != null)
			_numberNameResizeGrip.GuiInput -= OnNumberNameGripGuiInput;
		if (_durationHeaderLabel != null)
			_durationHeaderLabel.GuiInput -= OnTimeHeaderGuiInput;
		base._ExitTree();
	}

	/// <summary>
	/// Wires the cuelist header to mirror shell column chrome and user-resizable widths.
	/// Order matches rows: Color | Drag(Add) | Issue pad | Collapse(Expand) | Number | grip | Name | times.
	/// </summary>
	private void SetupShellColumnHeader()
	{
		if (_addCueButton != null)
			_addCueButton.CustomMinimumSize = new Vector2(ShellColumnLayout.DragWidth, 18);
		if (_expandAllButton != null)
			_expandAllButton.CustomMinimumSize = new Vector2(ShellColumnLayout.CollapseWidth, 18);

		// Color strip + 1px nest gap so Number columns line up with shell rows.
		if (_headerColorPad != null)
			_headerColorPad.CustomMinimumSize = new Vector2(
				ShellColumnLayout.ColorWidth + ShellColumnLayout.ColorNestGap, 15);
		if (_headerIssuePad != null)
			_headerIssuePad.CustomMinimumSize = new Vector2(ShellColumnLayout.IssueWidth, 15);

		// Dedicated grip is more reliable than HSplit for this header (Godot 4.6 multi-split quirks).
		if (_numberNameResizeGrip != null)
		{
			_numberNameResizeGrip.MouseDefaultCursorShape = Control.CursorShape.Hsize;
			_numberNameResizeGrip.GuiInput += OnNumberNameGripGuiInput;
		}

		// Drag on duration header to resize all three time columns together.
		if (_durationHeaderLabel != null)
			_durationHeaderLabel.GuiInput += OnTimeHeaderGuiInput;

		ApplyHeaderColumnLayout();
	}

	private void OnShellColumnLayoutChanged()
	{
		if (!IsInstanceValid(this))
			return;
		// ShellBar instances subscribe to ShellColumnLayout.Changed themselves.
		ApplyHeaderColumnLayout();
		PersistShellColumnPrefs();
	}

	/// <summary>
	/// Applies current <see cref="ShellColumnLayout"/> widths to header labels.
	/// </summary>
	private void ApplyHeaderColumnLayout()
	{
		float numW = ShellColumnLayout.NumberWidth;
		float timeW = ShellColumnLayout.TimeWidth;
		float followW = ShellColumnLayout.FollowWidth;

		if (_numberHeaderLabel != null)
			_numberHeaderLabel.CustomMinimumSize = new Vector2(numW, 0);
		if (_preWaitHeaderLabel != null)
			_preWaitHeaderLabel.CustomMinimumSize = new Vector2(timeW, 0);
		if (_durationHeaderLabel != null)
		{
			_durationHeaderLabel.CustomMinimumSize = new Vector2(timeW, 0);
			_durationHeaderLabel.TooltipText = "Drag horizontally to resize Pre-Wait / Duration / Post-Wait columns.";
			_durationHeaderLabel.MouseDefaultCursorShape = Control.CursorShape.Hsize;
		}
		if (_postWaitHeaderLabel != null)
			_postWaitHeaderLabel.CustomMinimumSize = new Vector2(timeW, 0);
		if (_followHeaderLabel != null)
			_followHeaderLabel.CustomMinimumSize = new Vector2(followW, 0);
	}

	/// <summary>
	/// Drags the Number/Name boundary grip to resize the number column.
	/// </summary>
	private void OnNumberNameGripGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
		{
			_isDraggingNumberColumn = mb.Pressed;
			if (!mb.Pressed)
				PersistShellColumnPrefs();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (@event is InputEventMouseMotion motion
		    && _isDraggingNumberColumn
		    && (motion.ButtonMask & MouseButtonMask.Left) != 0)
		{
			ShellColumnLayout.NumberWidth = ShellColumnLayout.NumberWidth + motion.Relative.X;
			GetViewport().SetInputAsHandled();
		}
	}

	/// <summary>
	/// Horizontal drag on the Duration header resizes the three timing columns together.
	/// </summary>
	private void OnTimeHeaderGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseMotion motion
		    && (motion.ButtonMask & MouseButtonMask.Left) != 0)
		{
			ShellColumnLayout.TimeWidth = ShellColumnLayout.TimeWidth + motion.Relative.X;
			GetViewport().SetInputAsHandled();
		}
	}

	/// <summary>
	/// Pushes shared column widths to every shell row in the hierarchy.
	/// </summary>
	private void ApplyColumnLayoutToAllShells()
	{
		if (_cueContainer == null)
			return;
		ApplyColumnLayoutRecursive(_cueContainer);
	}

	private static void ApplyColumnLayoutRecursive(VBoxContainer container)
	{
		if (container == null)
			return;
		foreach (var child in container.GetChildren())
		{
			if (child is ShellBar shell)
			{
				shell.ApplyColumnLayout();
				if (shell.ShellChildContainer != null)
					ApplyColumnLayoutRecursive(shell.ShellChildContainer);
			}
		}
	}

	/// <summary>
	/// Loads persisted shell column widths from user preferences when available.
	/// </summary>
	private void LoadShellColumnPrefs()
	{
		var udm = _globalData?.UserDataManager;
		if (udm == null)
			return;

		float number = udm.ShellNumberColumnWidth;
		float time = udm.ShellTimeColumnWidth;
		if (number > 0 || time > 0)
		{
			ShellColumnLayout.SetWidthsSilent(
				number > 0 ? number : ShellColumnLayout.DefaultNumberWidth,
				time > 0 ? time : ShellColumnLayout.DefaultTimeWidth);
		}
	}

	/// <summary>
	/// Saves current shell column widths into user preferences.
	/// </summary>
	private void PersistShellColumnPrefs()
	{
		var udm = _globalData?.UserDataManager;
		if (udm == null)
			return;
		udm.SetShellColumnWidths(ShellColumnLayout.NumberWidth, ShellColumnLayout.TimeWidth);
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
	/// Shell properties come from show-scoped <see cref="Settings"/> cue defaults.
	/// When <see cref="Settings.SelectNewCues"/> is enabled, the new cue becomes the selection.
	/// </summary>
	public void CreateCue()
	{
		_globalData?.HistoryManager?.RecordCuelistChange("Create cue");
		var newCue = new Cue();
		_globalData?.Settings?.ApplyShellDefaults(newCue);
		AddCue(newCue);
		MaybeSelectNewCue(newCue);
	}

	/// <summary>
	/// Selects and focuses <paramref name="cue"/> when the show setting
	/// <see cref="Settings.SelectNewCues"/> is enabled; otherwise leaves selection unchanged.
	/// </summary>
	/// <param name="cue">Cue that was just created.</param>
	/// <remarks>
	/// <see cref="ShellSelection.SelectIndividualShell"/> already emits <c>ShellFocused</c>
	/// via <see cref="ShellSelection.AddSelection"/> — do not emit it again, or audio/video
	/// inspectors can cancel their first async load (generation bump) and skip
	/// <c>PopulateOutputOptions</c>, leaving the Output dropdown on "No output".
	/// </remarks>
	private void MaybeSelectNewCue(Cue cue)
	{
		if (cue == null) return;
		if (_globalData?.Settings?.SelectNewCues != true) return;

		_globalData.ShellSelection?.SelectIndividualShell(cue);
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

		_globalData?.HistoryManager?.RecordCuelistChange("Group cues");

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

		// Create the wrapping group cue (shell defaults, then override display name)
		var groupCue = new Cue();
		_globalData?.Settings?.ApplyShellDefaults(groupCue);
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

		_globalData?.HistoryManager?.RecordCuelistChange("Import media cues");

		var mediaEngine = GetNodeOrNull<MediaEngine>("/root/MediaEngine");
		var newCues = new List<Cue>();
		Cue groupCue = null;

		// Determine insertion base location once
		var (targetContainer, baseInsertIndex, parentIdForNew) = ResolveInsertLocation(targetCueId, insertMode);

		if (asGroup && files.Length > 1)
		{
			// Create a wrapper group cue first (shell defaults, then override display name)
			groupCue = new Cue();
			_globalData?.Settings?.ApplyShellDefaults(groupCue);
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
			_globalData?.Settings?.ApplyShellDefaults(cue);
			string baseName = Path.GetFileNameWithoutExtension(filePath);
			cue.Name = string.IsNullOrWhiteSpace(baseName) ? $"Cue {cue.Id}" : baseName;
			cue.CueNum = cue.Id.ToString();

			// Add the appropriate component
			string ext = Path.GetExtension(filePath).ToLowerInvariant();
			bool isAudio = GlobalData.AudioFileFilters.Any(e => e.TrimStart('*').Equals(ext, StringComparison.OrdinalIgnoreCase));
			bool isVideoOrImage = GlobalData.VideoFileFilters.Any(e => e.TrimStart('*').Equals(ext, StringComparison.OrdinalIgnoreCase)) ||
			                       GlobalData.ImageFileFilters.Any(e => e.TrimStart('*').Equals(ext, StringComparison.OrdinalIgnoreCase));

			// Store show-relative path immediately when media backup is enabled
			string pathToStore = ResolveMediaPathForNewCue(filePath, isAudio);

			if (isAudio)
			{
				cue.AddAudioComponent(pathToStore);
			}
			else if (isVideoOrImage)
			{
				cue.AddVideoComponent(pathToStore);
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

		// Optionally select the first newly created cue (or the group if we made one)
		var cueToFocus = groupCue ?? newCues.FirstOrDefault();
		MaybeSelectNewCue(cueToFocus);

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
		NotifyTotalCuesChanged();

		cue.ParentId = parentId;
		// ParentId is applied after SetCue — refresh depth-based indent now.
		shellBar.ApplyTreeIndent();
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
	/// When media backup is enabled and a show is open, returns a show-relative path and queues copy.
	/// Otherwise returns the original absolute path. Metadata loading should still use the absolute source.
	/// </summary>
	private string ResolveMediaPathForNewCue(string absolutePath, bool isAudio)
	{
		try
		{
			var backup = GetNodeOrNull<MediaBackupManager>("/root/MediaBackupManager");
			if (backup == null)
				return absolutePath;

			var kind = isAudio
				? MediaBackupKind.Audio
				: MediaBackupManager.DetectKindFromPath(absolutePath);

			string relative = backup.EnsureMediaBackedUp(absolutePath, kind);
			return string.IsNullOrEmpty(relative) ? absolutePath : relative;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"CueList:ResolveMediaPathForNewCue - {ex.Message}");
			return absolutePath;
		}
	}

	/// <summary>
	/// Asynchronously fetches metadata (and waveform for audio), attaches it to the component, and updates cue duration.
	/// Safe to fire-and-forget. <paramref name="filePath"/> should be a readable absolute path (source file).
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
		NotifyTotalCuesChanged();
	}

	/// <summary>
	/// Syncs <see cref="GlobalData.CueTotal"/> and notifies listeners when the cue count changes.
	/// </summary>
	/// <summary>
	/// Assigns even/odd zebra indices to every visible shell (including expanded children)
	/// so shell washes and the blank-space stripes stay consistent.
	/// </summary>
	public void RefreshShellZebra()
	{
		if (_cueContainer == null) return;
		int index = 0;
		AssignZebraRecursive(_cueContainer, ref index);
	}

	private static void AssignZebraRecursive(VBoxContainer container, ref int index)
	{
		if (container == null) return;
		foreach (var child in container.GetChildren())
		{
			if (child is not ShellBar shell) continue;
			shell.SetZebraIndex(index++);
			// Only walk into currently visible nested shells.
			if (shell.ShellChildContainer != null && shell.ShellChildContainer.Visible)
				AssignZebraRecursive(shell.ShellChildContainer, ref index);
		}
	}

	private void NotifyTotalCuesChanged()
	{
		int total = TotalCueCount;
		if (_globalData != null)
			_globalData.CueTotal = total;
		_globalSignals?.EmitSignal(nameof(GlobalSignals.TotalCuesChanged), total);
		// Count/structure changes almost always affect visual order.
		CallDeferred(nameof(RefreshShellZebra));
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
	/// Duplicates selected cues (Ctrl+D).
	/// <list type="bullet">
	/// <item>If a parent is selected, its full child tree is duplicated; selected descendants of that parent are ignored.</item>
	/// <item>A single selection is cloned directly below the origin.</item>
	/// <item>Multiple roots insert as one contiguous block below the most recently selected cue.</item>
	/// </list>
	/// </summary>
	public void DuplicateSelectedCues()
	{
		var selected = ShellSelection.SelectedCues?.ToList() ?? new List<Cue>();
		if (selected.Count == 0)
		{
			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log), "No cues selected to duplicate.", (int)LogType.Info);
			return;
		}

		// Roots only: parent selected ⇒ whole tree; do not also treat selected children as roots
		var selectedIds = new HashSet<int>(selected.Select(c => c.Id));
		var roots = selected
			.Where(c => c != null && !IsDescendantOfAnySelectedAncestor(c, selectedIds))
			.ToList();

		if (roots.Count == 0)
		{
			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log), "No cues selected to duplicate.", (int)LogType.Info);
			return;
		}

		_globalData?.HistoryManager?.RecordCuelistChange("Duplicate cues");

		// Visual/document order for a stable duplicate block
		var visualOrder = GetVisualCueOrderIncludingCollapsed();
		roots = roots
			.OrderBy(c =>
			{
				int idx = visualOrder.IndexOf(c.Id);
				return idx < 0 ? int.MaxValue : idx;
			})
			.ToList();

		// Anchor: most recently selected cue → insert block Below it
		var anchor = selected.Last();
		if (anchor?.ShellBar == null || !IsInstanceValid(anchor.ShellBar))
		{
			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log), "Cannot duplicate: selection has no shell UI.", 2);
			return;
		}

		var (container, insertIndex, parentId) = ResolveInsertLocation(anchor.Id, DropInsertMode.Below);
		var newTopLevel = new List<Cue>();

		int index = insertIndex;
		foreach (var root in roots)
		{
			var clone = CloneCueTree(root, parentId, container, index);
			if (clone != null)
			{
				newTopLevel.Add(clone);
				index++; // next top-level sibling after this clone root
			}
		}

		// Keep parent ChildCues list aligned with UI order after mid-list inserts
		if (parentId != -1)
		{
			var parentCue = FetchCueFromId(parentId);
			SyncChildCuesFromShellContainer(parentCue);
		}

		if (newTopLevel.Count == 0)
		{
			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log), "Duplication produced no cues.", 1);
			return;
		}

		// Select the new duplicates (top-level of the block)
		foreach (var c in ShellSelection.SelectedCues.ToList())
			c.ShellBar?.Deselect();
		ShellSelection.SelectedCues.Clear();
		foreach (var c in newTopLevel)
		{
			if (c.ShellBar != null)
			{
				c.ShellBar.Select();
				ShellSelection.SelectedCues.Add(c);
			}
		}

		_globalSignals?.EmitSignal(nameof(GlobalSignals.ShellFocused), newTopLevel.Last().Id);
		_globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
			newTopLevel.Count == 1
				? $"Duplicated cue \"{newTopLevel[0].Name}\"."
				: $"Duplicated {newTopLevel.Count} cues.",
			(int)LogType.Info);
		GD.Print($"CueList:DuplicateSelectedCues - Created {newTopLevel.Count} top-level duplicate(s).");
	}

	/// <summary>
	/// True if <paramref name="cue"/> has an ancestor whose id is in <paramref name="selectedIds"/>.
	/// </summary>
	private static bool IsDescendantOfAnySelectedAncestor(Cue cue, HashSet<int> selectedIds)
	{
		int parentId = cue.ParentId;
		while (parentId != -1)
		{
			if (selectedIds.Contains(parentId))
				return true;
			var parent = FetchCueFromId(parentId);
			if (parent == null) break;
			parentId = parent.ParentId;
		}
		return false;
	}

	/// <summary>
	/// Flat list of all cue ids in UI order (including inside collapsed groups).
	/// </summary>
	private List<int> GetVisualCueOrderIncludingCollapsed()
	{
		var result = new List<int>();
		void Walk(VBoxContainer container)
		{
			if (container == null) return;
			foreach (var child in container.GetChildren())
			{
				if (child is not ShellBar sb) continue;
				int id = sb.CueId;
				result.Add(id);
				Walk(sb.ShellChildContainer);
			}
		}
		Walk(_cueContainer);
		return result;
	}

	/// <summary>
	/// Deep-clones a cue and all descendants into <paramref name="container"/> at <paramref name="insertIndex"/>.
	/// </summary>
	/// <returns>The new root clone, or null on failure.</returns>
	private Cue CloneCueTree(Cue source, int newParentId, VBoxContainer container, int insertIndex)
	{
		if (source == null) return null;

		var clone = CloneCueShallow(source);
		CreateShellAndInsert(clone, container, insertIndex, newParentId);

		// Children of the source go under the clone's child container, in ChildCues order
		var childContainer = clone.ShellBar?.ShellChildContainer;
		if (childContainer != null && source.ChildCues.Count > 0)
		{
			int childIndex = 0;
			foreach (int childId in source.ChildCues.ToList())
			{
				var child = FetchCueFromId(childId);
				if (child == null) continue;
				CloneCueTree(child, clone.Id, childContainer, childIndex);
				childIndex++;
			}
			clone.Expanded = source.Expanded;
			clone.ShellBar?.RelationshipChanged();
			clone.ShellBar?.SetExpanded(clone.Expanded);
		}

		clone.CalculateTotalDuration();
		return clone;
	}

	/// <summary>
	/// Creates a new cue with a fresh id and copies scalar fields + components from <paramref name="source"/>.
	/// Does not copy ParentId/ChildCues (set by tree insert).
	/// </summary>
	private static Cue CloneCueShallow(Cue source)
	{
		var clone = new Cue();
		clone.Name = source.Name;
		clone.CueNum = source.CueNum;
		clone.PreWait = source.PreWait;
		clone.Duration = source.Duration;
		clone.TotalDuration = source.TotalDuration;
		clone.PostWait = source.PostWait;
		clone.Follow = source.Follow;
		clone.Expanded = source.Expanded;
		clone.Color = source.Color;
		clone.ParentId = -1;
		clone.ChildCues = new List<int>();

		// Deep-copy components via serialize/deserialize
		clone.Components.Clear();
		foreach (var comp in source.Components)
		{
			if (comp == null) continue;
			try
			{
				var compDict = comp.GetData();
				compDict["Type"] = comp.Type;
				ICueComponent newComp = comp.Type switch
				{
					"Audio" => new AudioComponent(),
					"Video" => new VideoComponent(),
					"Network" => new NetworkComponent(),
					"CueLight" => new CueLightComponent(),
					"OscComponent" => new OscComponent(),
					"Control" => new ControlComponent(),
					_ => null
				};
				if (newComp == null) continue;
				newComp.LoadFromData(compDict);
				clone.Components.Add(newComp);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"CueList:CloneCueShallow - Failed to clone component {comp.Type}: {ex.Message}");
			}
		}

		return clone;
	}

	/// <summary>
	/// Rebuilds <see cref="Cue.ChildCues"/> from the shell child container order.
	/// </summary>
	private static void SyncChildCuesFromShellContainer(Cue parent)
	{
		if (parent?.ShellBar?.ShellChildContainer == null) return;
		parent.ChildCues.Clear();
		foreach (var child in parent.ShellBar.ShellChildContainer.GetChildren())
		{
			if (child is ShellBar sb)
				parent.ChildCues.Add(sb.CueId);
		}
		parent.ShellBar.RelationshipChanged();
	}

	/// <summary>
	/// Deletes all currently selected cues (Delete key / shell inspector).
	/// When a parent is selected, children are removed with it even if not multi-selected.
	/// </summary>
	public void DeleteSelectedCues()
	{
		var selected = ShellSelection.SelectedCues?.ToList() ?? new List<Cue>();
		if (selected.Count == 0)
		{
			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log), "No cues selected to delete.", (int)LogType.Info);
			return;
		}

		// Avoid double-delete: only delete roots of the selection (parent selected ⇒ children go with it)
		var selectedIds = new HashSet<int>(selected.Select(c => c.Id));
		var roots = selected
			.Where(c => c != null && (c.ParentId == -1 || !selectedIds.Contains(c.ParentId)))
			.ToList();

		_globalData?.HistoryManager?.RecordCuelistChange("Delete cues");

		int count = 0;
		foreach (var cue in roots)
		{
			count += RemoveCueRecursive(cue);
		}

		ShellSelection.SelectedCues.Clear();
		// Clear inspectors that still reference deleted cues
		_globalSignals?.EmitSignal(nameof(GlobalSignals.ShellFocused), -1);
		_globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
			count == 1 ? "Deleted 1 cue." : $"Deleted {count} cues.", (int)LogType.Info);
		GD.Print($"CueList:DeleteSelectedCues - Removed {count} cue(s).");
	}

	/// <summary>
	/// Removes the cue from the index and queues its ShellBar for deletion.
	/// Prunes from parent's ChildCues (if any) and refreshes the parent's collapse/expand UI.
	/// Does not recurse into children — use <see cref="RemoveCueRecursive"/> for groups.
	/// </summary>
	/// <param name="cue">The cue to remove.</param>
	public void RemoveCue(Cue cue)
	{
		if (cue == null) return;

		// Drop selection state
		if (ShellSelection.SelectedCues != null && ShellSelection.SelectedCues.Contains(cue))
		{
			cue.ShellBar?.Deselect();
			ShellSelection.SelectedCues.Remove(cue);
		}

		// Clear media-health tracking for this cue
		try
		{
			GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.ClearIssue(cue.Id);
		}
		catch { /* optional */ }

		if (cue.ParentId != -1)
		{
			var p = FetchCueFromId(cue.ParentId);
			if (p != null)
			{
				p.ChildCues.Remove(cue.Id);
				p.ShellBar?.RelationshipChanged();
			}
		}

		if (cue.ShellBar != null && IsInstanceValid(cue.ShellBar))
			cue.ShellBar.QueueFree();
		cue.ShellBar = null;

		CueIndex?.Remove(cue.Id);
		NotifyTotalCuesChanged();
	}

	/// <summary>
	/// Removes a cue and all descendants. Returns number of cues removed.
	/// </summary>
	public int RemoveCueRecursive(Cue cue)
	{
		if (cue == null) return 0;

		int removed = 0;
		// Copy child list — RemoveCue mutates parent ChildCues
		foreach (int childId in cue.ChildCues.ToList())
		{
			var child = FetchCueFromId(childId);
			if (child != null)
				removed += RemoveCueRecursive(child);
		}

		RemoveCue(cue);
		return removed + 1;
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

	/// <summary>
	/// Retrieves the first cue whose <see cref="Cue.CueNum"/> matches <paramref name="cueNum"/> (exact, case-sensitive).
	/// </summary>
	/// <param name="cueNum">User-facing cue number string.</param>
	/// <returns>The matching cue, or null if none / empty input.</returns>
	public static Cue FetchCueFromCueNum(string cueNum)
	{
		if (string.IsNullOrWhiteSpace(cueNum) || CueIndex == null)
			return null;

		string needle = cueNum.Trim();
		foreach (var cue in CueIndex.Values)
		{
			if (cue != null && string.Equals(cue.CueNum, needle, StringComparison.Ordinal))
				return cue;
		}

		return null;
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
	/// <summary>
	/// True while a drag-reorder session is active (drop indicator visible).
	/// </summary>
	public bool IsReordering => _reorderController != null && _reorderController.IsActive;

	public void StartReorder(ShellBar shellbar)
	{
		// Drop blue hover wash for the session; reorder indicator owns highlight.
		ClearAllShellHoverChrome();
		_reorderController.Start(shellbar);
	}

	/// <summary>
	/// Clears hover chrome on every shell so reorder does not inherit a stuck hover wash.
	/// </summary>
	internal void ClearAllShellHoverChrome()
	{
		if (_cueContainer == null)
			return;
		ClearShellHoverRecursive(_cueContainer);
	}

	private static void ClearShellHoverRecursive(VBoxContainer container)
	{
		if (container == null)
			return;
		foreach (var child in container.GetChildren())
		{
			if (child is ShellBar shell)
			{
				shell.ClearHoverChrome();
				if (shell.ShellChildContainer != null)
					ClearShellHoverRecursive(shell.ShellChildContainer);
			}
		}
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
	/// Deferred grabber unstick after reorder ends (called from <see cref="CueReorder"/>).
	/// Ensures BaseButton pressed state is cleared after the mouse-up event finishes.
	/// </summary>
	/// <param name="draggedCueId">Cue that initiated the drag, or -1.</param>
	public void DeferredReleaseReorderGrabbers(int draggedCueId)
	{
		if (draggedCueId >= 0)
			FetchCueFromId(draggedCueId)?.ShellBar?.ReleaseDragGrabber();

		if (ShellSelection.SelectedCues == null)
			return;

		foreach (var cue in ShellSelection.SelectedCues)
			cue?.ShellBar?.ReleaseDragGrabber();
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

	/// <summary>
	/// Records a cuelist-scoped history checkpoint (used by structural ops and <see cref="CueReorder"/>).
	/// </summary>
	/// <param name="description">Human-readable undo description.</param>
	/// <param name="coalesceKey">Optional coalesce key for continuous edits.</param>
	internal void RecordHistory(string description, string coalesceKey = null)
	{
		_globalData?.HistoryManager?.RecordCuelistChange(description, coalesceKey);
	}

	/// <summary>
	/// Re-links component runtime references (patches, cue lights, OSC) after a cue data apply.
	/// </summary>
	/// <param name="cue">Cue whose components should be re-linked.</param>
	internal void RelinkCueComponents(Cue cue)
	{
		if (cue == null) return;

		var audio = cue.GetAudioComponent();
		if (audio != null)
		{
			var patches = _globalData.Settings.GetAudioOutputPatches();
			patches.TryGetValue(audio.PatchId, out var patch);
			audio.Patch = patch;
		}

		var video = cue.GetVideoComponent();
		if (video != null)
		{
			var patches = _globalData.Settings.GetAudioOutputPatches();
			patches.TryGetValue(video.PatchId, out var patch);
			video.Patch = patch;
		}

		var cueLightComps = cue.GetCueLightComponents();
		if (cueLightComps != null)
		{
			foreach (var cueLightComp in cueLightComps)
			{
				var cuelight = _globalData.CueLightManager.GetCueLight(cueLightComp.CueLightId);
				cueLightComp.CueLight = cuelight;
			}
		}

		var oscComponents = cue.GetOscComponents();
		if (oscComponents != null)
		{
			foreach (var oscComp in oscComponents)
			{
				var oscConnection = OscConnections.GetCueOscConnection(oscComp.OscConnectionId);
				oscComp.OscConnection = oscConnection;
			}
		}
	}

	/// <summary>
	/// Applies a single-cue history snapshot in place without rebuilding the cuelist or settings.
	/// </summary>
	/// <param name="cueId">Target cue id.</param>
	/// <param name="cueData">Deep-cloned <see cref="Cue.GetData"/> dictionary.</param>
	/// <returns>True if the cue was found and applied.</returns>
	internal bool ApplyCueHistorySnapshot(int cueId, Dictionary cueData)
	{
		var cue = FetchCueFromId(cueId);
		if (cue == null || cueData == null)
		{
			GD.PrintErr($"CueList:ApplyCueHistorySnapshot - Cue {cueId} not found or data null.");
			return false;
		}

		cue.ApplyFromData(cueData);
		RelinkCueComponents(cue);
		cue.ShellBar?.RefreshAllFromCue();

		// Keep shell selection list pointing at the same cue instance.
		GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.CheckCue(cueId);

		_globalSignals?.EmitSignal(nameof(GlobalSignals.UpdateShellBar), cueId);
		// Always request shell-inspector repaint from model (no-op if nothing focused).
		// Do not rely on re-emitting ShellFocused: ShellSelected early-outs when id is unchanged.
		_globalSignals?.EmitSignal(nameof(GlobalSignals.SyncShellInspector));

		// Other inspectors (audio/video) rebuild on ShellFocused; force when this cue is focused.
		if (_globalData != null && _globalData.FocusedCue == cueId)
			_globalSignals?.EmitSignal(nameof(GlobalSignals.ShellFocused), cueId);

		return true;
	}

	/// <summary>
	/// Rebuilds only the cuelist from a history snapshot (no settings / displays reload).
	/// </summary>
	/// <param name="cuesData">Deep-cloned <see cref="GetData"/> dictionary.</param>
	internal void ApplyCuelistHistorySnapshot(Dictionary cuesData)
	{
		_globalSignals?.EmitSignal(nameof(GlobalSignals.StopAll));
		int previousFocus = _globalData?.FocusedCue ?? -1;

		ResetCuelist();
		GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.ClearAll();

		if (cuesData != null)
			LoadData(cuesData);

		if (previousFocus >= 0 && CueIndex.ContainsKey(previousFocus))
			_globalSignals?.EmitSignal(nameof(GlobalSignals.ShellFocused), previousFocus);
		else
			_globalSignals?.EmitSignal(nameof(GlobalSignals.ShellFocused), -1);

		GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.RecheckAllQuiet();
		_globalSignals?.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
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
		RefreshShellZebra();
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
		RefreshShellZebra();
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
		Cue.ResetIdAllocator();
		NotifyTotalCuesChanged();
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

		RefreshShellZebra();
	}

	/// <summary>
	/// Encapsulates the complex state machine and mouse-driven logic for reordering cues
	/// (including support for inserting above/below or as child of a group).
	/// This keeps the main CueList class focused and improves readability.
	/// </summary>
}

