using System;
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
		else
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
				$"Saving session to: {_globalData.SessionPath} with name: {_globalData.SessionName}:", 0);
			SaveSession(_globalData.SessionPath, _globalData.SessionName);
		}
	}

	private void SaveAs()
	{
		GetNode<FileDialog>("/root/Cue2Base/SaveDialog").Visible = true;
		_globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Waiting on save directory and show name to continue save", 0);
	}
	
	private void SaveSession(string selectedPath, string sessionName)
	{
		var saveData = new Hashtable(); // Save type (cues, cue data)
		
		var cueSaveData = FormatCuesForSave();
		saveData.Add("cues", cueSaveData); // Save type (cues, cue data)();
		
		var cuelistSaveData = FormatCueslistForSave();
		saveData.Add("cuelist", cuelistSaveData); // Save type (cues, cue data)();

		var settingsData = FormatSettingsForSave();
		saveData.Add("settings", settingsData);
		
		string baseDir = Path.GetDirectoryName(selectedPath);
		if (string.IsNullOrEmpty(baseDir))
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Invalid save path provided.", 2);
			GD.PrintErr("SaveManager:SaveSession - Invalid save path: " + selectedPath);
			return;
		}

		string sessionFolder = Path.Combine(baseDir, sessionName);
		if (!FolderCreator(sessionFolder))
		{
			// Folder already exists; proceed.
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Show folder already exists: {sessionFolder}", 1);
			GD.Print($"SaveManager:SaveSession - Show folder already exists: {sessionFolder}");
		}
		
		// Define the full save path: /path/to/sessionName/sessionName.c2
		string savePath = Path.Combine(sessionFolder, sessionName + ".c2");

		try
		{
			string saveJson = JsonSerializer.Serialize(saveData);
			Godot.FileAccess file = Godot.FileAccess.OpenEncryptedWithPass(savePath, Godot.FileAccess.ModeFlags.Write, _decodepass);
			if (file == null)
			{
				Error err = Godot.FileAccess.GetOpenError();
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to open file for writing: {savePath} with error: {err}", 2);
				GD.PrintErr("SaveManager:SaveSession - Failed to open file: " + savePath + " Error: " + err);
				return;
			}

			file.StoreString(saveJson);
			file.Close();

			_globalData.SessionName = sessionName;
			_globalData.SessionPath = savePath;
			
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Session saved successfully to: {savePath}", 0);
			GD.Print($"SaveManager:SaveSession - Session saved to: {savePath}");

		}
		catch (Exception ex)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Error saving session: {ex.Message}", 2);
			GD.PrintErr($"SaveManager:SaveSession - Error saving: {ex.Message}");
		}
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

		var devices = _globalData.AudioDevices.GetOpenAudioDevicesNames();

		saveTable.Add("AudioPatch", patchTable);
		saveTable.Add("AudioDevices", devices);
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
							var patchAsDict = patch.Value.AsGodotDictionary();
							var patchObj = AudioOutputPatch.FromData(patchAsDict);
							_globalData.Settings.AddPatch(patchObj);
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

	private bool FolderCreator(string folderPath)
	{
		_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Attempting to create folder: {folderPath}", 0);
		if (!Directory.Exists(folderPath))
		{
			try
			{
				GD.Print($"THIS IS THE FOLDER PATH!!!!: {folderPath}");
				Directory.CreateDirectory(folderPath);
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Directory created: {folderPath}", 0);
				return true;
			}
			catch (Exception ex)
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to create directory: {folderPath} Error: {ex.Message}", 2);
				return false;
			}
		}
		else
		{
			GD.Print("SaveManager:FolderCreator - Folder already exists: " + folderPath);
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

