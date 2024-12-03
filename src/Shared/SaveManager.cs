using Godot;
using System.IO;
using Newtonsoft.Json;


public partial class SaveManager : Node
{
	private GlobalSignals _globalSignals;
	public GlobalData _gd;

	//private Dictionary<string, string> saveData;

	private string DECODEPASS = "f8237hr8hnfv3fH@#R";


	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public bool saveShow(string url, string showName)
	{
		folderCreator(url);
		DirAccess.MakeDirRecursiveAbsolute(url.GetBaseDir());
		DirAccess dir = DirAccess.Open(url);
		
		_gd = GetNode<GlobalData>("/root/GlobalData");


		string saveJson = JsonConvert.SerializeObject(_gd.cuelist);


		Godot.FileAccess file = Godot.FileAccess.OpenEncryptedWithPass(url+"/" + showName+".c2", Godot.FileAccess.ModeFlags.Write, DECODEPASS);
		file.StoreString(saveJson);
		file.Close();
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

