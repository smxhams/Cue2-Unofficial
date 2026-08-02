//==================================================================================//
// GlobalSignals.cs																	//
// This file is part of Cue2														//
// http://cue2.live/																//
//==================================================================================//
// MIT License																		//
//																					//
// Copyright © 2025 Samuel Moxham													//
//																					//
// Permission is hereby granted, free of charge, to any person obtaining a copy		//
// 	of this software and associated documentation files (the ""Software""), to deal	//
// 	in the Software without restriction, including without limitation the rights	//
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell		//
// copies of the Software, and to permit persons to whom the Software is			//
// 	furnished to do so, subject to the following conditions:						//
//																					//
// The above copyright notice and this permission notice shall be included in all	//
// 	copies or substantial portions of the Software.									//
//																					//
// 	THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR	//
// 	IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,		//
// 	FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE		//
// 	AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER			//
// 	LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,	//
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE	//
// SOFTWARE.																		//
//==================================================================================//

using Godot;
using System;
using System.Collections.Generic;
using Cue2.Domain.Cuelist;
using Cue2.Domain.Playback;
using Cue2.Domain.Devices;
using Cue2.Domain.ShowSettings;
using Cue2.Domain.Metadata;
using Cue2.Domain.Cues;
using Cue2.Domain.Connections;
using Cue2.Domain.Library;
using Cue2.Domain.Commands;

namespace Cue2.Services;
public partial class GlobalSignals : Node
{
	[Signal]  public delegate void ShellFocusedEventHandler(int cueId);
	[Signal]  public delegate void LogEventHandler(string log, int type);
	// 0 = info, 1 = warning, 2 = error, 3 = alert
	[Signal]  public delegate void LogUpdatedEventHandler(string printout, int type);
	[Signal] public delegate void LogAlertEventHandler();
	[Signal]  public delegate void FileSelectedEventHandler(string path);
	[Signal]  public delegate void FileDroppedEventHandler(string[] files, string targetControlName);
	[Signal]  public delegate void CueGoEventHandler(int playbackId, int cueId);
	[Signal]  public delegate void UpdateShellBarEventHandler(int cue);
	[Signal]  public delegate void OpenSelectedSessionEventHandler(string path);
	[Signal]  public delegate void SaveFileEventHandler(string url, string showName);
	[Signal] public delegate void SyncShellInspectorEventHandler();
	
	// Sub-window events
	//[Signal]  public delegate void CloseSettingsWindowEventHandler();
	//[Signal] public delegate void AboutWindowClosedEventHandler();
	
	// Signals Associated with InputActions
	[Signal] public delegate void NewSessionEventHandler();
	[Signal] public delegate void SaveEventHandler();
	[Signal] public delegate void SaveAsEventHandler();

	[Signal] public delegate void OpenSessionEventHandler();

	[Signal] public delegate void GoEventHandler();
	
	[Signal] public delegate void ResumeAllEventHandler();
	[Signal] public delegate void PauseAllEventHandler();
	[Signal] public delegate void StopAllEventHandler();
	
	[Signal] public delegate void CreateCueEventHandler();

	/// <summary>
	/// Fired when the total number of cues in the show changes (create, delete, load, reset, undo/redo).
	/// </summary>
	/// <remarks>Argument is the new total cue count (all cues including group children).</remarks>
	[Signal] public delegate void TotalCuesChangedEventHandler(int total);

	/// <summary>Delete currently selected cue(s) from the cuelist.</summary>
	[Signal] public delegate void DeleteSelectedCuesEventHandler();

	/// <summary>Duplicate currently selected cue(s) (and full child trees when a parent is selected).</summary>
	[Signal] public delegate void DuplicateSelectedCuesEventHandler();

	/// <summary>Cut currently selected cue(s) to the cue clipboard (copy then delete).</summary>
	[Signal] public delegate void CutSelectedCuesEventHandler();

