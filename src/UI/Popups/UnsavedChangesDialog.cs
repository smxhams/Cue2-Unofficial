// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using System;
using Cue2.Services;
using Cue2.UI.Utilities;
using Godot;

namespace Cue2.UI.Popups;

/// <summary>
/// Modal prompt when closing or replacing a session that has unsaved document changes.
/// </summary>
public partial class UnsavedChangesDialog : Window
{
	/// <summary>Scene path for <see cref="SceneLoader"/>.</summary>
	public const string ScenePath = "res://src/UI/Popups/UnsavedChangesDialog.tscn";

	/// <summary>Raised when the user chooses Save &amp; close.</summary>
	public event Action SaveAndClose;

	/// <summary>Raised when the user chooses Close (discard changes).</summary>
	public event Action DiscardAndClose;

	/// <summary>Raised when the user cancels.</summary>
	public event Action Cancelled;

	private GlobalData _globalData;
	private GlobalSignals _globalSignals;

	private Label _titleLabel;
	private Label _bodyLabel;
	private Button _cancelButton;
	private Button _closeButton;
	private Button _saveCloseButton;

	private bool _signalsConnected;
	private bool _choiceMade;

	/// <summary>
	/// Applies Linux native-window policy before the scene window enters the tree.
	/// </summary>
	public UnsavedChangesDialog()
	{
		LinuxWindowEmbedPolicy.ApplyToAppWindow(this);
	}

	/// <inheritdoc />
	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

		UiUtilities.RescaleWindow(this, _globalData.BaseDisplayScale);
		float userScale = _globalData.UserDataManager?.UiScale ?? UserDataManager.DefaultUiScale;
		UiUtilities.RescaleUi(this, userScale, _globalData.BaseDisplayScale);

		if (_globalSignals != null)
			_globalSignals.UiScaleChanged += ScaleUi;

		ResolveNodes();
		ConnectUiSignals();
		UiLocalizer.LocalizeTree(this);
		ApplyLocalizedCopy();
	}

	/// <inheritdoc />
	public override void _ExitTree()
	{
		if (_globalSignals != null)
			_globalSignals.UiScaleChanged -= ScaleUi;

		DisconnectUiSignals();
	}

	/// <summary>
	/// Instantiates the dialog scene.
	/// </summary>
	public static UnsavedChangesDialog Create(out string errorMessage)
	{
		var node = SceneLoader.LoadScene(ScenePath, out errorMessage);
		if (node is UnsavedChangesDialog dialog)
			return dialog;

		if (node != null)
		{
			node.QueueFree();
			errorMessage = "Loaded scene is not an UnsavedChangesDialog.";
		}
		else if (string.IsNullOrEmpty(errorMessage))
		{
			errorMessage = $"Failed to load {ScenePath}.";
		}

		return null;
	}

	/// <summary>
	/// Sets title/body from the current session name (call before show).
	/// </summary>
	/// <param name="sessionLabel">Show name or empty for an unsaved session.</param>
	public void Configure(string sessionLabel)
	{
		ResolveNodes();
		ConnectUiSignals();
		ApplyLocalizedCopy(sessionLabel);
	}

	/// <summary>Shows the dialog centered.</summary>
	public void ShowConfigured()
	{
		PopupCentered();
		GrabFocus();
	}

	private void ApplyLocalizedCopy(string sessionLabel = null)
	{
		Title = UiLocalizer.T("Unsaved Changes");
		if (_titleLabel != null)
			_titleLabel.Text = UiLocalizer.T("Unsaved Changes");

		string name = string.IsNullOrWhiteSpace(sessionLabel)
			? UiLocalizer.T("Untitled")
			: sessionLabel.Trim();
		if (_bodyLabel != null)
			_bodyLabel.Text = UiLocalizer.Tf(
				"\"{0}\" has unsaved changes. Save before closing this session?",
				name);

		if (_cancelButton != null)
			_cancelButton.Text = UiLocalizer.T("Cancel");
		if (_closeButton != null)
			_closeButton.Text = UiLocalizer.T("Close");
		if (_saveCloseButton != null)
			_saveCloseButton.Text = UiLocalizer.T("Save & close");
	}

	private void ResolveNodes()
	{
		_titleLabel ??= GetNodeOrNull<Label>("%TitleLabel");
		_bodyLabel ??= GetNodeOrNull<Label>("%BodyLabel");
		_cancelButton ??= GetNodeOrNull<Button>("%CancelButton");
		_closeButton ??= GetNodeOrNull<Button>("%CloseButton");
		_saveCloseButton ??= GetNodeOrNull<Button>("%SaveCloseButton");
	}

	private void ConnectUiSignals()
	{
		if (_signalsConnected)
			return;

		ResolveNodes();
		if (_cancelButton == null || _closeButton == null || _saveCloseButton == null)
			return;

		_cancelButton.Pressed += OnCancelPressed;
		_closeButton.Pressed += OnClosePressed;
		_saveCloseButton.Pressed += OnSaveClosePressed;
		CloseRequested += OnCancelPressed;
		_signalsConnected = true;
	}

	private void DisconnectUiSignals()
	{
		if (!_signalsConnected)
			return;

		if (_cancelButton != null)
			_cancelButton.Pressed -= OnCancelPressed;
		if (_closeButton != null)
			_closeButton.Pressed -= OnClosePressed;
		if (_saveCloseButton != null)
			_saveCloseButton.Pressed -= OnSaveClosePressed;
		CloseRequested -= OnCancelPressed;
		_signalsConnected = false;
	}

	private void ScaleUi(float value)
	{
		try
		{
			float effectiveScale = value * _globalData.BaseDisplayScale;
			WrapControls = true;
			ContentScaleFactor = effectiveScale;
			ChildControlsChanged();
		}
		catch (Exception ex)
		{
			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
				$"Error applying UI scale: {ex.Message}", (int)LogType.Warning);
		}
	}

	private void OnCancelPressed()
	{
		if (_choiceMade)
			return;
		_choiceMade = true;
		Cancelled?.Invoke();
		Hide();
		QueueFree();
	}

	private void OnClosePressed()
	{
		if (_choiceMade)
			return;
		_choiceMade = true;
		DiscardAndClose?.Invoke();
		Hide();
		QueueFree();
	}

	private void OnSaveClosePressed()
	{
		if (_choiceMade)
			return;
		_choiceMade = true;
		SaveAndClose?.Invoke();
		Hide();
		QueueFree();
	}
}
