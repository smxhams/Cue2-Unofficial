// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Godot;

namespace Cue2.Services;

/// <summary>
/// Per-open stage timings for showfile load (P0). Active only while
/// <see cref="Current"/> is set — New Session and history restore do not record.
/// </summary>
/// <remarks>
/// Look for <c>SessionLoadTimer:</c> in the Godot output. Paste blocks into
/// <c>docs/showfile-load-plan.md</c> after each load-path PR.
/// </remarks>
public sealed class SessionLoadTimer
{
	/// <summary>
	/// Timer for the in-flight showfile open, or null when not opening a show.
	/// </summary>
	public static SessionLoadTimer Current { get; private set; }

	private readonly Stopwatch _wall = Stopwatch.StartNew();
	private readonly Stopwatch _stage = new();
	private readonly List<(string Name, long Ms)> _stages = new();
	private readonly string _path;
	private string _activeStage;
	private long _applyMs = -1;
	private bool _finished;

	/// <summary>Absolute showfile path being opened.</summary>
	public string Path => _path;

	/// <summary>File size in bytes when known (0 if not set).</summary>
	public long FileBytes { get; set; }

	/// <summary>Cue count applied (0 until set by the cue loader).</summary>
	public int CueCount { get; set; }

	private SessionLoadTimer(string path)
	{
		_path = path ?? string.Empty;
	}

	/// <summary>
	/// Starts a new open measurement and makes it <see cref="Current"/>.
	/// Finishes any previous timer so overlapping opens cannot leak.
	/// </summary>
	/// <param name="path">Absolute showfile path.</param>
	/// <returns>The new current timer.</returns>
	public static SessionLoadTimer Start(string path)
	{
		if (Current != null && !Current._finished)
			Current.Finish("superseded");
		var timer = new SessionLoadTimer(path);
		Current = timer;
		return timer;
	}

	/// <summary>
	/// Ends the previous stage (if any) and starts <paramref name="stageName"/>.
	/// No-op when this instance is not <see cref="Current"/> or already finished.
	/// </summary>
	/// <param name="stageName">Short stable id (e.g. <c>parse</c>, <c>settings.audio</c>).</param>
	public void Begin(string stageName)
	{
		if (_finished || Current != this)
			return;
		EndActiveStage();
		_activeStage = stageName;
		_stage.Restart();
	}

	/// <summary>
	/// Ends the active stage without starting another (yields, dialogs, idle gaps).
	/// Those gaps still count toward wall time, not toward a named stage.
	/// </summary>
	public void Pause()
	{
		if (_finished || Current != this)
			return;
		EndActiveStage();
	}

	/// <summary>
	/// Records elapsed wall time at GO-safe apply end (overlay about to hide).
	/// Housekeeping stages may still be appended after this.
	/// </summary>
	public void MarkApplyComplete()
	{
		if (_finished || Current != this)
			return;
		EndActiveStage();
		if (_applyMs < 0)
			_applyMs = _wall.ElapsedMilliseconds;
	}

	/// <summary>
	/// Ends the last stage, prints the breakdown, and clears <see cref="Current"/>.
	/// Safe to call more than once.
	/// </summary>
	/// <param name="outcome">e.g. <c>complete</c>, <c>failed</c>, <c>pre-apply</c>.</param>
	public void Finish(string outcome)
	{
		if (_finished)
			return;
		EndActiveStage();
		_finished = true;
		if (Current == this)
			Current = null;

		long wallMs = _wall.ElapsedMilliseconds;
		var sb = new StringBuilder(512);
		sb.Append("SessionLoadTimer: ");
		sb.Append(outcome ?? "done");
		sb.Append(" path=").Append(_path);
		sb.Append(" bytes=").Append(FileBytes);
		sb.Append(" cues=").Append(CueCount);
		sb.AppendLine();
		foreach (var (name, ms) in _stages)
		{
			sb.Append("  ");
			sb.Append(name.PadRight(24));
			sb.Append(ms.ToString().PadLeft(8));
			sb.AppendLine(" ms");
		}

		if (_applyMs >= 0)
		{
			sb.Append("  ");
			sb.Append("--- apply (GO-safe)".PadRight(24));
			sb.Append(_applyMs.ToString().PadLeft(8));
			sb.AppendLine(" ms ---");
		}

		sb.Append("  ");
		sb.Append("--- wall".PadRight(24));
		sb.Append(wallMs.ToString().PadLeft(8));
		sb.Append(" ms ---");

		GD.Print(sb.ToString());
	}

	/// <summary>
	/// One-line user log after a successful GO-safe apply (not the full breakdown).
	/// </summary>
	/// <returns>Short summary, or empty when apply was not marked.</returns>
	public string FormatApplySummary()
	{
		if (_applyMs < 0)
			return string.Empty;
		string size = FileBytes > 0 ? $", {FormatBytes(FileBytes)}" : string.Empty;
		string cues = CueCount > 0 ? $", {CueCount} cues" : string.Empty;
		return $"Showfile apply {(_applyMs / 1000.0):0.0}s{cues}{size}";
	}

	private void EndActiveStage()
	{
		if (string.IsNullOrEmpty(_activeStage))
			return;
		_stage.Stop();
		_stages.Add((_activeStage, _stage.ElapsedMilliseconds));
		_activeStage = null;
	}

	private static string FormatBytes(long bytes)
	{
		if (bytes < 1024)
			return $"{bytes} B";
		double kb = bytes / 1024.0;
		if (kb < 1024)
			return $"{kb:0.0} KB";
		return $"{kb / 1024.0:0.00} MB";
	}
}
