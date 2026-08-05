// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using System;
using System.Threading.Tasks;
using System.Linq;
using Cue2.Domain.Cuelist;
using Cue2.Domain.Playback;
using Cue2.Domain.Devices;
using Cue2.Domain.ShowSettings;
using Cue2.Domain.Metadata;
using Cue2.Domain.Cues;
using Cue2.Domain.Connections;
using Cue2.Domain.Library;
using Cue2.Domain.Commands;
using Cue2.Services;
using Cue2.UI.Utilities;
using Godot;
using AppSettings = Cue2.Domain.ShowSettings.Settings;
using static Cue2.UI.Utilities.UiLocalizer;

namespace Cue2.UI.Shell;

/// <summary>
/// Main header: GO button and standby cue name/notes for the next cue(s) to play.
/// </summary>
/// <remarks>
/// Standby reflects the current shell selection (what GO will trigger). A single
/// selected cue shows its number, name, and notes; multiple selection is summarized.
/// Go scale 0 ("No Go") hides this entire header; half scale hides the notes field only.
/// The GO hotkey still works when the header is hidden.
/// </remarks>
public partial class HeaderUI : Control
{
	private GlobalSignals _globalSignals;
	private GlobalData _globalData;

	private Node _settingsWindow;
	private Button _goButton;
	private LineEdit _standbyCueText;
	private LineEdit _standbyCueNote;

	/// <summary>Cue whose Name/CueNum/Notes events we are currently subscribed to (single-select only).</summary>
	private Cue _trackedStandbyCue;

	private double _baseGoSize;

	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_globalData = GetNodeOrNull<GlobalData>("/root/GlobalData");

		_goButton = GetNode<Button>("%GoButton");
		_standbyCueText = GetNodeOrNull<LineEdit>("%StandbyCueText");
		_standbyCueNote = GetNodeOrNull<LineEdit>("%StandbyCueNote");

		_baseGoSize = _goButton.GetSize().X;
		if (_baseGoSize <= 0)
			_baseGoSize = 50.0;

