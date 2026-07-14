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
		_cueNum.TextSubmitted += _ => { _cueNum.ReleaseFocus(); };
		_cueName.TextSubmitted += _ => { _cueName.ReleaseFocus(); };

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
				_focusedCue.NameChanged -= OnNameChanged;
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
		_cueNum.Text = _focusedCue.CueNum;
		_cueName.Text = _focusedCue.Name;
		
		_focusedCue.NameChanged += OnNameChanged;

		_cueId.Text = $"ID: {_focusedCue.Id.ToString()}";
		if (_focusedCue.ParentId != -1)
		{
			var parent = CueList.FetchCueFromId(_focusedCue.ParentId);
			_parentCueLabel.Text = parent != null ? ("Parent: " + parent.Name) : "";
		}
		else _parentCueLabel.Text = "";
		
		
		var followOptions = Enum.GetValues(typeof(FollowType));
		_followOption.Clear();
		for (int i = 0; i < followOptions.Length; i++)
		{
			var enumValue = (FollowType)followOptions.GetValue(i)!;
			_followOption.AddItem(enumValue.ToString());
			_followOption.SetItemMetadata(i, (int)enumValue);
			_followOption.TooltipText = _followOption.TooltipText;
		}
		_followOption.Selected = (int)_focusedCue.Follow;
		
		_preWaitInput.Text = UiUtilities.FormatTime(_focusedCue.PreWait);
		_durationValue.Text = UiUtilities.FormatTime(_focusedCue.TotalDuration);
		_postWaitInput.Text = UiUtilities.FormatTime(_focusedCue.PostWait);
		
		_colorPicker.Color = _focusedCue.Color;

	}

	/// <summary>
	/// Refreshes pre/post wait and duration fields after media duration changes.
	/// Safe no-op if no shell is focused yet.
	/// </summary>
	public void UpdateFields()
	{
		if (_focusedCue == null || _preWaitInput == null || _postWaitInput == null || _durationValue == null)
			return;

		_preWaitInput.Text = UiUtilities.FormatTime(_focusedCue.PreWait);
		_postWaitInput.Text = UiUtilities.FormatTime(_focusedCue.PostWait);
		var duration = _focusedCue.TotalDuration;
		if (duration < 0)
		{
			_durationValue.Text = "Until Stopped";
		}
		else _durationValue.Text = UiUtilities.FormatTime(_focusedCue.TotalDuration);
	}

	private void OnNameChanged(string name)
	{
		int caretPosition = _cueName.CaretColumn;
		_cueName.Text = name;
		_cueName.SetCaretColumn(caretPosition);
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
				_focusedCue.PreWait = timeSecs;
			}
			else if (textField == _postWaitInput)
			{
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
		int selectedValue = _followOption.GetItemMetadata((int)index).AsInt32();
		_focusedCue.Follow = (FollowType)selectedValue;
	}

	private void AssignColor()
	{
		GD.Print($"ShellInspector:AsignColor - Assigning color. {_colorPicker.Color.R}, {_colorPicker.Color.G}, {_colorPicker.Color.B}");
		_focusedCue.Color = _colorPicker.Color;
	}

	// Handling the updating of fields
	private void _onCueNumTextChanged(string data)
	{
		_focusedCue.CueNum = data; // Updates Cue with user input
		var shellObj = _focusedCue.ShellBar;
		shellObj.GetNode<LineEdit>("%CueNumber").Text = data;
		
	}
	
	private void _onCueNameTextChanged(string data)
	{
		_focusedCue.Name = data;

		var shellObj = _focusedCue.ShellBar;
		shellObj.GetNode<LineEdit>("%CueName").Text = data;
	}
}
