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
	
	private async void LoadOnLaunch(string path)
	{
		await ToSignal(GetTree(), "process_frame");
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
		GD.Print($"SaveMasnager:SaveSession - SessionFolder: {selectedPath}, SessionName: {sessionName}");
		var saveData = new Dictionary(); // Save type (cues, cue data)
		
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

		string sessionFolder = selectedPath;//Path.Combine(baseDir, sessionName);
		if (!FolderCreator(sessionFolder))
		{
			// Folder already exists; proceed.
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Show folder already exists: {sessionFolder}", 1);
			GD.Print($"SaveManager:SaveSession - Show folder already exists: {sessionFolder}");
		}
		
		// Define the full save path: /path/to/sessionName/sessionName.c2
		string savePath = Path.Combine(sessionFolder, sessionName + ".c2");
		GD.Print($"SaveManager:SaveSession - SAVE PATH: {savePath}, SessionFolder: {sessionFolder}, SessionName: {sessionName}");
		try
		{
			string saveJson = Json.Stringify(saveData);
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
			_globalData.SessionPath = sessionFolder;
			
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Session saved successfully to: {savePath}", 0);
		}
		catch (Exception ex)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Error saving session: {ex.Message}", 2);
			GD.PrintErr($"SaveManager:SaveSession - Error saving: {ex.Message}");
		}
	}
	

	private Dictionary FormatCuesForSave()
	{
		var cueSaveTable = new Dictionary();
		foreach (var cue1 in _globalData.Cuelist.Cuelist)
		{
			var cue = (Cue)cue1;
			var cueData = cue.GetData();
			
			cueSaveTable.Add(cue.Id, cueData);
		}
		GD.Print($"\"SaveManager:FormatCuesForSave - {cueSaveTable}");
		return cueSaveTable;
	}

	private Godot.Collections.Dictionary<int, int> FormatCueslistForSave()
	{
		var cuelistSaveTable = _globalData.Cuelist.GetCueOrder();
		return cuelistSaveTable;
	}
	
	private Dictionary FormatSettingsForSave()
	{ 
		var saveTable = new Dictionary();
		// Audio patch
		var patchTable = new Dictionary();
		
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
		try
		{
			Godot.FileAccess file =
				Godot.FileAccess.OpenEncryptedWithPass(path, Godot.FileAccess.ModeFlags.Read, _decodepass);
			if (file == null)
			{
				Error err = Godot.FileAccess.GetOpenError();
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to open file for reading: {path} with error: {err}", 2);
				GD.PrintErr("SaveManager:OpenSelectedSession - Failed to open file: " + path + " Error: " + err);
				return;
			}
			string jsonString = file.GetAsText();
			file.Close();
			var json = new Json();
			Error parseResult = json.Parse(jsonString);
			if (parseResult != Error.Ok)
			{
				GD.PrintErr($"JSON parse error: {parseResult}");
				return;
			}
			var data = json.Data.AsGodotDictionary();
			ResetSession();
			_globalData.SessionName = Path.GetFileNameWithoutExtension(path);
			_globalData.SessionPath = Path.GetDirectoryName(path);
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Session loaded successfully.", 0);
			LoadSession(data);
			LinkAudioPatches();
		}
		catch (Exception ex)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Error loading session: {ex.Message}", 2);
		}
		
	}

	private void ResetSession()
	{
		_globalData.Cuelist.ResetCuelist();
		_globalData.Devices.ResetAudioDevices();
		_globalData.Settings.ResetSettings();
	}

	private void LoadSession(Dictionary data)
	{
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

	/// <summary>
	/// This is a temporary solution to link patches to audio components.
	/// Currently needed because of load ordering. 
	/// </summary>
	private void LinkAudioPatches()
	{
		var patches = _globalData.Settings.GetAudioOutputPatches();
		foreach (var cueObj in _globalData.Cuelist.Cuelist)
		{
			var cue = (Cue)cueObj;
			foreach (var component in cue.Components)
			{
				if (component is AudioComponent audioComponent)
				{
					if (patches.TryGetValue(audioComponent.PatchId, out var patch))
					{
						audioComponent.Patch = patch;
						audioComponent.Patch = patches[audioComponent.PatchId];
						GD.Print($"SaveManager:LinkAudioPatches - Linked patch {audioComponent.PatchId} to audio component in cue {cue.Id}");
					}
					else
					{
						_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Patch {audioComponent.PatchId} not found for audio component in cue {cue.Id}", 2); //!!!
					}
				}
			}
		}
	}

	private bool FolderCreator(string folderPath)
	{
		_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Attempting to create folder: {folderPath}", 0);
		if (!Directory.Exists(folderPath))
		{
			try
			{
				Directory.CreateDirectory(folderPath);
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Directory created: {folderPath}", 0);
				return true;
			}
			catch (Exception ex)
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Directory existing: {folderPath}", 0);
				return false;
			}
		}

		GD.Print("SaveManager:FolderCreator - Folder already exists: " + folderPath);
		return false;
	} 
}

