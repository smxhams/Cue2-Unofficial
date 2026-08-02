using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Godot;
using Godot.Collections;

using Cue2.Domain.Connections;
using Cue2.Domain.Cues;
using Cue2.Domain.Library;
using Cue2.UI.Shell;
using Cue2.Services;
using Cue2.UI.Popups;
using Cue2.UI.Utilities;

// This script is attached to the cuelist in main UI
// Originator
namespace Cue2.Domain.Cuelist;

/// <summary>
/// Manages the main cue list UI, including creation, removal, drag-and-drop reordering
/// (with support for nesting/grouping), box multi-select, and save/load of cue hierarchy and order.
/// </summary>
/// <remarks>
/// Follows project MVVM-like separation: UI shells in ShellBar, data in Cue objects,
/// shared state via GlobalData/GlobalSignals. Reordering uses custom mouse tracking
/// (not native Godot drag/drop) to support above/below/into-child zones for multi-selection.
/// Box-select is handled by <see cref="CueBoxSelect"/> and never starts from the reorder grabber.
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

	// Box (marquee) multi-select controller (see CueBoxSelect.cs).
	private CueBoxSelect _boxSelectController;

	// Reorder constants (avoid magic numbers; actual shell min size ~26 in ShellBar.tscn)
	private const int ShellHeight = 26;
	private const int ShellMarginDiv = 4;

	
	private PackedScene _shellBarPackedScene = SceneLoader.LoadPackedScene("uid://d207a67e3ebww", out _);

	// Ui
	internal VBoxContainer _cueContainer;
	private ScrollContainer _cueListScroll;
	/// <summary>
	/// Trailing pad below the last cue so the list can scroll slightly past the end.
	/// Lives inside the scroll content (with zebra) — not a MarginContainer margin.
	/// </summary>
	private Control _scrollEndPad;
	private Button _addCueButton;
	private Button _expandAllButton;

	/// <summary>
	/// Extra scrollable space below the last cue (as a fraction of row height).
	/// Lets the final row clear the bottom of the viewport when the list is full.
	/// </summary>
	private const float ScrollEndPaddingRows = 3.0f;

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

	/// <summary>
	/// In-app cue clipboard: ordered root original ids and a flat map of original-id → deep-cloned <see cref="Cue.GetData"/>.
	/// Survives cut (sources deleted) and supports multi-root paste with full child trees.
	/// </summary>
	private List<int> _clipboardRootIds = new();
	private Dictionary _clipboardCuesByOldId;

	/// <summary>
	/// True while a frame-sliced bulk structural operation (duplicate/paste/copy/cut/delete) is running.
	/// Concurrent cuelist mutations and cuelist-scope undo/redo are blocked while set.
	/// </summary>
	private bool _bulkOpInProgress;

	/// <summary>
	/// Whether a bulk cuelist structural operation is currently frame-slicing work.
	/// </summary>
	public bool IsBulkOpInProgress => _bulkOpInProgress;

	/// <summary>
	/// Nested suppress depth for <see cref="NotifyTotalCuesChanged"/> during bulk shell create/delete.
	/// When &gt; 0, count/zebra updates are deferred until the outer bulk ends.
	/// </summary>
	private int _bulkNotifySuppressDepth;

	/// <summary>
	/// Ops that touch this many cues (including group children) run async with footer progress;
	/// smaller ops stay synchronous for snappy single-cue editing.
	/// </summary>
	private const int BulkAsyncThreshold = 20;

	/// <summary>Shell creates / clipboard clones processed per frame during async bulk ops.</summary>
	private const int BulkItemsPerFrame = 12;
	
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
		_cueListScroll = GetNodeOrNull<ScrollContainer>("VBoxContainer/Cue List");
		_scrollEndPad = GetNodeOrNull<Control>("%ScrollEndPad");
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
		// Apply show-scoped cuelist density before wiring header (so chrome sizes match).
		ApplyCueListScale(_globalData?.Settings?.CueListScale ?? Settings.DefaultCueListScale, silent: true);
		SetupShellColumnHeader();
		ShellColumnLayout.Changed += OnShellColumnLayoutChanged;

		_syncHotkeys();

		_reorderController = new CueReorder(this, _reorderCueControl, _reorderLocationLabel, _reorderListContainer, _reorderIndicatorPanel, _cueContainer);
		_boxSelectController = new CueBoxSelect(this, _cueContainer, _cueListScroll, this);

		// Empty space in the list (below last cue / gutters / end pad) starts box-select / clear.
		if (_cueContainer != null)
			_cueContainer.GuiInput += OnCueContainerGuiInput;
		if (_scrollEndPad != null)
			_scrollEndPad.GuiInput += OnCueContainerGuiInput;

		_globalSignals.CreateCue += CreateCue;
		_globalSignals.DeleteSelectedCues += DeleteSelectedCues;
		_globalSignals.DuplicateSelectedCues += DuplicateSelectedCues;
		_globalSignals.CutSelectedCues += CutSelectedCues;
		_globalSignals.CopySelectedCues += CopySelectedCues;
		_globalSignals.PasteCues += PasteCues;
		_globalSignals.GroupSelectedCues += GroupSelectedCues;
		_globalSignals.CuelistExpandOneLayer += ExpandOneLayer;
		_globalSignals.CuelistCollapseOneLayer += CollapseOneLayer;
		_globalSignals.ToggleExpandAll += OnExpandAllPressed;
		_addCueButton.Pressed += CreateCue;
		_expandAllButton.Pressed += OnExpandAllPressed;

		_globalSignals.ShowModeChanged += OnShowModeChanged;
		_globalSignals.CueListScaleChanged += OnCueListScaleChanged;
		_globalSignals.NewSession += OnNewSession;
		_globalSignals.LocaleChanged += OnLocaleChanged;
		ApplyShowModeUi(_globalData?.Settings?.IsCueEditingLocked == true);
		UiLocalizer.LocalizeTree(this);
	}

	public override void _ExitTree()
	{
		ShellColumnLayout.Changed -= OnShellColumnLayoutChanged;
		if (_numberNameResizeGrip != null)
			_numberNameResizeGrip.GuiInput -= OnNumberNameGripGuiInput;
		if (_durationHeaderLabel != null)
			_durationHeaderLabel.GuiInput -= OnTimeHeaderGuiInput;
		if (_cueContainer != null)
			_cueContainer.GuiInput -= OnCueContainerGuiInput;
		if (_scrollEndPad != null)
			_scrollEndPad.GuiInput -= OnCueContainerGuiInput;
		if (_globalSignals != null)
		{
			_globalSignals.ShowModeChanged -= OnShowModeChanged;
			_globalSignals.CueListScaleChanged -= OnCueListScaleChanged;
			_globalSignals.NewSession -= OnNewSession;
			_globalSignals.LocaleChanged -= OnLocaleChanged;
		}
		base._ExitTree();
	}

	/// <summary>
	/// Re-localizes cuelist header labels and tooltips when the UI language changes.
	/// </summary>
	/// <param name="localeCode">New locale code.</param>
	private void OnLocaleChanged(string localeCode)
	{
		if (!GodotObject.IsInstanceValid(this))
			return;
		UiLocalizer.LocalizeTree(this);
		_syncHotkeys();
	}

	/// <summary>
	/// Applies cuelist UI scale from General Settings (row height / chrome / fonts only).
	/// </summary>
	private void OnCueListScaleChanged(float scale)
	{
		ApplyCueListScale(scale, silent: false);
	}

	/// <summary>
	/// Re-applies scale after New Session / show reset.
	/// </summary>
	private void OnNewSession()
	{
		ApplyCueListScale(_globalData?.Settings?.CueListScale ?? Settings.DefaultCueListScale, silent: false);
	}

	/// <summary>
	/// Updates <see cref="ShellColumnLayout.Scale"/> and refreshes header chrome.
	/// Shell rows re-layout via <see cref="ShellColumnLayout.Changed"/>.
	/// </summary>
	/// <param name="scale">Scale factor (Small / Medium / Large).</param>
	/// <param name="silent">When true, set scale without raising Changed (caller will apply once).</param>
	private void ApplyCueListScale(float scale, bool silent)
	{
		if (silent)
			ShellColumnLayout.SetScaleSilent(scale);
		else
			ShellColumnLayout.Scale = scale;

		// Header button sizes use scaled chrome; re-apply even after silent set.
		ApplyHeaderChromeSizes();
		ApplyHeaderColumnLayout();
		UpdateScrollEndPadding();
	}

	/// <summary>
	/// Sizes the trailing scroll pad so the list can scroll slightly past the last cue.
	/// Pad is a sibling of <see cref="_cueContainer"/> inside the zebra-covered content,
	/// so stripes continue through the blank space.
	/// </summary>
	private void UpdateScrollEndPadding()
	{
		if (_scrollEndPad == null || !IsInstanceValid(_scrollEndPad))
			return;

		float padPx = Mathf.Max(24f, ShellColumnLayout.RowMinHeight * ScrollEndPaddingRows);
		_scrollEndPad.CustomMinimumSize = new Vector2(0f, padPx);
	}

	/// <summary>
	/// After GO advances the playhead: if the newly selected cue sits at or past the bottom
	/// of the visible cuelist (or outside the viewport), scroll so that cue is centered.
	/// </summary>
	/// <param name="cue">Playhead cue to keep in view.</param>
	public void EnsurePlayheadVisibleAfterGo(Cue cue)
	{
		if (cue == null || _cueListScroll == null)
			return;
		// Defer one frame so selection chrome / layout settle after SelectIndividualShell.
		CallDeferred(nameof(EnsurePlayheadVisibleAfterGoDeferred), cue.Id);
	}

	/// <summary>
	/// Deferred body for <see cref="EnsurePlayheadVisibleAfterGo"/>.
	/// </summary>
	/// <param name="cueId">Id of the playhead cue.</param>
	private void EnsurePlayheadVisibleAfterGoDeferred(int cueId)
	{
		if (_cueListScroll == null || !IsInstanceValid(_cueListScroll))
			return;

		var cue = FetchCueFromId(cueId);
		var shell = cue?.ShellBar;
		if (shell == null || !IsInstanceValid(shell))
			return;

		// Use the cue row only — group shells include nested children in their full height.
		Control row = shell.GetNodeOrNull<Control>("%RowHBox") ?? shell;
		if (!IsInstanceValid(row))
			return;

		var scrollRect = _cueListScroll.GetGlobalRect();
		var rowRect = row.GetGlobalRect();
		if (scrollRect.Size.Y <= 1f || rowRect.Size.Y <= 0f)
			return;

		float viewTop = scrollRect.Position.Y;
		float viewBottom = viewTop + scrollRect.Size.Y;
		float rowTop = rowRect.Position.Y;
		float rowBottom = rowTop + rowRect.Size.Y;

		// Comfort zone: fully visible with at least half a row of space under the shell.
		// When the next playhead cue sits at/below that band (bottom of visible area) or is
		// outside the viewport, re-center it.
		float comfortBottom = viewBottom - Mathf.Max(1f, ShellColumnLayout.RowMinHeight * 0.5f);
		bool outsideAbove = rowBottom <= viewTop;
		bool outsideBelow = rowTop >= viewBottom;
		bool atOrPastBottom = rowBottom > comfortBottom;
		if (!outsideAbove && !outsideBelow && !atOrPastBottom)
			return;

		ScrollControlToVerticalCenter(row);
	}

	/// <summary>
	/// Scrolls the cuelist so <paramref name="control"/> is vertically centered in the viewport.
	/// </summary>
	/// <param name="control">Control to center (typically a shell row).</param>
	private void ScrollControlToVerticalCenter(Control control)
	{
		if (_cueListScroll == null || control == null || !IsInstanceValid(control))
			return;

		var scrollRect = _cueListScroll.GetGlobalRect();
		var controlRect = control.GetGlobalRect();
		float viewCenterY = scrollRect.Position.Y + scrollRect.Size.Y * 0.5f;
		float controlCenterY = controlRect.Position.Y + controlRect.Size.Y * 0.5f;
		float delta = controlCenterY - viewCenterY;

		int next = _cueListScroll.ScrollVertical + Mathf.RoundToInt(delta);
		var vBar = _cueListScroll.GetVScrollBar();
		if (vBar != null)
			next = (int)Mathf.Clamp(next, vBar.MinValue, vBar.MaxValue);
		else
			next = Mathf.Max(0, next);

		_cueListScroll.ScrollVertical = next;
	}

	/// <summary>
	/// True when Show Mode is locking cue/cuelist document edits.
	/// </summary>
	private bool IsCueEditingLocked() =>
		_globalData?.Settings?.IsCueEditingLocked == true;

	/// <summary>
	/// Logs and returns true when a mutating cue operation should be blocked.
	/// </summary>
	/// <param name="actionLabel">Short action name for the log message.</param>
	/// <returns>True if the caller should abort.</returns>
	private bool BlockIfShowMode(string actionLabel)
	{
		if (!IsCueEditingLocked())
			return false;
		_globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
			$"Show Mode: cannot {actionLabel}. Turn off Show Mode to edit cues.", (int)LogType.Info);
		return true;
	}

	/// <summary>
	/// Logs and returns true when another bulk cuelist operation is already running.
	/// </summary>
	/// <param name="actionLabel">Short action name for the log message.</param>
	/// <returns>True if the caller should abort.</returns>
	private bool BlockIfBulkBusy(string actionLabel)
	{
		if (!_bulkOpInProgress)
			return false;
		_globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
			$"Please wait — a cuelist operation is in progress. Cannot {actionLabel}.", (int)LogType.Info);
		return true;
	}

	private void OnShowModeChanged(bool enabled)
	{
		ApplyShowModeUi(enabled);
	}

	/// <summary>
	/// Disables Add Cue (and similar chrome) while Show Mode is active.
	/// </summary>
	private void ApplyShowModeUi(bool showMode)
	{
		if (_addCueButton != null)
			_addCueButton.Disabled = showMode;
	}

	/// <summary>
	/// Wires the cuelist header to mirror shell column chrome and user-resizable widths.
	/// Order matches rows: Color | Drag(Add) | Issue pad | Collapse(Expand) | Number | grip | Name | times.
	/// </summary>
	private void SetupShellColumnHeader()
	{
		ApplyHeaderChromeSizes();

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

	/// <summary>
	/// Sizes header chrome (Add / Expand / pads) from current <see cref="ShellColumnLayout"/> scale.
	/// </summary>
	private void ApplyHeaderChromeSizes()
	{
		float headerBtnH = Mathf.Max(14f, 18f * ShellColumnLayout.Scale);
		float padH = Mathf.Max(12f, 15f * ShellColumnLayout.Scale);

		if (_addCueButton != null)
		{
			_addCueButton.CustomMinimumSize = new Vector2(ShellColumnLayout.DragWidth, headerBtnH);
			_addCueButton.AddThemeConstantOverride("icon_max_width", ShellColumnLayout.IconMaxWidth);
		}
		if (_expandAllButton != null)
		{
			_expandAllButton.CustomMinimumSize = new Vector2(ShellColumnLayout.CollapseWidth, headerBtnH);
			_expandAllButton.AddThemeConstantOverride("icon_max_width", ShellColumnLayout.IconMaxWidth);
		}

		// Color strip + nest gap so Number columns line up with shell rows.
		if (_headerColorPad != null)
			_headerColorPad.CustomMinimumSize = new Vector2(
				ShellColumnLayout.ColorWidth + ShellColumnLayout.ColorNestGap, padH);
		if (_headerIssuePad != null)
			_headerIssuePad.CustomMinimumSize = new Vector2(ShellColumnLayout.IssueWidth, padH);
	}

	private void OnShellColumnLayoutChanged()
	{
		if (!IsInstanceValid(this))
			return;
		// ShellBar instances subscribe to ShellColumnLayout.Changed themselves.
		// Scale changes also fire Changed — refresh header chrome + column labels.
		ApplyHeaderChromeSizes();
		ApplyHeaderColumnLayout();
		// Only persist when user-resized widths changed (scale changes do not write prefs).
		// Persist is cheap and keeps number/time widths saved after drag end via grip handlers;
		// calling here on scale is harmless (same values).
		PersistShellColumnPrefs();
	}

	/// <summary>
	/// Applies current <see cref="ShellColumnLayout"/> widths and font scale to header labels.
	/// </summary>
	private void ApplyHeaderColumnLayout()
	{
		float numW = ShellColumnLayout.NumberWidth;
		float timeW = ShellColumnLayout.TimeWidth;
		float followW = ShellColumnLayout.FollowWidth;
		int headerFont = ShellColumnLayout.HeaderFontSize;

		if (_numberHeaderLabel != null)
		{
			_numberHeaderLabel.CustomMinimumSize = new Vector2(numW, 0);
			_numberHeaderLabel.AddThemeFontSizeOverride("font_size", headerFont);
		}
		if (_nameHeaderLabel != null)
			_nameHeaderLabel.AddThemeFontSizeOverride("font_size", headerFont);
		if (_preWaitHeaderLabel != null)
		{
			_preWaitHeaderLabel.CustomMinimumSize = new Vector2(timeW, 0);
			_preWaitHeaderLabel.AddThemeFontSizeOverride("font_size", headerFont);
		}
		if (_durationHeaderLabel != null)
		{
			_durationHeaderLabel.CustomMinimumSize = new Vector2(timeW, 0);
			_durationHeaderLabel.TooltipText = "Drag horizontally to resize Pre-Wait / Duration / Post-Wait columns.";
			_durationHeaderLabel.MouseDefaultCursorShape = Control.CursorShape.Hsize;
			_durationHeaderLabel.AddThemeFontSizeOverride("font_size", headerFont);
		}
		if (_postWaitHeaderLabel != null)
		{
			_postWaitHeaderLabel.CustomMinimumSize = new Vector2(timeW, 0);
			_postWaitHeaderLabel.AddThemeFontSizeOverride("font_size", headerFont);
		}
		if (_followHeaderLabel != null)
		{
			_followHeaderLabel.CustomMinimumSize = new Vector2(followW, 0);
			_followHeaderLabel.AddThemeFontSizeOverride("font_size", headerFont);
		}
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
		if (BlockIfShowMode("create cues") || BlockIfBulkBusy("create cues")) return;
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

		// Covered by the create/import cuelist history step — do not push a separate selection undo.
		_globalData.ShellSelection?.SelectIndividualShell(cue, recordHistory: false);
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
		if (BlockIfShowMode("group cues") || BlockIfBulkBusy("group cues")) return;
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

		// Select the group cue (replacing the previous multi-selection).
		// Covered by the group cuelist history step.
		_globalData.ShellSelection.SelectIndividualShell(groupCue, recordHistory: false);

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
		if (BlockIfShowMode("create cues from dropped files") || BlockIfBulkBusy("create cues from dropped files")) return;
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
		if (_bulkNotifySuppressDepth == 0)
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
				videoComp.RefreshIsImageFromPath();
				var meta = await mediaEngine.GetVideoFileMetadataAsync(filePath);
				videoComp.Metadata = meta;
				if (videoComp.IsImage)
				{
					// Still image: no embedded audio; duration is user hold time (0 = until stopped).
					videoComp.HasAudio = false;
					videoComp.UseAudio = false;
					videoComp.StartTime = 0;
					videoComp.EndTime = -1;
				}
				else
				{
					videoComp.HasAudio = meta.AudioChannels > 0;
					videoComp.UseAudio = videoComp.HasAudio;
				}
				videoComp.ScaledWidth = meta.Width;
				videoComp.ScaledHeight = meta.Height;
				videoComp.RecalculateDuration();

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
		if (_bulkNotifySuppressDepth == 0)
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
	/// Large forests are frame-sliced with footer progress so the UI stays responsive.
	/// </summary>
	public void DuplicateSelectedCues()
	{
		_ = DuplicateSelectedCuesAsync();
	}

	/// <summary>
	/// Copies selected cue roots (and full child trees) to the in-app cue clipboard (Ctrl+C).
	/// Parent selected ⇒ whole tree; selected descendants of that parent are ignored as separate roots.
	/// Large captures are frame-sliced with footer progress.
	/// </summary>
	public void CopySelectedCues()
	{
		_ = CopySelectedCuesAsync();
	}

	/// <summary>
	/// Cuts selected cue roots to the clipboard then deletes them (Ctrl+X).
	/// One cuelist history step covers the removal; paste is a separate undo step.
	/// Large cuts are frame-sliced with footer progress.
	/// </summary>
	public void CutSelectedCues()
	{
		_ = CutSelectedCuesAsync();
	}

	/// <summary>
	/// Pastes the cue clipboard as a contiguous block below the last selected cue (Ctrl+V).
	/// If nothing is selected, pastes at the end of the top-level list.
	/// Control targets that point inside the pasted forest are remapped to the new ids.
	/// Large pastes are frame-sliced with footer progress.
	/// </summary>
	public void PasteCues()
	{
		_ = PasteCuesAsync();
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
	/// Imports a library cue forest (temp-id keyed dictionaries) into the live cuelist.
	/// Allocates new session cue ids, remaps parent/child and in-tree control targets, creates shells.
	/// </summary>
	/// <param name="cuesByTempId">Cue <see cref="Cue.GetData"/> dicts keyed by temp id string.</param>
	/// <param name="rootTempId">Temp id of the forest root.</param>
	/// <param name="insertMode">Where to place the new root relative to the current selection.</param>
	/// <returns>New root cue id, or -1 on failure.</returns>
	public int ImportCueTreeFromLibrary(
		Dictionary cuesByTempId,
		int rootTempId,
		LibraryInsertMode insertMode)
	{
		if (BlockIfShowMode("load library cues into the cuelist") || BlockIfBulkBusy("load library cues"))
			return -1;
		if (cuesByTempId == null || cuesByTempId.Count == 0)
			return -1;

		string rootKey = rootTempId.ToString();
		if (!cuesByTempId.ContainsKey(rootKey) && cuesByTempId.Count > 0)
		{
			// Fallback: first key
			rootKey = cuesByTempId.Keys.First().AsString();
			if (!int.TryParse(rootKey, out rootTempId))
				return -1;
		}

		if (!cuesByTempId.ContainsKey(rootKey))
			return -1;

		// Resolve insert location from focused / selected cue
		int focusId = _globalData?.FocusedCue ?? -1;
		if (focusId < 0 && ShellSelection.SelectedCues != null && ShellSelection.SelectedCues.Count > 0)
			focusId = ShellSelection.SelectedCues[0].Id;

		VBoxContainer container;
		int insertIndex;
		int parentId;
		switch (insertMode)
		{
			case LibraryInsertMode.AsChild:
				(container, insertIndex, parentId) = ResolveInsertLocation(focusId, DropInsertMode.AsChild);
				break;
			case LibraryInsertMode.End:
				(container, insertIndex, parentId) = ResolveInsertLocation(-1, DropInsertMode.AtEnd);
				break;
			default:
				(container, insertIndex, parentId) = ResolveInsertLocation(
					focusId, focusId >= 0 ? DropInsertMode.Below : DropInsertMode.AtEnd);
				break;
		}

		// First pass: build live cues from data with reminted ids; track temp→new map
		var tempToNew = new System.Collections.Generic.Dictionary<int, int>();
		var tempToCue = new System.Collections.Generic.Dictionary<int, Cue>();
		var tempChildOrder = new System.Collections.Generic.Dictionary<int, List<int>>();

		foreach (var kv in cuesByTempId)
		{
			string key = kv.Key.AsString();
			if (!int.TryParse(key, out int tempId))
				continue;
			if (kv.Value.VariantType != Variant.Type.Dictionary)
				continue;

			var data = kv.Value.AsGodotDictionary();

			// Capture child order (temp ids) before we clear hierarchy fields
			var childTemps = new List<int>();
			if (data.TryGetValue("ChildCues", out var childVar))
			{
				foreach (var c in childVar.AsGodotArray())
					childTemps.Add(c.AsInt32());
			}
			tempChildOrder[tempId] = childTemps;

			// Build a clean copy for ApplyFromData: hierarchy applied by CreateShellAndInsert
			var applyData = DeepCloneDict(data);
			applyData["ParentId"] = "-1";
			applyData["ChildCues"] = new Godot.Collections.Array();
			// Id in data is ignored by ApplyFromData (keeps live Id)

			var cue = new Cue();
			cue.ApplyFromData(applyData);
			// Ensure ChildCues empty until we insert children
			cue.ChildCues = new List<int>();
			cue.ParentId = -1;

			tempToNew[tempId] = cue.Id;
			tempToCue[tempId] = cue;
		}

		if (!tempToCue.ContainsKey(rootTempId))
		{
			GD.PrintErr($"CueList:ImportCueTreeFromLibrary - Root temp id {rootTempId} missing.");
			return -1;
		}

		// Remap Control targets: library temp ids → new session ids
		foreach (var cue in tempToCue.Values)
		{
			foreach (var comp in cue.Components)
			{
				if (comp is not ControlComponent control) continue;
				if (control.TargetCueId < 0) continue;
				if (tempToNew.TryGetValue(control.TargetCueId, out int newTarget))
					control.TargetCueId = newTarget;
				else
					control.TargetCueId = -1;
			}

			RelinkCueComponents(cue);
		}

		// Second pass: insert shells in tree order
		Cue ImportNode(int tempId, int newParentId, VBoxContainer cont, int index)
		{
			if (!tempToCue.TryGetValue(tempId, out var cue))
				return null;

			CreateShellAndInsert(cue, cont, index, newParentId);

			var children = tempChildOrder.TryGetValue(tempId, out var list) ? list : new List<int>();
			var childContainer = cue.ShellBar?.ShellChildContainer;
			if (childContainer != null && children.Count > 0)
			{
				int childIndex = 0;
				foreach (int childTemp in children)
				{
					ImportNode(childTemp, cue.Id, childContainer, childIndex);
					childIndex++;
				}

				// Restore expanded state from data if present
				if (cuesByTempId.TryGetValue(tempId.ToString(), out var raw) &&
				    raw.VariantType == Variant.Type.Dictionary)
				{
					var d = raw.AsGodotDictionary();
					if (d.TryGetValue("Expanded", out var exp))
						cue.Expanded = exp.AsBool();
				}

				cue.ShellBar?.RelationshipChanged();
				cue.ShellBar?.SetExpanded(cue.Expanded);
			}

			cue.CalculateTotalDuration();
			return cue;
		}

		var rootCue = ImportNode(rootTempId, parentId, container, insertIndex);
		if (rootCue == null)
			return -1;

		MaybeSelectNewCue(rootCue);
		_globalSignals?.EmitSignal(nameof(GlobalSignals.UpdateShellBar), rootCue.Id);
		_globalSignals?.EmitSignal(nameof(GlobalSignals.SyncShellInspector));

		GD.Print($"CueList:ImportCueTreeFromLibrary - Imported root id {rootCue.Id} ({tempToCue.Count} cue(s)).");
		return rootCue.Id;
	}

	private static Dictionary DeepCloneDict(Dictionary source)
	{
		if (source == null) return new Dictionary();
		string json = Json.Stringify(source);
		using var parser = new Json();
		var err = parser.Parse(json);
		if (err != Error.Ok)
			throw new InvalidOperationException($"CueList deep-clone JSON parse failed: {err}");
		return parser.Data.AsGodotDictionary();
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
		clone.Notes = source.Notes;
		clone.Memo = source.Memo;
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
					"Text" => new TextComponent(),
					"Network" => new NetworkComponent(),
					"CueLight" => new CueLightComponent(),
					"OscComponent" => new OscComponent(),
					"Control" => new ControlComponent(),
					"MidiOutput" => new MidiOutputComponent(),
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
	/// Large deletes are frame-sliced with footer progress.
	/// </summary>
	public void DeleteSelectedCues()
	{
		_ = DeleteSelectedCuesAsync();
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
		if (_bulkNotifySuppressDepth == 0)
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
		if (IsCueEditingLocked())
			return;
		// Reorder wins over any in-progress marquee / pending click-drag select.
		_boxSelectController?.Cancel();
		// Drop blue hover wash for the session; reorder indicator owns highlight.
		ClearAllShellHoverChrome();
		_reorderController.Start(shellbar);
	}

	/// <summary>
	/// Starts a potential box-select after a left press on a shell row (not the drag grabber).
	/// Click without drag still selects; drag past threshold draws a marquee.
	/// </summary>
	/// <param name="originShell">Shell under the press, or null for empty list space.</param>
	/// <param name="globalPos">Press position in global coordinates.</param>
	/// <param name="additive">Ctrl/Cmd: union with existing selection on marquee commit.</param>
	public void BeginPotentialBoxSelect(ShellBar originShell, Vector2 globalPos, bool additive = false)
	{
		if (IsReordering)
			return;
		_boxSelectController?.BeginPending(originShell, globalPos, additive);
	}

	/// <summary>
	/// Empty-area clicks in the cue container: box-select / clear selection.
	/// Shell rows handle their own presses; this only receives events not taken by children.
	/// </summary>
	private void OnCueContainerGuiInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mb || !mb.Pressed)
			return;
		if (mb.ButtonIndex != MouseButton.Left)
			return;
		if (IsReordering)
			return;

		// Shift range-select only applies to shells; ignore here.
		if (Input.IsKeyPressed(Key.Shift))
			return;

		bool additive = Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Meta);
		BeginPotentialBoxSelect(null, mb.GlobalPosition, additive);
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
		// Reorder and box-select both track global mouse-up; reorder takes exclusive priority.
		if (_reorderController != null && _reorderController.IsActive)
		{
			_reorderController.ProcessInput(@event);
			return;
		}

		_boxSelectController?.ProcessInput(@event);
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

		ResetCuelist();
		GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.ClearAll();

		if (cuesData != null)
			LoadData(cuesData);

		// Selection + focus are restored by HistoryManager after this returns
		// (snapshots include ordered selected cue ids).

		GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.RecheckAllQuiet();
		_globalSignals?.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
	}

	internal void SelectIndividualForReorder(int cueId)
	{
		var cue = FetchCueFromId(cueId);
		if (cue != null)
			_globalData.ShellSelection.SelectIndividualShell(cue, recordHistory: false);
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
			// Assign child shellbars to parents (reparent only; ordering done after).
			// Intentionally silent: load/rebuild can reparent many cues and would spam the log.
			if (cue.ParentId != -1)
			{
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

