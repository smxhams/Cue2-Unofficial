// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using System;
using Godot;
using System.IO;
using System.Threading.Tasks;
using Cue2.UI.Popups;
using Cue2.UI.Shell;
using Cue2.UI.Utilities;
using Godot.Collections;

namespace Cue2.Services;

/// <summary>
/// Manages saving and loading of session data, including cues and settings.
/// Handles file dialogs, encryption via Godot's FileAccess, and data serialization/deserialization using Godot's Json.
/// </summary>
/// <remarks>
/// Showfiles are versioned via <see cref="ShowfileFormat"/>. On open, version is checked
/// <b>before</b> session reset; mismatches prompt <see cref="VersionMismatchDialog"/> and may
/// run <see cref="ShowfileMigrator"/> before load.
/// </remarks>
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

	/// <summary>Prevents overlapping open/version-dialog flows for concurrent open requests.</summary>
	private bool _isOpenInProgress;

	private VersionMismatchDialog _activeVersionDialog;

	/// <summary>
	/// True when the live session was opened from a showfile whose formatVersion is newer than
	/// this build. Overwriting that file would re-stamp the current schema and can drop data.
	/// </summary>
	private bool _openedFromNewerFormat;
	
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

		if (!string.IsNullOrEmpty(_globalData.StartupOpenPath))
		{
			LoadStartupSession();
		}
		else
		{
			ConfigureAutosave();
		}
		
	}
	
	/// <summary>
	/// Opens the showfile selected by startup preference (open last from user data), after one process frame.
	/// </summary>
	private async void LoadStartupSession()
	{
		await ToSignal(GetTree(), "process_frame");
		string path = _globalData.StartupOpenPath;
		GD.Print($"SaveManager:LoadStartupSession - Opening startup showfile: {path}");
		if (!string.IsNullOrEmpty(path))
			OpenSelectedSession(path);
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
			return;
		}

		// Never overwrite a newer-format original with this build's schema.
		if (_openedFromNewerFormat)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
				"This show was opened from a newer Cue2 format. Use Save As to write a copy " +
				"this version can own — overwriting the original is blocked.",
				(int)LogType.Warning);
			SaveAs();
			return;
		}

		_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
			$"Saving session to: {_globalData.SessionPath} with name: {_globalData.SessionName}:", 0);
		SaveSession(_globalData.SessionPath);
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
		// Save As always writes this build's format; user chose a new path (or accepted overwrite).
		SaveSession(path, skipMediaBackup: false, clearNewerFormatGuard: true);
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

		if (_openedFromNewerFormat)
		{
			GD.Print(
				"SaveManager:ResaveSessionAfterMediaPathUpdate - Skipped (session opened from newer format; " +
				"use Save As to create a this-version copy).");
			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
				"Media paths updated, but silent re-save was skipped because this show uses a newer format. " +
				"Use Save As if you want a copy this version of Cue2 can own.",
				(int)LogType.Warning);
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
	/// <param name="clearNewerFormatGuard">
	/// When true (Save As), clears the forward-compat open guard after a successful write.
	/// </param>
	private void SaveSession(string selectedPath, bool skipMediaBackup = false, bool clearNewerFormatGuard = false)
	{
		// Block accidental overwrite of a newer-format original via any path (autosave, etc.).
		if (_openedFromNewerFormat &&
		    !string.IsNullOrEmpty(_globalData.SessionPath) &&
		    !clearNewerFormatGuard &&
		    PathsEqual(selectedPath, _globalData.SessionPath))
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
				"Save blocked: this show was opened from a newer Cue2 format. Use Save As to write a new file.",
				(int)LogType.Warning);
			GD.Print("SaveManager:SaveSession - Blocked overwrite of newer-format session path.");
			return;
		}

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
		var saveData = BuildSaveDataDictionary();

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

		// Successful Save As of a forward-compat open: this file is now owned by current format.
		if (clearNewerFormatGuard)
			_openedFromNewerFormat = false;

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
	/// Loads and processes a selected session file.
	/// Version is checked first (before any session reset); mismatches require user confirmation.
	/// </summary>
	/// <param name="selectedPath">The file path of the session to load.</param>
	private void OpenSelectedSession(string selectedPath)
	{
		if (_isOpenInProgress)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
				"A showfile open is already in progress.", (int)LogType.Warning);
			GD.Print("SaveManager:OpenSelectedSession - Open already in progress; ignoring.");
			return;
		}

		// Verify file before resetting current session.
		if (!File.Exists(selectedPath))
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Session file not found: {selectedPath}", 2);
			GD.PrintErr("SaveManager:OpenSelectedSession - File not found: " + selectedPath);
			return;
		}

		// Peek encrypted JSON without mutating the live session.
		if (!TryReadSaveData(selectedPath, out var saveData, out string readError))
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
				$"Failed to read session (session not changed): {readError}", 2);
			GD.PrintErr($"SaveManager:OpenSelectedSession - Read failed: {readError}");
			return;
		}

		var fileVersion = ShowfileFormat.ReadVersion(saveData);
		GD.Print(
			$"SaveManager:OpenSelectedSession - File version: {fileVersion.ToDisplayString()}; " +
			$"app: {Cue2.Version.SemanticVersionString} format {ShowfileFormat.CurrentFormatVersion}");

		if (fileVersion.MatchesCurrent)
		{
			CompleteOpenSession(selectedPath, saveData, fileVersion, userConfirmedMismatch: false);
			return;
		}

		// Version differs: confirm before any open/reset actions.
		ShowVersionMismatchDialog(selectedPath, saveData, fileVersion);
	}

	/// <summary>
	/// Shows the version-mismatch confirmation dialog. Session remains untouched until Attempt Open.
	/// </summary>
	private void ShowVersionMismatchDialog(string selectedPath, Dictionary saveData, ShowfileVersionInfo fileVersion)
	{
		DismissActiveVersionDialog();

		var dialog = VersionMismatchDialog.Create(out string loadError);
		if (dialog == null)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
				$"Could not show version dialog ({loadError}). Open cancelled; session unchanged.", 2);
			GD.PrintErr($"SaveManager:ShowVersionMismatchDialog - {loadError}");
			return;
		}

		_isOpenInProgress = true;
		_activeVersionDialog = dialog;

		dialog.Configure(selectedPath, fileVersion);

		dialog.AttemptOpen += () =>
		{
			_activeVersionDialog = null;
			_isOpenInProgress = false;
			CompleteOpenSession(selectedPath, saveData, fileVersion, userConfirmedMismatch: true);
		};

		dialog.Cancelled += () =>
		{
			_activeVersionDialog = null;
			_isOpenInProgress = false;
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
				$"Open cancelled (version mismatch): {selectedPath}", 0);
			GD.Print("SaveManager:ShowVersionMismatchDialog - User cancelled open.");
		};

		// Parent under main scene when available so the window attaches cleanly.
		var parent = GetTree()?.Root?.GetNodeOrNull("Cue2Base") ?? (Node)this;
		parent.AddChild(dialog);
		dialog.ShowConfigured();

		_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
			$"Showfile version differs ({fileVersion.ToDisplayString()}). Confirm before open.", 1);
	}

	/// <summary>
	/// Closes any open version dialog without proceeding with load.
	/// </summary>
	private void DismissActiveVersionDialog()
	{
		if (_activeVersionDialog != null && IsInstanceValid(_activeVersionDialog))
		{
			_activeVersionDialog.Hide();
			_activeVersionDialog.QueueFree();
		}

		_activeVersionDialog = null;
		_isOpenInProgress = false;
	}

	/// <summary>
	/// Migrates (if needed), resets the session, and applies save data.
	/// Called only after version matches or the user confirms Attempt Open.
	/// </summary>
	/// <remarks>
	/// The live session is wiped only after migration succeeds. If load then fails, the
	/// session is forced back to an empty New Session state and the path is <b>not</b>
	/// added to recents (see <see cref="FailOpenAfterReset"/>).
	/// </remarks>
	/// <param name="selectedPath">Absolute path of the .c2 file.</param>
	/// <param name="saveData">Already-parsed root dictionary (may be mutated by migration).</param>
	/// <param name="fileVersion">Version metadata as read from the file.</param>
	/// <param name="userConfirmedMismatch">True when the user accepted the version warning.</param>
	private void CompleteOpenSession(
		string selectedPath,
		Dictionary saveData,
		ShowfileVersionInfo fileVersion,
		bool userConfirmedMismatch)
	{
		bool sessionWiped = false;
		try
		{
			// Migrate older (or stamp current) formats before applying to the live model.
			// Session is not wiped yet — failed migration leaves the previous show intact.
			if (ShowfileMigrator.NeedsMigration(fileVersion.FormatVersion) ||
			    ShowfileMigrator.IsNewerThanSupported(fileVersion.FormatVersion) ||
			    userConfirmedMismatch)
			{
				var migration = ShowfileMigrator.MigrateToCurrent(saveData, fileVersion.FormatVersion);
				if (!string.IsNullOrEmpty(migration.Log))
				{
					GD.Print($"SaveManager:CompleteOpenSession - Migration log:\n{migration.Log}");
					_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
						$"Showfile migration: {migration.Log.Replace("\n", " | ")}", 0);
				}

				if (!migration.Success)
				{
					_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
						$"Showfile migration failed (session not changed): {migration.Error}", 2);
					GD.PrintErr($"SaveManager:CompleteOpenSession - Migration failed: {migration.Error}");
					return;
				}
			}
			else
			{
				// Matching open path: ensure stamps exist for any later re-save consistency.
				ShowfileFormat.StampCurrentVersion(saveData);
			}

			if (!TryValidateOpenPayload(saveData, out var validateError))
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
					$"Showfile rejected (session not changed): {validateError}", 2);
				GD.PrintErr($"SaveManager:CompleteOpenSession - Validation failed: {validateError}");
				return;
			}

			// Wipe previous show only after version gate + successful migration + shape check.
			ResetSession(clearSessionIdentity: true, logAsNewSession: false);
			sessionWiped = true;

			ApplySessionPaths(selectedPath);

			if (!LoadSessionFromData(saveData, out var loadError))
			{
				FailOpenAfterReset(selectedPath, loadError ?? "Unknown load error");
				return;
			}

			// Success only: history, recents, autosave, title.
			_openedFromNewerFormat = fileVersion.IsNewerFormat;
			_globalData.HistoryManager?.Clear();
			_globalData.UserDataManager?.AddRecentShowFile(selectedPath);
			ConfigureAutosave();
			GetNodeOrNull<MainTitleBarUI>("/root/Cue2Base/MainWindowHandles/MainTitleBar")
				?.CallDeferred("UpdateTitle");

			_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
				$"Session opened: {selectedPath}", 0);

			if (_openedFromNewerFormat)
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
					"Opened a newer showfile format. Saving will not overwrite this file — use Save As " +
					"to create a copy this version of Cue2 can own.",
					(int)LogType.Warning);
			}

			if (userConfirmedMismatch)
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
					$"Opened showfile after version confirmation: {selectedPath}", 0);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"SaveManager:CompleteOpenSession - Error: {ex.Message}\n{ex.StackTrace}");
			if (sessionWiped)
			{
				// Previous show already gone — do not leave a half-applied open or pollute recents.
				try
				{
					FailOpenAfterReset(selectedPath, ex.Message);
				}
				catch (Exception failEx)
				{
					GD.PrintErr($"SaveManager:CompleteOpenSession - FailOpenAfterReset: {failEx.Message}");
					_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
						$"Failed to open session: {ex.Message}", 2);
				}
			}
			else
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
					$"Failed to open session (session not changed): {ex.Message}", 2);
			}
		}
	}

	/// <summary>
	/// Lightweight pre-reset check so we do not wipe the live show for an obviously unusable file.
	/// </summary>
	/// <param name="saveData">Parsed showfile root.</param>
	/// <param name="error">Human-readable failure reason.</param>
	/// <returns>True when the payload is worth attempting a load after reset.</returns>
	private static bool TryValidateOpenPayload(Dictionary saveData, out string error)
	{
		error = null;
		if (saveData == null || saveData.Count == 0)
		{
			error = "Showfile is empty or not a valid object.";
			return false;
		}

		// Accept shows that only have settings or only cues (legacy / partial tooling dumps),
		// but reject non-dictionary blocks that would throw after wipe.
		if (saveData.ContainsKey("settings"))
		{
			var settingsVar = saveData["settings"];
			if (settingsVar.VariantType != Variant.Type.Dictionary)
			{
				error = "Showfile 'settings' block is not an object.";
				return false;
			}
		}

		if (saveData.ContainsKey("cues"))
		{
			var cuesVar = saveData["cues"];
			if (cuesVar.VariantType != Variant.Type.Dictionary)
			{
				error = "Showfile 'cues' block is not an object.";
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// After a failed open that already wiped the previous show: force a clean empty session,
	/// do not add the path to recents, and surface a clear error.
	/// </summary>
	/// <param name="selectedPath">Path that failed to open (for logs only).</param>
	/// <param name="reason">Failure reason shown to the user.</param>
	private void FailOpenAfterReset(string selectedPath, string reason)
	{
		GD.PrintErr($"SaveManager:FailOpenAfterReset - path={selectedPath} reason={reason}");

		// Drop any half-applied settings/cues from the failed load.
		ResetSession(clearSessionIdentity: true, logAsNewSession: false);
		_globalData.HistoryManager?.Clear();
		ConfigureAutosave();
		GetNodeOrNull<MainTitleBarUI>("/root/Cue2Base/MainWindowHandles/MainTitleBar")
			?.CallDeferred("UpdateTitle");

		_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
			$"Failed to open showfile — session reset to empty (not added to recents): {reason}", 2);
	}

	/// <summary>
	/// Decrypts and parses a showfile without modifying session state.
	/// </summary>
	/// <param name="selectedPath">Absolute path to the .c2 file.</param>
	/// <param name="saveData">Parsed root dictionary on success.</param>
	/// <param name="error">Error description on failure.</param>
	/// <returns>True when the file was read and parsed as a dictionary.</returns>
	private bool TryReadSaveData(string selectedPath, out Dictionary saveData, out string error)
	{
		saveData = null;
		error = null;

		try
		{
			using var file = Godot.FileAccess.OpenEncryptedWithPass(
				selectedPath, Godot.FileAccess.ModeFlags.Read, _decodepass);
			if (file == null)
			{
				Error err = Godot.FileAccess.GetOpenError();
				error = $"Failed to open file for reading: {selectedPath} (error {err})";
				return false;
			}

			string jsonString = file.GetAsText();
			using var json = new Json();
			Error parseResult = json.Parse(jsonString);
			if (parseResult != Error.Ok)
			{
				error = $"JSON parse error: {parseResult}";
				return false;
			}

			saveData = json.Data.AsGodotDictionary();
			if (saveData == null)
			{
				error = "Showfile root is not a dictionary.";
				return false;
			}

			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	/// <summary>
	/// Builds the root save dictionary (version stamps + cues + settings).
	/// </summary>
	/// <returns>Dictionary ready for JSON serialization.</returns>
	private Dictionary BuildSaveDataDictionary()
	{
		var saveData = new Dictionary();
		ShowfileFormat.StampCurrentVersion(saveData);

		var cueSaveData = _globalData.Cuelist.GetData();
		saveData.Add("cues", cueSaveData);

		var settingsData = _globalData.Settings.GetData();
		saveData.Add("settings", settingsData);

		return saveData;
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

		// New / failed open wipe — no longer protecting a newer-format original path.
		_openedFromNewerFormat = false;

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
	/// Loads session data from an already-parsed (and optionally migrated) dictionary.
	/// </summary>
	/// <param name="saveData">Root showfile dictionary with settings/cues keys.</param>
	/// <param name="error">On failure, a human-readable reason (null on success).</param>
	/// <returns>
	/// True when settings and cues were applied without throwing. False on null payload or
	/// any exception (caller must treat the live model as unusable / half-applied).
	/// </returns>
	private bool LoadSessionFromData(Dictionary saveData, out string error)
	{
		error = null;
		if (saveData == null)
		{
			error = "Save data is null.";
			GD.PrintErr("SaveManager:LoadSessionFromData - save data is null");
			return false;
		}

		try
		{
			if (saveData.ContainsKey("settings"))
			{
				GD.Print("SaveManager:LoadSessionFromData - Loading Settings");
				var settingsData = saveData["settings"].AsGodotDictionary();
				_globalData.Settings.LoadSettings(settingsData);
			}

			if (saveData.ContainsKey("cues"))
			{
				GD.Print("SaveManager:LoadSessionFromData - Loading Cues");
				var cuesData = saveData["cues"].AsGodotDictionary();
				_globalData.Cuelist.LoadData(cuesData);
			}

			// After settings + cues are linked, evaluate missing files / output / target layers.
			GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.RecheckAllQuiet();
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			GD.PrintErr($"SaveManager:LoadSessionFromData - Error: {ex.Message}  \n{ex.StackTrace}");
			return false;
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

			// Serialize current data (same as SaveSession, including version stamps)
			var saveData = BuildSaveDataDictionary();
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
	/// Path equality for save/open guards (normalized absolute paths, case-insensitive on Windows).
	/// </summary>
	private static bool PathsEqual(string a, string b)
	{
		if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
			return false;
		try
		{
			string na = System.IO.Path.GetFullPath(a.Replace('\\', '/'));
			string nb = System.IO.Path.GetFullPath(b.Replace('\\', '/'));
			return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
		}
	}

	/// <summary>
	/// Removes old autosave backups so that only <paramref name="maxBackups"/> most recent ones remain.
	/// </summary>
	/// <remarks>
	/// Uses <see cref="Directory.GetFiles"/> rather than Godot <see cref="DirAccess.GetNext"/> alone
	/// (listing requires <c>ListDirBegin</c>; without it prune never saw any files and backups grew unbounded).
	/// </remarks>
	private void PruneAutosaveBackups(string backupDir, int maxBackups)
	{
		if (maxBackups < 1)
			maxBackups = 1;

		if (string.IsNullOrEmpty(backupDir) || !Directory.Exists(backupDir))
			return;

		var backupFiles = new System.Collections.Generic.List<string>();
		try
		{
			foreach (string path in Directory.GetFiles(backupDir))
			{
				string fileName = Path.GetFileName(path);
				if (string.IsNullOrEmpty(fileName))
					continue;
				// Match CreateAutosaveBackup naming: {SessionName}_autosave_{timestamp}.c2
				if (fileName.Contains("_autosave_", StringComparison.Ordinal) &&
				    fileName.EndsWith(".c2", StringComparison.OrdinalIgnoreCase))
				{
					backupFiles.Add(path);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"SaveManager:PruneAutosaveBackups - Failed to list {backupDir}: {ex.Message}");
			return;
		}

		if (backupFiles.Count <= maxBackups)
			return;

		// Sort by last write time, oldest first
		backupFiles.Sort((a, b) => File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)));

		int toDelete = backupFiles.Count - maxBackups;
		for (int i = 0; i < toDelete; i++)
		{
			try
			{
				File.Delete(backupFiles[i]);
				GD.Print($"SaveManager:PruneAutosaveBackups - Deleted old backup {Path.GetFileName(backupFiles[i])}");
			}
			catch (Exception ex)
			{
				GD.PrintErr($"SaveManager:PruneAutosaveBackups - Failed to delete {backupFiles[i]}: {ex.Message}");
			}
		}
	}
}

