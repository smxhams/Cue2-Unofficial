using Godot;
using System;
using System.Collections;
using Cue2.Base.Classes;

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



	private StyleBoxFlat _hoverStyle = new StyleBoxFlat();
	private StyleBoxFlat _nextStyle = new StyleBoxFlat();
	private StyleBoxFlat _focusedStyle = new StyleBoxFlat();
	private StyleBoxFlat _activeStyle = new StyleBoxFlat();
	private StyleBoxFlat _defaultStyle = new StyleBoxFlat();


	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		
		
		//cueID = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData").cueCount;

		_globalStyles = GetNode<GlobalStyles>("/root/GlobalStyles");
		_hoverStyle = _globalStyles.HoverStyle;
		_nextStyle = _globalStyles.NextStyle;
		_focusedStyle = _globalStyles.FocusedStyle;

		_gd = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData");

	}
	
	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void _on_mouse_entered()
	{
		var panel = GetNode<Panel>("Panel");
		if (CueList.FocusedCueId != CueId){
			GetNode<Panel>("Panel").AddThemeStyleboxOverride("panel", _hoverStyle);
		}
	}
	private void _on_mouse_exited()
	{
		if (CueList.FocusedCueId != CueId){
			GetNode<Panel>("Panel").RemoveThemeStyleboxOverride("panel");
		}

	}
	private void _on_focus_entered(){
		// Need to validate cue here on selection
		
		if (CueList.FocusedCueId == -1) // -1 if no current focused cue
		{
			CueList.FocusedCueId = CueId;
		}
		else if (CueList.FocusedCueId != CueId) 
		{
			var previouslyFocusedCue = CueList.FetchCueFromId(CueList.FocusedCueId);
			var previousCueShellBar = previouslyFocusedCue.ShellBar;
			// Needs to remove focus style of previous Shell
			previousCueShellBar.GetNode<Panel>("Panel").RemoveThemeStyleboxOverride("panel");
		}
		
		// Tell CueList the ID of a focused cue.
		CueList.FocusedCueId = CueId;
		
		GetNode<Panel>("Panel").AddThemeStyleboxOverride("panel", _focusedStyle);
		// Emit signal that this shell has been selected
		_globalSignals.EmitSignal(nameof(GlobalSignals.ShellSelected), CueId);
	}
	private void _on_focus_exited()
	{
	}
	
	


}
