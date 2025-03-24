using Godot;
using System;
using System.IO;
using Cue2.Shared;

public partial class SaveDialog : FileDialog
{
	private GlobalData _globalData;
	private GlobalSignals _globalSignals;
	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		FileSelected += _onFileSelected;
	}
	
	private void _onFileSelected(String @path)
	{
		string showName = Path.GetFileName(@path);
		GD.Print(@path + " and filename : "+ showName);
		_globalData.SessionName = showName;
		_globalData.SessionPath = @path;

		// URL and showname made to continue Save process		
		_globalSignals.EmitSignal(nameof(GlobalSignals.Save));

	}
}
