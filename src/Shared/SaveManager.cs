using Godot;
using System.IO;
using Newtonsoft.Json;


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
		FolderCreator(url);
		DirAccess.MakeDirRecursiveAbsolute(url.GetBaseDir());
		DirAccess dir = DirAccess.Open(url);
		
		Gd = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData");


		string saveJson = JsonConvert.SerializeObject(Gd.Cuelist);


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

