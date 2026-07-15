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
	
	private LineEdit _cueNumLineEdit;
	private LineEdit _cueNameLineEdit;

	private LineEdit _preWaitLineEdit;
	private LineEdit _durationLineEdit;
	private LineEdit _postWaitLineEdit;
	private CheckBox _followCheckBox;

	public VBoxContainer ShellChildContainer;
	
	private bool _isEditingName = false;
	private bool _isEditingCueNum = false;
	private bool _isEditingPreWait = false;
	private bool _isEditingPostWait = false;
	
	public bool Selected = false;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		
		DefineUi();
		
		_cueNumLineEdit.Editable = false;
		_cueNameLineEdit.Editable = false;
		
		// Connect Ui events
		GuiInput += OnInput;
		MouseEntered += OnMouseEntered;
		MouseExited += OnMouseExited;
		
		_dragButton.ButtonDown += DragPressed;
		
		_collapseButton.Pressed += CollapsedPressed;
		_cueNumLineEdit.GuiInput += OnCueNumGuiInput;
		_cueNumLineEdit.EditingToggled += OnCueNumEditToggled;
		_cueNameLineEdit.GuiInput += OnNameGuiInput;
		_cueNameLineEdit.EditingToggled += OnNameEditToggled;
		_preWaitLineEdit.EditingToggled += OnPreWaitEditToggled;
		_postWaitLineEdit.EditingToggled += OnPostWaitEditToggled;
		if (_followCheckBox != null)
			_followCheckBox.Toggled += OnFollowToggled;

		_dragButton.Icon = GetThemeIcon("Rearrange", "AtlasIcons");
		_collapseButton.Icon = GetThemeIcon("Right", "AtlasIcons");

		// Optional global refresh targeting this cue id
		_globalSignals.UpdateShellBar += OnUpdateShellBar;
		_globalSignals.CueMediaHealthChanged += OnCueMediaHealthChanged;
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
			_cue = null;
		}
		// Drop duplicated theme StyleBox so it is not retained after node free
		if (_colorPanel != null && IsInstanceValid(_colorPanel))
			_colorPanel.RemoveThemeStyleboxOverride("panel");
		_colorBarStyle = null;
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
	/// Shows or hides the red ✕ issue indicator (missing media file, audio output, or video target layer).
	/// </summary>
	private void ApplyIssueIndicator(bool hasIssue, string message)
	{
		if (_issueIndicator == null)
			return;

		_issueIndicator.Visible = hasIssue;
		_issueIndicator.TooltipText = hasIssue
			? (string.IsNullOrEmpty(message) ? "Issue" : message)
			: string.Empty;
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
		if (_issueIndicator != null)
		{
			_issueIndicator.Visible = false;
			// QLab-style red X
			_issueIndicator.AddThemeColorOverride("font_color", GlobalStyles.Danger);
			_issueIndicator.AddThemeColorOverride("font_hover_color", GlobalStyles.Danger);
			_issueIndicator.AddThemeColorOverride("font_pressed_color", GlobalStyles.Danger);
		}
		
		_cueNumLineEdit = GetNode<LineEdit>("%CueNumLineEdit");
		_cueNameLineEdit = GetNode<LineEdit>("%CueNameLineEdit");

		_preWaitLineEdit = GetNode<LineEdit>("%PreWaitLineEdit");
		_durationLineEdit = GetNode<LineEdit>("%DurationLineEdit");
		_postWaitLineEdit = GetNode<LineEdit>("%PostWaitLineEdit");
		_followCheckBox = GetNode<CheckBox>("%FollowCheckBox");
		
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
		}
		_cue = cue;
		_cue.NameChanged += UpdateName;
		_cue.CueNumChanged += UpdateCueNum;
		_cue.ColorChanged += UpdateColor;
		_cue.DurationChanged += UpdateDuration;
		_cue.TotalDurationChanged += UpdateTotalDuration;
		_cue.PreWaitChanged += UpdatePreWait;
		_cue.PostWaitChanged += UpdatePostWait;
		_cueNumLineEdit.Text = cue.CueNum;
		_cueNameLineEdit.Text = cue.Name;
		RefreshTimesFromCue();
		_colorBarStyle = _colorPanel.GetThemeStylebox("panel").Duplicate() as StyleBoxFlat;
		_colorBarStyle.BgColor = _cue.Color;
		_colorPanel.AddThemeStyleboxOverride("panel", _colorBarStyle);
		if (_followCheckBox != null)
			_followCheckBox.SetPressedNoSignal(cue.Follow == FollowType.Follow);
		// Initialize collapse/expand UI based on children (SetCue path)
		UpdateCollapseUI();
		RefreshIssueIndicatorFromService();

		_cueNumLineEdit.Editable = false;
		_cueNameLineEdit.Editable = false;
		_isEditingCueNum = false;
		_isEditingName = false;
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
		if (_followCheckBox != null)
			_followCheckBox.SetPressedNoSignal(_cue.Follow == FollowType.Follow);
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
	/// Follow checkbox on the shell row: toggles Follow vs None (matches prior display mapping).
	/// </summary>
	private void OnFollowToggled(bool pressed)
	{
		if (_cue == null) return;
		if (_globalData?.HistoryManager?.IsRestoring == true) return;

		var desired = pressed ? FollowType.Follow : FollowType.None;
		if (_cue.Follow == desired) return;

		_globalData?.HistoryManager?.RecordCueChange(_cue.Id, "Edit follow mode");
		_cue.Follow = desired;
		NotifyInspectorsOfCueEdit();
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
	}

	private void CollapsedPressed()
	{
		if (_cue == null) return;
		_cue.Expanded = !_cue.Expanded;
		UpdateCollapseUI();
	}

	/// <summary>
	/// Central method to show/hide collapse button, set container visibility and icon
	/// based on the cue's current Expanded state (and whether it has children).
	/// The "default to expanded" for newly parented cues is applied at the point
	/// children are added (see CreateNewShell and EndReorder).
	/// </summary>
	private void UpdateCollapseUI()
	{
		if (_cue == null || _collapseButton == null) return;

		bool hasChildren = _cue.ChildCues.Count > 0;

		_collapseButton.Visible = hasChildren;

		if (hasChildren)
		{
			ShellChildContainer.Visible = _cue.Expanded;
			_collapseButton.Icon = GetThemeIcon(_cue.Expanded ? "Down" : "Right", "AtlasIcons");
		}
		else
		{
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
		if (Selected == false){
			AddThemeStyleboxOverride("panel", GlobalStyles.HoverStyle());
		}
	}
	
	private void OnMouseExited()
	{
		if (Selected == false)
		{
			RemoveThemeStyleboxOverride("panel");
		}
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
		AddThemeStyleboxOverride("panel", GlobalStyles.FocusedStyle());
	}
	

	public void Deselect()
	{
		_shellPanel.RemoveThemeStyleboxOverride("panel");
		_groupPanel.RemoveThemeStyleboxOverride("panel");
		_shellPanel.Visible = false;
		_groupPanel.Visible = false;
		Selected = false;
	}

	public void Select()
	{
		_shellPanel.AddThemeStyleboxOverride("panel", GlobalStyles.FocusedStyle());
		_groupPanel.AddThemeStyleboxOverride("panel", GlobalStyles.FocusedStyle());
		_shellPanel.Visible = true;
		_groupPanel.Visible = true;
		Selected = true;
	}
	

	// Re-ordering functions
	private void DragPressed()
	{
		_globalData.Cuelist.StartReorder(this);
	}
}
