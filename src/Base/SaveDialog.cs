using Godot;
using System;
using System.IO;

public partial class SaveDialog : FileDialog
{

	private GlobalSignals _globalSignals;
	public Cue2.Shared.GlobalData _gd;
	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_gd = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData");
		this.FileSelected += _onFileSelected;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
	private void _onFileSelected(String @path)
	{
		string showName = Path.GetFileName(@path);
		GD.Print(@path + " and filename : "+ showName);
		_gd.showName = showName;
		_gd.showPath = @path;

		// URL and showname made to continue Save process		
		_globalSignals.EmitSignal(nameof(GlobalSignals.Save));

	}
}
