using Godot;
using System;
using System.Linq;
using System.IO;
using Cue2.Shared;
using Cue2.UI.Utilities;

namespace Cue2.UI.Scenes.Popups;

public partial class FileDropPopup : Window
{
	private GlobalData _globalData;
	private GlobalSignals _globalSignals;
	
	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

		GD.Print("FileDropPopup:Loading FileDropPopup");
		
		UiUtilities.RescaleWindow(this, _globalData.BaseDisplayScale);
		UiUtilities.RescaleUi(this, _globalData.Settings.UiScale, _globalData.BaseDisplayScale);

		_globalSignals.UiScaleChanged += ScaleUi;
		
	}

	public override void _ExitTree()
	{
		_globalSignals.UiScaleChanged -= ScaleUi;
	}
	
	public void SetDropInfo(string[] files, string targetName)
	{
		var dropTargetLabel = GetNode<Label>("%DropTargetLabel");
		var dropFileNameLabel = GetNode<Label>("%DropFileName");
		
		dropTargetLabel.Text = $"Drop Target: {targetName}";
		
		var fileNames = files.Select(f => Path.GetFileName(f));
		dropFileNameLabel.Text = $"Files: {string.Join(", ", fileNames)}";
	}
	
	private void ScaleUi(float value)
	{
		try
		{
			float effectiveScale = value * _globalData.BaseDisplayScale;
			WrapControls = true;
			ContentScaleFactor = effectiveScale;
			ChildControlsChanged();
			GD.Print($"FileDropPopup:_scaleUI - Applied effective UI scale: {effectiveScale} (user: {value} * base: {_globalData.BaseDisplayScale})");
		} 
		catch (Exception ex)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Error applying UI scale: {ex.Message}", 2);
			GetWindow().ContentScaleFactor = value; // Fallback to original value without multiplier
		}
	}
}
