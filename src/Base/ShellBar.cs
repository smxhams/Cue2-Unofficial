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

		_dragButton.Icon = GetThemeIcon("Rearrange", "AtlasIcons");
		_collapseButton.Icon = GetThemeIcon("Right", "AtlasIcons");

		// Optional global refresh targeting this cue id
		_globalSignals.UpdateShellBar += OnUpdateShellBar;
	}

	public override void _ExitTree()
	{
		if (_globalSignals != null)
			_globalSignals.UpdateShellBar -= OnUpdateShellBar;
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
		base._ExitTree();
	}

	private void OnUpdateShellBar(int cueId)
	{
		if (_cue != null && _cue.Id == cueId)
			RefreshTimesFromCue();
	}

	private void DefineUi()
	{
		// Define Ui
		_colorPanel = GetNode<Panel>("%ColorPanel");
		_groupPanel = GetNode<Panel>("%GroupPanel");
		_shellPanel = GetNode<Panel>("%ShellPanel");
		_dragButton = GetNode<Button>("%DragBar");
		_collapseButton = GetNode<Button>("%CollapseButton");
		
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
		if (cue.Follow == FollowType.Follow) _followCheckBox.ButtonPressed = true;
		else _followCheckBox.ButtonPressed = false;
		// Initialize collapse/expand UI based on children (SetCue path)
		UpdateCollapseUI();

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
		if (_isEditingCueNum && editing == false)
		{
			_cueNumLineEdit.Editable = false;
			_cue.CueNum = _cueNumLineEdit.Text;
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
		if (_isEditingName && editing == false)
		{
			_cueNameLineEdit.Editable = false;
			_cue.Name = _cueNameLineEdit.Text;
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
		if (_isEditingPreWait && editing == false)
		{
			var ret = UiUtilities.ParseAndFormatTime(_preWaitLineEdit.Text, out var time, out bool isValid);
			if (string.IsNullOrEmpty(ret) || !isValid)
			{
				_preWaitLineEdit.Text = FormatDurationField(_cue.PreWait);
			}
			else
			{
				_cue.PreWait = time;
				_cue.CalculateTotalDuration();
				_preWaitLineEdit.Text = ret;
				_globalSignals?.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
			}
			_preWaitLineEdit.ReleaseFocus();
			_isEditingPreWait = false;
		}
		else if (_isEditingPreWait == false && editing) _isEditingPreWait = true;
	}

	private void OnPostWaitEditToggled(bool editing)
	{
		if (_isEditingPostWait && editing == false)
		{
			var ret = UiUtilities.ParseAndFormatTime(_postWaitLineEdit.Text, out var time, out bool isValid);
			if (string.IsNullOrEmpty(ret) || !isValid)
			{
				_postWaitLineEdit.Text = FormatDurationField(_cue.PostWait);
			}
			else
			{
				_cue.PostWait = time;
				_cue.CalculateTotalDuration();
				_postWaitLineEdit.Text = ret;
				_globalSignals?.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
			}
			_postWaitLineEdit.ReleaseFocus();
			_isEditingPostWait = false;
		}
		else if (_isEditingPostWait == false && editing) _isEditingPostWait = true;
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
