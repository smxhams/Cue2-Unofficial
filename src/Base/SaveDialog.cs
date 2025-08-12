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

		FileMode = FileModeEnum.SaveFile;
		AddFilter("*.c2 ; Cue2 Session");
	}
	
	private void _onFileSelected(String @path)
	{
		string sessionName = Path.GetFileNameWithoutExtension(@path);
		string sessionPath = Path.GetDirectoryName(@path) + "\\" + Path.GetFileNameWithoutExtension(@path);
		GD.Print(sessionPath + " and filename : "+ sessionName);
		_globalData.SessionName = sessionName;
		_globalData.SessionPath = @sessionPath;

		// URL and showname made to continue Save process		
		_globalSignals.EmitSignal(nameof(GlobalSignals.Save));

	}
}
