using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Cue2.Shared;

namespace Cue2.Shared;
	public partial class EventLogger : Node
{

	private GlobalSignals _globalSignals;

	private static List<string> _logList = new List<string>();
	private static int _logCount;

	private const string LogFilePath = "user://logs/cue2.log";
	private const long MaxLogFileSize = 1 * 1024 * 1024; // 1 MB max size - rotate to .old when exceeded to avoid overflowing the log file

	/*
	 * Receives log signals to register in log list. Each logged event has a "type" refering to what it indicates. See LogType enum.
	 * 0 = Information (white text and default)
	 * 1 = Warning (yellow text)
	 * 2 = System error (red text)
	 * 3 = Alert (red text, flash window border red) This is to only be called for issues that may effect playback. Ie Devices disconnecting, network dropout etc.
	 */
	
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
	
	public static int GetLogCount()
	{
		return _logCount;
	}
	
	public List<string> GetLogList()
	{
		return _logList;
	}
}
