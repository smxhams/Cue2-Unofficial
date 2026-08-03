// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cue2.UI.Shell;
using Cue2.Services;
using Cue2.UI.Utilities;
using Cue2.UI.Popups;

namespace Cue2.UI.Popups;

/// <summary>
/// Popup window shown when files are dropped onto the cue list or a shell bar.
/// Presents context-sensitive options for how to turn the dropped media files into cues.
/// </summary>
public partial class FileDropPopup : Window
{
	private GlobalData _globalData;
	private GlobalSignals _globalSignals;

	private string[] _files = Array.Empty<string>();
	private FileDropTargetType _targetType = FileDropTargetType.None;
	private string _targetDisplayName = "";
	private int _targetCueId = -1;

	private Button _cancelButton;
	private Button _createButton;

	private DropInsertMode _chosenInsertMode = DropInsertMode.Below;
	private bool _chosenAsGroup = false;

	/// <summary>
	/// Raised when the user confirms the drop action with their choices.
	/// </summary>
	public event Action<FileDropChoices> Confirmed;

	/// <summary>
	/// Raised when the user cancels the drop.
	/// </summary>
	public event Action Cancelled;

	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

		GD.Print("FileDropPopup:Loading FileDropPopup");
		
		UiUtilities.RescaleWindow(this, _globalData.BaseDisplayScale);
		UiUtilities.RescaleUi(this, _globalData.Settings.UiScale, _globalData.BaseDisplayScale);

		_globalSignals.UiScaleChanged += ScaleUi;

		_cancelButton = GetNode<Button>("%CancelButton");
		_createButton = GetNode<Button>("%CreateButton");

