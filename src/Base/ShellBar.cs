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
	private Cue2.Shared.GlobalData _gd;
	private GlobalSignals _globalSignals;
	private GlobalStyles _globalStyles;

	[Export] public int CueId { get; set; } = -1;
	

	private Panel _backPanel;
	private Button _dragButton;

	private bool _isDragging = false;
	
	[Export] public bool Selected = false;


	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		
		
		//cueID = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData").cueCount;

		_globalStyles = GetNode<GlobalStyles>("/root/GlobalStyles");

		_gd = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData");
		
		_backPanel = GetChild<Panel>(0);
		_dragButton = GetChild(1).GetChild(0).GetChild<Button>(0);
		_dragButton.ButtonDown += DragPressed;
		_dragButton.ButtonUp += DragReleased;
	}
	
	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (_isDragging)
		{
			
			//GD.Print(Position);
			SetGlobalPosition(new Vector2(GetGlobalMousePosition().X + 5, GetGlobalPosition().Y));
		}
	}

	private void _on_mouse_entered()
	{
		if (_isDragging!) return;
		if (CueList.ShellBeingDragged != -1)
		{
			_gd.Cuelist.ShellMouseOverByDraggedShell(CueId);
		}

		if (Selected == false){
			_backPanel.AddThemeStyleboxOverride("panel", GlobalStyles.HoverStyle());
		}
	}
	private void _on_mouse_exited()
	{
		if (_isDragging!) return;
		if (Selected == false){
			_backPanel.RemoveThemeStyleboxOverride("panel");
		}

	}

	private void _OnInput(InputEvent @event)
	{
		// Gets if input is Left mouse button
		if (@event is not InputEventMouseButton mouseEvent || !mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left)
			return;
		
		if (Input.IsKeyPressed(Key.Shift))
		{
			_gd.ShellSelection.SelectThrough(CueList.FetchCueFromId(CueId));
			return;
		}

		if (Input.IsKeyPressed(Key.Ctrl))
		{
			_gd.ShellSelection.AddSelection(CueList.FetchCueFromId(CueId));
			return;
		}
		
		//Select single shell
		_gd.ShellSelection.SelectIndividualShell(CueList.FetchCueFromId(CueId));
	}

	public void Focus()
	{
		_backPanel.AddThemeStyleboxOverride("panel", GlobalStyles.FocusedStyle());
	}
	
	private void DragPressed()
	{
		_isDragging = true;
		CueList.ShellBeingDragged = CueId;
		_backPanel.AddThemeStyleboxOverride("panel", GlobalStyles.DangerStyle());
		_backPanel.SetMouseFilter(MouseFilterEnum.Ignore);
		GD.Print(Position);
	}
	
	private void DragReleased()
	{
		_isDragging = false;
		CueList.ShellBeingDragged = -1;
		_backPanel.RemoveThemeStyleboxOverride("panel");
		_backPanel.SetMouseFilter(MouseFilterEnum.Stop);
		_gd.ShellSelection.SelectIndividualShell(CueList.FetchCueFromId(CueId));
		_backPanel.AddThemeStyleboxOverride("panel", GlobalStyles.FocusedStyle());
		SetPosition(new Vector2(0, Position.Y));
	}



}
