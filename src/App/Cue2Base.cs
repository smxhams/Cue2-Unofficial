using Godot;
using Cue2.Services;
using Cue2.UI.Utilities;

namespace Cue2.App;

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
		_globalData = GetNode<Cue2.Services.GlobalData>("/root/GlobalData");

		_globalSignals.UiScaleChanged += ScaleUI;
		_globalSignals.ShowModeChanged += OnShowModeChanged;

		// Prefer unique name; fall back to path for older scene instances.
		_inspectorSplit = GetNodeOrNull<Control>("%HSplitContainer2")
			?? GetNodeOrNull<Control>("MarginContainer/BoxContainer/VSplitContainer/HSplitContainer2");

		ApplyShowModeUi(_globalData?.Settings?.ShowMode == true);

		// Initial window size/position/scale order is owned by MainWindowHandles:
		// content scale first, then restore saved geometry (or design-size RescaleWindow once).
		// Do not call RescaleWindow here — it runs after child restore and corrupts saved size/pos
		// (especially on macOS HiDPI where BaseDisplayScale is often 2).
	}

	private void ScaleUI(float uiScale)
	{
		// Runtime scale changes only touch ContentScaleFactor, not outer pixel size.
		var window = GetWindow();
		UiUtilities.RescaleUi(window, _globalData.Settings.UiScale, _globalData.BaseDisplayScale);
		// Keep explicit geometry; do not let WrapControls auto-resize the main frame.
		if (window != null)
			window.WrapControls = false;
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
