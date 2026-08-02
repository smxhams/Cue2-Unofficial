using System;
using Godot;
using System.IO;
using System.Threading.Tasks;
using Cue2.UI.Shell;
using Cue2.UI.Utilities;
using Godot.Collections;

namespace Cue2.Services;

/// <summary>
/// Manages saving and loading of session data, including cues and settings.
/// Handles file dialogs, encryption via Godot's FileAccess, and data serialization/deserialization using Godot's Json.
/// </summary>
public partial class SaveManager : Node
{
	private GlobalSignals _globalSignals;
	private GlobalData _globalData;
	private AudioDevices _audioDevices;
	private MediaBackupManager _mediaBackupManager;

	private PackedScene _saveDialogScene;
	private PackedScene _openDialogScene;
	private FileDialog _saveDialog;
	private FileDialog _openDialog;
	
	
	
	private string _decodepass = "f8237hr8hnfv3fH@#R";

	private Timer _autosaveTimer;

	public override void _Ready()
	{
		_globalData = GetNode<Cue2.Services.GlobalData>("/root/GlobalData");
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_audioDevices = GetNode<AudioDevices>("/root/AudioDevices");
		_mediaBackupManager = GetNodeOrNull<MediaBackupManager>("/root/MediaBackupManager");
		
		_saveDialogScene = SceneLoader.LoadPackedScene("uid://0dv6dq3u20ku", out _); 

		_globalSignals.NewSession += OnNewSession;
		_globalSignals.Save += Save;
		_globalSignals.SaveAs += SaveAs;
		_globalSignals.OpenSession += OpenSession;
		_globalSignals.OpenSelectedSession += OpenSelectedSession;
		
		
		// Setup autosave timer (disabled by default until interval set)
		_autosaveTimer = new Timer { OneShot = false };
		_autosaveTimer.Timeout += PerformAutosave;
		AddChild(_autosaveTimer);

		if (_globalData.LaunchLoadPath != null)
		{
			LoadOnLaunch();
		}
		else
		{
			ConfigureAutosave();
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
		ConfigureAutosave();
	}

	/// <summary>
	/// Configures the autosave timer based on UserDataManager.AutosaveInterval (in minutes).
	/// If interval is 0 or no active session, autosave is disabled.
	/// </summary>
	public void ConfigureAutosave()
	{
		if (_autosaveTimer == null) return;

		int intervalMinutes = _globalData.UserDataManager?.AutosaveInterval ?? 0;
		if (intervalMinutes <= 0 || string.IsNullOrEmpty(_globalData.SessionPath))
		{
			_autosaveTimer.Stop();
			return;
		}

		_autosaveTimer.WaitTime = intervalMinutes * 60.0;
		_autosaveTimer.Start();
		GD.Print($"SaveManager:ConfigureAutosave - Autosave enabled every {intervalMinutes} minute(s).");
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
			SaveSession(_globalData.SessionPath);
		}
	}

	/// <summary>
	/// Opens the save file dialog to allow the user to choose a directory and name for the session.
	/// </summary>
	private void SaveAs()
	{
		_saveDialog = _saveDialogScene.Instantiate<FileDialog>();
		AddChild(_saveDialog);
		_saveDialog.FileSelected += OnSaveFileSelected;
		_saveDialog.FileMode = FileDialog.FileModeEnum.SaveFile;
		_saveDialog.AddFilter("*.c2 ; Cue2 Session");
		if (!string.IsNullOrEmpty(_globalData.SessionPath))
		{
			try
			{
				string baseDir = _globalData.SessionPath.GetBaseDir();
				if (DirAccess.DirExistsAbsolute(baseDir))
				{
					_saveDialog.CurrentDir = baseDir;
					GD.Print($"SaveManager:SaveAs - Set save dialog initial directory to existing session path: {baseDir}");
				}
				else
				{
					GD.Print($"SaveManager:SaveAs - Stored session directory does not exist: {baseDir}. Using default directory.");
				}
			}
			catch (Exception ex)
			{
				GD.Print($"SaveManager:SaveAs - Error setting initial directory from session path: {ex.Message}");
			}
		}
		_saveDialog.Visible = true;
		_globalSignals.EmitSignal(nameof(GlobalSignals.Log), "SaveManager:SaveAs - Waiting on save directory and show name to continue save", 0);
	}

