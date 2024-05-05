using Godot;
using System;

public partial class DropMenuFile : PanelContainer
{
	private GlobalSignals _globalSignals;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void _onHeaderFileMenuPressed(){
		Visible = true;
	}
	private void _onMarginContainerMouseExited(){
		Visible = false;
	}

	private void _onFileSavePressed()
	{
		_globalSignals.EmitSignal(nameof(GlobalSignals.Save));

	}

}
