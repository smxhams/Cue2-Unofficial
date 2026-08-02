using Godot;
using Cue2.Shared;
using Cue2.UI.Utilities;

namespace Cue2.Base;

/// <summary>
/// Root workspace control: layout, UI scale, and show-mode inspector visibility.
/// </summary>
public partial class Cue2Base : Control
{
	private GlobalSignals _globalSignals;
	private GlobalData _globalData;

	private Node _settingsWindow;

	//private Window _uiWindow;
	private Window VideoWindow;
	private int _playbackIndex;

	/// <summary>Bottom inspector row (Shell Inspector + InspectorTabs). Hidden in Show Mode.</summary>
	private Control _inspectorSplit;

	public WorkspaceStates State { get; set; }

	//public GlobalMediaPlayerManager mediaManager;

	public override void _Ready()
	{
		//Connect global signals
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_globalData = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData");

		_globalSignals.UiScaleChanged += ScaleUI;
		_globalSignals.ShowModeChanged += OnShowModeChanged;

		// Prefer unique name; fall back to path for older scene instances.
		_inspectorSplit = GetNodeOrNull<Control>("%HSplitContainer2")
			?? GetNodeOrNull<Control>("MarginContainer/BoxContainer/VSplitContainer/HSplitContainer2");

		ApplyShowModeUi(_globalData?.Settings?.ShowMode == true);

		UiUtilities.RescaleWindow(GetWindow(), _globalData.BaseDisplayScale);
		UiUtilities.RescaleUi(GetWindow(), _globalData.Settings.UiScale, _globalData.BaseDisplayScale);
	}

	private void ScaleUI(float uiScale)
	{
		UiUtilities.RescaleUi(GetWindow(), _globalData.Settings.UiScale, _globalData.BaseDisplayScale);
	}

	/// <summary>
	/// Hides or shows the inspector strip when entering/leaving Show Mode.
	/// </summary>
	private void OnShowModeChanged(bool enabled)
	{
		ApplyShowModeUi(enabled);
	}

	/// <summary>
	/// In Show Mode the inspector row is hidden so the cuelist uses full workspace height.
	/// </summary>
	/// <param name="showMode">True when Show Mode is active.</param>
	private void ApplyShowModeUi(bool showMode)
	{
		if (_inspectorSplit != null && IsInstanceValid(_inspectorSplit))
			_inspectorSplit.Visible = !showMode;
	}

	public override void _ExitTree()
	{
		if (_globalSignals != null)
		{
			_globalSignals.UiScaleChanged -= ScaleUI;
			_globalSignals.ShowModeChanged -= OnShowModeChanged;
		}
	}
}