		_cancelButton.Pressed += OnCancelPressed;
		_createButton.Pressed += OnCreatePressed;

	}

	public override void _ExitTree()
	{
		_globalSignals.UiScaleChanged -= ScaleUi;
		if (_cancelButton != null) _cancelButton.Pressed -= OnCancelPressed;
		if (_createButton != null) _createButton.Pressed -= OnCreatePressed;
	}

	/// <summary>
	/// Configures the popup for a specific drop and populates dynamic option controls.
	/// Call this before or instead of Show().
	/// </summary>
	/// <param name="files">The dropped file paths (already filtered to valid media).</param>
	/// <param name="targetType">Where the files landed.</param>
	/// <param name="targetDisplayName">Human readable target (cue name or "Cue List").</param>
	/// <param name="targetCueId">Cue ID if dropped on a specific shell, otherwise -1.</param>
	public void ConfigureForDrop(string[] files, FileDropTargetType targetType, string targetDisplayName, int targetCueId)
	{
		_files = files ?? Array.Empty<string>();
		_targetType = targetType;
		_targetDisplayName = targetDisplayName ?? targetType.ToString();
		_targetCueId = targetCueId;

		_chosenInsertMode = DropInsertMode.Below;
		_chosenAsGroup = false;

		PopulateOptions();
	}

	private void PopulateOptions()
	{
		var container = GetNode<VBoxContainer>("%OptionsListContiner");
		if (container == null) return;

		// Clear previous dynamic content except the two static header labels (identified by node Name)
		foreach (Node child in container.GetChildren())
		{
			if (child is Label lbl && (lbl.Name == "DropTargetLabel" || lbl.Name == "DropFileName"))
				continue;
			child.QueueFree();
		}

		// Update header labels
		var dropTargetLabel = GetNode<Label>("%DropTargetLabel");
		var dropFileNameLabel = GetNode<Label>("%DropFileName");

		dropTargetLabel.Text = $"Drop Target: {_targetDisplayName}";
		var fileNames = _files.Select(f => Path.GetFileName(f)).ToArray();
		dropFileNameLabel.Text = _files.Length == 1 
			? $"File: {fileNames[0]}" 
			: $"Files ({_files.Length}): {string.Join(", ", fileNames.Take(3))}{( _files.Length > 3 ? "..." : "")}";

		// Dynamic rows use project base_theme (set on Window) — no font-size overrides.
		var filesHeader = new Label { Text = "Dropped Files:" };
		container.AddChild(filesHeader);

		foreach (string f in _files.Take(6))
		{
			container.AddChild(new Label { Text = $"  • {Path.GetFileName(f)}" });
		}
		if (_files.Length > 6)
		{
			container.AddChild(new Label { Text = $"  ... and {_files.Length - 6} more" });
		}

		// Separator
		container.AddChild(new HSeparator());

		// Position options (only meaningful for ShellBar target)
		if (_targetType == FileDropTargetType.ShellBar && _targetCueId >= 0)
		{
			container.AddChild(new Label { Text = "Insert Position:" });

			var posContainer = new VBoxContainer();
			posContainer.AddThemeConstantOverride("separation", 2);
			posContainer.AddChild(CreatePositionChoice("Above target cue", DropInsertMode.Above));
			posContainer.AddChild(CreatePositionChoice("Below target cue", DropInsertMode.Below, isDefault: true));
			posContainer.AddChild(CreatePositionChoice("As child of target cue", DropInsertMode.AsChild));
			container.AddChild(posContainer);
		}
		else
		{
			container.AddChild(new Label { Text = "Insert Location: End of list (or after current selection)" });
		}

		// Multi-file options
		if (_files.Length > 1)
		{
			container.AddChild(new HSeparator());
			container.AddChild(new Label { Text = "Multiple Files Action:" });

			var separateBtn = new CheckBox { Text = "Create separate cue for each file (recommended)", ButtonPressed = !_chosenAsGroup };
			var groupBtn = new CheckBox { Text = "Wrap all files inside one new Group cue", ButtonPressed = _chosenAsGroup };

			separateBtn.Toggled += pressed =>
			{
				if (pressed)
				{
					_chosenAsGroup = false;
					groupBtn.ButtonPressed = false;
				}
			};
			groupBtn.Toggled += pressed =>
			{
				if (pressed)
				{
					_chosenAsGroup = true;
					separateBtn.ButtonPressed = false;
				}
			};

			container.AddChild(separateBtn);
			container.AddChild(groupBtn);
		}
	}

	private CheckBox CreatePositionChoice(string text, DropInsertMode mode, bool isDefault = false)
	{
		var cb = new CheckBox
		{
			Text = text,
			ButtonPressed = isDefault || _chosenInsertMode == mode
		};

		if (isDefault) _chosenInsertMode = mode;

		cb.Toggled += pressed =>
		{
			if (pressed)
			{
				_chosenInsertMode = mode;
				// Uncheck siblings (simple manual mutual exclusion)
				var parent = cb.GetParent();
				if (parent != null)
				{
					foreach (Node sibling in parent.GetChildren())
					{
						if (sibling is CheckBox other && other != cb)
							other.ButtonPressed = false;
					}
				}
			}
		};

		return cb;
	}

	private void OnCreatePressed()
	{
		var choices = new FileDropChoices
		{
			InsertMode = _chosenInsertMode,
			CreateAsGroup = _chosenAsGroup
		};

		Confirmed?.Invoke(choices);
		// Do not queue free here — caller decides (usually after acting)
		Hide();
	}

	private void OnCancelPressed()
	{
		Cancelled?.Invoke();
		Hide();
	}

	private void ScaleUi(float value)
	{
		try
		{
			float effectiveScale = value * _globalData.BaseDisplayScale;
			WrapControls = true;
			ContentScaleFactor = effectiveScale;
			ChildControlsChanged();
			GD.Print($"FileDropPopup:ScaleUi - Applied effective UI scale: {effectiveScale} (user: {value} * base: {_globalData.BaseDisplayScale})");
		} 
		catch (Exception ex)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Error applying UI scale: {ex.Message}", (int)LogType.Warning);
			GetWindow().ContentScaleFactor = value;
		}
	}

	/// <summary>
	/// Shows the configured popup centered.
	/// </summary>
	/// <remarks>
	/// Avoid runtime ResetSize / min-size rewrites on full-rect layout — they break anchors.
	/// Size is set by the scene and <see cref="UiUtilities.RescaleWindow"/>.
	/// </remarks>
	public void ShowConfigured()
	{
		PopupCentered();
	}

	/// <summary>
	/// Returns the files that were passed to ConfigureForDrop (used by caller after confirmation).
	/// </summary>
	public string[] GetFilesForCreation() => _files?.ToArray() ?? Array.Empty<string>();

	/// <summary>
	/// Returns the cue id of the shell that was the drop target (or -1).
	/// </summary>
	public int GetTargetCueId() => _targetCueId;
}