	/// <summary>Copy currently selected cue(s) to the cue clipboard.</summary>
	[Signal] public delegate void CopySelectedCuesEventHandler();

	/// <summary>Paste cue clipboard contents below the last selected cue.</summary>
	[Signal] public delegate void PasteCuesEventHandler();
	
	[Signal] public delegate void GroupSelectedCuesEventHandler();

	/// <summary>Select all currently visible cues in the cuelist.</summary>
	[Signal] public delegate void SelectAllCuesEventHandler();

	[Signal] public delegate void SelectNextCueEventHandler();
	[Signal] public delegate void SelectPreviousCueEventHandler();
	
	[Signal] public delegate void ToggleSettingsWindowEventHandler();
	[Signal] public delegate void ToggleLogWindowEventHandler();

	[Signal] public delegate void CuelistExpandOneLayerEventHandler();
	[Signal] public delegate void CuelistCollapseOneLayerEventHandler();
	[Signal] public delegate void ToggleExpandAllEventHandler();

	/// <summary>Undo the last document edit (cues / settings data).</summary>
	[Signal] public delegate void UndoEventHandler();

	/// <summary>Redo the last undone document edit.</summary>
	[Signal] public delegate void RedoEventHandler();

	/// <summary>
	/// Fired when show mode is enabled or disabled (showfile setting).
	/// </summary>
	/// <param name="enabled">True = Show Mode (cue edits locked); false = Edit Mode.</param>
	[Signal] public delegate void ShowModeChangedEventHandler(bool enabled);

	/// <summary>
	/// Toggle Show Mode / Edit Mode (Input Map / hotkey).
	/// </summary>
	[Signal] public delegate void ToggleShowModeEventHandler();
	
	// Text edit signal connector
	[Signal]  public delegate void TextEditFocusEnteredEventHandler();
	[Signal]  public delegate void TextEditFocusExitedEventHandler();
	
	
	// Singals assaciated with settings
	[Signal] public delegate void UiScaleChangedEventHandler(float value);
	[Signal] public delegate void GoScaleChangedEventHandler(float value);

	/// <summary>Fired when the cuelist UI scale (Small / Medium / Large) changes.</summary>
	[Signal] public delegate void CueListScaleChangedEventHandler(float value);

	/// <summary>Fired when the Timeline Inspector waveform display setting changes.</summary>
	[Signal] public delegate void ShowTimelineWaveformsChangedEventHandler(bool enabled);

	// Signals associated with devices
	[Signal] public delegate void AudioDevicesChangedEventHandler();

	/// <summary>
	/// Fired when session master audio volume or runtime mute changes.
	/// </summary>
	/// <param name="linear">Session master volume linear 0–1 (ignores mute).</param>
	/// <param name="muted">True when runtime master mute is active.</param>
	[Signal] public delegate void AudioMasterControlChangedEventHandler(float linear, bool muted);

	[Signal] public delegate void DisplaysChangedEventHandler();
	[Signal] public delegate void CanvasSizeChangedEventHandler(Vector2I newSize);

	/// <summary>
	/// Fired when master video output disable or blackout runtime state changes.
	/// </summary>
	/// <param name="disabled">True when all display windows are closed/hidden.</param>
	/// <param name="blackout">True when layers are blacked out (windows still open).</param>
	[Signal] public delegate void VideoOutputControlChangedEventHandler(bool disabled, bool blackout);

	/// <summary>
	/// Fired when the show-scoped output background colour changes.
	/// </summary>
	/// <param name="color">New background colour applied to output windows.</param>
	[Signal] public delegate void OutputBackgroundColorChangedEventHandler(Color color);

	/// <summary>
	/// Fired when a single target layer's size or canvas position changes
	/// (e.g. Translate Layer control animation). Lighter than <see cref="DisplaysChanged"/>.
	/// </summary>
	/// <param name="layerId">Layer that was updated.</param>
	[Signal] public delegate void LayerGeometryChangedEventHandler(int layerId);

