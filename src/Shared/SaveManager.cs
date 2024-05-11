using Godot;
using System;
using System.IO;
using System.Reflection.Metadata.Ecma335;

public partial class SaveManager : Node
{
	private GlobalSignals _globalSignals;
	public GlobalData _gd;


	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public bool saveShow(string url, string showname)
	{
		folderCreator(url);
		_globalSignals.EmitSignal(nameof(GlobalSignals.ErrorLog), "Save working: " + url, 0);

		
		return true;
	}

	public bool loadShow(string url)
	{
		GD.Print("Loading show: " + url);
		return true;
	}


	public bool folderCreator(string url)
	{
		string folderPath = url;

		if (!Directory.Exists(folderPath))
		{
			Directory.CreateDirectory(folderPath);
			_globalSignals.EmitSignal(nameof(GlobalSignals.ErrorLog), "Directory created: " + url, 0);
			return true;
		}
		else {return false;}
	}
}

