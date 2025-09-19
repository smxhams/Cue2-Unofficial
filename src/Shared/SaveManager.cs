using System;
using Godot;
using System.IO;
using System.Threading.Tasks;
using Godot.Collections;

namespace Cue2.Shared;

/// <summary>
/// Manages saving and loading of session data, including cues and settings.
/// Handles file dialogs, encryption via Godot's FileAccess, and data serialization/deserialization using Godot's Json.
/// </summary>
public partial class SaveManager : Node
{
	private GlobalSignals _globalSignals;
	private GlobalData _globalData;
	private AudioDevices _audioDevices;
	
	private string _decodepass = "f8237hr8hnfv3fH@#R";

	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_audioDevices = GetNode<AudioDevices>("/root/AudioDevices");
		
		_globalSignals.Save += Save;
		_globalSignals.SaveAs += SaveAs;
		_globalSignals.OpenSession += OpenSession;
		_globalSignals.OpenSelectedSession += OpenSelectedSession;
		
		_globalData = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData");

		if (_globalData.LaunchLoadPath != null)
		{
			LoadOnLaunch();
		}
		
	}
	
	/// <summary>
	/// Asynchronously loads a session file specified at launch, waiting for the next process frame to ensure the scene is ready.
	/// </summary>
	private async void LoadOnLaunch()
	{
		await ToSignal(GetTree(), "process_frame");
		GD.Print("SaveManager:LoadOnLaunch - Load On Launch");
		OpenSelectedSession(_globalData.LaunchLoadPath);
	}
	
	/// <summary>
	/// Initiates a save operation. If the session is unnamed or has no path, triggers SaveAs.
	/// Otherwise, saves to the existing path.
	/// </summary>
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

	/// <summary>
	/// Opens the save file dialog to allow the user to choose a directory and name for the session.
	/// </summary>
	private void SaveAs()
	{
		GetNode<FileDialog>("/root/Cue2Base/SaveDialog").Visible = true;
		_globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Waiting on save directory and show name to continue save", 0);
	}
	
	
	/// <summary>
	/// Saves the current session data to the specified path and name.
	/// Creates necessary folders, serializes data to JSON, encrypts it, and writes to file.
	/// </summary>
	/// <param name="selectedPath">The full path where the session file will be saved.</param>
	/// <param name="sessionName">The name of the session (used for logging).</param>
	private void SaveSession(string selectedPath, string sessionName)
	{
		GD.Print($"SaveManager:SaveSession - SessionFolder: {selectedPath}, SessionName: {sessionName}");
		
		// SAVE DATA
		var saveData = new Dictionary(); // Save type (cues, cue data)
		
		var cueSaveData = _globalData.Cuelist.GetData();
		saveData.Add("cues", cueSaveData); // Save type (cues, cue data)();

		var settingsData = _globalData.Settings.GetData();
		saveData.Add("settings", settingsData);
		
		// Create folder if needed
		string baseDir = Path.GetDirectoryName(selectedPath);
		if (string.IsNullOrEmpty(baseDir))
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Invalid save path provided.", 2);
			GD.PrintErr("SaveManager:SaveSession - Invalid save path: " + selectedPath);
			return;
		}
		FolderCreator(baseDir);
		
		// Serialize to JSON
		string jsonString = Json.Stringify(saveData);
		
		// Write encrypted file directly (no temp file)
		using var file = Godot.FileAccess.OpenEncryptedWithPass(selectedPath, Godot.FileAccess.ModeFlags.Write, _decodepass);
		if (file == null)
		{
			Error err = Godot.FileAccess.GetOpenError();
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to open file for writing: {selectedPath} with error: {err}", 2);
			GD.PrintErr($"SaveManager:SaveSession - Failed to open file: {selectedPath} Error: {err}");
			return;
		}
		file.StoreString(jsonString);
		file.Close(); // Explicit close, though using handles it

		_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Session saved successfully to {selectedPath}", 0);
        
		// Update session info
		_globalData.SessionPath = selectedPath;
		_globalData.SessionName = Path.GetFileNameWithoutExtension(selectedPath);
	}
	
	/// <summary>
	/// Opens the open file dialog for selecting a session to load.
	/// </summary>
	private void OpenSession()
	{
		GetNode<FileDialog>("/root/Cue2Base/OpenDialog").Visible = true;
	}
	
	/// <summary>
	/// Loads and processes a selected session file, decrypting it, parsing JSON, and applying data to settings and cuelist.
	/// </summary>
	/// <param name="selectedPath">The file path of the session to load.</param>
	private void OpenSelectedSession(string selectedPath)
	{
		// Verify file before resetting current session.
		if (!File.Exists(selectedPath))
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Session file not found: {selectedPath}", 2);
			GD.PrintErr("SaveManager:LoadSession - File not found: " + selectedPath);
			return;
		}
		
		// Reset session
		_globalData.Cuelist.ResetCuelist();
		_globalData.Devices.ResetAudioDevices();
		_globalData.Settings.ResetSettings();
		
		LoadSession(selectedPath);
		
		// Update session info
		_globalData.SessionPath = selectedPath;
		_globalData.SessionName = Path.GetFileNameWithoutExtension(selectedPath);
		
	}

	
	/// <summary>
	/// Loads the session data from the encrypted file, parses JSON, and delegates loading to settings and cuelist.
	/// </summary>
	/// <param name="selectedPath">The file path to load from.</param>
	private void LoadSession(string selectedPath)
	{
		try
		{
			using var file = Godot.FileAccess.OpenEncryptedWithPass(selectedPath, Godot.FileAccess.ModeFlags.Read, _decodepass);
			if (file == null)
			{
				Error err = Godot.FileAccess.GetOpenError();
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to open file for reading: {selectedPath} with error: {err}", 2);
				GD.PrintErr($"SaveManager:LoadSession - Failed to open file: {selectedPath} Error: {err}");
				return;
			}
			
			string jsonString = file.GetAsText();
			using var json = new Json();
			Error parseResult = json.Parse(jsonString);
			if (parseResult != Error.Ok)
			{
				GD.PrintErr($"SaveManager:LoadSession - JSON parse error: {parseResult}");
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"JSON parse error: {parseResult}", 2);
				return;
			}
			var saveData = json.Data.AsGodotDictionary();

			if (saveData.ContainsKey("settings"))
			{
				GD.Print("SaveManager:LoadSession - Loading Settings");
				var settingsData = saveData["settings"].AsGodotDictionary();
				_globalData.Settings.LoadSettings(settingsData);
			}
			
			if (saveData.ContainsKey("cues"))
			{
				GD.Print("SaveManager:LoadSession - Loading Cues");
				var cuesData = saveData["cues"].AsGodotDictionary();
				_globalData.Cuelist.LoadData(cuesData);
			}
		}
		catch (Exception ex)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to load session: {ex.Message}", 2);
			GD.PrintErr("SaveManager:LoadSession - Error: " + ex.Message);
		}
		
	}
	
	/// <summary>
	/// Creates a directory if it does not exist, logging the attempt and result.
	/// </summary>
	/// <param name="folderPath">The path of the folder to create.</param>
	/// <returns>True if created, false if it already exists or creation failed.</returns>
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

