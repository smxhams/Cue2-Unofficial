using System;
using System.Collections.Generic;
using System.Linq;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Cue2.UI.Utilities;
using Godot;

namespace Cue2.UI.Scenes.Inspectors;

/// <summary>
/// Shell context inspector: single-cue edit, or multi-edit when enabled and multiple cues are selected.
/// </summary>
/// <remarks>
/// Multi-edit (Settings → General → Multi-edit cues): fields stay blank; commits apply to every
/// selected cue. History for multi-edit uses a cuelist snapshot so all targets undo together.
/// </remarks>
public partial class ShellInspector : Control
{
	private GlobalSignals _globalSignals;
	private GlobalData _globalData;

	/// <summary>Last focused cue id. -1 = none (must not default to 0 — cue ids are 0-based).</summary>
	private int _focusedCueId = -1;

	private Cue _focusedCue;

	/// <summary>True when multi-edit is active for the current selection.</summary>
	private bool _isMultiEdit;

	private LineEdit _cueNum;
	private LineEdit _cueName;
	private Label _cueId;
	private Label _parentCueLabel;
	private LineEdit _preWaitInput;
	private LineEdit _durationValue;
	private LineEdit _postWaitInput;
	private OptionButton _followOption;
	private ColorPickerButton _colorPicker;
	private CheckBox _armedCheckBox;
	private CheckBox _skipIfDisarmedCheckBox;
	private Button _deleteCueButton;

	/// <summary>
	/// True while UI is being pushed from the model (undo/redo, sync). Prevents TextChanged handlers
	/// from writing back into the model / recording history.
	/// </summary>
	private bool _isRefreshingUi;

	private const string MultiNumCoalesceKey = "multi:shell:num";
	private const string MultiNameCoalesceKey = "multi:shell:name";
	private const string MultiPlaceholder = "Multiple selected";

	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

		_globalSignals.ShellFocused += ShellSelected;

		_cueName = GetNode<LineEdit>("%ShellName");
		_cueNum = GetNode<LineEdit>("%CueNum");
		_cueId = GetNode<Label>("%CueId");
		_parentCueLabel = GetNode<Label>("%ParentCueLabel");

		_preWaitInput = GetNode<LineEdit>("%PreWaitInput");
		_durationValue = GetNode<LineEdit>("%DurationValue");
		_postWaitInput = GetNode<LineEdit>("%PostWaitInput");
		_followOption = GetNode<OptionButton>("%FollowOption");
		_colorPicker = GetNode<ColorPickerButton>("%ColourPickerButton");
		_armedCheckBox = GetNodeOrNull<CheckBox>("%ArmedCheckBox");
		_skipIfDisarmedCheckBox = GetNodeOrNull<CheckBox>("%SkipIfDisarmedCheckBox");
		_deleteCueButton = GetNodeOrNull<Button>("%DeleteCueButton");

		UiUtilities.FormatLabelsColours(this, GlobalStyles.SoftFontColor);

		_cueNum.TextChanged += OnCueNumTextChanged;
		_cueName.TextChanged += OnCueNameTextChanged;
		// Seal continuous name/number typing so the next edit is a new undo step.
		_cueNum.TextSubmitted += _ => { _cueNum.ReleaseFocus(); };
		_cueName.TextSubmitted += _ => { _cueName.ReleaseFocus(); };
		_cueNum.FocusExited += OnCueNumFocusExited;
		_cueName.FocusExited += OnCueNameFocusExited;

		_colorPicker.PopupClosed += AssignColor;

		_preWaitInput.TextSubmitted += text => TimeFieldSubmitted(text, _preWaitInput);
		_postWaitInput.TextSubmitted += text => TimeFieldSubmitted(text, _postWaitInput);
		_followOption.ItemSelected += FollowOptionItemSelected;

		if (_armedCheckBox != null)
			_armedCheckBox.Toggled += OnArmedToggled;
		if (_skipIfDisarmedCheckBox != null)
			_skipIfDisarmedCheckBox.Toggled += OnSkipIfDisarmedToggled;