	private void OnSaveFileSelected(string path)
	{
		SaveSession(path);
	}
	
	
	/// <summary>
	/// Re-saves the current session after media paths were rewritten to show-relative URLs.
	/// Skips media-backup enqueue to avoid a recursive copy loop.
	/// </summary>
	public void ResaveSessionAfterMediaPathUpdate()
	{
		if (string.IsNullOrEmpty(_globalData.SessionPath))
		{
			GD.Print("SaveManager:ResaveSessionAfterMediaPathUpdate - No SessionPath; skip.");
			return;
		}

		SaveSession(_globalData.SessionPath, skipMediaBackup: true);
	}

	/// <summary>
	/// Saves the current session data to the specified path and name.
	/// Creates necessary folders, serializes data to JSON, encrypts it, and writes to file.
	/// </summary>
	/// <param name="selectedPath">The full path where the session file will be saved.</param>
	/// <param name="skipMediaBackup">When true, does not enqueue media copies (used after path rewrite re-save).</param>
	private void SaveSession(string selectedPath, bool skipMediaBackup = false)
	{
		// Verify save folder structure (type-based: Audio, Video, Images, Waveforms)
		var sessionPath = DirectoryUtils.PrepareSessionDirectory(selectedPath, out var folderPaths);
		GD.Print($"SaveManager:SaveSession - Session path: {sessionPath} skipMediaBackup={skipMediaBackup}");

		if (string.IsNullOrEmpty(sessionPath))
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to prepare session directory for: {selectedPath}", 2);
			GD.PrintErr($"SaveManager:SaveSession - PrepareSessionDirectory failed for: {selectedPath}");
			return;
		}

		// Session paths must be known before serializing so relative media URLs resolve correctly
		ApplySessionPaths(sessionPath, folderPaths);
		
		
		// SAVE DATA
		var saveData = new Dictionary(); // Save type (cues, cue data)
		
		var cueSaveData = _globalData.Cuelist.GetData();
		saveData.Add("cues", cueSaveData); // Save type (cues, cue data)();

		var settingsData = _globalData.Settings.GetData();
		saveData.Add("settings", settingsData);
		
		
		// Serialize to JSON
		string jsonString = Json.Stringify(saveData);
		
		// Write encrypted file directly (no temp file)
		using var file = Godot.FileAccess.OpenEncryptedWithPass(sessionPath, Godot.FileAccess.ModeFlags.Write, _decodepass);
		if (file == null)
		{
			Error err = Godot.FileAccess.GetOpenError();
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to open file for writing: {selectedPath} with error: {err}", 2);
			GD.PrintErr($"SaveManager:SaveSession - Failed to open file: {selectedPath} Error: {err}");
			return;
		}
		file.StoreString(jsonString);
		file.Close(); // Explicit close, though using handles it

