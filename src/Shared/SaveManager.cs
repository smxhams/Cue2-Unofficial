using System.Collections;
using Godot;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cue2.Base.Classes;

namespace Cue2.Shared;

public partial class SaveManager : Node
{
	
	
	private GlobalSignals _globalSignals;
	public Cue2.Shared.GlobalData Gd;

	//private Dictionary<string, string> saveData;

	private string _decodepass = "f8237hr8hnfv3fH@#R";


	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public bool SaveShow(string url, string showName)
	{
		var cueSaveData = FormatCuelistForSave();
		FolderCreator(url);
		Gd = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData");


		string saveJson = JsonSerializer.Serialize(cueSaveData);
		GD.Print(saveJson);

		Godot.FileAccess file = Godot.FileAccess.OpenEncryptedWithPass(url+"/" + showName+".c2", Godot.FileAccess.ModeFlags.Write, _decodepass);
		file.StoreString(saveJson);
		file.Close();
		_globalSignals.EmitSignal(nameof(GlobalSignals.ErrorLog), "Save working: " + url, 0);


		return true;
		
	}

	public bool LoadShow(string url)
	{
		GD.Print("Loading show: " + url);
		return true;
	}

	private Dictionary<string, Dictionary<string, string>> FormatCuelistForSave()
	{
		var saveTable = new Dictionary<string, Dictionary<string, string>>();
		var index = 0;
		foreach (Cue cue in CueList.Cuelist)
		{
			Dictionary<string, string> cueData = cue.GetData();
			
			saveTable.Add(index.ToString(), cueData);
			index++;
		}
		GD.Print(saveTable);
		return saveTable;
	}

	public bool FolderCreator(string url)
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

