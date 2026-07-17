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
	
	public bool Selected = false;

	/// <summary>Visual row index for zebra striping (0-based, includes nested visible shells).</summary>
	private int _zebraIndex;

	/// <summary>True while the mouse is over this shell (and not selected).</summary>
	private bool _hovered;

	/// <summary>Per-row panel style (metrics fixed; colours rebuilt for zebra + cue + state).</summary>
	private StyleBoxFlat _panelStyle;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		
		DefineUi();
		ApplyShellChromeDefaults();
		
		_cueNumLineEdit.Editable = false;
		_cueNameLineEdit.Editable = false;
		
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
		_cueNameLineEdit.GuiInput += OnNameGuiInput;
		_cueNameLineEdit.EditingToggled += OnNameEditToggled;
		_preWaitLineEdit.EditingToggled += OnPreWaitEditToggled;
		_postWaitLineEdit.EditingToggled += OnPostWaitEditToggled;
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

		ShellColumnLayout.Changed += OnShellColumnLayoutChanged;
		ApplyColumnLayout();
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

		ApplyCompactFieldStyle(_cueNumLineEdit);
		ApplyCompactFieldStyle(_cueNameLineEdit);
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
		_dragButton.AddThemeConstantOverride("icon_max_width", 14);
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
	/// </summary>
	private static void ApplyCompactFieldStyle(LineEdit field)
	{
		if (field == null) return;
		field.CustomMinimumSize = new Vector2(field.CustomMinimumSize.X, ShellColumnLayout.RowControlHeight);
		field.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		field.Alignment = HorizontalAlignment.Left;
		// Empty-ish flat style keeps height under control without fighting the global theme heavily.
		var compact = new StyleBoxFlat
		{
			BgColor = new Color(0.12f, 0.12f, 0.12f, 0.55f),
			ContentMarginLeft = 4,
			ContentMarginRight = 4,
			ContentMarginTop = 2,
			ContentMarginBottom = 2
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
	/// Applies shared column widths from <see cref="ShellColumnLayout"/> to this row.
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
		int sep = ShellColumnLayout.RowSeparation;

		// Full-height nest indicator: beside ContentVBox, not inside the cue row.
		ConfigureColorPanelLayout();
		if (_dragButton != null)
		{
			_dragButton.CustomMinimumSize = new Vector2(ShellColumnLayout.DragWidth, ctrlH);
			_dragButton.SizeFlagsHorizontal = SizeFlags.Fill;
			_dragButton.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		}
		if (_collapseButton != null)
		{
			_collapseButton.CustomMinimumSize = new Vector2(ShellColumnLayout.CollapseWidth, ctrlH);
			_collapseButton.SizeFlagsHorizontal = SizeFlags.Fill;
			_collapseButton.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		}
		if (_issueIndicator != null)
		{
			_issueIndicator.CustomMinimumSize = new Vector2(ShellColumnLayout.IssueWidth, ctrlH);
			_issueIndicator.SizeFlagsHorizontal = SizeFlags.Fill;
			_issueIndicator.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		}

		_cueNumLineEdit.CustomMinimumSize = new Vector2(numW, ctrlH);
		_cueNumLineEdit.SizeFlagsHorizontal = SizeFlags.Fill;
		_cueNumLineEdit.SizeFlagsVertical = SizeFlags.ShrinkCenter;

		// Name absorbs indent; keep min small so deep nests shrink name, not the trailing band.
		if (_cueNameLineEdit != null)
		{
			_cueNameLineEdit.CustomMinimumSize = new Vector2(40f, ctrlH);
			_cueNameLineEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_cueNameLineEdit.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		}

		// Fixed trailing band widths (Pre + Dur + Post + Follow + separations).
		_preWaitLineEdit.CustomMinimumSize = new Vector2(timeW, ctrlH);
		_preWaitLineEdit.SizeFlagsHorizontal = SizeFlags.Fill;
		_preWaitLineEdit.SizeFlagsVertical = SizeFlags.ShrinkCenter;

		_durationLineEdit.CustomMinimumSize = new Vector2(timeW, ctrlH);
		_durationLineEdit.SizeFlagsHorizontal = SizeFlags.Fill;
		_durationLineEdit.SizeFlagsVertical = SizeFlags.ShrinkCenter;

		_postWaitLineEdit.CustomMinimumSize = new Vector2(timeW, ctrlH);
		_postWaitLineEdit.SizeFlagsHorizontal = SizeFlags.Fill;
		_postWaitLineEdit.SizeFlagsVertical = SizeFlags.ShrinkCenter;

		if (_followButton != null)
		{
			_followButton.CustomMinimumSize = new Vector2(followW, ctrlH);
			_followButton.SizeFlagsHorizontal = SizeFlags.Fill;
			_followButton.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			_followButton.Alignment = HorizontalAlignment.Center;
		}

		if (_trailingHBox != null)
		{
			// Explicit fixed width so the band never shrinks or grows with indent.
			float trailW = timeW * 3f + followW + sep * 3f;
			_trailingHBox.CustomMinimumSize = new Vector2(trailW, ctrlH);
			_trailingHBox.SizeFlagsHorizontal = SizeFlags.Fill;
			_trailingHBox.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		}

		if (_leadingHBox != null)
		{
			_leadingHBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_leadingHBox.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		}

		if (_rowHBox != null)
		{
			_rowHBox.CustomMinimumSize = new Vector2(0, ShellColumnLayout.RowMinHeight);
			_rowHBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_rowHBox.SizeFlagsVertical = SizeFlags.ShrinkBegin;
			_rowHBox.Alignment = BoxContainer.AlignmentMode.Begin;
		}

		// Nested shells must stretch to parent content width.
		SizeFlagsHorizontal = SizeFlags.ExpandFill;

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
			RefreshTimesFromCue();
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

		_cueNumLineEdit.Editable = false;
		_cueNameLineEdit.Editable = false;
		_isEditingCueNum = false;
		_isEditingName = false;
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
	
	private void OnCueNumGuiInput(InputEvent @event)
	{
		OnInput(@event);
		if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.DoubleClick)
		{
			if (_isEditingCueNum) return;
			_cueNumLineEdit.Editable = true;
			_cueNumLineEdit.FocusMode = FocusModeEnum.Click;
			_cueNumLineEdit.GrabFocus();
			_isEditingCueNum = true;
		}
	}

	private void OnCueNumEditToggled(bool editing)
	{
		if (_cue == null) return;
		if (_isEditingCueNum && editing == false)
		{
			_cueNumLineEdit.Editable = false;
			string newNum = _cueNumLineEdit.Text ?? string.Empty;
			if (!string.Equals(_cue.CueNum ?? string.Empty, newNum, System.StringComparison.Ordinal))
			{
				// Discrete commit when leaving inline edit (double-click to edit on shell).
				_globalData?.HistoryManager?.RecordCueChange(_cue.Id, "Edit cue number");
				_cue.CueNum = newNum;
				NotifyInspectorsOfCueEdit();
			}
			_cueNumLineEdit.FocusMode = FocusModeEnum.None;
			_isEditingCueNum = false;
		}
		else if (_isEditingCueNum && !editing)
		{
			_cueNumLineEdit.Editable = true;
			_cueNumLineEdit.FocusMode = FocusModeEnum.Click;
			_cueNumLineEdit.GrabFocus();
			_isEditingCueNum = true;
		}
	}

	private void OnNameGuiInput(InputEvent @event)
	{
		OnInput(@event);
		if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.DoubleClick)
		{
			_cueNameLineEdit.Editable = true;
			_cueNameLineEdit.FocusMode = FocusModeEnum.Click;
			_cueNameLineEdit.GrabFocus();
			_isEditingName = true;
		}
	}

	private void OnNameEditToggled(bool editing)
	{
		if (_cue == null) return;
		if (_isEditingName && editing == false)
		{
			_cueNameLineEdit.Editable = false;
			string newName = _cueNameLineEdit.Text ?? string.Empty;
			if (!string.Equals(_cue.Name ?? string.Empty, newName, System.StringComparison.Ordinal))
			{
				_globalData?.HistoryManager?.RecordCueChange(_cue.Id, "Edit cue name");
				_cue.Name = newName;
				NotifyInspectorsOfCueEdit();
			}
			_cueNameLineEdit.FocusMode = FocusModeEnum.None;
			_isEditingName = false;
		}
		else if (_isEditingName && !editing)
		{
			_cueNameLineEdit.Editable = true;
			_cueNameLineEdit.FocusMode = FocusModeEnum.Click;
			_cueNameLineEdit.GrabFocus();
			_isEditingName = true;
		}
	}

	private void OnPreWaitEditToggled(bool editing)
	{
		if (_cue == null) return;
		if (_isEditingPreWait && editing == false)
		{
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
			_preWaitLineEdit.ReleaseFocus();
			_isEditingPreWait = false;
		}
		else if (_isEditingPreWait == false && editing) _isEditingPreWait = true;
	}

	private void OnPostWaitEditToggled(bool editing)
	{
		if (_cue == null) return;
		if (_isEditingPostWait && editing == false)
		{
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
			_postWaitLineEdit.ReleaseFocus();
			_isEditingPostWait = false;
		}
		else if (_isEditingPostWait == false && editing) _isEditingPostWait = true;
	}

	/// <summary>
	/// Continue-mode cycle on the shell row: None → Auto-continue → Auto-follow → None.
	/// </summary>
	private void OnFollowButtonPressed()
	{
		if (_cue == null) return;
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
		// Gets if input is Left mouse button
		if (@event is not InputEventMouseButton mouseEvent || !mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left)
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

		Color cueColor = _cue != null ? _cue.Color : new Color(0.4f, 0.4f, 0.4f);
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
