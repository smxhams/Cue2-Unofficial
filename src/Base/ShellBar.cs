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



	private StyleBoxFlat _hoverStyle = new StyleBoxFlat();
	private StyleBoxFlat _nextStyle = new StyleBoxFlat();
	private static StyleBoxFlat _focusedStyle = new StyleBoxFlat();
	private StyleBoxFlat _activeStyle = new StyleBoxFlat();
	private StyleBoxFlat _defaultStyle = new StyleBoxFlat();


	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		
		
		//cueID = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData").cueCount;

		_globalStyles = GetNode<GlobalStyles>("/root/GlobalStyles");
		_hoverStyle = GlobalStyles.HoverStyle();
		_nextStyle = _globalStyles.NextStyle;
		_focusedStyle = GlobalStyles.FocusedStyle();

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

	public void Focus()
	{
		GetNode<Panel>("Panel").AddThemeStyleboxOverride("panel", _focusedStyle);
	}
	
	private void _on_focus_entered(){
		// Need to validate cue here on selection
		_gd.Cuelist.FocusCue(CueList.FetchCueFromId(CueId));
	}
	private void _on_focus_exited()
	{
	}
	
	


}
