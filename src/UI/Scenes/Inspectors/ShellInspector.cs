using System;
using Godot;

using System.IO;
using Cue2.Base.Classes;
using Cue2.Shared;
using Cue2.UI.Utilities;

// This script is attached to shell context tab

namespace Cue2.UI.Scenes.Inspectors;
public partial class ShellInspector : Control
{
	// Called when the node enters the scene tree for the first time.
	private GlobalSignals _globalSignals;
	private GlobalData _globalData;
	
	private int _focusedCueId;

	private Cue _focusedCue;

	private LineEdit _cueNum;
	private LineEdit _cueName;
	private Label _cueId;
	private Label _parentCueLabel;
	private LineEdit _preWaitInput;
	private LineEdit _durationValue;
	private LineEdit _postWaitInput;
	private OptionButton _followOption;
	
	
	
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
		
		UiUtilities.FormatLabelsColours(this, GlobalStyles.SoftFontColor);
		
		_cueNum.TextChanged += _onCueNumTextChanged;
		_cueName.TextChanged += _onCueNameTextChanged;
		_cueNum.TextSubmitted += _ => { _cueNum.ReleaseFocus(); };
		_cueName.TextSubmitted += _ => { _cueName.ReleaseFocus(); };
		
		_preWaitInput.TextSubmitted += (string newText) => TimeFieldSubmitted(newText, _preWaitInput);
		_postWaitInput.TextSubmitted += (string newText) => TimeFieldSubmitted(newText, _postWaitInput);
		_followOption.ItemSelected += FollowOptionItemSelected;

		_globalSignals.SyncShellInspector += UpdateFields;
		
		
		
		
		Visible = false;
	}
	
	private void ShellSelected(int cueId)
	{
		Visible = true;
		
		_focusedCue = CueList.FetchCueFromId(cueId);
		// Init shell inspector and load relevant data
		_focusedCueId = cueId;
		_cueNum.Text = _focusedCue.CueNum;
		_cueName.Text = _focusedCue.Name;

		_cueId.Text = $"ID: {_focusedCue.Id.ToString()}";
		if (_focusedCue.ParentId != -1)
		{
			_parentCueLabel.Text = ("Parent: " + CueList.FetchCueFromId(_focusedCue.ParentId).Name);
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
		

	}
	
	
	// Shell Colour
	// Shell outline colour
	// Delete cue
	// pre-wait
	// post-wait
	// follow
	
	// TRIGGERS
	// Hotkey

	public void UpdateFields()
	{
		_preWaitInput.Text = UiUtilities.FormatTime(_focusedCue.PreWait);
		_postWaitInput.Text = UiUtilities.FormatTime(_focusedCue.PostWait);
		var duration = _focusedCue.TotalDuration;
		if (duration < 0)
		{
			_durationValue.Text = "Until Stopped";
		}
		else _durationValue.Text = UiUtilities.FormatTime(_focusedCue.TotalDuration);
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
			var time = UiUtilities.ParseAndFormatTime(text, out var timeSecs, out var labeledTime);

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
				UiUtilities.ParseAndFormatTime(durationSecs.ToString(), out var _, out var durLabeledTime);
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
