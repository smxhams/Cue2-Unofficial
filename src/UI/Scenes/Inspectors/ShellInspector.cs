using System;
using Godot;

using System.IO;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Cue2.UI.Utilities;
// ShellSelection lives in Cue2.Base.Classes

// This script is attached to shell context tab

namespace Cue2.UI.Scenes.Inspectors;
public partial class ShellInspector : Control
{
	// Called when the node enters the scene tree for the first time.
	private GlobalSignals _globalSignals;
	private GlobalData _globalData;
	
	/// <summary>Last focused cue id. -1 = none (must not default to 0 — cue ids are 0-based).</summary>
	private int _focusedCueId = -1;

	private Cue _focusedCue;

	private LineEdit _cueNum;
	private LineEdit _cueName;
	private Label _cueId;
	private Label _parentCueLabel;
	private LineEdit _preWaitInput;
	private LineEdit _durationValue;
	private LineEdit _postWaitInput;
	private OptionButton _followOption;
	private ColorPickerButton _colorPicker;
	private Button _deleteCueButton;

	/// <summary>
	/// True while UI is being pushed from the model (undo/redo, sync). Prevents TextChanged handlers
	/// from writing back into the model / recording history.
	/// </summary>
	private bool _isRefreshingUi;
	
	
	public override void _Ready()
	{
		_globalData = GetNode<Shared.GlobalData>("/root/GlobalData");
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
		_deleteCueButton = GetNodeOrNull<Button>("%DeleteCueButton");
		
		UiUtilities.FormatLabelsColours(this, GlobalStyles.SoftFontColor);
		
		_cueNum.TextChanged += _onCueNumTextChanged;
		_cueName.TextChanged += _onCueNameTextChanged;
		// Seal continuous name/number typing so the next edit is a new undo step.
		_cueNum.TextSubmitted += _ => { _cueNum.ReleaseFocus(); };
		_cueName.TextSubmitted += _ => { _cueName.ReleaseFocus(); };
		_cueNum.FocusExited += () => _globalData?.HistoryManager?.EndCoalesceSession($"cue:{_focusedCueId}:num");
		_cueName.FocusExited += () => _globalData?.HistoryManager?.EndCoalesceSession($"cue:{_focusedCueId}:name");

		_colorPicker.PopupClosed += AssignColor;
		
		_preWaitInput.TextSubmitted += (string newText) => TimeFieldSubmitted(newText, _preWaitInput);
		_postWaitInput.TextSubmitted += (string newText) => TimeFieldSubmitted(newText, _postWaitInput);
		_followOption.ItemSelected += FollowOptionItemSelected;

		if (_deleteCueButton != null)
		{
			_deleteCueButton.Pressed += OnDeleteCuePressed;
			_deleteCueButton.AddThemeColorOverride("font_color", GlobalStyles.Danger);
			try
			{
				_deleteCueButton.Icon = GetThemeIcon("DeleteBin", "AtlasIcons");
				_deleteCueButton.ExpandIcon = true;
			}
			catch { /* icon optional */ }
			SyncDeleteHotkeyTooltip();
		}

		_globalSignals.SyncShellInspector += UpdateFields;
		
		Visible = false;
	}

	private void SyncDeleteHotkeyTooltip()
	{
		if (_deleteCueButton == null) return;
		string hotkey = GlobalData.ParseHotkey("DeleteCue");
		string tip = "Delete this cue (and any child cues).";
		if (!string.IsNullOrEmpty(hotkey))
			tip += "\nHotkey: " + hotkey;
		_deleteCueButton.TooltipText = tip;
	}

	/// <summary>
	/// Deletes the focused cue via the cuelist (same path as Delete key).
	/// </summary>
	private void OnDeleteCuePressed()
	{
		if (_focusedCue == null)
			return;

		// Ensure this cue is selected so DeleteSelectedCues targets it
		if (!ShellSelection.SelectedCues.Contains(_focusedCue))
			_globalData?.ShellSelection?.SelectIndividualShell(_focusedCue);

		_globalSignals.EmitSignal(nameof(GlobalSignals.DeleteSelectedCues));

		_focusedCue = null;
		_focusedCueId = -1;
		Visible = false;
	}
	
	private void ShellSelected(int cueId)
	{
		// cueId < 0 = selection cleared (e.g. after delete)
		if (cueId < 0)
		{
			if (_focusedCue != null)
			{
				_focusedCue.NameChanged -= OnNameChanged;
				_focusedCue.FollowChanged -= OnFollowChanged;
			}
			_focusedCue = null;
			_focusedCueId = -1;
			Visible = false;
			return;
		}

		Visible = true;
		// Require both id match and a loaded cue — default _focusedCueId was 0, so
		// first selection of cue id 0 skipped load and left _focusedCue null.
		if (_focusedCueId == cueId && _focusedCue != null) return;
		if (_focusedCue != null)
		{
			_focusedCue.NameChanged -= OnNameChanged;
			_focusedCue.FollowChanged -= OnFollowChanged;
		}
		
		_focusedCue = CueList.FetchCueFromId(cueId);
		if (_focusedCue == null)
		{
			_focusedCueId = -1;
			Visible = false;
			GD.Print($"ShellInspector:ShellSelected - Cue id {cueId} not found (cleared).");
			return;
		}

		// Init shell inspector and load relevant data
		_focusedCueId = cueId;
		_focusedCue.NameChanged += OnNameChanged;
		_focusedCue.FollowChanged += OnFollowChanged;
		EnsureFollowOptions();
		UpdateFields();
	}

