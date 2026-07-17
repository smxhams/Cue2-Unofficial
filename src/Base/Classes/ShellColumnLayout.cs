using System;
using Godot;

namespace Cue2.Base.Classes;

/// <summary>
/// Shared column metrics for <see cref="ShellBar"/> rows and the cuelist header.
/// Fixed chrome columns (drag, expand, type, status) always reserve space so
/// optional UI (collapse chevron, issue X) never shifts the name/number columns.
/// </summary>
/// <remarks>
/// Industry-style tree table: reserved tree chrome + nested indent + flex name column.
/// Number and time column widths are user-resizable via the cuelist header.
/// </remarks>
public static class ShellColumnLayout
{
	/// <summary>Left color strip width.</summary>
	public const float ColorWidth = 3f;

	/// <summary>Drag handle column.</summary>
	public const float DragWidth = 18f;

	/// <summary>Expand/collapse chevron column (always reserved).</summary>
	public const float CollapseWidth = 18f;

	/// <summary>Issue / status indicator column (always reserved, full row height).</summary>
	public const float IssueWidth = 14f;

	/// <summary>Horizontal indent applied once per nesting level under a parent.</summary>
	public const float NestIndent = 16f;

	/// <summary>Continue / follow mode column (cycle button).</summary>
	public const float FollowWidth = 34f;

	/// <summary>Height of inline controls (LineEdit / compact buttons) inside a shell row.</summary>
	public const float RowControlHeight = 20f;

	/// <summary>Minimum height of the cue row strip (excluding nested children).</summary>
	public const float RowMinHeight = 24f;

	/// <summary>HBox separation used between shell row columns.</summary>
	public const int RowSeparation = 2;

	/// <summary>Default cue-number column width.</summary>
	public const float DefaultNumberWidth = 48f;

	/// <summary>Minimum cue-number column width.</summary>
	public const float MinNumberWidth = 28f;

	/// <summary>Maximum cue-number column width.</summary>
	public const float MaxNumberWidth = 120f;

	/// <summary>Default width for pre-wait / duration / post-wait fields.</summary>
	public const float DefaultTimeWidth = 60f;

	/// <summary>Minimum time-field width.</summary>
	public const float MinTimeWidth = 44f;

	/// <summary>Maximum time-field width.</summary>
	public const float MaxTimeWidth = 140f;

	private static float _numberWidth = DefaultNumberWidth;
	private static float _timeWidth = DefaultTimeWidth;

	/// <summary>
	/// Fired after a user-resizable column width changes. Listeners (shell rows, header) re-apply sizes.
	/// </summary>
	public static event Action Changed;

	/// <summary>User-resizable cue number column width.</summary>
	public static float NumberWidth
	{
		get => _numberWidth;
		set
		{
			float clamped = Mathf.Clamp(value, MinNumberWidth, MaxNumberWidth);
			if (Mathf.IsEqualApprox(_numberWidth, clamped))
				return;
			_numberWidth = clamped;
			Changed?.Invoke();
		}
	}

	/// <summary>User-resizable width shared by pre-wait, duration, and post-wait columns.</summary>
	public static float TimeWidth
	{
		get => _timeWidth;
		set
		{
			float clamped = Mathf.Clamp(value, MinTimeWidth, MaxTimeWidth);
			if (Mathf.IsEqualApprox(_timeWidth, clamped))
				return;
			_timeWidth = clamped;
			Changed?.Invoke();
		}
	}

	/// <summary>
	/// Width of fixed left chrome before tree indent (color + drag + issue + separations).
	/// Color, drag, and issue always stay flush left on every row.
	/// </summary>
	public static float FixedLeftChromeWidth =>
		ColorWidth + RowSeparation
		+ DragWidth + RowSeparation
		+ IssueWidth + RowSeparation;

	/// <summary>
	/// Sets widths without raising <see cref="Changed"/> (e.g. load prefs, then apply once).
	/// </summary>
	/// <param name="numberWidth">Cue number column width.</param>
	/// <param name="timeWidth">Pre/duration/post column width.</param>
	public static void SetWidthsSilent(float numberWidth, float timeWidth)
	{
		_numberWidth = Mathf.Clamp(numberWidth, MinNumberWidth, MaxNumberWidth);
		_timeWidth = Mathf.Clamp(timeWidth, MinTimeWidth, MaxTimeWidth);
	}

	/// <summary>
	/// Raises <see cref="Changed"/> so all listeners re-apply current widths.
	/// </summary>
	public static void NotifyChanged()
	{
		Changed?.Invoke();
	}
}
