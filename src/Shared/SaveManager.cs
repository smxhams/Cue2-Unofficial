using System.Collections;
using Godot;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Cue2.Base.Classes;

namespace Cue2.Shared;

public partial class SaveManager : Node
{
	private GlobalSignals _globalSignals;
	private Shared.GlobalData _globalData;
	
	//private Dictionary<string, string> saveData;

	private string _decodepass = "f8237hr8hnfv3fH@#R";


	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		
		_globalSignals.Save += Save;
		_globalSignals.OpenSession += OpenSession;
		_globalSignals.OpenSelectedSession += OpenSelectedSession;
		
		_globalData = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData");

		if (_globalData.LaunchLoadPath != null)
		{
			Task.Delay(300).ContinueWith(t => LoadOnLaunch(_globalData.LaunchLoadPath));
		}
		
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void LoadOnLaunch(string path)
	{
		OpenSelectedSession(_globalData.LaunchLoadPath);
	}
	// On "Save" signal opens save dialogue if session unnamed.
	private void Save()
	{
		if (GlobalData.SessionName == null)
		{
			GetNode<FileDialog>("/root/Cue2Base/SaveDialog").Visible = true;
			_globalSignals.EmitSignal(nameof(GlobalSignals.ErrorLog), "Waiting on save directory and show name to continue save", 0);
		}
		else {SaveSession(GlobalData.SessionPath, GlobalData.SessionName);}
	}
	
	private void SaveSession(string url, string showName)
	{
		var cueSaveData = FormatCuelistForSave();
		FolderCreator(url);
		string saveJson = JsonSerializer.Serialize(cueSaveData);
		GD.Print(saveJson);
		Godot.FileAccess file = Godot.FileAccess.OpenEncryptedWithPass(url+"/" + showName+".c2", Godot.FileAccess.ModeFlags.Write, _decodepass);
		file.StoreString(saveJson);
		file.Close();
		_globalSignals.EmitSignal(nameof(GlobalSignals.ErrorLog), "Save working: " + url, 0);

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
	private void OpenSession()
	{
		GetNode<FileDialog>("/root/Cue2Base/OpenDialog").Visible = true;
	}
	
	private void OpenSelectedSession(string path)
	{
		GD.Print("Made it to the opening");
		Godot.FileAccess file = Godot.FileAccess.OpenEncryptedWithPass(path, Godot.FileAccess.ModeFlags.Read, _decodepass);
		var json = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(file.GetAsText());
		GD.Print("Hello?");
		ResetSession();
		LoadSession(json);
	}

	private void ResetSession()
	{
		_globalData.Cuelist.ResetCuelist();
	}

	private void LoadSession(Dictionary<string, Dictionary<string, string>> json)
	{

		foreach (var cue in json)
		{
			_globalData.Cuelist.CreateCue(cue.Value);
		}

	}

	private bool FolderCreator(string url)
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

