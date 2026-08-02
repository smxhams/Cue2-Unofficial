using System;
using Cue2.Services;
using Cue2.UI.Utilities;
using Godot;
using AppSettings = Cue2.Domain.ShowSettings.Settings;

namespace Cue2.UI.Windows;

/// <summary>
/// First-time startup welcome sub-window shown when Cue2 opens for a new install
/// (or every launch while testing with the force-show flag).
/// </summary>
/// <remarks>
/// Welcome message, documentation/website links, optional UI scale adjustment (same
/// controls as Settings → General), then dismiss via Get Started or window chrome.
/// Dismiss marks <see cref="UserDataManager.IsFirstTimeStartup"/> complete.
/// </remarks>
public partial class FirstTimeStartupWindow : Window
{
	private GlobalData _globalData;
	private GlobalSignals _globalSignals;
	private UserDataManager _userDataManager;
	private HistoryManager _historyManager;

	private LinkButton _docsLinkButton;
	private LinkButton _websiteLinkButton;
	private Button _getStartedButton;

	private LineEdit _uiScaleNum;
	private HSlider _uiScaleSlider;
	private Button _uiScaleResetButton;

	/// <summary>True while pushing model → controls so handlers do not re-record history.</summary>
	private bool _isSyncingUi;

	/// <summary>
	/// Initializes UI, applies scale, wires dismiss and UI-scale handlers.
	/// </summary>
	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_userDataManager = _globalData?.UserDataManager;
		_historyManager = _globalData?.HistoryManager;

		_docsLinkButton = GetNode<LinkButton>("%DocsLinkButton");
		_websiteLinkButton = GetNode<LinkButton>("%WebsiteLinkButton");
		_getStartedButton = GetNode<Button>("%GetStartedButton");

		_docsLinkButton.Uri = Version.DocsWebsite;
		_websiteLinkButton.Uri = Version.Website;

		_getStartedButton.Pressed += OnGetStartedPressed;
		CloseRequested += OnCloseRequested;

		WireUiScaleControls();
		SyncUiScaleControls();

		// Re-sync if UI scale is undone/redone while this window is open (e.g. via Settings).
		if (_historyManager != null)
			_historyManager.HistoryRestored += OnHistoryRestored;

		UiUtilities.RescaleWindow(this, _globalData.BaseDisplayScale);
		UiUtilities.RescaleUi(this, _globalData.Settings.UiScale, _globalData.BaseDisplayScale);

