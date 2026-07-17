using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using Cue2.Shared;

namespace Cue2.Shared;

/// <summary>
/// Receives log signals, keeps an in-memory history, and appends to a rotating log file on disk.
/// </summary>
/// <remarks>
/// Log type meanings (see <see cref="LogType"/>):
/// 0 = Information (white text, default),
/// 1 = Warning (yellow text),
/// 2 = System error (red text),
/// 3 = Alert (red text; may flash window border) — for issues that may affect playback.
/// </remarks>
public partial class EventLogger : Node
{
	private GlobalSignals _globalSignals;

	private static List<string> _logList = new List<string>();
	private static int _logCount;

	private const string LogFilePath = "user://logs/cue2.log";
	private const long MaxLogFileSize = 1 * 1024 * 1024; // 1 MB max size - rotate to .old when exceeded

	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_globalSignals.Log += LogEvent;

		SetupLogFile();
		LoadPreviousLogs();

		_logCount = 0;
	}

	private void SetupLogFile()
	{
		try
		{
			string fullPath = ProjectSettings.GlobalizePath(LogFilePath);
			string logDir = Path.GetDirectoryName(fullPath);
			if (!Directory.Exists(logDir))
			{
				Directory.CreateDirectory(logDir);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr("EventLogger:SetupLogFile - Failed to setup log directory: " + ex.Message);
		}
	}

	private void LoadPreviousLogs()
	{
		try
		{
			string fullPath = ProjectSettings.GlobalizePath(LogFilePath);
			if (File.Exists(fullPath))
			{
				var lines = File.ReadAllLines(fullPath);
				foreach (var line in lines)
				{
					if (!string.IsNullOrWhiteSpace(line))
					{
						_logList.Add(line);
					}
				}
				// Limit in-memory history to prevent excessive memory use
				int maxHistory = 5000;
				while (_logList.Count > maxHistory)
				{
					_logList.RemoveAt(0);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr("EventLogger:LoadPreviousLogs - Failed to load previous logs: " + ex.Message);
		}
	}

	private void LogEvent(string @logString, int @type)
	{
		var typeString = GetLogTypeName(@type);
		var printout = typeString + "  :  " + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss tt") + "  :  " + @logString;
		_logList.Add(printout);
		_logCount++;
		_globalSignals.EmitSignal(nameof(GlobalSignals.LogUpdated), printout, @type);
		if (@type == 3) _globalSignals.EmitSignal(nameof(GlobalSignals.LogAlert));
		GD.Print(printout);
		WriteToLogFile(printout);
	}

	private void WriteToLogFile(string line)
	{
		try
		{
			string fullPath = ProjectSettings.GlobalizePath(LogFilePath);
			string logDir = Path.GetDirectoryName(fullPath);
			if (!Directory.Exists(logDir))
			{
				Directory.CreateDirectory(logDir);
			}

			var fileInfo = new FileInfo(fullPath);
			if (fileInfo.Exists && fileInfo.Length > MaxLogFileSize)
			{
				string oldPath = fullPath + ".old";
				if (File.Exists(oldPath))
				{
					File.Delete(oldPath);
				}
				File.Move(fullPath, oldPath);
			}

			File.AppendAllText(fullPath, line + System.Environment.NewLine);
		}
		catch (Exception ex)
		{
			GD.PrintErr("EventLogger:WriteToLogFile - Failed to write log: " + ex.Message);
		}
	}

	private string GetLogTypeName(int type)
	{
		if (Enum.IsDefined(typeof(LogType), type))
		{
			return ((LogType)type).ToString();
		}
		return "Unknown";
	}

	/// <summary>
	/// Returns the number of log entries recorded during the current session (not including prior file history).
	/// </summary>
	/// <returns>Session log count.</returns>
	public static int GetLogCount()
	{
		return _logCount;
	}

	/// <summary>
	/// Returns the full in-memory log list (oldest first). Callers must not mutate the list.
	/// </summary>
	/// <returns>In-memory log lines, oldest at index 0.</returns>
	public List<string> GetLogList()
	{
		return _logList;
	}

	/// <summary>
	/// Returns the total number of log lines currently held in memory (including prior sessions loaded from disk).
	/// </summary>
	/// <returns>Total in-memory log count.</returns>
	public int GetTotalLogCount()
	{
		return _logList.Count;
	}

	/// <summary>
	/// Clears all in-memory logs and deletes log files on disk (current and rotated .old).
	/// </summary>
	/// <remarks>
	/// Does not emit log signals; callers that need UI feedback should log after clearing.
	/// </remarks>
	public void ClearLogs()
	{
		_logList.Clear();
		_logCount = 0;

		try
		{
			string fullPath = ProjectSettings.GlobalizePath(LogFilePath);
			if (File.Exists(fullPath))
			{
				File.Delete(fullPath);
			}

			string oldPath = fullPath + ".old";
			if (File.Exists(oldPath))
			{
				File.Delete(oldPath);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr("EventLogger:ClearLogs - Failed to delete log file(s): " + ex.Message);
		}
	}
}