		_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
			skipMediaBackup
				? $"Session re-saved with relative media paths to {sessionPath}"
				: $"Session saved successfully to {sessionPath}",
			0);

		// Also track newly saved shows in recents
		_globalData.UserDataManager?.AddRecentShowFile(sessionPath);

		// Background-copy used media into Audio/Video/Images (respects MediaBackupEnabled)
		if (!skipMediaBackup)
		{
			try
			{
				_mediaBackupManager ??= GetNodeOrNull<MediaBackupManager>("/root/MediaBackupManager");
				_mediaBackupManager?.EnqueueShowMediaBackup();
			}
			catch (Exception ex)
			{
				GD.PrintErr($"SaveManager:SaveSession - Media backup enqueue failed: {ex.Message}");
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Media backup enqueue failed: {ex.Message}", 2);
			}
		}

		ConfigureAutosave();

		// Update title (in case signal timing)
		GetNodeOrNull<MainTitleBarUI>("/root/Cue2Base/MainWindowHandles/MainTitleBar")?.CallDeferred("UpdateTitle");
	}

	/// <summary>
	/// Applies session file path, name, and type-based media folder paths to <see cref="GlobalData"/>.
	/// </summary>
	/// <param name="sessionFilePath">Absolute path to the .c2 file.</param>
	/// <param name="folderPaths">Optional precomputed folder layout; derived from the session path when null.</param>
	private void ApplySessionPaths(string sessionFilePath, SessionFolderPaths folderPaths = null)
	{
		if (string.IsNullOrEmpty(sessionFilePath))
			return;

		folderPaths ??= DirectoryUtils.GetSessionFolderPaths(sessionFilePath);

		_globalData.SessionPath = sessionFilePath;
		_globalData.SessionName = Path.GetFileNameWithoutExtension(sessionFilePath);
		_globalData.SessionDir = folderPaths.SessionDir;
		_globalData.SessionAudioPath = folderPaths.AudioDir;
		_globalData.SessionVideoPath = folderPaths.VideoDir;
		_globalData.SessionImagesPath = folderPaths.ImagesDir;
		_globalData.SessionWaveformsPath = folderPaths.WaveformsDir;

		GD.Print($"SaveManager:ApplySessionPaths - SessionDir={_globalData.SessionDir}, Audio={_globalData.SessionAudioPath}, Video={_globalData.SessionVideoPath}, Images={_globalData.SessionImagesPath}, Waveforms={_globalData.SessionWaveformsPath}");
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

		// Wipe previous show first, then attach paths and load.
		ResetSession(clearSessionIdentity: true, logAsNewSession: false);

		ApplySessionPaths(selectedPath);
		
		LoadSession(selectedPath);

		// Document history is not retained across open/load.
		_globalData.HistoryManager?.Clear();
		
		// Track in persistent recent files for "Open Recent" in header
		_globalData.UserDataManager?.AddRecentShowFile(selectedPath);

		ConfigureAutosave();

		// Update title bar
		GetNodeOrNull<MainTitleBarUI>("/root/Cue2Base/MainWindowHandles/MainTitleBar")?.CallDeferred("UpdateTitle");
	}

	/// <summary>Signal handler for File → New / New Session hotkey.</summary>
	private void OnNewSession()
	{
		ResetSession(clearSessionIdentity: true, logAsNewSession: true);
	}

	/// <summary>
	/// Fully clears the open show and restores session defaults (File → New / New Session hotkey).
	/// Also used as the wipe step before Open Session.
	/// </summary>
	/// <param name="clearSessionIdentity">
	/// When true (New Session), clears SessionPath/name/media dirs.
	/// When opening a file, paths are cleared then re-applied by the caller after this wipe.
	/// </param>
	/// <param name="logAsNewSession">When true, logs the user-facing "New session" message.</param>
	private void ResetSession(bool clearSessionIdentity, bool logAsNewSession)
	{
		// Stop live playback before tearing down cues / devices
		_globalSignals?.EmitSignal(nameof(GlobalSignals.StopAll));

		if (clearSessionIdentity)
		{
			// Detach from any saved show path (hotkey New may skip the menu path-clearing)
			_globalData.SessionName = null;
			_globalData.SessionPath = null;
			_globalData.SessionDir = null;
			_globalData.SessionAudioPath = null;
			_globalData.SessionVideoPath = null;
			_globalData.SessionImagesPath = null;
			_globalData.SessionWaveformsPath = null;
			_globalData.ActiveShowFile = null;
		}

		_globalData.FocusedCue = -1;
		_globalData.NextCue = -1;
		_globalData.CueTotal = 0;

		// Document model
		_globalData.Cuelist?.ResetCuelist();
		_globalData.Devices?.ResetAudioDevices();
		_globalData.Settings?.ResetSettings();

		// Transient services
		GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.ClearAll();
		_mediaBackupManager?.ClearPendingJobs();

		// Inspectors / selection
		_globalSignals?.EmitSignal(nameof(GlobalSignals.ShellFocused), -1);
		_globalSignals?.EmitSignal(nameof(GlobalSignals.SyncShellInspector));

		// No path → autosave off (reconfigured again after Open applies paths)
		ConfigureAutosave();

		if (logAsNewSession)
			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log), "New session — show data reset to defaults.", 0);
		GD.Print("SaveManager:ResetSession - Full session reset complete.");
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

			// After settings + cues are linked, evaluate missing files / output / target layers.
			GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.RecheckAllQuiet();
		}
		catch (Exception ex)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Failed to load session: {ex.Message}", 2);
			GD.PrintErr($"SaveManager:LoadSession - Error: {ex.Message}  \n{ex.StackTrace}");
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
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Directory existing: {folderPath}, error: {ex.Message}", 0);
				return false;
			}
		}

		GD.Print("SaveManager:FolderCreator - Folder already exists: " + folderPath);
		return false;
	} 

	/// <summary>
	/// Performs an autosave by saving the current session data as a backup.
	/// Also ensures the main file is up to date.
	/// </summary>
	private void PerformAutosave()
	{
		if (string.IsNullOrEmpty(_globalData.SessionPath) || string.IsNullOrEmpty(_globalData.SessionName))
		{
			_autosaveTimer?.Stop();
			return;
		}

		_globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Performing autosave...", 0);
		GD.Print("SaveManager:PerformAutosave - Autosave triggered.");

		// First, update the main file
		SaveSession(_globalData.SessionPath);

		// Then create a backup copy in the Backups folder
		CreateAutosaveBackup();
	}

	/// <summary>
	/// Creates a timestamped backup of the current session in the session's Backups folder.
	/// Prunes old backups to respect the BackupDepth setting.
	/// </summary>
	private void CreateAutosaveBackup()
	{
		if (string.IsNullOrEmpty(_globalData.SessionPath) || string.IsNullOrEmpty(_globalData.SessionName))
			return;

		string sessionDir = _globalData.SessionPath.GetBaseDir();
		string backupDir = sessionDir + "/Backups";

		try
		{
			if (!DirAccess.DirExistsAbsolute(backupDir))
			{
				DirAccess.MakeDirAbsolute(backupDir);
			}

			int depth = _globalData.UserDataManager?.BackupDepth ?? 3;
			if (depth < 1) depth = 1;

			string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			string backupName = $"{_globalData.SessionName}_autosave_{timestamp}.c2";
			string backupPath = backupDir + "/" + backupName;

			// Serialize current data (same as SaveSession)
			var saveData = new Dictionary();
			var cueSaveData = _globalData.Cuelist.GetData();
			saveData.Add("cues", cueSaveData);

			var settingsData = _globalData.Settings.GetData();
			saveData.Add("settings", settingsData);

			string jsonString = Json.Stringify(saveData);

			using var file = Godot.FileAccess.OpenEncryptedWithPass(backupPath, Godot.FileAccess.ModeFlags.Write, _decodepass);
			if (file != null)
			{
				file.StoreString(jsonString);
				file.Close();
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Autosave backup created: {backupName}", 0);
				GD.Print($"SaveManager:CreateAutosaveBackup - Backup saved to {backupPath}");
			}

			// Prune old backups
			PruneAutosaveBackups(backupDir, depth);
		}
		catch (Exception ex)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Autosave backup failed: {ex.Message}", 2);
			GD.PrintErr($"SaveManager:CreateAutosaveBackup - Error: {ex.Message}");
		}
	}

	/// <summary>
	/// Removes old autosave backups so that only 'depth' most recent ones remain.
	/// </summary>
	private void PruneAutosaveBackups(string backupDir, int maxBackups)
	{
		if (!DirAccess.DirExistsAbsolute(backupDir)) return;

		var dir = DirAccess.Open(backupDir);
		if (dir == null) return;

		var backupFiles = new System.Collections.Generic.List<string>();
		string fileName = dir.GetNext();
		while (!string.IsNullOrEmpty(fileName))
		{
			if (!dir.CurrentIsDir() && fileName.Contains("_autosave_") && fileName.EndsWith(".c2"))
			{
				backupFiles.Add(backupDir + "/" + fileName);
			}
			fileName = dir.GetNext();
		}

		if (backupFiles.Count <= maxBackups) return;

		// Sort by last write time, oldest first
		backupFiles.Sort((a, b) => File.GetLastWriteTime(a).CompareTo(File.GetLastWriteTime(b)));

		int count = backupFiles.Count;
		int toDelete = count - maxBackups;
		for (int i = 0; i < toDelete; i++)
		{
			try
			{
				File.Delete(backupFiles[i]);
				GD.Print($"SaveManager:PruneAutosaveBackups - Deleted old backup {backupFiles[i]}");
			}
			catch (Exception ex)
			{
				GD.PrintErr($"SaveManager:PruneAutosaveBackups - Failed to delete {backupFiles[i]}: {ex.Message}");
			}
		}
	}
}