		_globalSignals.UiScaleChanged += ScaleUi;
	}

	/// <summary>
	/// Wires the UI scale LineEdit / HSlider / reset button to match Settings → General.
	/// </summary>
	private void WireUiScaleControls()
	{
		_uiScaleNum = GetNode<LineEdit>("%UiScaleNum");
		_uiScaleSlider = GetNode<HSlider>("%UiScaleSlider");
		_uiScaleResetButton = GetNodeOrNull<Button>("%UiScaleResetButton");

		if (_uiScaleResetButton != null)
		{
			_uiScaleResetButton.Icon = GetThemeIcon("Refresh", "AtlasIcons");
			_uiScaleResetButton.Pressed += OnUiScaleResetPressed;
		}

		// Ensure the percentage field can receive focus (scene default may be FOCUS_NONE).
		_uiScaleNum.FocusMode = Control.FocusModeEnum.All;
		_uiScaleNum.Editable = true;

		_uiScaleSlider.ValueChanged += OnUiScaleSliderValueChanged;
		_uiScaleSlider.DragEnded += OnUiScaleSliderDragEnded;
		// Commit typed scale on Enter only (same as SettingsGeneral).
		_uiScaleNum.TextSubmitted += OnUiScaleTextSubmitted;
	}

	/// <summary>
	/// Pulls current show UI scale into the form without re-firing edit handlers.
	/// </summary>
	private void SyncUiScaleControls()
	{
		if (_globalData?.Settings == null)
			return;

		_isSyncingUi = true;
		try
		{
			float uiPct = _globalData.Settings.UiScale * 100f;
			if (_uiScaleNum != null)
				_uiScaleNum.Text = uiPct + "%";
			_uiScaleSlider?.SetValueNoSignal(uiPct);
			UpdateUiScaleResetButton();
		}
		finally
		{
			_isSyncingUi = false;
		}
	}

	/// <summary>
	/// After undo/redo of a settings-scoped entry, refresh the scale controls if needed.
	/// </summary>
	/// <param name="scope">History scope enum cast to int.</param>
	private void OnHistoryRestored(int scope)
	{
		if (!GodotObject.IsInstanceValid(this) || _globalData?.Settings == null)
			return;
		if (scope != (int)HistoryManager.HistoryScope.Settings)
			return;
		SyncUiScaleControls();
	}

	// ── UI Scale (mirrors SettingsGeneral) ────────────────────────────────

	/// <summary>
	/// Live-updates the percentage field while dragging; applies on mouse release.
	/// </summary>
	/// <param name="value">Slider value in percent (50–200).</param>
	private void OnUiScaleSliderValueChanged(double value)
	{
		if (_isSyncingUi)
			return;
		if (_uiScaleNum != null)
			_uiScaleNum.Text = value + "%";
	}

	/// <summary>
	/// Commits UI scale when the slider drag ends.
	/// </summary>
	/// <param name="_">Unused; Godot drag-ended signal argument.</param>
	private void OnUiScaleSliderDragEnded(bool _)
	{
		if (_isSyncingUi || _uiScaleSlider == null)
			return;
		ApplyUiScale((float)(_uiScaleSlider.Value / 100.0));
	}

	/// <summary>
	/// Commits UI scale when Enter is pressed in the percentage field.
	/// </summary>
	/// <param name="input">Raw LineEdit text (may include %).</param>
	private void OnUiScaleTextSubmitted(string input)
	{
		if (_isSyncingUi)
			return;
		CommitUiScaleFromText(input);
	}

	/// <summary>
	/// Parses and clamps typed percent, then applies UI scale.
	/// </summary>
	/// <param name="input">Raw text from the LineEdit.</param>
	private void CommitUiScaleFromText(string input)
	{
		if (_globalData?.Settings == null || _uiScaleNum == null)
			return;

		string cleaned = (input ?? string.Empty).Replace("%", "").Trim();
		if (!float.TryParse(cleaned, out float value))
		{
			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log), "Invalid value for UI Scale entered", 1);
			_uiScaleNum.Text = _globalData.Settings.UiScale * 100f + "%";
			return;
		}

		value = Mathf.Clamp(value, 50f, 200f);
		_uiScaleNum.Text = value + "%";
		_uiScaleSlider?.SetValueNoSignal(value);
		ApplyUiScale(value / 100f);
		if (_uiScaleNum.HasFocus())
			_uiScaleNum.ReleaseFocus();
	}

	/// <summary>
	/// Writes UI scale to show settings, records history, and notifies listeners.
	/// </summary>
	/// <param name="scaleFactor">Scale factor in the range 0.5–2.0.</param>
	private void ApplyUiScale(float scaleFactor)
	{
		if (_isSyncingUi || _globalData?.Settings == null)
			return;
		if (_historyManager?.IsRestoring == true)
			return;

		scaleFactor = Mathf.Clamp(scaleFactor, 0.5f, 2.0f);
		if (Mathf.IsEqualApprox(_globalData.Settings.UiScale, scaleFactor))
		{
			UpdateUiScaleResetButton();
			return;
		}

		_historyManager?.RecordSettingsChange("Change UI scale", null, "UiScale");
		_globalData.Settings.UiScale = scaleFactor;
		_globalSignals?.EmitSignal(nameof(GlobalSignals.UiScaleChanged), scaleFactor);
		UpdateUiScaleResetButton();
	}

	/// <summary>
	/// Resets UI scale to the show default and syncs controls.
	/// </summary>
	private void OnUiScaleResetPressed()
	{
		if (_isSyncingUi || _globalData?.Settings == null)
			return;
		if (Mathf.IsEqualApprox(_globalData.Settings.UiScale, AppSettings.DefaultUiScale))
		{
			SyncUiScaleControls();
			return;
		}

		_historyManager?.RecordSettingsChange("Reset UI scale", null, "UiScale");
		_globalData.Settings.UiScale = AppSettings.DefaultUiScale;
		SyncUiScaleControls();
		_globalSignals?.EmitSignal(nameof(GlobalSignals.UiScaleChanged), AppSettings.DefaultUiScale);
	}

	/// <summary>
	/// Shows the reset button only when scale is not at the system default.
	/// </summary>
	private void UpdateUiScaleResetButton()
	{
		if (_uiScaleResetButton == null || _globalData?.Settings == null)
			return;

		bool atDefault = Mathf.IsEqualApprox(_globalData.Settings.UiScale, AppSettings.DefaultUiScale);
		_uiScaleResetButton.Visible = !atDefault;
		if (!atDefault)
			_uiScaleResetButton.TooltipText = $"Reset to default: {AppSettings.DefaultUiScale * 100f:0}%";
	}

	// ── Dismiss ───────────────────────────────────────────────────────────

	/// <summary>
	/// Marks first-time startup complete and closes the window.
	/// </summary>
	private void OnGetStartedPressed()
	{
		MarkFirstTimeComplete();
		QueueFree();
	}

	/// <summary>
	/// Handles OS/window close request (title bar / chrome).
	/// </summary>
	private void OnCloseRequested()
	{
		MarkFirstTimeComplete();
		QueueFree();
	}

	/// <summary>
	/// Persists the first-time flag. Safe to call more than once.
	/// </summary>
	/// <remarks>
	/// Also invoked from <see cref="_ExitTree"/> so the SubWindowHandles exit button
	/// (which QueueFrees without emitting CloseRequested) still completes the flow.
	/// </remarks>
	private void MarkFirstTimeComplete()
	{
		try
		{
			_userDataManager?.MarkFirstTimeStartupComplete();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"FirstTimeStartupWindow:MarkFirstTimeComplete - Failed to mark complete: {ex.Message}");
			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
				$"Failed to save first-time startup preference: {ex.Message}", 2);
		}
	}

	/// <summary>
	/// Applies runtime UI scale changes to this borderless sub-window.
	/// </summary>
	/// <param name="value">User UI scale multiplier.</param>
	private void ScaleUi(float value)
	{
		try
		{
			// Keep controls in sync if scale was changed elsewhere (main Settings, undo, etc.).
			if (!_isSyncingUi)
				SyncUiScaleControls();

			float effectiveScale = value * _globalData.BaseDisplayScale;
			WrapControls = true;
			ContentScaleFactor = effectiveScale;
			ChildControlsChanged();
			GD.Print($"FirstTimeStartupWindow:ScaleUi - Applied effective UI scale: {effectiveScale}");
		}
		catch (Exception ex)
		{
			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log), $"Error applying UI scale: {ex.Message}", 2);
			GetWindow().ContentScaleFactor = value;
		}
	}

	/// <inheritdoc />
	public override void _ExitTree()
	{
		// Cover dismiss via SubWindowHandles exit button (QueueFree without CloseRequested).
		MarkFirstTimeComplete();

		if (_getStartedButton != null)
			_getStartedButton.Pressed -= OnGetStartedPressed;

		CloseRequested -= OnCloseRequested;

		if (_uiScaleSlider != null)
		{
			_uiScaleSlider.ValueChanged -= OnUiScaleSliderValueChanged;
			_uiScaleSlider.DragEnded -= OnUiScaleSliderDragEnded;
		}
		if (_uiScaleNum != null)
			_uiScaleNum.TextSubmitted -= OnUiScaleTextSubmitted;
		if (_uiScaleResetButton != null)
			_uiScaleResetButton.Pressed -= OnUiScaleResetPressed;

		if (_historyManager != null)
			_historyManager.HistoryRestored -= OnHistoryRestored;

		if (_globalSignals != null)
			_globalSignals.UiScaleChanged -= ScaleUi;
	}
}
