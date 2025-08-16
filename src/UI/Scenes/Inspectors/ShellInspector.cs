using Godot;

using System.IO;
using Cue2.Base.Classes;
using Cue2.Shared;

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
	
	
	
	public override void _Ready()
	{
		_globalData = GetNode<Shared.GlobalData>("/root/GlobalData");
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		
		_globalSignals.ShellFocused += ShellSelected;
		
		_cueName = GetNode<LineEdit>("%ShellName");
		_cueNum = GetNode<LineEdit>("%CueNum");
		_cueId = GetNode<Label>("%CueId");
		_parentCueLabel = GetNode<Label>("%ParentCueLabel");
		
		GetNode<Label>("%CueNumLabel").AddThemeColorOverride("font_color", GlobalStyles.SoftFontColor);
		GetNode<Label>("%CueNameLabel").AddThemeColorOverride("font_color", GlobalStyles.SoftFontColor);
		GetNode<Label>("%ColourLabel").AddThemeColorOverride("font_color", GlobalStyles.SoftFontColor);
		GetNode<Label>("%PreWaitLabel").AddThemeColorOverride("font_color", GlobalStyles.SoftFontColor);
		GetNode<Label>("%DurationLabel").AddThemeColorOverride("font_color", GlobalStyles.SoftFontColor);
		GetNode<Label>("%PostWaitLabel").AddThemeColorOverride("font_color", GlobalStyles.SoftFontColor);
		_cueId.AddThemeColorOverride("font_color", GlobalStyles.SoftFontColor);
		_parentCueLabel.AddThemeColorOverride("font_color", GlobalStyles.SoftFontColor);
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

	}
	
	// Shell Colour
	// Shell outline colour
	// Delete cue
	// pre-wait
	// post-wait
	// follow
	
	// TRIGGERS
	// Hotkey
	
	
	


	
	// Handling the updating of fields
	private void _onCueNumTextChanged(string data)
	{
		_focusedCue.CueNum = data; // Updates Cue with user input
		var shellObj = _focusedCue.ShellBar;
		shellObj.GetNode<LineEdit>("%CueNumber").Text = data;
	}
	
	private void _onShellNameTextChanged(string data)
	{
		_focusedCue.Name = data;

		var shellObj = _focusedCue.ShellBar;
		shellObj.GetNode<LineEdit>("%CueName").Text = data;
	}
}