		if (_deleteCueButton != null)
		{
			_deleteCueButton.Pressed += OnDeleteCuePressed;
			_deleteCueButton.AddThemeColorOverride("font_color", GlobalStyles.Danger);
			try
			{
				_deleteCueButton.Icon = GetThemeIcon("DeleteBin", "AtlasIcons");
				_deleteCueButton.ExpandIcon = true;
			}
			catch
			{
				/* icon optional */
			}

			SyncDeleteHotkeyTooltip();
		}

		_globalSignals.SyncShellInspector += UpdateFields;

		Visible = false;
	}

	public override void _ExitTree()
	{
		if (_globalSignals != null)
		{
			_globalSignals.ShellFocused -= ShellSelected;
			_globalSignals.SyncShellInspector -= UpdateFields;
		}

		DetachFocusedCueEvents();
		base._ExitTree();
	}

	private void SyncDeleteHotkeyTooltip()
	{
		if (_deleteCueButton == null) return;
		string hotkey = GlobalData.ParseHotkey("DeleteCue");
		string tip = _isMultiEdit
			? "Delete all selected cues (and any child cues)."
			: "Delete this cue (and any child cues).";
		if (!string.IsNullOrEmpty(hotkey))
			tip += "\nHotkey: " + hotkey;
		_deleteCueButton.TooltipText = tip;
	}

	/// <summary>
	/// Deletes the focused / selected cue(s) via the cuelist (same path as Delete key).
	/// </summary>
	private void OnDeleteCuePressed()
	{
		if (!_isMultiEdit)
		{
			if (_focusedCue == null)
				return;

			// Ensure this cue is selected so DeleteSelectedCues targets it
			if (!ShellSelection.SelectedCues.Contains(_focusedCue))
				_globalData?.ShellSelection?.SelectIndividualShell(_focusedCue);
		}
		else if (ShellSelection.SelectedCues == null || ShellSelection.SelectedCues.Count == 0)
		{
			return;
		}

		_globalSignals.EmitSignal(nameof(GlobalSignals.DeleteSelectedCues));

		DetachFocusedCueEvents();
		_focusedCue = null;
		_focusedCueId = -1;
		_isMultiEdit = false;
		Visible = false;
	}

	private void ShellSelected(int cueId)
	{
		// cueId < 0 = selection cleared (e.g. after delete)
		if (cueId < 0)
		{
			DetachFocusedCueEvents();
			_focusedCue = null;
			_focusedCueId = -1;
			_isMultiEdit = false;
			Visible = false;
			return;
		}

		Visible = true;

		bool multiEditWanted = ShouldUseMultiEdit();
		if (multiEditWanted)
		{
			EnterMultiEditMode();
			return;
		}

		// Single-cue path (or multi-edit disabled with multiple selected).
		if (_focusedCueId == cueId && _focusedCue != null && !_isMultiEdit)
			return;

		DetachFocusedCueEvents();
		_isMultiEdit = false;

		_focusedCue = CueList.FetchCueFromId(cueId);
		if (_focusedCue == null)
		{
			_focusedCueId = -1;
			Visible = false;
			GD.Print($"ShellInspector:ShellSelected - Cue id {cueId} not found (cleared).");
			return;
		}

		_focusedCueId = cueId;
		_focusedCue.NameChanged += OnNameChanged;
		_focusedCue.FollowChanged += OnFollowChanged;
		EnsureFollowOptions();
		ClearMultiEditPlaceholders();
		UpdateFields();
	}

	/// <summary>
	/// True when multi-edit setting is on and more than one cue is selected.
	/// </summary>
	private bool ShouldUseMultiEdit()
	{
		if (_globalData?.Settings == null || !_globalData.Settings.MultiEditEnabled)
			return false;
		return ShellSelection.SelectedCues != null && ShellSelection.SelectedCues.Count > 1;
	}

	/// <summary>
	/// Activates multi-edit: shows ID list, leaves fields blank, does not bind a single cue for sync.
	/// </summary>
	private void EnterMultiEditMode()
	{
		DetachFocusedCueEvents();
		_isMultiEdit = true;

		// Keep last focused id for reference / delete path, but do not load its values.
		var selected = GetSelectedCuesSnapshot();
		if (selected.Count > 0)
		{
			_focusedCue = selected[^1];
			_focusedCueId = _focusedCue.Id;
		}

		EnsureFollowOptions();
		ShowMultiEditBlankFields(selected);
		SyncDeleteHotkeyTooltip();
	}

	private void DetachFocusedCueEvents()
	{
		if (_focusedCue == null) return;
		_focusedCue.NameChanged -= OnNameChanged;
		_focusedCue.FollowChanged -= OnFollowChanged;
	}

	/// <summary>
	/// Snapshot of currently selected cues (non-null only).
	/// </summary>
	private static List<Cue> GetSelectedCuesSnapshot()
	{
		if (ShellSelection.SelectedCues == null || ShellSelection.SelectedCues.Count == 0)
			return new List<Cue>();
		return ShellSelection.SelectedCues.Where(c => c != null).ToList();
	}

	/// <summary>
	/// Target cues for an edit: multi-selection when multi-editing, else the focused cue.
	/// </summary>
	private List<Cue> GetEditTargets()
	{
		if (_isMultiEdit)
			return GetSelectedCuesSnapshot();
		if (_focusedCue != null)
			return new List<Cue> { _focusedCue };
		return new List<Cue>();
	}

	/// <summary>
	/// Clears editable fields and shows multi-edit header / placeholders (no model values).
	/// </summary>
	private void ShowMultiEditBlankFields(List<Cue> selected)
	{
		_isRefreshingUi = true;
		try
		{
			if (_cueId != null)
			{
				_cueId.Text = "MULTI-EDITING";
				_cueId.TooltipText = FormatMultiEditIdTooltip(selected);
			}

			if (_parentCueLabel != null)
				_parentCueLabel.Text = "";

			if (_cueNum != null)
			{
				_cueNum.Text = "";
				_cueNum.PlaceholderText = MultiPlaceholder;
			}

			if (_cueName != null)
			{
				_cueName.Text = "";
				_cueName.PlaceholderText = MultiPlaceholder;
			}

			if (_preWaitInput != null)
			{
				_preWaitInput.Text = "";
				_preWaitInput.PlaceholderText = MultiPlaceholder;
			}

			if (_postWaitInput != null)
			{
				_postWaitInput.Text = "";
				_postWaitInput.PlaceholderText = MultiPlaceholder;
			}

			if (_durationValue != null)
			{
				_durationValue.Text = "";
				_durationValue.PlaceholderText = "—";
			}

			if (_followOption != null)
			{
				_followOption.SetBlockSignals(true);
				_followOption.Selected = -1;
				_followOption.SetBlockSignals(false);
			}

			// Leave colour picker as-is (no model load). User must open picker to commit a colour.

			if (_armedCheckBox != null)
				_armedCheckBox.SetPressedNoSignal(false);

			if (_skipIfDisarmedCheckBox != null)
				_skipIfDisarmedCheckBox.SetPressedNoSignal(false);
		}
		finally
		{
			_isRefreshingUi = false;
		}
	}

	/// <summary>
	/// Restores single-edit placeholders after leaving multi-edit.
	/// </summary>
	private void ClearMultiEditPlaceholders()
	{
		if (_cueNum != null)
			_cueNum.PlaceholderText = "No Selection";
		if (_cueName != null)
			_cueName.PlaceholderText = "No Selection";
		if (_preWaitInput != null)
			_preWaitInput.PlaceholderText = "";
		if (_postWaitInput != null)
			_postWaitInput.PlaceholderText = "";
		if (_durationValue != null)
			_durationValue.PlaceholderText = "";
		SyncDeleteHotkeyTooltip();
	}

	/// <summary>
	/// Builds a tooltip listing selected cue IDs (label itself stays short: "MULTI-EDITING").
	/// </summary>
	private static string FormatMultiEditIdTooltip(List<Cue> selected)
	{
		if (selected == null || selected.Count == 0)
			return "No cues selected.";

		string ids = string.Join(", ", selected.Select(c => c.Id));
		return $"Editing {selected.Count} cue(s).\nIDs: {ids}";
	}

	/// <summary>
	/// Keeps the follow OptionButton in sync when the mode is edited from the shell bar (single-edit only).
	/// </summary>
	private void OnFollowChanged(FollowType follow)
	{
		if (_isMultiEdit || _isRefreshingUi || _followOption == null) return;
		_isRefreshingUi = true;
		try
		{
			SelectFollowOption(follow);
		}
		finally
		{
			_isRefreshingUi = false;
		}
	}

	/// <summary>
	/// Selects the OptionButton item matching <paramref name="follow"/>.
	/// </summary>
	private void SelectFollowOption(FollowType follow)
	{
		if (_followOption == null) return;
		EnsureFollowOptions();
		for (int i = 0; i < _followOption.ItemCount; i++)
		{
			if (_followOption.GetItemMetadata(i).AsInt32() == (int)follow)
			{
				_followOption.Selected = i;
				return;
			}
		}
	}

	/// <summary>
	/// Ensures the follow OptionButton is populated with continue-mode labels.
	/// </summary>
	private void EnsureFollowOptions()
	{
		if (_followOption == null) return;
		if (_followOption.ItemCount > 0) return;

		_followOption.Clear();
		AddFollowOption(FollowType.None, "None");
		AddFollowOption(FollowType.Continue, "Auto-continue");
		AddFollowOption(FollowType.Follow, "Auto-follow");
	}

	/// <summary>
	/// Adds one continue-mode entry to the follow OptionButton.
	/// </summary>
	private void AddFollowOption(FollowType type, string label)
	{
		int index = _followOption.ItemCount;
		_followOption.AddItem(label);
		_followOption.SetItemMetadata(index, (int)type);
	}

	/// <summary>
	/// Full refresh of shell inspector fields from the focused cue model (single-edit),
	/// or re-applies blank multi-edit UI when multi-editing.
	/// Wired to <see cref="GlobalSignals.SyncShellInspector"/>.
	/// </summary>
	public void UpdateFields()
	{
		if (!GodotObject.IsInstanceValid(this))
			return;

		// Setting toggle or selection may have changed while inspector stayed open.
		if (ShouldUseMultiEdit())
		{
			// Enter or re-enter when selection set changes. Do not wipe fields on every
			// SyncShellInspector (would clear text the user is mid-typing).
			if (!_isMultiEdit || NeedsMultiHeaderRefresh())
				EnterMultiEditMode();
			return;
		}

		// Left multi-edit (selection shrunk or setting off) — fall back to single if possible.
		if (_isMultiEdit)
		{
			_isMultiEdit = false;
			ClearMultiEditPlaceholders();
			int id = _focusedCueId >= 0
				? _focusedCueId
				: (ShellSelection.SelectedCues?.LastOrDefault()?.Id ?? -1);
			// Clear focus markers so ShellSelected reloads fields even if the id is unchanged.
			DetachFocusedCueEvents();
			_focusedCue = null;
			_focusedCueId = -1;
			if (id >= 0)
			{
				ShellSelected(id);
				return;
			}

			Visible = false;
			return;
		}

		if (_focusedCue == null)
			return;
		if (_preWaitInput == null || _postWaitInput == null || _durationValue == null)
			return;

		_isRefreshingUi = true;
		try
		{
			if (_cueNum != null)
				_cueNum.Text = _focusedCue.CueNum ?? string.Empty;
			if (_cueName != null)
				_cueName.Text = _focusedCue.Name ?? string.Empty;

			if (_cueId != null)
			{
				_cueId.Text = $"ID: {_focusedCue.Id}";
				_cueId.TooltipText = "";
			}

			if (_parentCueLabel != null)
			{
				if (_focusedCue.ParentId != -1)
				{
					var parent = CueList.FetchCueFromId(_focusedCue.ParentId);
					_parentCueLabel.Text = parent != null ? ("Parent: " + parent.Name) : "";
				}
				else
				{
					_parentCueLabel.Text = "";
				}
			}

			EnsureFollowOptions();
			SelectFollowOption(_focusedCue.Follow);

			_preWaitInput.Text = UiUtilities.FormatTime(_focusedCue.PreWait);
			_postWaitInput.Text = UiUtilities.FormatTime(_focusedCue.PostWait);
			var duration = _focusedCue.TotalDuration;
			if (duration < 0)
				_durationValue.Text = "Until Stopped";
			else
				_durationValue.Text = UiUtilities.FormatTime(_focusedCue.TotalDuration);

			if (_colorPicker != null)
				_colorPicker.Color = _focusedCue.Color;

			if (_armedCheckBox != null)
				_armedCheckBox.SetPressedNoSignal(_focusedCue.Armed);
			if (_skipIfDisarmedCheckBox != null)
				_skipIfDisarmedCheckBox.SetPressedNoSignal(_focusedCue.SkipIfDisarmed);
		}
		finally
		{
			_isRefreshingUi = false;
		}
	}

	/// <summary>
	/// True when multi header should refresh because selection IDs changed.
	/// </summary>
	private bool NeedsMultiHeaderRefresh()
	{
		if (_cueId == null) return true;
		string expected = FormatMultiEditIdTooltip(GetSelectedCuesSnapshot());
		return _cueId.TooltipText != expected;
	}

	private void OnNameChanged(string name)
	{
		if (_isMultiEdit || _isRefreshingUi || _cueName == null) return;
		int caretPosition = _cueName.CaretColumn;
		_isRefreshingUi = true;
		try
		{
			_cueName.Text = name;
			_cueName.SetCaretColumn(caretPosition);
		}
		finally
		{
			_isRefreshingUi = false;
		}
	}

	/// <summary>
	/// Records history for the upcoming mutation (single cue or full cuelist for multi-edit).
	/// </summary>
	private void RecordHistoryBeforeEdit(string singleDescription, string multiDescription, string coalesceKey = null)
	{
		var history = _globalData?.HistoryManager;
		if (history == null || history.IsRestoring) return;

		if (_isMultiEdit)
			history.RecordCuelistChange(multiDescription, coalesceKey);
		else if (_focusedCue != null)
			history.RecordCueChange(_focusedCue.Id, singleDescription, coalesceKey);
	}

	private void EndCoalesceForCurrentEdit(string singleKeySuffix, string multiKey)
	{
		var history = _globalData?.HistoryManager;
		if (history == null) return;
		if (_isMultiEdit)
			history.EndCoalesceSession(multiKey);
		else if (_focusedCueId >= 0)
			history.EndCoalesceSession($"cue:{_focusedCueId}:{singleKeySuffix}");
	}

	private void OnCueNumFocusExited() => EndCoalesceForCurrentEdit("num", MultiNumCoalesceKey);

	private void OnCueNameFocusExited() => EndCoalesceForCurrentEdit("name", MultiNameCoalesceKey);

	/// <summary>
	/// Handles submission of time fields. Parses input and applies to edit target(s).
	/// </summary>
	private void TimeFieldSubmitted(string text, LineEdit textField)
	{
		var targets = GetEditTargets();
		if (targets.Count == 0) return;

		try
		{
			var time = UiUtilities.ParseAndFormatTime(text, out var timeSecs, out string labeledTime);

			if (time == "")
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
					$"Invalid time format in {textField.Name}: {text}", 1);
				return;
			}

			textField.Text = time;
			textField.TooltipText = labeledTime;

			if (textField == _preWaitInput)
			{
				RecordHistoryBeforeEdit("Edit pre-wait", "Multi-edit pre-wait");
				foreach (var cue in targets)
					cue.PreWait = timeSecs;
			}
			else if (textField == _postWaitInput)
			{
				RecordHistoryBeforeEdit("Edit post-wait", "Multi-edit post-wait");
				foreach (var cue in targets)
					cue.PostWait = timeSecs;
			}

			if (!_isMultiEdit && _focusedCue != null && _durationValue != null)
			{
				var durationSecs = _focusedCue.CalculateTotalDuration();
				_durationValue.Text =
					UiUtilities.ParseAndFormatTime(durationSecs.ToString(), out var _, out string durLabeledTime);
				_durationValue.TooltipText = durLabeledTime;
			}

			foreach (var cue in targets)
				_globalSignals?.EmitSignal(nameof(GlobalSignals.UpdateShellBar), cue.Id);

			textField.ReleaseFocus();
		}
		catch (Exception ex)
		{
			GD.Print($"ShellInspector:TimeFieldSubmitted - Error parsing time: {ex.Message}");
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Error parsing time: {ex.Message}", 2);
		}
	}

	private void FollowOptionItemSelected(long index)
	{
		if (_isRefreshingUi) return;
		if (_globalData?.HistoryManager?.IsRestoring == true) return;

		var targets = GetEditTargets();
		if (targets.Count == 0) return;

		int selectedValue = _followOption.GetItemMetadata((int)index).AsInt32();
		var follow = (FollowType)selectedValue;

		// Skip no-op when single-edit and already set.
		if (!_isMultiEdit && targets.Count == 1 && targets[0].Follow == follow)
			return;

		RecordHistoryBeforeEdit("Edit continue mode", "Multi-edit continue mode");
		foreach (var cue in targets)
		{
			if (cue.Follow == follow) continue;
			cue.Follow = follow;
			_globalSignals?.EmitSignal(nameof(GlobalSignals.UpdateShellBar), cue.Id);
		}
	}

	private void AssignColor()
	{
		if (_isRefreshingUi) return;

		var targets = GetEditTargets();
		if (targets.Count == 0 || _colorPicker == null) return;

		Color color = _colorPicker.Color;
		GD.Print($"ShellInspector:AssignColor - Assigning color. {color.R}, {color.G}, {color.B}");

		if (!_isMultiEdit && targets.Count == 1 && targets[0].Color.IsEqualApprox(color))
			return;

		RecordHistoryBeforeEdit("Edit cue color", "Multi-edit cue colour");
		foreach (var cue in targets)
		{
			if (cue.Color.IsEqualApprox(color)) continue;
			cue.Color = color;
			_globalSignals?.EmitSignal(nameof(GlobalSignals.UpdateShellBar), cue.Id);
		}
	}

	/// <summary>
	/// Toggles armed state for edit target(s).
	/// </summary>
	private void OnArmedToggled(bool pressed)
	{
		if (_isRefreshingUi) return;
		if (_globalData?.HistoryManager?.IsRestoring == true) return;

		var targets = GetEditTargets();
		if (targets.Count == 0) return;

		if (!_isMultiEdit && targets.Count == 1 && targets[0].Armed == pressed)
			return;

		RecordHistoryBeforeEdit(
			pressed ? "Arm cue" : "Disarm cue",
			pressed ? "Multi-edit arm cues" : "Multi-edit disarm cues");

		foreach (var cue in targets)
		{
			if (cue.Armed == pressed) continue;
			cue.Armed = pressed;
			_globalSignals?.EmitSignal(nameof(GlobalSignals.UpdateShellBar), cue.Id);
		}
	}

	/// <summary>
	/// Toggles skip-if-disarmed for edit target(s).
	/// </summary>
	private void OnSkipIfDisarmedToggled(bool pressed)
	{
		if (_isRefreshingUi) return;
		if (_globalData?.HistoryManager?.IsRestoring == true) return;

		var targets = GetEditTargets();
		if (targets.Count == 0) return;

		if (!_isMultiEdit && targets.Count == 1 && targets[0].SkipIfDisarmed == pressed)
			return;

		RecordHistoryBeforeEdit(
			pressed ? "Enable skip if disarmed" : "Disable skip if disarmed",
			pressed ? "Multi-edit enable skip if disarmed" : "Multi-edit disable skip if disarmed");

		foreach (var cue in targets)
		{
			if (cue.SkipIfDisarmed == pressed) continue;
			cue.SkipIfDisarmed = pressed;
			_globalSignals?.EmitSignal(nameof(GlobalSignals.UpdateShellBar), cue.Id);
		}
	}

	private void OnCueNumTextChanged(string data)
	{
		if (_isRefreshingUi) return;

		var targets = GetEditTargets();
		if (targets.Count == 0) return;

		string coalesce = _isMultiEdit ? MultiNumCoalesceKey : $"cue:{_focusedCueId}:num";
		RecordHistoryBeforeEdit("Edit cue number", "Multi-edit cue number", coalesce);

		foreach (var cue in targets)
			cue.CueNum = data;
	}

	private void OnCueNameTextChanged(string data)
	{
		if (_isRefreshingUi) return;

		var targets = GetEditTargets();
		if (targets.Count == 0) return;

		string coalesce = _isMultiEdit ? MultiNameCoalesceKey : $"cue:{_focusedCueId}:name";
		RecordHistoryBeforeEdit("Edit cue name", "Multi-edit cue name", coalesce);

		foreach (var cue in targets)
			cue.Name = data;
	}
}
