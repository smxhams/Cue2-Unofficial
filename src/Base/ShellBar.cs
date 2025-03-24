using Godot;
using System;
using System.Collections;
using System.Configuration;
using Cue2.Base.Classes;
using Cue2.Shared;

// This script is attached to instanced shell bars in the cue list, it handles
// -UI of itself
// -Emitting signals of interactions attached with it's relevant info
namespace Cue2.Base;

public partial class ShellBar : Control
{
	private GlobalData _globalData;
	private GlobalSignals _globalSignals;
	private GlobalStyles _globalStyles;

	[Export] public int CueId { get; set; } = -1;
	

	private Panel _backPanel;
	private Button _dragButton;

	private bool _isDragging = false;
	
	[Export] public bool Selected = false;

	private Container _topHalf;
	private Container _bottomHalf;

	private Button _expanded;
	private Button _collapsed;
	
	public int ShellOffset = 0;


	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		
		
		//cueID = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData").cueCount;

		_globalStyles = GetNode<GlobalStyles>("/root/GlobalStyles");

		_globalData = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData");
		
		_backPanel = GetNode<Panel>("%BackPanel");
		_dragButton = GetNode<Button>("%DragBar");
		_dragButton.ButtonDown += DragPressed;
		_dragButton.ButtonUp += DragReleased;

		_topHalf = GetNode<Container>("%TopHalfSensor");
		_topHalf.MouseEntered += MouseEnteredTopHalf;
		_topHalf.MouseExited += MouseExitedTopHalf;
		_bottomHalf = GetNode<Container>("%BottomHalfSensor");
		_bottomHalf.MouseEntered += MouseEnteredBottomHalf;
		_bottomHalf.MouseExited += MouseExitedBottomHalf;

		_expanded = GetNode<Button>("%ExpandedButton");
		_expanded.Pressed += ExpandedPressed;
		_collapsed = GetNode<Button>("%CollapsedButton");
		_collapsed.Pressed += CollapsedPressed;

	}

	private void CollapsedPressed()
	{
		_globalData.Cuelist.ExpandGroup(CueId);
		GetNode<Container>("%Expanded").Visible = true;
		GetNode<Container>("%Collapsed").Visible = false;
	}

	private void ExpandedPressed()
	{
		_globalData.Cuelist.CollapseGroup(CueId);
		GetNode<Container>("%Expanded").Visible = false;
		GetNode<Container>("%Collapsed").Visible = true;
	}


	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (_isDragging)
		{
			
			//GD.Print(Position);
			SetGlobalPosition(new Vector2(GetGlobalMousePosition().X + 5, GetGlobalPosition().Y));
			_globalData.Cuelist.MoveCueWithItsChildren(CueId);
		}
	}

	private void _on_mouse_entered()
	{
		if (_isDragging!) return;
		if (Selected == false){
			_backPanel.AddThemeStyleboxOverride("panel", GlobalStyles.HoverStyle());
		}

		if (_globalData.Cuelist.ShellBeingDragged != -1) GetNode<VBoxContainer>("%HoverSensors").Visible = true;

		
	}
	private void _on_mouse_exited()
	{
		if (_isDragging!) return;
		if (Selected == false){
			_backPanel.RemoveThemeStyleboxOverride("panel");
		}
		GetNode<VBoxContainer>("%HoverSensors").Visible = false;

	}
	

	private void _OnInput(InputEvent @event)
	{
		// Gets if input is Left mouse button
		if (@event is not InputEventMouseButton mouseEvent || !mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left)
			return;
		
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
		_backPanel.AddThemeStyleboxOverride("panel", GlobalStyles.FocusedStyle());
	}
	
	private void DragPressed()
	{
		_isDragging = true;
		_globalData.Cuelist.ShellBeingDragged = CueId;
		_backPanel.AddThemeStyleboxOverride("panel", GlobalStyles.DangerStyle());
		GD.Print(Position);
	}
	
	private void DragReleased()
	{
		_isDragging = false;
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
		_backPanel.RemoveThemeStyleboxOverride("panel");
		GetNode<VBoxContainer>("%HoverSensors").Visible = false;
		_globalData.ShellSelection.SelectIndividualShell(CueList.FetchCueFromId(CueId));
		_backPanel.AddThemeStyleboxOverride("panel", GlobalStyles.FocusedStyle());

		SetPosition(new Vector2(0, Position.Y));
		_globalData.Cuelist.ResetCuePositionXWithChildren(CueId);
	}

	private void MouseEnteredTopHalf()
	{
		if (_isDragging!) return;
		if (_globalData.Cuelist.ShellBeingDragged != -1)
		{
			_globalData.Cuelist.ShellMouseOverByDraggedShellTopHalf(CueId);
			_backPanel.AddThemeStyleboxOverride("panel", GlobalStyles.DangerStyle());
		}
	}
	private void MouseExitedTopHalf()
	{
		if (_isDragging!) return;
		if (_globalData.Cuelist.ShellBeingDragged != -1)
		{
			_backPanel.RemoveThemeStyleboxOverride("panel");
		}
	}
	private void MouseEnteredBottomHalf()
	{
		if (_isDragging!) return;
		if (_globalData.Cuelist.ShellBeingDragged != -1)
		{
			_globalData.Cuelist.ShellMouseOverByDraggedShellBottomHalf(CueId);
			GetNode<Line2D>("%LineBelow").Visible = true;
			//_gd.Cuelist.ShellMouseOverByDraggedShell(CueId);
		}
	}
	private void MouseExitedBottomHalf()
	{
		if (_isDragging!) return;
		GetNode<Line2D>("%LineBelow").Visible = false;
	}

	public void SetShellOffset(int offset)
	{
		var offsetContainer = GetNode<HBoxContainer>("%OffsetContainer");
		foreach (var child in offsetContainer.GetChildren())
		{
			child.QueueFree();
		}

		for (int i = 0; i < offset; i++)
		{
			var offsetNode = new Control();
			offsetNode.SetCustomMinimumSize(new Vector2(18, 0));
			offsetContainer.AddChild(offsetNode);
		}

		ShellOffset = offset;
	}


}
