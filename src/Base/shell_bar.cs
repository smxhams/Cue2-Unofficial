using Godot;
using System;

public partial class shell_bar : Control
{
	private GlobalData _globalData;
	private GlobalSignals _globalSignals;

	[Export]
	public int cueID;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		cueID = GetNode<GlobalData>("/root/GlobalData").cueCount;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void _on_mouse_entered(){
	}
	private void _on_mouse_exited(){
	}
	private void _on_focus_entered(){
		_globalSignals.EmitSignal(nameof(GlobalSignals.ShellSelected), cueID);
	}
	private void _on_focus_exited(){
	}


}
