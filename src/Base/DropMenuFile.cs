using Godot;
using System;
using Cue2.Shared;

namespace Cue2.Base;
public partial class DropMenuFile : PanelContainer
{
	private GlobalSignals _globalSignals;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
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
	private void _onOpenSessionPressed()
	{
		_globalSignals.EmitSignal(nameof(GlobalSignals.OpenSession));
	}

}