		_goButton.Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.Go));

		SyncHotkeys();

		_globalSignals.Go += GoButtonFeedback;
		_globalSignals.GoScaleChanged += GoScaleChange;
		_globalSignals.ShellFocused += OnShellFocused;
		_globalSignals.UpdateShellBar += OnUpdateShellBar;
		_globalSignals.SyncShellInspector += RefreshStandbyDisplay;

		// Apply saved / current scale (signal may have fired before this node was ready).
		float initialScale = _globalData?.Settings?.GoScale ?? 1.0f;
		GoScaleChange(initialScale);

		RefreshStandbyDisplay();
		LocalizeTree(this);
		if (_globalSignals != null)
			_globalSignals.LocaleChanged += OnLocaleChanged;
	}

	public override void _ExitTree()
	{
		DetachTrackedStandbyCue();

		if (_globalSignals != null)
		{
			_globalSignals.Go -= GoButtonFeedback;
			_globalSignals.GoScaleChanged -= GoScaleChange;
			_globalSignals.ShellFocused -= OnShellFocused;
			_globalSignals.UpdateShellBar -= OnUpdateShellBar;
			_globalSignals.SyncShellInspector -= RefreshStandbyDisplay;
			_globalSignals.LocaleChanged -= OnLocaleChanged;
		}

		base._ExitTree();
	}

	/// <summary>
	/// Re-localizes header chrome when the application language changes.
	/// </summary>
	/// <param name="localeCode">New locale code.</param>
	private void OnLocaleChanged(string localeCode)
	{
		if (!GodotObject.IsInstanceValid(this))
			return;
		LocalizeTree(this);
		SyncHotkeys();
	}

	private void SyncHotkeys()
	{
		_goButton.TooltipText = Tf("Hotkey: {0}", GlobalData.ParseHotkey("Go"));
	}

	private void GoButtonFeedback()
	{
		TaskUtil.Run(GoButtonFeedbackAsync, "HeaderUI.GoButtonFeedback");
	}

	private async Task GoButtonFeedbackAsync()
	{
		var pressed = _goButton.GetThemeStylebox("pressed");
		var normal = _goButton.GetThemeStylebox("normal");
		_goButton.AddThemeStyleboxOverride("normal", pressed);
		await ToSignal(GetTree().CreateTimer(0.2), "timeout");
		_goButton.AddThemeStyleboxOverride("normal", normal);
	}

	/// <summary>
	/// Applies Go button scale from settings.
	/// Scale 0 hides the whole header; 0.5 hides notes and uses a compact height.
	/// </summary>
	/// <param name="scale">Relative GO size (0 = No Go, 0.5 = Half Go, 1 = base, etc.).</param>
	private void GoScaleChange(float scale)
	{
		if (!IsInstanceValid(this))
			return;

		// No Go: hide the entire header (hotkey GO still works via InputMap).
		if (scale <= AppSettings.GoScaleNoGo || Mathf.IsEqualApprox(scale, AppSettings.GoScaleNoGo))
		{
			Visible = false;
			SetCustomMinimumSize(new Vector2(GetCustomMinimumSize().X, 0));
			return;
		}

		Visible = true;

		bool halfSize = Mathf.IsEqualApprox(scale, AppSettings.GoScaleHalf);
		if (_standbyCueNote != null)
			_standbyCueNote.Visible = !halfSize;

		var newGoScale = (float)(_baseGoSize * scale);
		if (newGoScale < 1f)
			newGoScale = 1f;

		_goButton.SetCustomMinimumSize(new Vector2(newGoScale, newGoScale));
		_goButton.Visible = true;

		// Header height: track GO button when large; compact when notes are hidden; else base 50.
		float headerHeight;
		if (newGoScale > 50f)
			headerHeight = newGoScale;
		else if (halfSize)
			headerHeight = Mathf.Max(newGoScale, 28f);
		else
			headerHeight = 50.0f;

		SetCustomMinimumSize(new Vector2(GetCustomMinimumSize().X, headerHeight));
	}

	private void OnShellFocused(int cueId)
	{
		RefreshStandbyDisplay();
	}

	/// <summary>
	/// Shell bar edits (name/number) may not re-fire ShellFocused; refresh when the
	/// updated bar belongs to the current standby selection.
	/// </summary>
	private void OnUpdateShellBar(int cueId)
	{
		var selected = ShellSelection.SelectedCues;
		if (selected == null || selected.Count == 0)
			return;
		if (selected.Any(c => c != null && c.Id == cueId))
			RefreshStandbyDisplay();
	}

	/// <summary>
	/// Updates standby name/notes from the current shell selection.
	/// </summary>
	private void RefreshStandbyDisplay()
	{
		if (!IsInstanceValid(this))
			return;

		var selected = ShellSelection.SelectedCues?
			.Where(c => c != null)
			.ToList();

		if (selected == null || selected.Count == 0)
		{
			DetachTrackedStandbyCue();
			SetStandbyFields(string.Empty, string.Empty, "No cue selected.", "Notes");
			return;
		}

		if (selected.Count > 1)
		{
			DetachTrackedStandbyCue();
			string multi = $"Multiple cues selected ({selected.Count})";
			SetStandbyFields(multi, string.Empty, multi, "Multiple cues selected — notes not shown.");
			return;
		}

		var cue = selected[0];
		AttachTrackedStandbyCue(cue);
		ApplySingleCueStandby(cue);
	}

	private void ApplySingleCueStandby(Cue cue)
	{
		if (cue == null)
		{
			SetStandbyFields(string.Empty, string.Empty, "No cue selected.", "Notes");
			return;
		}

		string num = cue.CueNum ?? string.Empty;
		string name = cue.Name ?? string.Empty;
		string display = string.IsNullOrWhiteSpace(num)
			? name
			: string.IsNullOrWhiteSpace(name)
				? num
				: $"{num}  {name}";

		string notes = FlattenNotes(cue.Notes);
		string tipName = string.IsNullOrEmpty(display)
			? "Standby cue (next to play)."
			: $"Standby: {display}";
		string tipNotes = string.IsNullOrEmpty(cue.Notes)
			? "No notes for this cue."
			: cue.Notes;

		SetStandbyFields(display, notes, tipName, tipNotes);
	}

	private void SetStandbyFields(string nameText, string notesText, string nameTooltip, string notesTooltip)
	{
		if (_standbyCueText != null)
		{
			_standbyCueText.Text = nameText ?? string.Empty;
			_standbyCueText.TooltipText = nameTooltip ?? string.Empty;
		}

		if (_standbyCueNote != null)
		{
			_standbyCueNote.Text = notesText ?? string.Empty;
			_standbyCueNote.TooltipText = notesTooltip ?? string.Empty;
		}
	}

	/// <summary>
	/// Collapses multi-line notes to a single line for the header LineEdit.
	/// </summary>
	private static string FlattenNotes(string notes)
	{
		if (string.IsNullOrEmpty(notes))
			return string.Empty;
		return notes
			.Replace("\r\n", " ")
			.Replace('\n', ' ')
			.Replace('\r', ' ')
			.Trim();
	}

	private void AttachTrackedStandbyCue(Cue cue)
	{
		if (ReferenceEquals(_trackedStandbyCue, cue))
			return;

		DetachTrackedStandbyCue();
		_trackedStandbyCue = cue;
		if (_trackedStandbyCue == null)
			return;

		_trackedStandbyCue.NameChanged += OnTrackedStandbyNameOrNumChanged;
		_trackedStandbyCue.CueNumChanged += OnTrackedStandbyNameOrNumChanged;
		_trackedStandbyCue.NotesChanged += OnTrackedStandbyNotesChanged;
	}

	private void DetachTrackedStandbyCue()
	{
		if (_trackedStandbyCue == null)
			return;

		_trackedStandbyCue.NameChanged -= OnTrackedStandbyNameOrNumChanged;
		_trackedStandbyCue.CueNumChanged -= OnTrackedStandbyNameOrNumChanged;
		_trackedStandbyCue.NotesChanged -= OnTrackedStandbyNotesChanged;
		_trackedStandbyCue = null;
	}

	private void OnTrackedStandbyNameOrNumChanged(string _)
	{
		if (_trackedStandbyCue != null)
			ApplySingleCueStandby(_trackedStandbyCue);
	}

	private void OnTrackedStandbyNotesChanged(string _)
	{
		if (_trackedStandbyCue != null)
			ApplySingleCueStandby(_trackedStandbyCue);
	}
}