	// Media backup (show-local file copies)
	/// <summary>
	/// Fired when media backup progress changes.
	/// Args: percent (0–100), busy, statusText (e.g. "Copying 45%"), originPath, destPath, completedCount, totalCount.
	/// </summary>
	[Signal] public delegate void MediaBackupProgressEventHandler(
		float percent, bool busy, string statusText, string originPath, string destPath, int completedCount, int totalCount);
	/// <summary>Fired when the media backup queue becomes idle.</summary>
	[Signal] public delegate void MediaBackupCompletedEventHandler();

	/// <summary>
	/// Generic footer background-process progress (cuelist bulk ops, etc.).
	/// Args: percent (0–100), busy, statusText (shown on the bar), detail (tooltip secondary line),
	/// completedCount, totalCount.
	/// </summary>
	[Signal] public delegate void BackgroundProcessProgressEventHandler(
		float percent, bool busy, string statusText, string detail, int completedCount, int totalCount);

	/// <summary>Fired when a generic background process finishes (footer may hide after a short delay).</summary>
	[Signal] public delegate void BackgroundProcessCompletedEventHandler();

	// Media health (missing files, etc.)
	/// <summary>
	/// Fired when a cue's media health state changes.
	/// Args: cueId, hasIssue, message (tooltip text when hasIssue is true).
	/// </summary>
	[Signal] public delegate void CueMediaHealthChangedEventHandler(int cueId, bool hasIssue, string message);

	public static event Action<string, int> Logger;

	/// <summary>Text fields wired for focus-gate + Esc/submit unfocus.</summary>
	private readonly HashSet<Node> _connectedTextFields = new();

	/// <summary>Per-LineEdit hooks so they can be disconnected cleanly on free.</summary>
	private readonly Dictionary<LineEdit, (Control.GuiInputEventHandler Gui, LineEdit.TextSubmittedEventHandler Submit)> _lineEditHooks = new();

	/// <summary>Per-TextEdit Esc hooks.</summary>
	private readonly Dictionary<TextEdit, Control.GuiInputEventHandler> _textEditHooks = new();

	/// <summary>
	/// Scans the tree for LineEdit/TextEdit and wires focus + Esc (and Enter unfocus for LineEdit).
	/// Focus signals drive <see cref="InputActionsListener"/> so typing does not fire hotkeys.
	/// </summary>
	public override void _Ready()
	{
		// Scan for existing text fields at startup
		ScanForTextFields(GetTree().Root);

		// Listen for new nodes added dynamically (inspectors, settings cards, SpinBox embeds, etc.)
		GetTree().NodeAdded += OnNodeAdded;
		GetTree().NodeRemoved += OnNodeRemoved;
	}

	public static void StaticLog(string s, int i)
	{
		// TODO: This can be made static in the future, will require changing all calls everywhere though!
		Logger?.Invoke("hi", 1);
	}

	private void ScanForTextFields(Node node)
	{
		if (node is LineEdit or TextEdit)
			ConnectFocusSignals(node);

		foreach (Node child in node.GetChildren())
			ScanForTextFields(child);
	}

	private void OnNodeAdded(Node node)
	{
		if (node is LineEdit or TextEdit)
			ConnectFocusSignals(node);
	}

	private void OnNodeRemoved(Node node)
	{
		if (node is LineEdit or TextEdit)
			DisconnectFocusSignals(node);
	}

