using System.Collections;
using System.Collections.Generic;
using Godot;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Cue2.Base.Classes;
using Godot.Collections;

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
		_globalSignals.SaveAs += SaveAs;
		_globalSignals.OpenSession += OpenSession;
		_globalSignals.OpenSelectedSession += OpenSelectedSession;
		
		_globalData = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData");

		if (_globalData.LaunchLoadPath != null)
		{
			Task.Delay(0).ContinueWith(t => LoadOnLaunch(_globalData.LaunchLoadPath));
		}
		
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void LoadOnLaunch(string path)
	{
		GD.Print("Load On Launch");
		OpenSelectedSession(_globalData.LaunchLoadPath);
	}
	// On "Save" signal opens save dialogue if session unnamed.
	private void Save()
	{
		if (_globalData.SessionName == null || _globalData.SessionPath == null)
		{
			SaveAs();
		}
		else {SaveSession(_globalData.SessionPath, _globalData.SessionName);}
	}

	private void SaveAs()
	{
		GetNode<FileDialog>("/root/Cue2Base/SaveDialog").Visible = true;
		_globalSignals.EmitSignal(nameof(GlobalSignals.ErrorLog), "Waiting on save directory and show name to continue save", 0);
	}
	
	private void SaveSession(string url, string showName)
	{
		var saveData = new Hashtable(); // Save type (cues, cue data)
		
		var cueSaveData = FormatCuesForSave();
		saveData.Add("cues", cueSaveData); // Save type (cues, cue data)();
		
		var cuelistSaveData = FormatCueslistForSave();
		saveData.Add("cuelist", cuelistSaveData); // Save type (cues, cue data)();

		var settingsData = FormatSettingsForSave();
		saveData.Add("settings", settingsData);
		
		int lastSlachIndex = url.LastIndexOfAny(new char[] { '/', '\\' });
		if (lastSlachIndex != -1)
		{
			url = url.Substr(0, lastSlachIndex);
		}
		
		
		PrintHashtable(saveData);
		FolderCreator(url);
		string saveJson = JsonSerializer.Serialize(saveData);
		//GD.Print("This: " +saveJson);
		Godot.FileAccess file = Godot.FileAccess.OpenEncryptedWithPass(url+"/" + showName, Godot.FileAccess.ModeFlags.Write, _decodepass);
		file.StoreString(saveJson);
		file.Close();
		_globalSignals.EmitSignal(nameof(GlobalSignals.ErrorLog), "Save working: " + url, 0);

	}
	

	private Hashtable FormatCuesForSave()
	{
		var cueSaveTable = new Hashtable();
		foreach (var cue1 in _globalData.Cuelist.Cuelist)
		{
			var cue = (Cue)cue1;
			var cueData = cue.GetData();
			
			cueSaveTable.Add(cue.Id, cueData);
		}
		GD.Print(cueSaveTable);
		return cueSaveTable;
	}

	private Godot.Collections.Dictionary<int, int> FormatCueslistForSave()
	{
		var cuelistSaveTable = _globalData.Cuelist.GetCueOrder();
		return cuelistSaveTable;
	}
	
	private Hashtable FormatSettingsForSave()
	{ 
		var saveTable = new Hashtable();
		// Audio patch
		var patchTable = new Hashtable();
		foreach (var patch in _globalData.Settings.GetAudioOutputPatches())
		{
			patchTable.Add(patch.Key, patch.Value.GetData());
		}
		var devices = new Hashtable();
		foreach (var device in _globalData.Devices.GetAudioDevices())
		{
			devices.Add(device.DeviceId, device.Name);
			GD.Print(device.DeviceId + " " + device.Name);
		}
		
		saveTable.Add("AudioDevices", devices);
		saveTable.Add("AudioPatch", patchTable);
		//saveTable.Add("AudioPatch", _globalData.Settings.GetAudioOutputPatches());
		return saveTable;
	}
	
	private void OpenSession()
	{
		GetNode<FileDialog>("/root/Cue2Base/OpenDialog").Visible = true;
	}
	
	private void OpenSelectedSession(string path)
	{
		Godot.FileAccess file = Godot.FileAccess.OpenEncryptedWithPass(path, Godot.FileAccess.ModeFlags.Read, _decodepass);
		//var json = JsonSerializer.Deserialize<Hashtable>(file.GetAsText());
		string jsonString = file.GetAsText();
		var json = new Json();
		Error parseResult = json.Parse(jsonString);
		if (parseResult != Error.Ok)
		{
			GD.PrintErr($"JSON parse error: {parseResult}");
			return;
		}
		
		var data = json.Data.AsGodotDictionary();
		ResetSession();
		_globalData.SessionName = Path.GetFileName(path);
		_globalData.SessionPath = path;
		LoadSession(data);
	}

	private void ResetSession()
	{
		_globalData.Cuelist.ResetCuelist();
		_globalData.Devices.ResetAudioDevices();
		_globalData.Settings.ResetSettings();
	}

	private void LoadSession(Godot.Collections.Dictionary data)
	{
		//GD.Print(data);

		//GD.Print("This here: " + data["Settings"]);
		
		bool foundCuelist = false;
		var cuelistOrder = new Godot.Collections.Dictionary<int, int>();
		
		foreach (var saveType in data)
		{
			// Load Settings
			if ((string)saveType.Key == "settings")
			{
				GD.Print("Found Settings");
				//GD.Print(saveType);
				foreach (var setting in (Godot.Collections.Dictionary)saveType.Value)
				{
					// Load Audio Devices
					if ((string)setting.Key == "AudioDevices")
					{
						foreach (var device in (Godot.Collections.Dictionary)setting.Value)
						{
							_globalData.Devices.AddAudioDeviceWithId(device.Key.AsInt32(), device.Value.ToString());
						}
					}
					// Load audio patch -- this whole system might need a serious revisit- there no need for so many nested dictionaries. 
					else if ((string)setting.Key == "AudioPatch")
					{
						GD.Print("Found audio patch");
						foreach (var patch in (Dictionary)setting.Value)
						{
							// Convert to dictionary
							var patchAsDict = patch.Value.AsGodotDictionary();
							var patchName = "";
							var patchId = -1;
							var channelData = new Godot.Collections.Dictionary<int, Godot.Collections.Dictionary<string, bool>>();
							foreach (var key in patchAsDict.Keys)
							{
								var value = patchAsDict[key];
								string keyStr = key.ToString();
								string valueStr = value.ToString();
								if (keyStr == "Name") {patchName = valueStr;}
								if (keyStr == "Id") {patchId = value.AsInt32();}
								if (keyStr == "Channels")
								{
									GD.Print("Formatting channels");
									
									var channelsAsDict = value.AsGodotDictionary();
									//var channelData = new Dictionary<int, Dictionary<string, bool>>();
									
									foreach (var channel in channelsAsDict.Keys) // 1 thorugh 6 of the channels
									{
										var channelValue = channelsAsDict[channel];
										int channelKeyInt = channel.AsInt32();
										//string channelValueStr = channelValue.ToString();
										
										//GD.Print("Channel Looop " + channelValueStr + " " + channelKeyStr);
										
										var outputAsDict = channelValue.AsGodotDictionary();
										var outputData = new Godot.Collections.Dictionary<string, bool>();
										foreach (var output in outputAsDict.Keys) // all the output routes of the channel
										{
											var outputValue = outputAsDict[output];
											string outputKeyStr = output.ToString();
											bool outputValueBool = outputValue.AsBool();
											//GD.Print(outputKeyStr + " " + outputValueBool);
											outputData[outputKeyStr] = outputValueBool;
										}
										channelData[channelKeyInt] = outputData;
									}
								}
							}
							_globalData.Settings.CreatePatchFromData(patchName, patchId, channelData);
						}
					}
				}
			}

			// Load cues
			if ((string)saveType.Key == "cues")
			{
				// Cues need to be converted back into Dictionary, then created. 
				foreach (var cue in (Godot.Collections.Dictionary)saveType.Value)
				{
					var asDict = cue.Value.AsGodotDictionary();
					var cueData = new Dictionary();
					foreach (var key in asDict.Keys)
					{
						var value = asDict[key];
						string keyStr = key.ToString();
						
						cueData[keyStr] = value;
					}
					_globalData.Cuelist.CreateCue(cueData);
				}
			}

			if ((string)saveType.Key == "cuelist")
			{
				foundCuelist = true;
				//GD.Print("CUELIST FOUND IN SAVE DATA " + saveType);
				foreach (var cue in (Godot.Collections.Dictionary)saveType.Value)
				{
					cuelistOrder.Add((int)cue.Key, (int)cue.Value);
					//GD.Print(cue.Key + " <-order cue -> " + (int)cue.Value);
				}

			}
		}
		if (foundCuelist) _globalData.Cuelist.StructureCuelistToData(cuelistOrder); // Need to be executed at end
	}

	private bool FolderCreator(string url)
	{
		string folderPath = url;
		GD.Print(url);
		if (!Directory.Exists(folderPath))
		{
			GD.Print("Saving into folder");
			Directory.CreateDirectory(folderPath);
			_globalSignals.EmitSignal(nameof(GlobalSignals.ErrorLog), "Directory created: " + url, 0);
			return true;
		}
		else
		{
			GD.Print("Save folder is existing: " + folderPath);
			return false;
		}
	} 
	
	public void PrintHashtable(Hashtable table, string indent = "")
	{
		foreach (DictionaryEntry entry in table)
		{
			var key = entry.Key;
			var value = entry.Value;

			if (value is Hashtable nestedTable)
			{
				// Nested hashtable: recurse with increased indent
				GD.Print($"{indent}{key}: {{");
				PrintHashtable(nestedTable, indent + "  ");
				GD.Print($"{indent}}}");
			}
			else
			{
				// Non-hashtable value: print key-value pair
				GD.Print($"{indent}{key}: {value ?? "null"}");
			}
		}
	}
	
}

