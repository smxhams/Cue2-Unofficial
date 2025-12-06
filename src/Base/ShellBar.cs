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
	private Container _topHalf;
	private Container _bottomHalf;
	private Button _collapseButton;
	
	private LineEdit _cueNumLineEdit;
	private LineEdit _cueNameLineEdit;

	private LineEdit _preWaitLineEdit;
	private LineEdit _durationLineEdit;
	private LineEdit _postWaitLineEdit;
	private CheckBox _followCheckBox;

	private VBoxContainer _shellChildContainer;
	
	private bool _isEditingName = false;
	private bool _isEditingCueNum = false;
	private bool _isEditingPreWait = false;
	private bool _isEditingPostWait = false;
	
	public bool Selected = false;

	public int ShellOffset = 0;
	

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalData = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData");
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		
		DefineUi();
		
		_cueNumLineEdit.Editable = false;
		_cueNameLineEdit.Editable = false;
		
		// Connect Ui events
		GuiInput += OnInput;
		MouseEntered += OnMouseEntered;
		MouseExited += OnMouseExited;
		
		_dragButton.ButtonDown += DragPressed;
		/*_dragButton.ButtonUp += DragReleased;

		_topHalf.MouseEntered += MouseEnteredTopHalf;
		_topHalf.MouseExited += MouseExitedTopHalf;
		_bottomHalf.MouseEntered += MouseEnteredBottomHalf;
		_bottomHalf.MouseExited += MouseExitedBottomHalf;*/
		
		_collapseButton.Pressed += CollapsedPressed;

		_cueNameLineEdit.FocusEntered += () => GD.Print($"FOCUS ENTERED");
		
		_cueNumLineEdit.GuiInput += OnCueNumGuiInput;
		_cueNumLineEdit.EditingToggled += OnCueNumEditToggled;
		_cueNameLineEdit.GuiInput += OnNameGuiInput;
		_cueNameLineEdit.EditingToggled += OnNameEditToggled;
		_preWaitLineEdit.EditingToggled += OnPreWaitEditToggled;
		_postWaitLineEdit.EditingToggled += OnPostWaitEditToggled;

		_dragButton.Icon = GetThemeIcon("Rearrange", "AtlasIcons");
		_collapseButton.Icon = GetThemeIcon("Right", "AtlasIcons");


	}

	private void DefineUi()
	{
		// Define Ui
		_colorPanel = GetNode<Panel>("%ColorPanel");
		_groupPanel = GetNode<Panel>("%GroupPanel");
		_shellPanel = GetNode<Panel>("%ShellPanel");
		_dragButton = GetNode<Button>("%DragBar");
		_topHalf = GetNode<Container>("%TopHalfSensor");
		_bottomHalf = GetNode<Container>("%BottomHalfSensor");
		_collapseButton = GetNode<Button>("%CollapseButton");
		
		_cueNumLineEdit = GetNode<LineEdit>("%CueNumLineEdit");
		_cueNameLineEdit = GetNode<LineEdit>("%CueNameLineEdit");

		_preWaitLineEdit = GetNode<LineEdit>("%PreWaitLineEdit");
		_durationLineEdit = GetNode<LineEdit>("%DurationLineEdit");
		_postWaitLineEdit = GetNode<LineEdit>("%PostWaitLineEdit");
		_followCheckBox = GetNode<CheckBox>("%FollowCheckBox");
		
		_shellChildContainer = GetNode<VBoxContainer>("%ShellChildContainer");
	}

	public void SetCue(Cue cue)
	{
		if (_cue != null)
		{
			_cue.NameChanged -= UpdateName;
			_cue.CueNumChanged -= UpdateCueNum;
		}
		_cue = cue;
		_cue.NameChanged += UpdateName;
		_cue.CueNumChanged += UpdateCueNum;
		_cueNumLineEdit.Text = cue.CueNum;
		_cueNameLineEdit.Text = cue.Name;
		_preWaitLineEdit.Text = UiUtilities.FormatTime(cue.PreWait);
		_durationLineEdit.Text = UiUtilities.FormatTime(cue.Duration);
		_postWaitLineEdit.Text = UiUtilities.FormatTime(cue.PostWait);
		_colorBarStyle = _colorPanel.GetThemeStylebox("panel").Duplicate() as StyleBoxFlat;
		_colorBarStyle.BgColor = _cue.Color;
		_colorPanel.AddThemeStyleboxOverride("panel", _colorBarStyle);
		if (cue.Follow == FollowType.Follow) _followCheckBox.ButtonPressed = true;
		else _followCheckBox.ButtonPressed = false;

		_cueNumLineEdit.Editable = false;
		_cueNameLineEdit.Editable = false;
		_isEditingCueNum = false;
		_isEditingName = false;
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
				_preWaitLineEdit.Text = UiUtilities.FormatTime(_cue.PreWait);
			}
			else
			{
				_cue.PreWait = time;
				_preWaitLineEdit.Text = ret;
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
				_postWaitLineEdit.Text = UiUtilities.FormatTime(_cue.PreWait);
			}
			else
			{
				_cue.PreWait = time;
				_postWaitLineEdit.Text = ret;
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

	private void CollapsedPressed()
	{
		//_globalData.Cuelist.ExpandGroup(CueId);
		GetNode<Container>("%Expanded").Visible = true;
		GetNode<Container>("%Collapsed").Visible = false;
	}

	private void ExpandedPressed()
	{
		//_globalData.Cuelist.CollapseGroup(CueId);
		GetNode<Container>("%Expanded").Visible = false;
		GetNode<Container>("%Collapsed").Visible = true;
	}

	private void OnMouseEntered()
	{
		if (Selected == false){
			AddThemeStyleboxOverride("panel", GlobalStyles.HoverStyle());
		}
		if (_globalData.Cuelist.ShellBeingDragged != -1) GetNode<VBoxContainer>("%HoverSensors").Visible = true;
	}
	
	private void OnMouseExited()
	{
		if (Selected == false)
		{
			RemoveThemeStyleboxOverride("panel");
		}
		GetNode<VBoxContainer>("%HoverSensors").Visible = false;
	}
	

	private void OnInput(InputEvent @event)
	{
		// Gets if input is Left mouse button
		if (@event is not InputEventMouseButton mouseEvent || !mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left)
			return;

		GD.Print($"Shell Clicked");
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
		
		/*_isDragging = true;
		_globalData.Cuelist.ShellBeingDragged = CueId;
		AddThemeStyleboxOverride("panel", GlobalStyles.DangerStyle());
		CreateDragGhost();
		_dragGhost.Visible = true;
		_globalData.Cuelist.EmitSignal("CueDragStarted", CueId);
		GD.Print($"ShellBar:DragPressed - Started dragging cue {CueId}");*/
	}

	/*private void CreateDragGhost()
	{
		_dragGhost.Size = Size;
		_dragGhost.Position = Position;
		// Optionally copy background style
		var style = GetThemeStylebox("panel").Duplicate() as StyleBox;
		_dragGhost.AddThemeStyleboxOverride("panel", style);
	}

	private void ShowInsertionLine(bool visible, bool isBottom)
	{
		_insertionLine.Visible = visible;
		if (visible)
		{
			float y = isBottom ? Size.Y : 0;
			_insertionLine.Points = new Vector2[] { new Vector2(0, y), new Vector2(Size.X, y) };
		}
	}

	private void DragReleased()
	{
		_isDragging = false;
		_dragGhost.QueueFree();
		_globalData.Cuelist.EmitSignal("CueDragEnded", CueId, GetGlobalMousePosition());

		if (GetNode<Container>("%OffSetWithLine").Visible == true)
		{
			//If this is visible, it means it was wanting to be grouped
			_globalData.Cuelist.AddCueToGroup(CueId);
		}
		else
		{
			_globalData.Cuelist.CheckCuesNewPosition(CueId);
		}
		_globalData.Cuelist.ShellBeingDragged = -1;
		RemoveThemeStyleboxOverride("panel");
		GetNode<VBoxContainer>("%HoverSensors").Visible = false;
		_globalData.ShellSelection.SelectIndividualShell(CueList.FetchCueFromId(CueId));
		AddThemeStyleboxOverride("panel", GlobalStyles.FocusedStyle());

		SetPosition(new Vector2(0, Position.Y));
		_globalData.Cuelist.ResetCuePositionXWithChildren(CueId);
	}

	private void MouseEnteredTopHalf()
	{
		if (_isDragging!) return;
		if (_globalData.Cuelist.ShellBeingDragged != -1)
		{
			_globalData.Cuelist.ShellMouseOverByDraggedShellTopHalf(CueId);
			AddThemeStyleboxOverride("panel", GlobalStyles.DangerStyle());
			ShowInsertionLine(true, false); // Top
		}
	}
	private void MouseExitedTopHalf()
	{
		if (_isDragging!) return;
		if (_globalData.Cuelist.ShellBeingDragged != -1)
		{
			RemoveThemeStyleboxOverride("panel");
			_insertionLine.Visible = false;
		}
	}
	private void MouseEnteredBottomHalf()
	{
		if (_isDragging!) return;
		if (_globalData.Cuelist.ShellBeingDragged != -1)
		{
			_globalData.Cuelist.ShellMouseOverByDraggedShellBottomHalf(CueId);
			ShowInsertionLine(true, true); // Bottom
		}
	}
	private void MouseExitedBottomHalf()
	{
		if (_isDragging!) return;
		_insertionLine.Visible = false;
	}*/


}