	/// <summary>
	/// Keeps the follow OptionButton in sync when the mode is edited from the shell bar.
	/// </summary>
	private void OnFollowChanged(FollowType follow)
	{
		if (_isRefreshingUi || _followOption == null) return;
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
	/// <param name="type">Enum value stored as item metadata.</param>
	/// <param name="label">User-facing label.</param>
	private void AddFollowOption(FollowType type, string label)
	{
		int index = _followOption.ItemCount;
		_followOption.AddItem(label);
		_followOption.SetItemMetadata(index, (int)type);
	}

	/// <summary>
	/// Full refresh of shell inspector fields from the focused cue model.
	/// Wired to <see cref="GlobalSignals.SyncShellInspector"/> so undo/redo and other
	/// model changes can repaint without re-selecting the cue.
	/// </summary>
	public void UpdateFields()
	{
		if (_focusedCue == null || !GodotObject.IsInstanceValid(this))
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
				_cueId.Text = $"ID: {_focusedCue.Id}";

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
		}
		finally
		{
			_isRefreshingUi = false;
		}
	}

	private void OnNameChanged(string name)
	{
		if (_isRefreshingUi || _cueName == null) return;
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
	/// Handles submission of time fields (start/end). Parses input, updates component, and recalculates duration.
	/// </summary>
	/// <param name="text">The submitted text.</param>
	/// <param name="textField">The LineEdit field.</param>
	private void TimeFieldSubmitted(string text, LineEdit textField)
	{
		try
		{
			var time = UiUtilities.ParseAndFormatTime(text, out var timeSecs, out string labeledTime);

			if (time == "")
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid time format in {textField.Name}: {text}",
					1); // Warning log
				return;
			}

			textField.Text = time;
			textField.TooltipText = labeledTime;
			if (textField == _preWaitInput)
			{
				// Discrete commit (Enter / submit) — each change is its own undo step.
				_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit pre-wait");
				_focusedCue.PreWait = timeSecs;
			}
			else if (textField == _postWaitInput)
			{
				_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit post-wait");
				_focusedCue.PostWait = timeSecs;
			}

			// Recalculate duration
			var durationSecs = _focusedCue.CalculateTotalDuration();
			_durationValue.Text =
				UiUtilities.ParseAndFormatTime(durationSecs.ToString(), out var _, out string durLabeledTime);
			//? durLabeledTime : _durationValue.Text; // Fallback to previous if parse fails
			_durationValue.TooltipText = durLabeledTime;
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
		if (_isRefreshingUi || _focusedCue == null) return;
		if (_globalData?.HistoryManager?.IsRestoring == true) return;
		int selectedValue = _followOption.GetItemMetadata((int)index).AsInt32();
		if ((int)_focusedCue.Follow == selectedValue) return;
		_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit continue mode");
		_focusedCue.Follow = (FollowType)selectedValue;
		// Shell bar listens to FollowChanged; also push a general sync for any other listeners.
		_globalSignals?.EmitSignal(nameof(GlobalSignals.UpdateShellBar), _focusedCue.Id);
	}

	private void AssignColor()
	{
		if (_isRefreshingUi || _focusedCue == null) return;
		GD.Print($"ShellInspector:AsignColor - Assigning color. {_colorPicker.Color.R}, {_colorPicker.Color.G}, {_colorPicker.Color.B}");
		if (_focusedCue.Color.IsEqualApprox(_colorPicker.Color)) return;
		_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit cue color");
		_focusedCue.Color = _colorPicker.Color;
	}

	// Handling the updating of fields
	private void _onCueNumTextChanged(string data)
	{
		if (_isRefreshingUi || _focusedCue == null) return;
		_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit cue number", $"cue:{_focusedCue.Id}:num");
		// ShellBar listens to CueNumChanged — do not look up shell LineEdits by hand
		// (unique names are %CueNumLineEdit / %CueNameLineEdit, not %CueNumber / %CueName).
		_focusedCue.CueNum = data;
	}
	
	private void _onCueNameTextChanged(string data)
	{
		if (_isRefreshingUi || _focusedCue == null) return;
		_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit cue name", $"cue:{_focusedCue.Id}:name");
		// ShellBar listens to NameChanged and updates %CueNameLineEdit.
		_focusedCue.Name = data;
	}
}
