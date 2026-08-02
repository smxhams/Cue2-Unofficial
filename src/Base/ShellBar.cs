using Godot;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Cue2.UI.Utilities;

// This script is attached to instanced shell bars in the cue list, it handles
// -UI of itself
// -Emitting signals of interactions attached with it's relevant info
namespace Cue2.Base;

public partial class ShellBar : PanelContainer
{
	private GlobalData _globalData;
	private GlobalSignals _globalSignals;
	private GlobalStyles _globalStyles;

	[Export] public int CueId { get; set; } = -1;
	private Cue _cue;
	
	// Ui Properties
	private Panel _colorPanel;
	private StyleBoxFlat _colorBarStyle;
	private Panel _groupPanel;
	private Panel _shellPanel;
	private Button _dragButton;
	private Button _collapseButton;
	private Button _issueIndicator;
	private StyleBoxFlat _issueActiveStyle;
	private StyleBoxEmpty _issueIdleStyle;
	private Control _treeIndent;
	
	private LineEdit _cueNumLineEdit;
	private LineEdit _cueNameLineEdit;
	/// <summary>Shown in place of number/name/times when the cue is in memo mode.</summary>
	private LineEdit _memoLineEdit;

	private LineEdit _preWaitLineEdit;
	private LineEdit _durationLineEdit;
	private LineEdit _postWaitLineEdit;
	/// <summary>Cycles None → Auto-continue → Auto-follow; shows → / ↳.</summary>
	private Button _followButton;

	/// <summary>Main cue strip HBox (not including nested children).</summary>
	private HBoxContainer _rowHBox;

	/// <summary>Left flexible band: color, drag, issue, indent, collapse, number, name.</summary>
	private HBoxContainer _leadingHBox;

	/// <summary>Right fixed band: pre-wait, duration, post-wait, follow (pinned to trailing edge).</summary>
	private HBoxContainer _trailingHBox;

	public VBoxContainer ShellChildContainer;
	
	private bool _isEditingName = false;
	private bool _isEditingCueNum = false;
	private bool _isEditingPreWait = false;
	private bool _isEditingPostWait = false;
	private bool _isEditingMemo = false;
	
	public bool Selected = false;

	/// <summary>Visual row index for zebra striping (0-based, includes nested visible shells).</summary>
	private int _zebraIndex;

	/// <summary>True while the mouse is over this shell (and not selected).</summary>
	private bool _hovered;

	/// <summary>Per-row panel style (metrics fixed; colours rebuilt for zebra + cue + state).</summary>
	private StyleBoxFlat _panelStyle;

	/// <summary>Right-click context menu for cue editing actions (replaces built-in LineEdit menu).</summary>
	private PopupMenu _contextMenu;

	/// <summary>Ids for <see cref="_contextMenu"/> items.</summary>
	private enum ShellContextMenuId
	{
		Cut = 0,
		Copy = 1,
		Paste = 2,
		Duplicate = 3,
		Delete = 4,
		Group = 5,
		CreateCue = 6
	}

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		
		DefineUi();
		ApplyShellChromeDefaults();
		
		_cueNumLineEdit.Editable = false;
		_cueNameLineEdit.Editable = false;
		if (_memoLineEdit != null)
			_memoLineEdit.Editable = false;
		
		// Connect Ui events
		GuiInput += OnInput;
		MouseEntered += OnMouseEntered;
		MouseExited += OnMouseExited;
		
		// GuiInput + AcceptEvent: do not use ButtonDown. Reorder commits on global mouse-up
		// in CueList._Input (before GUI), which left BaseButton latched with keep_pressed_outside
		// and required a second click on the same grabber.
		_dragButton.GuiInput += OnDragBarGuiInput;
		
		_collapseButton.Pressed += CollapsedPressed;
		_cueNumLineEdit.GuiInput += OnCueNumGuiInput;
		_cueNumLineEdit.EditingToggled += OnCueNumEditToggled;
		_cueNumLineEdit.FocusExited += OnCueNumFocusExited;
		_cueNumLineEdit.TextSubmitted += OnCueNumTextSubmitted;
		_cueNameLineEdit.GuiInput += OnNameGuiInput;
		_cueNameLineEdit.EditingToggled += OnNameEditToggled;
		_cueNameLineEdit.FocusExited += OnNameFocusExited;
		_cueNameLineEdit.TextSubmitted += OnNameTextSubmitted;
		// Pre/post waits are always-editable time fields (not double-click-to-edit like name/num).
		WireTimeField(_preWaitLineEdit, isPreWait: true);
		WireTimeField(_postWaitLineEdit, isPreWait: false);
		// Duration is read-only but still needs right-click context menu + selection.
		if (_durationLineEdit != null)
			_durationLineEdit.GuiInput += OnTimeFieldGuiInput;
		if (_memoLineEdit != null)
		{
			_memoLineEdit.GuiInput += OnMemoGuiInput;
			_memoLineEdit.EditingToggled += OnMemoEditToggled;
		}
		if (_followButton != null)
			_followButton.Pressed += OnFollowButtonPressed;

		// Reorder grabber — force visible chrome (theme icon + readable modulate).
		ApplyDragGrabberStyle();
		// Collapse column is always reserved; icon is applied only when the cue has children.
		_collapseButton.Icon = null;
		_collapseButton.Disabled = true;
		_collapseButton.MouseFilter = MouseFilterEnum.Ignore;

		// Optional global refresh targeting this cue id
		_globalSignals.UpdateShellBar += OnUpdateShellBar;
		_globalSignals.CueMediaHealthChanged += OnCueMediaHealthChanged;
		_globalSignals.ShowModeChanged += OnShowModeChanged;

