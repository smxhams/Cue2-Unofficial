using Godot;
using System;
using System.Collections;
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
			
			GD.Print(Position);
			SetGlobalPosition(new Vector2(GetGlobalMousePosition().X + 5, GetGlobalMousePosition().Y));
		}
	}

	private void _on_mouse_entered()
	{
		if (_isDragging!) return;
		if (CueList.ShellBeingDragged != -1)
		{
			_gd.Cuelist.ShellMouseOverByDraggedShell(CueId);
		}

		if (CueList.FocusedCueId != CueId){
			_backPanel.AddThemeStyleboxOverride("panel", GlobalStyles.HoverStyle());
		}
	}
	private void _on_mouse_exited()
	{
		if (_isDragging!) return;
		if (CueList.FocusedCueId != CueId){
			_backPanel.RemoveThemeStyleboxOverride("panel");
		}

	}

	public void Focus()
	{
		_backPanel.AddThemeStyleboxOverride("panel", GlobalStyles.FocusedStyle());
	}
	
	private void _on_focus_entered(){
		// Need to validate cue here on selection
		_gd.Cuelist.FocusCue(CueList.FetchCueFromId(CueId));
	}
	private void _on_focus_exited()
	{
	}

	private void DragPressed()
	{
		_isDragging = true;
		CueList.ShellBeingDragged = CueId;
		_backPanel.AddThemeStyleboxOverride("panel", GlobalStyles.DangerStyle());
		_backPanel.SetMouseFilter(MouseFilterEnum.Ignore);
	}
	
	private void DragReleased()
	{
		_isDragging = false;
		CueList.ShellBeingDragged = -1;
		_backPanel.RemoveThemeStyleboxOverride("panel");
		_backPanel.SetMouseFilter(MouseFilterEnum.Stop);
		_gd.Cuelist.FocusCue(CueList.FetchCueFromId(CueId));
		_backPanel.AddThemeStyleboxOverride("panel", GlobalStyles.FocusedStyle());
	}



}