	/// <summary>
	/// Wires focus tracking (hotkey gate) and keyboard unfocus helpers for a text field.
	/// </summary>
	private void ConnectFocusSignals(Node node)
	{
		if (node is not Control textField || _connectedTextFields.Contains(node))
			return;

		textField.FocusEntered += FocusEntered;
		textField.FocusExited += FocusExited;

		if (node is LineEdit lineEdit)
		{
			// Esc → leave field; Enter/submit → leave field (consistent across the app).
			Control.GuiInputEventHandler gui = @event => OnLineEditGuiInput(lineEdit, @event);
			LineEdit.TextSubmittedEventHandler submit = _ => ReleaseLineEditFocus(lineEdit);
			lineEdit.GuiInput += gui;
			lineEdit.TextSubmitted += submit;
			_lineEditHooks[lineEdit] = (gui, submit);
		}
		else if (node is TextEdit textEdit)
		{
			// Multi-line: Esc only (Enter inserts a newline).
			Control.GuiInputEventHandler gui = @event => OnTextEditGuiInput(textEdit, @event);
			textEdit.GuiInput += gui;
			_textEditHooks[textEdit] = gui;
		}

		_connectedTextFields.Add(node);
	}

	/// <summary>
	/// Unhooks focus and keyboard helpers when a text field leaves the tree.
	/// </summary>
	private void DisconnectFocusSignals(Node node)
	{
		if (!_connectedTextFields.Contains(node) || node is not Control textField)
			return;

		textField.FocusEntered -= FocusEntered;
		textField.FocusExited -= FocusExited;

		if (node is LineEdit lineEdit && _lineEditHooks.TryGetValue(lineEdit, out var lineHooks))
		{
			lineEdit.GuiInput -= lineHooks.Gui;
			lineEdit.TextSubmitted -= lineHooks.Submit;
			_lineEditHooks.Remove(lineEdit);
		}
		else if (node is TextEdit textEdit && _textEditHooks.TryGetValue(textEdit, out var textGui))
		{
			textEdit.GuiInput -= textGui;
			_textEditHooks.Remove(textEdit);
		}

		_connectedTextFields.Remove(node);
	}

	private void OnLineEditGuiInput(LineEdit lineEdit, InputEvent @event)
	{
		if (!IsEscapePressed(@event) || !GodotObject.IsInstanceValid(lineEdit) || !lineEdit.HasFocus())
			return;

		ReleaseLineEditFocus(lineEdit);
		lineEdit.GetViewport()?.SetInputAsHandled();
	}

	private void OnTextEditGuiInput(TextEdit textEdit, InputEvent @event)
	{
		if (!IsEscapePressed(@event) || !GodotObject.IsInstanceValid(textEdit) || !textEdit.HasFocus())
			return;

		textEdit.ReleaseFocus();
		textEdit.GetViewport()?.SetInputAsHandled();
	}

	/// <summary>
	/// True when Escape was just pressed (ignores key-repeat).
	/// </summary>
	private static bool IsEscapePressed(InputEvent @event)
	{
		if (@event is not InputEventKey key || !key.Pressed || key.Echo)
			return false;

		return key.Keycode == Key.Escape
			|| key.PhysicalKeycode == Key.Escape
			|| key.IsAction("ui_cancel");
	}

	/// <summary>
	/// Ends edit mode and clears focus so InputMap listening can resume.
	/// Deferred so Godot cannot re-grab focus on the same frame as TextSubmitted.
	/// </summary>
	/// <param name="lineEdit">LineEdit to unfocus.</param>
	private static void ReleaseLineEditFocus(LineEdit lineEdit)
	{
		if (lineEdit == null || !GodotObject.IsInstanceValid(lineEdit))
			return;

		// Godot 4.4+: leave "editing" state explicitly (caret/IME), then clear focus.
		if (lineEdit.HasMethod("unedit") && lineEdit.IsEditing())
			lineEdit.Unedit();

		if (lineEdit.HasFocus())
			lineEdit.ReleaseFocus();

		// Belt-and-braces: submit can race with engine re-focus on the same frame.
		lineEdit.CallDeferred(Control.MethodName.ReleaseFocus);
	}

	private void FocusEntered()
	{
		EmitSignal(SignalName.TextEditFocusEntered);
	}

	private void FocusExited()
	{
		EmitSignal(SignalName.TextEditFocusExited);
	}
}