		ShellColumnLayout.Changed += OnShellColumnLayoutChanged;
		ApplyColumnLayout();
		ApplyShowModeEditLock(_globalData?.Settings?.IsCueEditingLocked == true);
	}

	/// <summary>
	/// Stable panel chrome + compact field styling so row controls stay inside the outline
	/// and align on a single vertical center line.
	/// </summary>
	private void ApplyShellChromeDefaults()
	{
		// Do not clip the grabber icon; shell metrics are stable so overflow is not needed.
		ClipContents = false;
		CustomMinimumSize = new Vector2(0, ShellColumnLayout.RowMinHeight);
		SizeFlagsVertical = SizeFlags.Fill;

		// Per-row style with locked metrics (zebra + cue wash applied in RefreshShellChrome).
		EnsurePanelStyle();
		RefreshShellChrome();

		// Legacy overlay panels used for selection drew under OuterHBox and got covered.
		// Keep them non-interactive and hidden — selection uses this container's panel instead.
		if (_shellPanel != null)
		{
			_shellPanel.Visible = false;
			_shellPanel.MouseFilter = MouseFilterEnum.Ignore;
		}
		if (_groupPanel != null)
		{
			_groupPanel.Visible = false;
			_groupPanel.MouseFilter = MouseFilterEnum.Ignore;
		}

		// Structure:
		//   OuterHBox
		//     ColorPanel          ← full shell height (row + nested children)
		//     ContentVBox
		//       RowHBox (Leading | Trailing)
		//       ShellChildContainer
		// ColorPanel is a sibling of ContentVBox so it spans expanded groups as a nest visual.
		// ColorNestGap (1px) sits between parent and child colour strips when nested.
		var outerHBox = GetNodeOrNull<HBoxContainer>("OuterHBox");
		if (outerHBox != null)
		{
			outerHBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			outerHBox.SizeFlagsVertical = SizeFlags.ExpandFill;
			outerHBox.AddThemeConstantOverride("separation", ShellColumnLayout.ColorNestGap);
		}

		var contentVBox = GetNodeOrNull<VBoxContainer>("OuterHBox/ContentVBox");
		if (contentVBox != null)
		{
			contentVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			contentVBox.SizeFlagsVertical = SizeFlags.ExpandFill;
			contentVBox.AddThemeConstantOverride("separation", 0);
		}

		if (_rowHBox != null)
		{
			_rowHBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_rowHBox.AddThemeConstantOverride("separation", ShellColumnLayout.RowSeparation);
			_rowHBox.Alignment = BoxContainer.AlignmentMode.Begin;
		}

		if (_leadingHBox != null)
		{
			_leadingHBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_leadingHBox.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			_leadingHBox.AddThemeConstantOverride("separation", ShellColumnLayout.RowSeparation);
		}

		if (_trailingHBox != null)
		{
			// Never expand — stays glued to the right of RowHBox.
			_trailingHBox.SizeFlagsHorizontal = SizeFlags.Fill;
			_trailingHBox.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			_trailingHBox.AddThemeConstantOverride("separation", ShellColumnLayout.RowSeparation);
		}

		if (ShellChildContainer != null)
		{
			ShellChildContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			// Zero gap so parent colour strip reads continuous through nested children.
			ShellChildContainer.AddThemeConstantOverride("separation", 0);
		}

		// Nested shells must fill parent width (not shrink to content).
		SizeFlagsHorizontal = SizeFlags.ExpandFill;
		SizeFlagsVertical = SizeFlags.Fill;

		if (_cueNameLineEdit != null)
			_cueNameLineEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		if (_memoLineEdit != null)
			_memoLineEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		ApplyCompactFieldStyle(_cueNumLineEdit);
		ApplyCompactFieldStyle(_cueNameLineEdit);
		ApplyCompactFieldStyle(_memoLineEdit);
		ApplyCompactFieldStyle(_preWaitLineEdit);
		ApplyCompactFieldStyle(_durationLineEdit);
		ApplyCompactFieldStyle(_postWaitLineEdit);
	}

	/// <summary>
	/// Restores the reorder grabber icon and hit target (readable on dark rows).
	/// Grabber is visual-only for press state: start is handled in <see cref="OnDragBarGuiInput"/>.
	/// </summary>
	private void ApplyDragGrabberStyle()
	{
		if (_dragButton == null) return;

		_dragButton.Visible = true;
		_dragButton.Disabled = false;
		_dragButton.ToggleMode = false;
		_dragButton.KeepPressedOutside = false;
		_dragButton.FocusMode = FocusModeEnum.None;
		_dragButton.MouseFilter = MouseFilterEnum.Stop;
		_dragButton.MouseDefaultCursorShape = Control.CursorShape.Drag;
		_dragButton.Flat = true;
		_dragButton.ExpandIcon = true;
		_dragButton.IconAlignment = HorizontalAlignment.Center;
		_dragButton.VerticalIconAlignment = VerticalAlignment.Center;
		_dragButton.AddThemeConstantOverride("icon_max_width", ShellColumnLayout.IconMaxWidth);
		// Brighter than the old dark-grey so the grabber is visible on shell chrome.
		var grabberColor = new Color(0.72f, 0.78f, 0.82f, 0.95f);
		_dragButton.AddThemeColorOverride("icon_normal_color", grabberColor);
		_dragButton.AddThemeColorOverride("icon_hover_color", Colors.White);
		_dragButton.AddThemeColorOverride("icon_pressed_color", new Color(0.55f, 0.85f, 0.9f, 1f));

		var icon = GetThemeIcon("Rearrange", "AtlasIcons");
		if (icon != null)
			_dragButton.Icon = icon;

		// Ensure no latched press from a previous session.
		_dragButton.SetPressedNoSignal(false);
		_dragButton.ButtonPressed = false;
	}

	/// <summary>
	/// Tight LineEdit padding so theme content margins don't force fields taller than the row.
	/// Disables Godot's built-in right-click text menu (replaced by shell cue context menu).
	/// Font size follows <see cref="ShellColumnLayout.Scale"/>.
	/// </summary>
	private static void ApplyCompactFieldStyle(LineEdit field)
	{
		if (field == null) return;
		field.ContextMenuEnabled = false;
		field.CustomMinimumSize = new Vector2(field.CustomMinimumSize.X, ShellColumnLayout.RowControlHeight);
		field.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		field.Alignment = HorizontalAlignment.Left;
		field.AddThemeFontSizeOverride("font_size", ShellColumnLayout.FontSize);
		// Empty-ish flat style keeps height under control without fighting the global theme heavily.
		float padH = Mathf.Max(2f, 4f * ShellColumnLayout.Scale);
		float padV = Mathf.Max(1f, 2f * ShellColumnLayout.Scale);
		var compact = new StyleBoxFlat
		{
			BgColor = new Color(0.12f, 0.12f, 0.12f, 0.55f),
			ContentMarginLeft = padH,
			ContentMarginRight = padH,
			ContentMarginTop = padV,
			ContentMarginBottom = padV
		};
		compact.SetCornerRadiusAll(3);
		field.AddThemeStyleboxOverride("normal", compact);
		field.AddThemeStyleboxOverride("read_only", compact);
		var focus = compact.Duplicate() as StyleBoxFlat;
		if (focus != null)
		{
			focus.SetBorderWidthAll(1);
			focus.BorderColor = new Color(0.02f, 0.33f, 0.36f, 0.9f);
			field.AddThemeStyleboxOverride("focus", focus);
		}
		field.AddThemeConstantOverride("minimum_character_width", 1);
	}

	public override void _ExitTree()
	{
		// ShellBars are frequently reparented (group, reorder, show load nesting).
		// Godot calls _ExitTree on reparent as well as free — do not tear down UI/cue
		// state here or ColorPanel styling, signal wiring, and cue binding are lost.
		base._ExitTree();
	}

	public override void _Notification(int what)
	{
		// Permanent cleanup only when the node is actually being destroyed.
		if (what != NotificationPredelete)
			return;

		ShellColumnLayout.Changed -= OnShellColumnLayoutChanged;

		if (_globalSignals != null)
		{
			_globalSignals.UpdateShellBar -= OnUpdateShellBar;
			_globalSignals.CueMediaHealthChanged -= OnCueMediaHealthChanged;
			_globalSignals.ShowModeChanged -= OnShowModeChanged;
		}
		if (_cue != null)
		{
			_cue.NameChanged -= UpdateName;
			_cue.CueNumChanged -= UpdateCueNum;
			_cue.ColorChanged -= UpdateColor;
			_cue.DurationChanged -= UpdateDuration;
			_cue.TotalDurationChanged -= UpdateTotalDuration;
			_cue.PreWaitChanged -= UpdatePreWait;
			_cue.PostWaitChanged -= UpdatePostWait;
			_cue.FollowChanged -= UpdateFollowMode;
			_cue.ArmedChanged -= OnArmedVisualChanged;
			_cue.SkipIfDisarmedChanged -= OnArmedVisualChanged;
			_cue.NotesChanged -= OnNotesChanged;
			_cue.MemoChanged -= OnMemoChanged;
			_cue = null;
		}
		// Drop duplicated theme StyleBox so it is not retained after node free
		if (_colorPanel != null && IsInstanceValid(_colorPanel))
			_colorPanel.RemoveThemeStyleboxOverride("panel");
		_colorBarStyle = null;
		_issueActiveStyle = null;
		_issueIdleStyle = null;
	}

	private void OnShellColumnLayoutChanged()
	{
		if (!IsInstanceValid(this))
			return;
		ApplyColumnLayout();
	}

	/// <summary>
	/// Applies shared column widths and cuelist scale from <see cref="ShellColumnLayout"/> to this row.
	/// Color strip lives in OuterHBox (full shell height including nested children).
	/// Row layout: [Leading: Drag|Issue|Indent|Collapse|Num|Name(expand)] | [Trailing: Pre|Dur|Post|Follow].
	/// Trailing is fixed-width and non-expanding so nest indent never shifts time columns.
	/// </summary>
	public void ApplyColumnLayout()
	{
		if (_cueNumLineEdit == null)
			return;

		float numW = ShellColumnLayout.NumberWidth;
		float timeW = ShellColumnLayout.TimeWidth;
		float followW = ShellColumnLayout.FollowWidth;
		float ctrlH = ShellColumnLayout.RowControlHeight;
		float rowH = ShellColumnLayout.RowMinHeight;
		int sep = ShellColumnLayout.RowSeparation;
		int fontSize = ShellColumnLayout.FontSize;
		int iconMax = ShellColumnLayout.IconMaxWidth;

		// Keep nest gap / separations in sync when cuelist scale changes.
		var outerHBox = GetNodeOrNull<HBoxContainer>("OuterHBox");
		outerHBox?.AddThemeConstantOverride("separation", ShellColumnLayout.ColorNestGap);

		// Full-height nest indicator: beside ContentVBox, not inside the cue row.
		ConfigureColorPanelLayout();
		if (_dragButton != null)
		{
			_dragButton.CustomMinimumSize = new Vector2(ShellColumnLayout.DragWidth, ctrlH);
			_dragButton.SizeFlagsHorizontal = SizeFlags.Fill;
			_dragButton.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			_dragButton.AddThemeConstantOverride("icon_max_width", iconMax);
		}
		if (_collapseButton != null)
		{
			_collapseButton.CustomMinimumSize = new Vector2(ShellColumnLayout.CollapseWidth, ctrlH);
			_collapseButton.SizeFlagsHorizontal = SizeFlags.Fill;
			_collapseButton.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			_collapseButton.AddThemeConstantOverride("icon_max_width", iconMax);
		}
		if (_issueIndicator != null)
		{
			_issueIndicator.CustomMinimumSize = new Vector2(ShellColumnLayout.IssueWidth, ctrlH);
			_issueIndicator.SizeFlagsHorizontal = SizeFlags.Fill;
			_issueIndicator.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			_issueIndicator.AddThemeConstantOverride("icon_max_width", iconMax);
		}

		_cueNumLineEdit.CustomMinimumSize = new Vector2(numW, ctrlH);
		_cueNumLineEdit.SizeFlagsHorizontal = SizeFlags.Fill;
		_cueNumLineEdit.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		_cueNumLineEdit.AddThemeFontSizeOverride("font_size", fontSize);

		// Name absorbs indent; keep min small so deep nests shrink name, not the trailing band.
		if (_cueNameLineEdit != null)
		{
			_cueNameLineEdit.CustomMinimumSize = new Vector2(40f, ctrlH);
			_cueNameLineEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_cueNameLineEdit.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			_cueNameLineEdit.AddThemeFontSizeOverride("font_size", fontSize);
		}

		if (_memoLineEdit != null)
		{
			_memoLineEdit.CustomMinimumSize = new Vector2(40f, ctrlH);
			_memoLineEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_memoLineEdit.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			_memoLineEdit.AddThemeFontSizeOverride("font_size", fontSize);
		}

		// Fixed trailing band widths (Pre + Dur + Post + Follow + separations).
		// In memo mode only Follow remains visible — shrink the band so notes get the space.
		bool memo = _cue?.Memo == true;

		_preWaitLineEdit.CustomMinimumSize = new Vector2(timeW, ctrlH);
		_preWaitLineEdit.SizeFlagsHorizontal = SizeFlags.Fill;
		_preWaitLineEdit.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		_preWaitLineEdit.AddThemeFontSizeOverride("font_size", fontSize);

		_durationLineEdit.CustomMinimumSize = new Vector2(timeW, ctrlH);
		_durationLineEdit.SizeFlagsHorizontal = SizeFlags.Fill;
		_durationLineEdit.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		_durationLineEdit.AddThemeFontSizeOverride("font_size", fontSize);

		_postWaitLineEdit.CustomMinimumSize = new Vector2(timeW, ctrlH);
		_postWaitLineEdit.SizeFlagsHorizontal = SizeFlags.Fill;
		_postWaitLineEdit.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		_postWaitLineEdit.AddThemeFontSizeOverride("font_size", fontSize);

		if (_followButton != null)
		{
			_followButton.CustomMinimumSize = new Vector2(followW, ctrlH);
			_followButton.SizeFlagsHorizontal = SizeFlags.Fill;
			_followButton.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			_followButton.Alignment = HorizontalAlignment.Center;
			_followButton.AddThemeFontSizeOverride("font_size", fontSize);
		}

		if (_trailingHBox != null)
		{
			// Explicit fixed width so the band never shrinks or grows with indent.
			float trailW = memo
				? followW
				: timeW * 3f + followW + sep * 3f;
			_trailingHBox.CustomMinimumSize = new Vector2(trailW, ctrlH);
			_trailingHBox.SizeFlagsHorizontal = SizeFlags.Fill;
			_trailingHBox.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		}

		if (_leadingHBox != null)
		{
			_leadingHBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_leadingHBox.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			_leadingHBox.AddThemeConstantOverride("separation", sep);
		}

		if (_trailingHBox != null)
			_trailingHBox.AddThemeConstantOverride("separation", sep);

		if (_rowHBox != null)
		{
			_rowHBox.CustomMinimumSize = new Vector2(0, rowH);
			_rowHBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_rowHBox.SizeFlagsVertical = SizeFlags.ShrinkBegin;
			_rowHBox.Alignment = BoxContainer.AlignmentMode.Begin;
			_rowHBox.AddThemeConstantOverride("separation", sep);
		}

		CustomMinimumSize = new Vector2(0, rowH);

		// Nested shells must stretch to parent content width.
		SizeFlagsHorizontal = SizeFlags.ExpandFill;

		// Re-apply compact padding / font when cuelist scale changes mid-session.
		ApplyCompactFieldStyle(_cueNumLineEdit);
		ApplyCompactFieldStyle(_cueNameLineEdit);
		ApplyCompactFieldStyle(_memoLineEdit);
		ApplyCompactFieldStyle(_preWaitLineEdit);
		ApplyCompactFieldStyle(_durationLineEdit);
		ApplyCompactFieldStyle(_postWaitLineEdit);

		ApplyTreeIndent();
	}

	/// <summary>
	/// Ensures ColorPanel is a full-height left strip (row + nested ShellChildContainer).
	/// </summary>
	private void ConfigureColorPanelLayout()
	{
		if (_colorPanel == null)
			return;

		_colorPanel.CustomMinimumSize = new Vector2(ShellColumnLayout.ColorWidth, 0);
		_colorPanel.SizeFlagsHorizontal = SizeFlags.Fill;
		_colorPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
		_colorPanel.MouseFilter = MouseFilterEnum.Ignore;

		// Continuous vertical strip: no rounded corners or content inset on the bar itself.
		if (_colorBarStyle != null)
		{
			_colorBarStyle.ContentMarginLeft = 0;
			_colorBarStyle.ContentMarginRight = 0;
			_colorBarStyle.ContentMarginTop = 0;
			_colorBarStyle.ContentMarginBottom = 0;
			_colorBarStyle.SetCornerRadiusAll(0);
			_colorBarStyle.AntiAliasing = false;
		}
	}

	/// <summary>
	/// Sets the tree indent from nesting depth. Indent sits in LeadingHBox only
	/// (after Drag/Issue) and steals width from the expanding Name — trailing times stay put.
	/// </summary>
	public void ApplyTreeIndent()
	{
		if (_treeIndent == null)
			return;

		int depth = ComputeNestDepth();
		float indent = depth * ShellColumnLayout.NestIndent;
		_treeIndent.CustomMinimumSize = new Vector2(indent, 0);
		_treeIndent.SizeFlagsHorizontal = SizeFlags.Fill;
		// Hide zero-width spacer so HBox separation is not doubled at root.
		_treeIndent.Visible = depth > 0;

		// Depth affects panel margins used for column alignment.
		ApplyPanelMetricsForDepth();
		if (_panelStyle != null)
			AddThemeStyleboxOverride("panel", _panelStyle);
	}

	/// <summary>
	/// Walks <see cref="Cue.ParentId"/> to count nesting levels (0 = root list).
	/// </summary>
	private int ComputeNestDepth()
	{
		if (_cue == null)
			return 0;

		int depth = 0;
		int parentId = _cue.ParentId;
		while (parentId != -1 && depth < 64)
		{
			depth++;
			var parent = CueList.FetchCueFromId(parentId);
			if (parent == null)
				break;
			parentId = parent.ParentId;
		}
		return depth;
	}

	/// <summary>
	/// Refreshes column layout and tree indent for this shell and all nested descendants.
	/// Call after reparent / group / reorder so depth-based indent stays correct.
	/// </summary>
	public void RefreshTreeLayoutRecursive()
	{
		ApplyColumnLayout();
		if (ShellChildContainer == null)
			return;
		foreach (var child in ShellChildContainer.GetChildren())
		{
			if (child is ShellBar childShell)
				childShell.RefreshTreeLayoutRecursive();
		}
	}

	private void OnUpdateShellBar(int cueId)
	{
		if (_cue != null && _cue.Id == cueId)
		{
			// Times only — do not re-run ApplyMemoMode/ApplyColumnLayout here (that thrash can
			// interrupt or re-style the active pre/post LineEdit mid-commit).
			RefreshTimesFromCue();
			QueueRedraw();
		}
	}

	private void OnCueMediaHealthChanged(int cueId, bool hasIssue, string message)
	{
		if (_cue == null || _cue.Id != cueId)
			return;
		ApplyIssueIndicator(hasIssue, message);
	}

	/// <summary>
	/// Shows or clears the full-height issue indicator using AtlasIcons "Stop" (X).
	/// The column always reserves space after Drag so presence of the icon never shifts other columns.
	/// </summary>
	private void ApplyIssueIndicator(bool hasIssue, string message)
	{
		if (_issueIndicator == null)
			return;

		EnsureIssueStyles();

		// Always visible for layout stability; icon only when unhealthy.
		_issueIndicator.Visible = true;
		_issueIndicator.Text = string.Empty;
		_issueIndicator.Icon = hasIssue ? GetThemeIcon("Stop", "AtlasIcons") : null;
		_issueIndicator.TooltipText = hasIssue
			? (string.IsNullOrEmpty(message) ? "Issue" : message)
			: string.Empty;
		_issueIndicator.MouseFilter = hasIssue
			? MouseFilterEnum.Stop
			: MouseFilterEnum.Ignore;
		_issueIndicator.Disabled = !hasIssue;

		// Full-height tint when unhealthy; transparent when idle (column still reserved).
		var style = hasIssue ? (StyleBox)_issueActiveStyle : _issueIdleStyle;
		_issueIndicator.AddThemeStyleboxOverride("normal", style);
		_issueIndicator.AddThemeStyleboxOverride("hover", style);
		_issueIndicator.AddThemeStyleboxOverride("pressed", style);
		_issueIndicator.AddThemeStyleboxOverride("disabled", style);
		_issueIndicator.AddThemeStyleboxOverride("focus", style);
	}

	/// <summary>
	/// Builds flat styles for the full-height issue column (active danger tint vs empty slot).
	/// </summary>
	private void EnsureIssueStyles()
	{
		if (_issueActiveStyle != null)
			return;

		_issueActiveStyle = new StyleBoxFlat
		{
			BgColor = new Color(GlobalStyles.Danger.R, GlobalStyles.Danger.G, GlobalStyles.Danger.B, 0.28f),
			ContentMarginLeft = 0,
			ContentMarginRight = 0,
			ContentMarginTop = 0,
			ContentMarginBottom = 0
		};
		_issueIdleStyle = new StyleBoxEmpty();
	}

	/// <summary>
	/// Syncs issue indicator from <see cref="MediaHealthService"/> for the bound cue.
	/// </summary>
	private void RefreshIssueIndicatorFromService()
	{
		if (_cue == null || _issueIndicator == null)
			return;

		var health = GetNodeOrNull<MediaHealthService>("/root/MediaHealthService");
		if (health == null)
		{
			ApplyIssueIndicator(false, string.Empty);
			return;
		}

		bool has = health.TryGetIssue(_cue.Id, out var issue);
		ApplyIssueIndicator(has, has ? issue.Message : string.Empty);
	}

	private void DefineUi()
	{
		// Define Ui
		_colorPanel = GetNode<Panel>("%ColorPanel");
		_groupPanel = GetNode<Panel>("%GroupPanel");
		_shellPanel = GetNode<Panel>("%ShellPanel");
		_dragButton = GetNode<Button>("%DragBar");
		_collapseButton = GetNode<Button>("%CollapseButton");
		_issueIndicator = GetNodeOrNull<Button>("%IssueIndicator");
		_treeIndent = GetNodeOrNull<Control>("%TreeIndent");
		if (_issueIndicator != null)
		{
			// Column always reserved after Drag; full row height via SIZE_EXPAND_FILL.
			// Icon is AtlasIcons "Stop" (X) — sized via icon_max_width / expand_icon.
			_issueIndicator.Visible = true;
			_issueIndicator.Text = string.Empty;
			_issueIndicator.Icon = null;
			_issueIndicator.MouseFilter = MouseFilterEnum.Ignore;
			_issueIndicator.Disabled = true;
			_issueIndicator.SizeFlagsVertical = SizeFlags.ExpandFill;
			_issueIndicator.ExpandIcon = true;
			_issueIndicator.IconAlignment = HorizontalAlignment.Center;
			_issueIndicator.VerticalIconAlignment = VerticalAlignment.Center;
			_issueIndicator.AddThemeConstantOverride("icon_max_width", 12);
			// Red X via icon modulate colors
			_issueIndicator.AddThemeColorOverride("icon_normal_color", GlobalStyles.Danger);
			_issueIndicator.AddThemeColorOverride("icon_hover_color", GlobalStyles.Danger);
			_issueIndicator.AddThemeColorOverride("icon_pressed_color", GlobalStyles.Danger);
			_issueIndicator.AddThemeColorOverride("icon_disabled_color", GlobalStyles.Danger);
			ApplyIssueIndicator(false, string.Empty);
		}
		
		_rowHBox = GetNodeOrNull<HBoxContainer>("%RowHBox");
		_leadingHBox = GetNodeOrNull<HBoxContainer>("%LeadingHBox");
		_trailingHBox = GetNodeOrNull<HBoxContainer>("%TrailingHBox");
		_cueNumLineEdit = GetNode<LineEdit>("%CueNumLineEdit");
		_cueNameLineEdit = GetNode<LineEdit>("%CueNameLineEdit");
		_memoLineEdit = GetNodeOrNull<LineEdit>("%MemoLineEdit");

		_preWaitLineEdit = GetNode<LineEdit>("%PreWaitLineEdit");
		_durationLineEdit = GetNode<LineEdit>("%DurationLineEdit");
		_postWaitLineEdit = GetNode<LineEdit>("%PostWaitLineEdit");
		// Prefer new name; fall back if an older packed scene still uses the checkbox.
		_followButton = GetNodeOrNull<Button>("%FollowButton");
		if (_followButton == null)
			_followButton = GetNodeOrNull<Button>("%FollowCheckBox");
		
		ShellChildContainer = GetNode<VBoxContainer>("%ShellChildContainer");
	}

	public void SetCue(Cue cue)
	{
		if (_cue != null)
		{
			_cue.NameChanged -= UpdateName;
			_cue.CueNumChanged -= UpdateCueNum;
			_cue.ColorChanged -= UpdateColor;
			_cue.DurationChanged -= UpdateDuration;
			_cue.TotalDurationChanged -= UpdateTotalDuration;
			_cue.PreWaitChanged -= UpdatePreWait;
			_cue.PostWaitChanged -= UpdatePostWait;
			_cue.FollowChanged -= UpdateFollowMode;
			_cue.ArmedChanged -= OnArmedVisualChanged;
			_cue.SkipIfDisarmedChanged -= OnArmedVisualChanged;
			_cue.NotesChanged -= OnNotesChanged;
			_cue.MemoChanged -= OnMemoChanged;
		}
		_cue = cue;
		_cue.NameChanged += UpdateName;
		_cue.CueNumChanged += UpdateCueNum;
		_cue.ColorChanged += UpdateColor;
		_cue.DurationChanged += UpdateDuration;
		_cue.TotalDurationChanged += UpdateTotalDuration;
		_cue.PreWaitChanged += UpdatePreWait;
		_cue.PostWaitChanged += UpdatePostWait;
		_cue.FollowChanged += UpdateFollowMode;
		_cue.ArmedChanged += OnArmedVisualChanged;
		_cue.SkipIfDisarmedChanged += OnArmedVisualChanged;
		_cue.NotesChanged += OnNotesChanged;
		_cue.MemoChanged += OnMemoChanged;
		_cueNumLineEdit.Text = cue.CueNum;
		_cueNameLineEdit.Text = cue.Name;
		RefreshTimesFromCue();
		_colorBarStyle = _colorPanel.GetThemeStylebox("panel").Duplicate() as StyleBoxFlat;
		if (_colorBarStyle != null)
			_colorBarStyle.BgColor = _cue.Color;
		_colorPanel.AddThemeStyleboxOverride("panel", _colorBarStyle);
		ConfigureColorPanelLayout();
		UpdateFollowMode(cue.Follow);
		// Initialize collapse/expand UI based on children (SetCue path)
		UpdateCollapseUI();
		RefreshIssueIndicatorFromService();
		ApplyTreeIndent();
		RefreshShellChrome();
		ApplyMemoMode();
		QueueRedraw();

		_cueNumLineEdit.Editable = false;
		_cueNameLineEdit.Editable = false;
		if (_memoLineEdit != null)
			_memoLineEdit.Editable = false;
		_isEditingCueNum = false;
		_isEditingName = false;
		_isEditingMemo = false;
	}

	/// <summary>
	/// Redraws disarmed hatch when armed / skip-if-disarmed flags change.
	/// </summary>
	private void OnArmedVisualChanged(bool _)
	{
		QueueRedraw();
	}

	/// <summary>
	/// Sets the visual zebra index for this shell (even/odd row). Called by <see cref="CueList"/> after structure changes.
	/// </summary>
	/// <param name="index">0-based index in visual list order.</param>
	public void SetZebraIndex(int index)
	{
		_zebraIndex = index;
		RefreshShellChrome();
	}

	/// <summary>
	/// Refreshes pre-wait, duration, and post-wait fields from the bound cue.
	/// </summary>
	public void RefreshTimesFromCue()
	{
		if (_cue == null) return;
		if (!_isEditingPreWait)
			_preWaitLineEdit.Text = FormatDurationField(_cue.PreWait);
		// Shell shows content duration (TotalDuration when not looping includes pre/post in inspector only)
		_durationLineEdit.Text = FormatDurationField(_cue.Duration);
		if (!_isEditingPostWait)
			_postWaitLineEdit.Text = FormatDurationField(_cue.PostWait);
	}

	/// <summary>
	/// Refreshes shell fields from the bound cue after an in-place history restore.
	/// Property change events cover name/num/color/times; this covers follow and a full times pass.
	/// </summary>
	public void RefreshAllFromCue()
	{
		if (_cue == null) return;
		if (!_isEditingName)
			_cueNameLineEdit.Text = _cue.Name;
		if (!_isEditingCueNum)
			_cueNumLineEdit.Text = _cue.CueNum;
		RefreshTimesFromCue();
		if (_colorBarStyle != null)
		{
			_colorBarStyle.BgColor = _cue.Color;
			_colorPanel.AddThemeStyleboxOverride("panel", _colorBarStyle);
		}
		UpdateFollowMode(_cue.Follow);
		UpdateCollapseUI();
		RefreshIssueIndicatorFromService();
		RefreshShellChrome();
		ApplyMemoMode();
	}

	/// <summary>
	/// Swaps shell row content between standard fields and memo (notes) layout.
	/// Memo mode hides number, name, pre-wait, duration, and post-wait; follow stays.
	/// </summary>
	private void ApplyMemoMode()
	{
		bool memo = _cue?.Memo == true;

		if (_cueNumLineEdit != null)
			_cueNumLineEdit.Visible = !memo;
		if (_cueNameLineEdit != null)
			_cueNameLineEdit.Visible = !memo;
		if (_preWaitLineEdit != null)
			_preWaitLineEdit.Visible = !memo;
		if (_durationLineEdit != null)
			_durationLineEdit.Visible = !memo;
		if (_postWaitLineEdit != null)
			_postWaitLineEdit.Visible = !memo;

		if (_memoLineEdit != null)
		{
			_memoLineEdit.Visible = memo;
			if (memo && !_isEditingMemo)
			{
				_memoLineEdit.Text = FlattenNotesForShell(_cue?.Notes);
				_memoLineEdit.TooltipText = string.IsNullOrEmpty(_cue?.Notes)
					? "Memo cue — double-click to edit notes."
					: _cue.Notes;
			}
		}

		// Trailing band width changes when times hide/show.
		ApplyColumnLayout();
	}

	private void OnMemoChanged(bool _)
	{
		ApplyMemoMode();
	}

	private void OnNotesChanged(string notes)
	{
		if (_memoLineEdit == null || _isEditingMemo)
			return;
		if (_cue?.Memo != true)
			return;
		_memoLineEdit.Text = FlattenNotesForShell(notes);
		_memoLineEdit.TooltipText = string.IsNullOrEmpty(notes)
			? "Memo cue — double-click to edit notes."
			: notes;
	}

	/// <summary>Collapses multi-line notes for the single-line shell field.</summary>
	private static string FlattenNotesForShell(string notes)
	{
		if (string.IsNullOrEmpty(notes))
			return string.Empty;
		return notes
			.Replace("\r\n", " ")
			.Replace('\n', ' ')
			.Replace('\r', ' ')
			.Trim();
	}

	private static string FormatDurationField(double seconds)
	{
		if (seconds < 0)
			return "∞";
		return UiUtilities.FormatTime(seconds);
	}

	private void UpdateDuration(double duration) =>
		_durationLineEdit.Text = FormatDurationField(duration);

	private void UpdateTotalDuration(double totalDuration)
	{
		// Content duration field already updated via DurationChanged; keep tooltip with total
		if (_durationLineEdit != null && _cue != null)
			_durationLineEdit.TooltipText = _cue.TotalDuration < 0
				? "Looping"
				: $"Total (with waits): {UiUtilities.FormatTime(_cue.TotalDuration)}";
	}

	private void UpdatePreWait(double preWait)
	{
		if (!_isEditingPreWait)
			_preWaitLineEdit.Text = FormatDurationField(preWait);
	}

	private void UpdatePostWait(double postWait)
	{
		if (!_isEditingPostWait)
			_postWaitLineEdit.Text = FormatDurationField(postWait);
	}
	
	/// <summary>
	/// True when Show Mode is locking cue property / structure edits.
	/// </summary>
	private bool IsCueEditingLocked() =>
		_globalData?.Settings?.IsCueEditingLocked == true;

	/// <summary>
	/// Applies or clears inline-edit lock when show mode changes.
	/// </summary>
	private void OnShowModeChanged(bool enabled)
	{
		ApplyShowModeEditLock(enabled);
	}

	/// <summary>
	/// Disables shell-row editing chrome in Show Mode (keeps selection / collapse / playback usable).
	/// </summary>
	/// <param name="locked">True when Show Mode is active.</param>
	private void ApplyShowModeEditLock(bool locked)
	{
		if (locked)
			CancelInlineEdits();

		// Pre/post wait are normally always editable; lock them in show mode.
		ConfigureTimeFieldEditability(_preWaitLineEdit, !locked);
		ConfigureTimeFieldEditability(_postWaitLineEdit, !locked);

		if (_followButton != null)
			_followButton.Disabled = locked;

		if (_dragButton != null)
		{
			_dragButton.Disabled = locked;
			_dragButton.MouseFilter = locked ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
			_dragButton.MouseDefaultCursorShape = locked
				? CursorShape.Arrow
				: CursorShape.Drag;
		}
	}

	/// <summary>
	/// Applies focus/editable defaults so pre/post wait fields accept single-click typing.
	/// </summary>
	/// <param name="field">Pre-wait or post-wait LineEdit.</param>
	/// <param name="editable">Whether the field should accept text input.</param>
	private static void ConfigureTimeFieldEditability(LineEdit field, bool editable)
	{
		if (field == null) return;
		field.Editable = editable;
		// Always keep Click/All so a single click focuses and enters edit mode.
		field.FocusMode = editable ? FocusModeEnum.All : FocusModeEnum.None;
		field.MouseFilter = MouseFilterEnum.Stop;
		field.SelectAllOnFocus = true;
	}

	/// <summary>
	/// Wires pre/post wait for focus, submit, and context-menu input.
	/// </summary>
	/// <param name="field">Pre-wait or post-wait LineEdit.</param>
	/// <param name="isPreWait">True for pre-wait; false for post-wait.</param>
	private void WireTimeField(LineEdit field, bool isPreWait)
	{
		if (field == null) return;

		ConfigureTimeFieldEditability(field, editable: true);
		field.GuiInput += OnTimeFieldGuiInput;
		if (isPreWait)
		{
			field.FocusEntered += OnPreWaitFocusEntered;
			field.FocusExited += OnPreWaitFocusExited;
			field.TextSubmitted += OnPreWaitTextSubmitted;
			field.EditingToggled += OnPreWaitEditToggled;
		}
		else
		{
			field.FocusEntered += OnPostWaitFocusEntered;
			field.FocusExited += OnPostWaitFocusExited;
			field.TextSubmitted += OnPostWaitTextSubmitted;
			field.EditingToggled += OnPostWaitEditToggled;
		}
	}

	/// <summary>
	/// Aborts any in-progress double-click inline edit without committing.
	/// </summary>
	private void CancelInlineEdits()
	{
		if (_isEditingCueNum && _cueNumLineEdit != null)
		{
			_cueNumLineEdit.Text = _cue?.CueNum ?? string.Empty;
			_cueNumLineEdit.Editable = false;
			_cueNumLineEdit.FocusMode = FocusModeEnum.None;
			if (_cueNumLineEdit.HasFocus())
				_cueNumLineEdit.ReleaseFocus();
			_isEditingCueNum = false;
		}
		if (_isEditingName && _cueNameLineEdit != null)
		{
			_cueNameLineEdit.Text = _cue?.Name ?? string.Empty;
			_cueNameLineEdit.Editable = false;
			_cueNameLineEdit.FocusMode = FocusModeEnum.None;
			if (_cueNameLineEdit.HasFocus())
				_cueNameLineEdit.ReleaseFocus();
			_isEditingName = false;
		}
		if (_isEditingMemo && _memoLineEdit != null)
		{
			_memoLineEdit.Text = FlattenNotesForShell(_cue?.Notes);
			_memoLineEdit.Editable = false;
			_memoLineEdit.FocusMode = FocusModeEnum.None;
			if (_memoLineEdit.HasFocus())
				_memoLineEdit.ReleaseFocus();
			_isEditingMemo = false;
		}
		if (_isEditingPreWait && _preWaitLineEdit != null && _cue != null)
		{
			_preWaitLineEdit.Text = FormatDurationField(_cue.PreWait);
			if (_preWaitLineEdit.HasFocus())
				_preWaitLineEdit.ReleaseFocus();
			_isEditingPreWait = false;
		}
		if (_isEditingPostWait && _postWaitLineEdit != null && _cue != null)
		{
			_postWaitLineEdit.Text = FormatDurationField(_cue.PostWait);
			if (_postWaitLineEdit.HasFocus())
				_postWaitLineEdit.ReleaseFocus();
			_isEditingPostWait = false;
		}
	}

	private void OnCueNumGuiInput(InputEvent @event)
	{
		OnInput(@event);
		if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.DoubleClick)
		{
			if (IsCueEditingLocked()) return;
			if (_isEditingCueNum) return;
			BeginInlineCueNumEdit();
		}
	}

	/// <summary>
	/// Enters double-click inline edit for the cue number field.
	/// </summary>
	private void BeginInlineCueNumEdit()
	{
		if (_cueNumLineEdit == null || _cue == null) return;
		_isEditingCueNum = true;
		_cueNumLineEdit.Editable = true;
		_cueNumLineEdit.FocusMode = FocusModeEnum.All;
		_cueNumLineEdit.GrabFocus();
		if (_cueNumLineEdit.HasMethod("edit") && !_cueNumLineEdit.IsEditing())
			_cueNumLineEdit.Edit();
		_cueNumLineEdit.SelectAll();
	}

	private void OnCueNumEditToggled(bool editing)
	{
		if (_cue == null) return;
		if (IsCueEditingLocked())
		{
			if (editing)
				CancelInlineEdits();
			return;
		}

		if (editing)
		{
			_isEditingCueNum = true;
			return;
		}

		CommitCueNumEdit(releaseFocus: false);
	}

	private void OnCueNumFocusExited()
	{
		CommitCueNumEdit(releaseFocus: false);
	}

	private void OnCueNumTextSubmitted(string _)
	{
		CommitCueNumEdit(releaseFocus: true);
	}

	/// <summary>
	/// Commits double-click cue-number edit to the model and refreshes inspectors.
	/// </summary>
	/// <param name="releaseFocus">When true, clear focus after commit (Enter path).</param>
	private void CommitCueNumEdit(bool releaseFocus)
	{
		if (!_isEditingCueNum || _cueNumLineEdit == null || _cue == null)
			return;

		_isEditingCueNum = false;
		_cueNumLineEdit.Editable = false;
		_cueNumLineEdit.FocusMode = FocusModeEnum.None;

		if (IsCueEditingLocked() || _globalData?.HistoryManager?.IsRestoring == true)
		{
			_cueNumLineEdit.Text = _cue.CueNum ?? string.Empty;
			if (releaseFocus && _cueNumLineEdit.HasFocus())
				_cueNumLineEdit.CallDeferred(Control.MethodName.ReleaseFocus);
			return;
		}

		string newNum = _cueNumLineEdit.Text ?? string.Empty;
		if (!string.Equals(_cue.CueNum ?? string.Empty, newNum, System.StringComparison.Ordinal))
		{
			_globalData?.HistoryManager?.RecordCueChange(_cue.Id, "Edit cue number");
			_cue.CueNum = newNum;
			NotifyInspectorsOfCueEdit();
		}

		if (releaseFocus && _cueNumLineEdit.HasFocus())
			_cueNumLineEdit.CallDeferred(Control.MethodName.ReleaseFocus);
	}

	private void OnNameGuiInput(InputEvent @event)
	{
		OnInput(@event);
		if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.DoubleClick)
		{
			if (IsCueEditingLocked()) return;
			if (_isEditingName) return;
			BeginInlineNameEdit();
		}
	}

	/// <summary>
	/// Enters double-click inline edit for the cue name field.
	/// </summary>
	private void BeginInlineNameEdit()
	{
		if (_cueNameLineEdit == null || _cue == null) return;
		_isEditingName = true;
		_cueNameLineEdit.Editable = true;
		_cueNameLineEdit.FocusMode = FocusModeEnum.All;
		_cueNameLineEdit.GrabFocus();
		if (_cueNameLineEdit.HasMethod("edit") && !_cueNameLineEdit.IsEditing())
			_cueNameLineEdit.Edit();
		_cueNameLineEdit.SelectAll();
	}

	private void OnNameEditToggled(bool editing)
	{
		if (_cue == null) return;
		if (IsCueEditingLocked())
		{
			if (editing)
				CancelInlineEdits();
			return;
		}

		if (editing)
		{
			_isEditingName = true;
			return;
		}

		CommitNameEdit(releaseFocus: false);
	}

	private void OnNameFocusExited()
	{
		CommitNameEdit(releaseFocus: false);
	}

	private void OnNameTextSubmitted(string _)
	{
		CommitNameEdit(releaseFocus: true);
	}

	/// <summary>
	/// Commits double-click cue-name edit to the model and refreshes inspectors.
	/// </summary>
	/// <param name="releaseFocus">When true, clear focus after commit (Enter path).</param>
	private void CommitNameEdit(bool releaseFocus)
	{
		if (!_isEditingName || _cueNameLineEdit == null || _cue == null)
			return;

		_isEditingName = false;
		_cueNameLineEdit.Editable = false;
		_cueNameLineEdit.FocusMode = FocusModeEnum.None;

		if (IsCueEditingLocked() || _globalData?.HistoryManager?.IsRestoring == true)
		{
			_cueNameLineEdit.Text = _cue.Name ?? string.Empty;
			if (releaseFocus && _cueNameLineEdit.HasFocus())
				_cueNameLineEdit.CallDeferred(Control.MethodName.ReleaseFocus);
			return;
		}

		string newName = _cueNameLineEdit.Text ?? string.Empty;
		if (!string.Equals(_cue.Name ?? string.Empty, newName, System.StringComparison.Ordinal))
		{
			_globalData?.HistoryManager?.RecordCueChange(_cue.Id, "Edit cue name");
			_cue.Name = newName;
			NotifyInspectorsOfCueEdit();
		}

		if (releaseFocus && _cueNameLineEdit.HasFocus())
			_cueNameLineEdit.CallDeferred(Control.MethodName.ReleaseFocus);
	}

	private void OnMemoGuiInput(InputEvent @event)
	{
		OnInput(@event);
		if (_memoLineEdit == null || _cue?.Memo != true) return;
		if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.DoubleClick)
		{
			if (IsCueEditingLocked()) return;
			if (_isEditingMemo) return;
			_memoLineEdit.Editable = true;
			_memoLineEdit.FocusMode = FocusModeEnum.Click;
			_memoLineEdit.GrabFocus();
			_isEditingMemo = true;
		}
	}

	private void OnMemoEditToggled(bool editing)
	{
		if (_cue == null || _memoLineEdit == null) return;
		if (_isEditingMemo && editing == false)
		{
			_memoLineEdit.Editable = false;
			string newNotes = _memoLineEdit.Text ?? string.Empty;
			// Compare against flattened form so multi-line notes are not wiped by an unchanged display.
			string flatCurrent = FlattenNotesForShell(_cue.Notes);
			if (!string.Equals(flatCurrent, newNotes, System.StringComparison.Ordinal))
			{
				_globalData?.HistoryManager?.RecordCueChange(_cue.Id, "Edit cue notes");
				_cue.Notes = newNotes;
				NotifyInspectorsOfCueEdit();
			}
			else
			{
				_memoLineEdit.Text = flatCurrent;
			}
			_memoLineEdit.FocusMode = FocusModeEnum.None;
			_isEditingMemo = false;
			_memoLineEdit.TooltipText = string.IsNullOrEmpty(_cue.Notes)
				? "Memo cue — double-click to edit notes."
				: _cue.Notes;
		}
		else if (_isEditingMemo && !editing)
		{
			_memoLineEdit.Editable = true;
			_memoLineEdit.FocusMode = FocusModeEnum.Click;
			_memoLineEdit.GrabFocus();
			_isEditingMemo = true;
		}
	}

	private void OnPreWaitFocusEntered()
	{
		if (_cue == null || _preWaitLineEdit == null) return;
		if (IsCueEditingLocked())
		{
			_preWaitLineEdit.Text = FormatDurationField(_cue.PreWait);
			_preWaitLineEdit.CallDeferred(Control.MethodName.ReleaseFocus);
			return;
		}
		_isEditingPreWait = true;
		// Godot 4.4+: keyboard focus alone does not always enter edit mode — force it.
		if (_preWaitLineEdit.HasMethod("edit") && !_preWaitLineEdit.IsEditing())
			_preWaitLineEdit.Edit();
	}

	private void OnPostWaitFocusEntered()
	{
		if (_cue == null || _postWaitLineEdit == null) return;
		if (IsCueEditingLocked())
		{
			_postWaitLineEdit.Text = FormatDurationField(_cue.PostWait);
			_postWaitLineEdit.CallDeferred(Control.MethodName.ReleaseFocus);
			return;
		}
		_isEditingPostWait = true;
		if (_postWaitLineEdit.HasMethod("edit") && !_postWaitLineEdit.IsEditing())
			_postWaitLineEdit.Edit();
	}

	private void OnPreWaitTextSubmitted(string _)
	{
		CommitPreWaitEdit(releaseFocus: true);
	}

	private void OnPostWaitTextSubmitted(string _)
	{
		CommitPostWaitEdit(releaseFocus: true);
	}

	private void OnPreWaitFocusExited()
	{
		// Commit when leaving the field (click away / tab). EditingToggled may also fire —
		// Commit* is idempotent via the editing flag.
		CommitPreWaitEdit(releaseFocus: false);
	}

	private void OnPostWaitFocusExited()
	{
		CommitPostWaitEdit(releaseFocus: false);
	}

	private void OnPreWaitEditToggled(bool editing)
	{
		if (_cue == null) return;
		if (IsCueEditingLocked())
		{
			if (editing && _preWaitLineEdit != null)
			{
				_preWaitLineEdit.Text = FormatDurationField(_cue.PreWait);
				_preWaitLineEdit.CallDeferred(Control.MethodName.ReleaseFocus);
			}
			_isEditingPreWait = false;
			return;
		}

		if (editing)
		{
			_isEditingPreWait = true;
			return;
		}

		// Edit mode ended (Enter / Unedit / click away). Commit once.
		CommitPreWaitEdit(releaseFocus: false);
	}

	private void OnPostWaitEditToggled(bool editing)
	{
		if (_cue == null) return;
		if (IsCueEditingLocked())
		{
			if (editing && _postWaitLineEdit != null)
			{
				_postWaitLineEdit.Text = FormatDurationField(_cue.PostWait);
				_postWaitLineEdit.CallDeferred(Control.MethodName.ReleaseFocus);
			}
			_isEditingPostWait = false;
			return;
		}

		if (editing)
		{
			_isEditingPostWait = true;
			return;
		}

		CommitPostWaitEdit(releaseFocus: false);
	}

	/// <summary>
	/// Parses and applies the pre-wait field. Safe to call multiple times for the same edit session.
	/// </summary>
	/// <param name="releaseFocus">When true, unfocus after commit (Enter / TextSubmitted path).</param>
	private void CommitPreWaitEdit(bool releaseFocus)
	{
		if (!_isEditingPreWait || _preWaitLineEdit == null || _cue == null)
			return;

		// Clear flag first so nested Unedit/FocusExited/UpdateShellBar cannot re-enter.
		_isEditingPreWait = false;

		if (IsCueEditingLocked() || _globalData?.HistoryManager?.IsRestoring == true)
		{
			_preWaitLineEdit.Text = FormatDurationField(_cue.PreWait);
			if (releaseFocus && _preWaitLineEdit.HasFocus())
				_preWaitLineEdit.CallDeferred(Control.MethodName.ReleaseFocus);
			return;
		}

		var ret = UiUtilities.ParseAndFormatTime(_preWaitLineEdit.Text, out var time, out bool isValid);
		if (string.IsNullOrEmpty(ret) || !isValid)
		{
			_preWaitLineEdit.Text = FormatDurationField(_cue.PreWait);
		}
		else if (System.Math.Abs(_cue.PreWait - time) >= 1e-9)
		{
			_globalData?.HistoryManager?.RecordCueChange(_cue.Id, "Edit pre-wait");
			_cue.PreWait = time;
			_cue.CalculateTotalDuration();
			_preWaitLineEdit.Text = ret;
			NotifyInspectorsOfCueEdit();
		}
		else
		{
			_preWaitLineEdit.Text = ret;
		}

		if (releaseFocus && _preWaitLineEdit.HasFocus())
			_preWaitLineEdit.CallDeferred(Control.MethodName.ReleaseFocus);
	}

	/// <summary>
	/// Parses and applies the post-wait field. Safe to call multiple times for the same edit session.
	/// </summary>
	/// <param name="releaseFocus">When true, unfocus after commit (Enter / TextSubmitted path).</param>
	private void CommitPostWaitEdit(bool releaseFocus)
	{
		if (!_isEditingPostWait || _postWaitLineEdit == null || _cue == null)
			return;

		_isEditingPostWait = false;

		if (IsCueEditingLocked() || _globalData?.HistoryManager?.IsRestoring == true)
		{
			_postWaitLineEdit.Text = FormatDurationField(_cue.PostWait);
			if (releaseFocus && _postWaitLineEdit.HasFocus())
				_postWaitLineEdit.CallDeferred(Control.MethodName.ReleaseFocus);
			return;
		}

		var ret = UiUtilities.ParseAndFormatTime(_postWaitLineEdit.Text, out var time, out bool isValid);
		if (string.IsNullOrEmpty(ret) || !isValid)
		{
			_postWaitLineEdit.Text = FormatDurationField(_cue.PostWait);
		}
		else if (System.Math.Abs(_cue.PostWait - time) >= 1e-9)
		{
			_globalData?.HistoryManager?.RecordCueChange(_cue.Id, "Edit post-wait");
			_cue.PostWait = time;
			_cue.CalculateTotalDuration();
			_postWaitLineEdit.Text = ret;
			NotifyInspectorsOfCueEdit();
		}
		else
		{
			_postWaitLineEdit.Text = ret;
		}

		if (releaseFocus && _postWaitLineEdit.HasFocus())
			_postWaitLineEdit.CallDeferred(Control.MethodName.ReleaseFocus);
	}

	/// <summary>
	/// Continue-mode cycle on the shell row: None → Auto-continue → Auto-follow → None.
	/// </summary>
	private void OnFollowButtonPressed()
	{
		if (_cue == null) return;
		if (IsCueEditingLocked()) return;
		if (_globalData?.HistoryManager?.IsRestoring == true) return;

		var desired = _cue.Follow switch
		{
			FollowType.None => FollowType.Continue,
			FollowType.Continue => FollowType.Follow,
			_ => FollowType.None
		};
		if (_cue.Follow == desired) return;

		_globalData?.HistoryManager?.RecordCueChange(_cue.Id, "Edit continue mode");
		_cue.Follow = desired;
		// UpdateFollowMode runs via FollowChanged; still notify inspectors for full sync.
		NotifyInspectorsOfCueEdit();
	}

	/// <summary>
	/// Updates the continue-mode button glyph and tooltip from the cue model.
	/// </summary>
	/// <param name="follow">Current continue mode.</param>
	private void UpdateFollowMode(FollowType follow)
	{
		if (_followButton == null) return;

		switch (follow)
		{
			case FollowType.Continue:
				_followButton.Text = "→";
				_followButton.TooltipText = "Auto-continue: next cue starts after post-wait.\nClick to cycle → Auto-follow → None";
				_followButton.Modulate = Colors.White;
				break;
			case FollowType.Follow:
				_followButton.Text = "↳";
				_followButton.TooltipText = "Auto-follow: next cue starts when this cue completes.\nClick to cycle → None → Auto-continue";
				_followButton.Modulate = Colors.White;
				break;
			default:
				_followButton.Text = "";
				_followButton.TooltipText = "Continue mode: None.\nClick to set Auto-continue (→), then Auto-follow (↳).";
				// Keep hit target visible without drawing a permanent glyph.
				_followButton.Modulate = new Color(1, 1, 1, 0.35f);
				break;
		}
	}

	/// <summary>
	/// Pushes shell-row model edits into inspectors (and any other SyncShellInspector listeners).
	/// Name also notifies via NameChanged; cue number / waits / follow need an explicit refresh.
	/// </summary>
	private void NotifyInspectorsOfCueEdit()
	{
		if (_cue == null) return;
		_globalSignals?.EmitSignal(nameof(GlobalSignals.UpdateShellBar), _cue.Id);
		_globalSignals?.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
	}


	private void UpdateName(string name)
	{
		if (!_isEditingName)
		{
			_cueNameLineEdit.Text = name;
		}
	}

	private void UpdateCueNum(string cueNum)
	{
		if (!_isEditingCueNum)
		{
			_cueNumLineEdit.Text = cueNum;
		}
	}

	private void UpdateColor(Color color)
	{
		_colorBarStyle ??= _colorPanel.GetThemeStylebox("panel").Duplicate() as StyleBoxFlat;
		if (_colorBarStyle == null) return;
		_colorBarStyle.BgColor = _cue.Color;
		_colorPanel.AddThemeStyleboxOverride("panel", _colorBarStyle);
		ConfigureColorPanelLayout();
		// Shell body wash follows cue colour.
		RefreshShellChrome();
	}

	public void RelationshipChanged()
	{
		// Always update collapse UI state. Do not early-return on transient mismatch;
		// after sync in reorder or structure the counts should align.
		if (_cue != null && _cue.ChildCues.Count > 0 &&
			ShellChildContainer.GetChildCount() != _cue.ChildCues.Count)
		{
			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
				$"ShellBar:RelationshipChanged - Mismatch of data with UI elements for cue {_cue.Id}", (int)LogType.Warning);
		}

		UpdateCollapseUI();
		// Hierarchy changes (group/reorder) can change nesting depth for this row and children.
		RefreshTreeLayoutRecursive();
	}

	private void CollapsedPressed()
	{
		if (_cue == null) return;
		_cue.Expanded = !_cue.Expanded;
		UpdateCollapseUI();
		// Visibility of nested rows changed → re-stripe even/odd.
		_globalData?.Cuelist?.RefreshShellZebra();
	}

	/// <summary>
	/// Updates the expand/collapse chevron and nested child visibility.
	/// Row order is Color | Drag | Issue | TreeIndent | Collapse | … so left chrome stays flush
	/// while collapse/name step right with nest depth.
	/// </summary>
	private void UpdateCollapseUI()
	{
		if (_cue == null || _collapseButton == null) return;

		bool hasChildren = _cue.ChildCues.Count > 0;

		// Always keep the column so parent/child names stay vertically aligned at a given depth.
		_collapseButton.Visible = true;
		_collapseButton.Disabled = !hasChildren;
		_collapseButton.MouseFilter = hasChildren
			? MouseFilterEnum.Stop
			: MouseFilterEnum.Ignore;

		if (hasChildren)
		{
			_collapseButton.Icon = GetThemeIcon(_cue.Expanded ? "Down" : "Right", "AtlasIcons");
			if (ShellChildContainer != null)
				ShellChildContainer.Visible = _cue.Expanded;
		}
		else
		{
			_collapseButton.Icon = null;
			if (ShellChildContainer != null)
				ShellChildContainer.Visible = false;
		}
	}

	/// <summary>
	/// Public helper to set the expanded/collapsed state for this group (used by Expand All).
	/// </summary>
	/// <param name="expanded">Whether children should be shown.</param>
	public void SetExpanded(bool expanded)
	{
		if (_cue == null || _cue.ChildCues.Count == 0) return;
		_cue.Expanded = expanded;
		UpdateCollapseUI();
	}

	private void OnMouseEntered()
	{
		// During reorder the drop indicator owns highlight; mouse enter/exit also
		// thrash between the floating preview and shells (blue hover flicker).
		if (IsReorderActive())
		{
			ClearHoverChrome();
			return;
		}

		_hovered = true;
		if (!Selected)
			RefreshShellChrome();
	}
	
	private void OnMouseExited()
	{
		if (!_hovered)
			return;
		_hovered = false;
		if (!Selected)
			RefreshShellChrome();
	}

	/// <summary>
	/// True while the cuelist drag-reorder session is active.
	/// </summary>
	private bool IsReorderActive() =>
		_globalData?.Cuelist?.IsReordering == true;

	/// <summary>
	/// Clears hover chrome without affecting selection. Used when reorder starts/ends.
	/// </summary>
	public void ClearHoverChrome()
	{
		if (!_hovered)
			return;
		_hovered = false;
		if (!Selected)
			RefreshShellChrome();
	}
	

	private void OnInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mouseEvent || !mouseEvent.Pressed)
			return;

		// Right-click: cue context menu (Cut / Copy / Paste / … with hotkeys).
		if (mouseEvent.ButtonIndex == MouseButton.Right)
		{
			ShowShellContextMenu();
			AcceptEvent();
			return;
		}

		if (mouseEvent.ButtonIndex != MouseButton.Left)
			return;

		// Selection logging is low-volume; retained or replace with conditional _globalSignals if desired.
		if (Input.IsKeyPressed(Key.Shift))
		{
			_globalData.ShellSelection.SelectThrough(CueList.FetchCueFromId(CueId));
			return;
		}

		if (Input.IsKeyPressed(Key.Ctrl))
		{
			_globalData.ShellSelection.AddSelection(CueList.FetchCueFromId(CueId));
			return;
		}
		
		//Select single shell
		_globalData.ShellSelection.SelectIndividualShell(CueList.FetchCueFromId(CueId));
	}

	/// <summary>
	/// Time-field GuiInput: right-click → shell context menu; left-click → select row
	/// without AcceptEvent so the LineEdit can still take focus and enter edit mode.
	/// </summary>
	private void OnTimeFieldGuiInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mouseEvent || !mouseEvent.Pressed)
			return;

		if (mouseEvent.ButtonIndex == MouseButton.Right)
		{
			ShowShellContextMenu();
			AcceptEvent();
			return;
		}

		if (mouseEvent.ButtonIndex != MouseButton.Left)
			return;

		// Select this cue (shift/ctrl multi-select) but do not AcceptEvent — the LineEdit
		// must still receive the click to focus and start editing pre/post wait.
		if (Input.IsKeyPressed(Key.Shift))
		{
			_globalData?.ShellSelection?.SelectThrough(CueList.FetchCueFromId(CueId));
			return;
		}

		if (Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Meta))
		{
			_globalData?.ShellSelection?.AddSelection(CueList.FetchCueFromId(CueId));
			return;
		}

		_globalData?.ShellSelection?.SelectIndividualShell(CueList.FetchCueFromId(CueId));
	}

	/// <summary>
	/// Builds and shows the shell cue-editing context menu at the mouse position.
	/// Ensures this cue is selected first so cut/copy/delete apply to a sensible selection.
	/// </summary>
	private void ShowShellContextMenu()
	{
		EnsureSelectedForContextMenu();

		if (_contextMenu == null || !IsInstanceValid(_contextMenu))
		{
			_contextMenu = new PopupMenu
			{
				Name = "ShellContextMenu",
				HideOnItemSelection = true
			};
			AddChild(_contextMenu);
			_contextMenu.IdPressed += OnShellContextMenuIdPressed;
		}

		_contextMenu.Clear();
		bool locked = IsCueEditingLocked();
		if (locked)
		{
			// Show Mode: copy is read-only; mutating actions are omitted.
			AddContextMenuItem("Copy", "CopySelectedCues", ShellContextMenuId.Copy);
		}
		else
		{
			AddContextMenuItem("Cut", "CutSelectedCues", ShellContextMenuId.Cut);
			AddContextMenuItem("Copy", "CopySelectedCues", ShellContextMenuId.Copy);
			AddContextMenuItem("Paste", "PasteCues", ShellContextMenuId.Paste);
			_contextMenu.AddSeparator();
			AddContextMenuItem("Duplicate", "DuplicateSelectedCues", ShellContextMenuId.Duplicate);
			AddContextMenuItem("Delete", "DeleteCue", ShellContextMenuId.Delete);
			_contextMenu.AddSeparator();
			AddContextMenuItem("Group", "GroupSelectedCues", ShellContextMenuId.Group);
			AddContextMenuItem("Create Cue", "CreateCue", ShellContextMenuId.CreateCue);
		}

		// Popup at cursor (screen coords — PopupMenu is a Window).
		_contextMenu.ResetSize();
		_contextMenu.Position = DisplayServer.MouseGetPosition();
		_contextMenu.Popup();
	}

	/// <summary>
	/// Adds a context-menu row with the action title on the left and a right-aligned hotkey
	/// via <see cref="PopupMenu.SetItemShortcut"/> (native accelerator column layout).
	/// </summary>
	/// <param name="title">Left-side action label.</param>
	/// <param name="inputAction">InputMap action used for the displayed shortcut.</param>
	/// <param name="id">Menu id for <see cref="OnShellContextMenuIdPressed"/>.</param>
	private void AddContextMenuItem(string title, string inputAction, ShellContextMenuId id)
	{
		_contextMenu.AddItem(title, (int)id);
		int index = _contextMenu.ItemCount - 1;
		var shortcut = CreateShortcutFromAction(inputAction);
		if (shortcut == null || shortcut.Events.Count == 0)
			return;

		// Display-only: InputActionsListener already handles these keys globally.
		// Disabled shortcuts still paint in the right-hand accelerator column.
		_contextMenu.SetItemShortcut(index, shortcut, global: false);
		_contextMenu.SetItemShortcutDisabled(index, true);
	}

	/// <summary>
	/// Builds a <see cref="Shortcut"/> from the live InputMap binding(s) for <paramref name="action"/>.
	/// </summary>
	/// <param name="action">Project InputMap action name.</param>
	/// <returns>Shortcut with duplicated events, or empty when unbound.</returns>
	private static Shortcut CreateShortcutFromAction(string action)
	{
		var shortcut = new Shortcut();
		if (string.IsNullOrEmpty(action) || !InputMap.HasAction(action))
			return shortcut;

		var events = new Godot.Collections.Array();
		foreach (InputEvent ev in InputMap.ActionGetEvents(action))
		{
			if (ev == null) continue;
			events.Add((InputEvent)ev.Duplicate());
		}
		shortcut.Events = events;
		return shortcut;
	}

	/// <summary>
	/// If this shell is not already part of the selection, select it alone before context actions.
	/// Keeps multi-select when right-clicking a cue that is already selected.
	/// </summary>
	private void EnsureSelectedForContextMenu()
	{
		var cue = CueList.FetchCueFromId(CueId);
		if (cue == null || _globalData?.ShellSelection == null) return;

		var selected = ShellSelection.SelectedCues;
		if (selected != null && selected.Contains(cue))
			return;

		_globalData.ShellSelection.SelectIndividualShell(cue);
	}

	/// <summary>
	/// Dispatches context menu choices to the same GlobalSignals as the Input Map hotkeys.
	/// </summary>
	/// <param name="id"><see cref="ShellContextMenuId"/> value.</param>
	private void OnShellContextMenuIdPressed(long id)
	{
		if (_globalSignals == null) return;

		switch ((ShellContextMenuId)id)
		{
			case ShellContextMenuId.Cut:
				_globalSignals.EmitSignal(nameof(GlobalSignals.CutSelectedCues));
				break;
			case ShellContextMenuId.Copy:
				_globalSignals.EmitSignal(nameof(GlobalSignals.CopySelectedCues));
				break;
			case ShellContextMenuId.Paste:
				_globalSignals.EmitSignal(nameof(GlobalSignals.PasteCues));
				break;
			case ShellContextMenuId.Duplicate:
				_globalSignals.EmitSignal(nameof(GlobalSignals.DuplicateSelectedCues));
				break;
			case ShellContextMenuId.Delete:
				_globalSignals.EmitSignal(nameof(GlobalSignals.DeleteSelectedCues));
				break;
			case ShellContextMenuId.Group:
				_globalSignals.EmitSignal(nameof(GlobalSignals.GroupSelectedCues));
				break;
			case ShellContextMenuId.CreateCue:
				_globalSignals.EmitSignal(nameof(GlobalSignals.CreateCue));
				break;
		}
	}

	public void Focus()
	{
		Selected = true;
		RefreshShellChrome();
	}
	

	public void Deselect()
	{
		Selected = false;
		// Keep legacy overlays hidden; chrome is the PanelContainer style only.
		if (_shellPanel != null)
			_shellPanel.Visible = false;
		if (_groupPanel != null)
			_groupPanel.Visible = false;
		RefreshShellChrome();
	}

	public void Select()
	{
		Selected = true;
		if (_shellPanel != null)
			_shellPanel.Visible = false;
		if (_groupPanel != null)
			_groupPanel.Visible = false;
		RefreshShellChrome();
	}

	/// <summary>
	/// Ensures the per-row StyleBox exists with locked metrics (no layout jump on state change).
	/// </summary>
	private void EnsurePanelStyle()
	{
		if (_panelStyle != null) return;
		_panelStyle = new StyleBoxFlat();
		ApplyPanelMetricsForDepth();
	}

	/// <summary>
	/// Root shells keep L/R content margins; nested shells use zero horizontal inset so their
	/// trailing Pre/Dur/Post/Follow columns line up with the root grid (margins would stack
	/// per nest level and shift those fields left).
	/// </summary>
	private void ApplyPanelMetricsForDepth()
	{
		if (_panelStyle == null) return;
		GlobalStyles.ApplyShellChromeMetrics(_panelStyle);

		int depth = ComputeNestDepth();
		if (depth > 0)
		{
			_panelStyle.ContentMarginLeft = 0;
			_panelStyle.ContentMarginRight = 0;
			// Nested selection still uses a left accent via colour strip; skip L/R border inset.
			_panelStyle.BorderWidthLeft = 0;
			_panelStyle.BorderWidthRight = 0;
		}
	}

	/// <summary>
	/// Rebuilds shell body colour: zebra base × desaturated cue wash × hover/selection.
	/// Border width/margins stay fixed per depth (root vs nested).
	/// </summary>
	public void RefreshShellChrome()
	{
		EnsurePanelStyle();
		ApplyPanelMetricsForDepth();

		Color cueColor = _cue != null ? _cue.Color : Colors.Black;
		bool even = (_zebraIndex % 2) == 0;
		// Hover wash is suppressed while reordering (drop indicator is the visual guide).
		bool showHover = _hovered && !IsReorderActive();
		var state = Selected
			? GlobalStyles.ShellChromeState.Selected
			: (showHover ? GlobalStyles.ShellChromeState.Hover : GlobalStyles.ShellChromeState.Normal);

		_panelStyle.BgColor = GlobalStyles.ShellBackgroundFor(cueColor, even, state);
		// Nested shells: no side border (avoids extra inset); root keeps state border colour.
		_panelStyle.BorderColor = ComputeNestDepth() > 0
			? new Color(0, 0, 0, 0)
			: GlobalStyles.ShellBorderFor(state);
		AddThemeStyleboxOverride("panel", _panelStyle);
		// Hatch overlay is drawn in _Draw; keep it in sync with chrome refreshes.
		QueueRedraw();
	}

	/// <summary>
	/// Draws disarmed hatch behind shell text: one diagonal direction when disarmed,
	/// both directions (X hatch) when also skip-if-disarmed. Limited to the main row
	/// (not nested child shells under an expanded group).
	/// </summary>
	public override void _Draw()
	{
		base._Draw();
		if (_cue == null || _cue.Armed)
			return;

		float rowH = ShellColumnLayout.RowMinHeight;
		if (_rowHBox != null && _rowHBox.Size.Y > 1f)
			rowH = _rowHBox.Size.Y;

		float width = Size.X;
		if (width < 2f || rowH < 2f)
			return;

		// Subtle lines so text stays readable over the hatch.
		var lineColor = new Color(0.85f, 0.9f, 0.95f, 0.18f);
		const float lineWidth = 1.25f;
		const float spacing = 10f;

		// Primary diagonal set (top-left → bottom-right).
		DrawDiagonalHatch(width, rowH, spacing, lineColor, lineWidth, forward: true);

		// Second set completes X hatch when skip-if-disarmed is enabled.
		if (_cue.SkipIfDisarmed)
			DrawDiagonalHatch(width, rowH, spacing, lineColor, lineWidth, forward: false);
	}

	/// <summary>
	/// Draws parallel diagonal lines across a row-sized rectangle at the top of this shell.
	/// </summary>
	/// <param name="width">Row width in local coordinates.</param>
	/// <param name="height">Row height in local coordinates.</param>
	/// <param name="spacing">Distance between parallel hatch lines.</param>
	/// <param name="color">Line colour.</param>
	/// <param name="lineWidth">Stroke width.</param>
	/// <param name="forward">True for \ direction; false for / direction.</param>
	private void DrawDiagonalHatch(float width, float height, float spacing, Color color, float lineWidth, bool forward)
	{
		// Offset range covers full rectangle with parallel diagonals.
		float start = -height;
		float end = width + height;
		for (float offset = start; offset <= end; offset += spacing)
		{
			Vector2 a;
			Vector2 b;
			if (forward)
			{
				// Line family: y - x = c  →  points (offset, 0) through (offset+height, height)
				a = new Vector2(offset, 0f);
				b = new Vector2(offset + height, height);
			}
			else
			{
				// Line family: y + x = c
				a = new Vector2(offset, 0f);
				b = new Vector2(offset - height, height);
			}

			if (!ClipLineToRect(ref a, ref b, width, height))
				continue;
			DrawLine(a, b, color, lineWidth, antialiased: true);
		}
	}

	/// <summary>
	/// Liang–Barsky style clip of a line segment to the [0,width]×[0,height] rect.
	/// </summary>
	/// <returns>False if the segment lies entirely outside the rect.</returns>
	private static bool ClipLineToRect(ref Vector2 a, ref Vector2 b, float width, float height)
	{
		float x0 = a.X, y0 = a.Y, x1 = b.X, y1 = b.Y;
		float dx = x1 - x0;
		float dy = y1 - y0;
		float t0 = 0f;
		float t1 = 1f;

		// p/q edges: left, right, bottom, top
		if (!ClipEdge(-dx, x0, ref t0, ref t1)) return false;
		if (!ClipEdge(dx, width - x0, ref t0, ref t1)) return false;
		if (!ClipEdge(-dy, y0, ref t0, ref t1)) return false;
		if (!ClipEdge(dy, height - y0, ref t0, ref t1)) return false;

		a = new Vector2(x0 + t0 * dx, y0 + t0 * dy);
		b = new Vector2(x0 + t1 * dx, y0 + t1 * dy);
		return true;
	}

	private static bool ClipEdge(float p, float q, ref float t0, ref float t1)
	{
		if (System.Math.Abs(p) < 1e-8f)
			return q >= 0f;
		float r = q / p;
		if (p < 0f)
		{
			if (r > t1) return false;
			if (r > t0) t0 = r;
		}
		else
		{
			if (r < t0) return false;
			if (r < t1) t1 = r;
		}
		return true;
	}

	// Re-ordering functions

	/// <summary>
	/// Starts reorder on left-press. Accepts the event so BaseButton does not latch pressed;
	/// mouse-up is owned by <see cref="CueReorder"/> via CueList._Input.
	/// </summary>
	private void OnDragBarGuiInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mb)
			return;
		if (mb.ButtonIndex != MouseButton.Left || !mb.Pressed)
			return;

		// Prevent BaseButton internal press-attempt (would stick if mouse-up is handled globally).
		AcceptEvent();
		_dragButton?.SetPressedNoSignal(false);

		if (IsCueEditingLocked())
			return;
		if (_globalData?.Cuelist == null)
			return;
		_globalData.Cuelist.StartReorder(this);
	}

	/// <summary>
	/// Clears any residual pressed/focus state on the reorder grabber after a drag session.
	/// </summary>
	public void ReleaseDragGrabber()
	{
		if (_dragButton == null || !IsInstanceValid(_dragButton))
			return;

		_dragButton.KeepPressedOutside = false;
		_dragButton.SetPressedNoSignal(false);
		_dragButton.ButtonPressed = false;
		if (_dragButton.HasFocus())
			_dragButton.ReleaseFocus();
	}
}
