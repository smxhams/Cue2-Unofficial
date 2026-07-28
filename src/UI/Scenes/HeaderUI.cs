using System;
using System.Linq;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Godot;

namespace Cue2.UI.Scenes;

/// <summary>
/// Main header: GO button and standby cue name/notes for the next cue(s) to play.
/// </summary>
/// <remarks>
/// Standby reflects the current shell selection (what GO will trigger). A single
/// selected cue shows its number, name, and notes; multiple selection is summarized.
/// </remarks>
public partial class HeaderUI : Control
{
	private GlobalSignals _globalSignals;

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

		_goButton = GetNode<Button>("%GoButton");
		_standbyCueText = GetNodeOrNull<LineEdit>("%StandbyCueText");
		_standbyCueNote = GetNodeOrNull<LineEdit>("%StandbyCueNote");

		_baseGoSize = _goButton.GetSize().X;

		_goButton.Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.Go));

		SyncHotkeys();

		_globalSignals.Go += GoButtonFeedback;
		_globalSignals.GoScaleChanged += GoScaleChange;
		_globalSignals.ShellFocused += OnShellFocused;
		_globalSignals.UpdateShellBar += OnUpdateShellBar;
		_globalSignals.SyncShellInspector += RefreshStandbyDisplay;

		RefreshStandbyDisplay();
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
		}

		base._ExitTree();
	}

	private void SyncHotkeys()
	{
		_goButton.TooltipText = "Hotkey: " + GlobalData.ParseHotkey("Go");
	}

	private async void GoButtonFeedback()
	{
		var pressed = _goButton.GetThemeStylebox("pressed");
		var normal = _goButton.GetThemeStylebox("normal");
		_goButton.AddThemeStyleboxOverride("normal", pressed);
		await ToSignal(GetTree().CreateTimer(0.2), "timeout");
		_goButton.AddThemeStyleboxOverride("normal", normal);
	}

	private void GoScaleChange(float scale)
	{
		var newGoScale = (float)_baseGoSize * scale;
		// Go Button scale
		_goButton.SetCustomMinimumSize(new Vector2(newGoScale, newGoScale));

		// Header size
		if (newGoScale > 50)
			SetCustomMinimumSize(new Vector2(GetCustomMinimumSize().X, newGoScale));
		else
			SetCustomMinimumSize(new Vector2(GetCustomMinimumSize().X, 50.0f));
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
