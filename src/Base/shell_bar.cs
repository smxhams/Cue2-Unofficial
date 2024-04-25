using Godot;
using System;
using System.Collections;

// This script is attached to instanced shell bars in the cue list, it handles
// -UI of itself
// -Emitting signals of interactions attached with it's relevant info


public partial class shell_bar : Control
{
	private GlobalData _gd;
	private GlobalSignals _globalSignals;
	private GlobalStyles _globalStyles;

	[Export]
	public int cueID;



	private StyleBoxFlat hoverStyle = new StyleBoxFlat();
	private StyleBoxFlat nextStyle = new StyleBoxFlat();
	private StyleBoxFlat selectedStyle = new StyleBoxFlat();
	private StyleBoxFlat activeStyle = new StyleBoxFlat();
	private StyleBoxFlat defaultStyle = new StyleBoxFlat();


	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		cueID = GetNode<GlobalData>("/root/GlobalData").cueCount;

		_globalStyles = GetNode<GlobalStyles>("/root/GlobalStyles");
		hoverStyle = _globalStyles.hoverStyle;
		nextStyle = _globalStyles.nextStyle;

		_gd = GetNode<GlobalData>("/root/GlobalData");

	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void _on_mouse_entered()
	{
		var panel = GetNode<Panel>("Panel");
		if (_gd.nextCue != cueID){
			GetNode<Panel>("Panel").AddThemeStyleboxOverride("panel", hoverStyle);
		}
		
	}
	private void _on_mouse_exited()
	{
		if (_gd.nextCue != cueID){
			GetNode<Panel>("Panel").RemoveThemeStyleboxOverride("panel");
		}

	}
	private void _on_focus_entered(){
		if (_gd.selectedIsNext == true) // Set shell as next cue if settings selectedIsNext
		{
			// Get existing next cue and reset style
			var shellData = (Hashtable)_gd.cuelist[_gd.nextCue];
			var shellObj = (Node)shellData["shellObj"];
			shellObj.GetChild<Panel>(0).RemoveThemeStyleboxOverride("panel");

			// Set this shell as next cue
			_gd.nextCue = cueID;
			GetNode<Panel>("Panel").AddThemeStyleboxOverride("panel", nextStyle);
		}

		// Emit signal that this shell has been selected
		_globalSignals.EmitSignal(nameof(GlobalSignals.ShellSelected), cueID);
	}
	private void _on_focus_exited(){
	}


}
