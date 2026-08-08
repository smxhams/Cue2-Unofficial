// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using Godot;
using Cue2.Domain.Cuelist;
using Cue2.Domain.Playback;
using Cue2.Domain.Devices;
using Cue2.Domain.ShowSettings;
using Cue2.Domain.Metadata;
using Cue2.Domain.Cues;
using Cue2.Domain.Connections;
using Cue2.Domain.Library;
using Cue2.Domain.Commands;
using Cue2.Services;
using Cue2.UI.Utilities;

// This script is attached to instanced shell bars in the cue list, it handles
// -UI of itself
// -Emitting signals of interactions attached with it's relevant info
namespace Cue2.UI.Shell;

/// <summary>
/// Partial: Selection/context menu, panel chrome, draw hatch, drag grabber
/// </summary>
public partial class ShellBar
{
	public void ClearHoverChrome()
	{
		if (!_hovered)
			return;
		_hovered = false;
		if (!Selected)
			RefreshShellChrome();
	}
	

	private void OnInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mouseEvent || !mouseEvent.Pressed)
			return;

		// Right-click: cue context menu (Cut / Copy / Paste / … with hotkeys).
		if (mouseEvent.ButtonIndex == MouseButton.Right)
		{
			ShowShellContextMenu();
			AcceptEvent();
			return;
		}

		if (mouseEvent.ButtonIndex != MouseButton.Left)
			return;

		// Shift-click: range select (immediate; not marquee).
		if (Input.IsKeyPressed(Key.Shift))
		{
			_globalData.ShellSelection.SelectThrough(CueList.FetchCueFromId(CueId));
			return;
		}

		// Plain / Ctrl-Cmd left press: pending box-select. Click without drag still
		// selects (or Ctrl/Cmd-toggles membership); drag past threshold draws a marquee.
		// Drag grabber does not reach here (AcceptEvent + StartReorder on the grabber).
		bool additive = Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Meta);
		_globalData?.Cuelist?.BeginPotentialBoxSelect(this, mouseEvent.GlobalPosition, additive);
	}

	/// <summary>
	/// Time-field GuiInput: right-click → shell context menu; left-click → pending
	/// box-select / select without AcceptEvent so the LineEdit can still take focus.
	/// </summary>
	private void OnTimeFieldGuiInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mouseEvent || !mouseEvent.Pressed)
			return;

		if (mouseEvent.ButtonIndex == MouseButton.Right)
		{
			ShowShellContextMenu();
			AcceptEvent();
			return;
		}

		if (mouseEvent.ButtonIndex != MouseButton.Left)
			return;

		// Shift range-select stays immediate. Plain / Ctrl-Cmd start box-select (click vs drag;
		// click with Ctrl/Cmd toggles membership). Do not AcceptEvent — pre/post LineEdits
		// must still receive focus for edit.
		if (Input.IsKeyPressed(Key.Shift))
		{
			_globalData?.ShellSelection?.SelectThrough(CueList.FetchCueFromId(CueId));
			return;
		}

		bool additive = Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Meta);
		_globalData?.Cuelist?.BeginPotentialBoxSelect(this, mouseEvent.GlobalPosition, additive);
	}

	/// <summary>
	/// Builds and shows the shell cue-editing context menu at the mouse position.
	/// Ensures this cue is selected first so cut/copy/delete apply to a sensible selection.
	/// </summary>
	private void ShowShellContextMenu()
	{
		EnsureSelectedForContextMenu();

		if (_contextMenu == null || !IsInstanceValid(_contextMenu))
		{
			_contextMenu = new PopupMenu
			{
				Name = "ShellContextMenu",
				HideOnItemSelection = true
			};
			AddChild(_contextMenu);
			_contextMenu.IdPressed += OnShellContextMenuIdPressed;
		}

		_contextMenu.Clear();
		bool locked = IsCueEditingLocked();
		if (locked)
		{
			// Show Mode: copy is read-only; mutating actions are omitted.
			AddContextMenuItem("Copy", "CopySelectedCues", ShellContextMenuId.Copy);
		}
		else
		{
			AddContextMenuItem("Cut", "CutSelectedCues", ShellContextMenuId.Cut);
			AddContextMenuItem("Copy", "CopySelectedCues", ShellContextMenuId.Copy);
			AddContextMenuItem("Paste", "PasteCues", ShellContextMenuId.Paste);
			_contextMenu.AddSeparator();
			AddContextMenuItem("Duplicate", "DuplicateSelectedCues", ShellContextMenuId.Duplicate);
			AddContextMenuItem("Delete", "DeleteCue", ShellContextMenuId.Delete);
			_contextMenu.AddSeparator();
			AddContextMenuItem("Group", "GroupSelectedCues", ShellContextMenuId.Group);
			AddContextMenuItem("Create Cue", "CreateCue", ShellContextMenuId.CreateCue);
		}

		// Popup at cursor (screen coords — PopupMenu is a Window).
		_contextMenu.ResetSize();
		_contextMenu.Position = DisplayServer.MouseGetPosition();
		_contextMenu.Popup();
	}

	/// <summary>
	/// Adds a context-menu row with the action title on the left and a right-aligned hotkey
	/// via <see cref="PopupMenu.SetItemShortcut"/> (native accelerator column layout).
	/// </summary>
	/// <param name="title">Left-side action label.</param>
	/// <param name="inputAction">InputMap action used for the displayed shortcut.</param>
	/// <param name="id">Menu id for <see cref="OnShellContextMenuIdPressed"/>.</param>
	private void AddContextMenuItem(string title, string inputAction, ShellContextMenuId id)
	{
		_contextMenu.AddItem(title, (int)id);
		int index = _contextMenu.ItemCount - 1;
		var shortcut = CreateShortcutFromAction(inputAction);
		if (shortcut == null || shortcut.Events.Count == 0)
			return;

		// Display-only: InputActionsListener already handles these keys globally.
		// Disabled shortcuts still paint in the right-hand accelerator column.
		_contextMenu.SetItemShortcut(index, shortcut, global: false);
		_contextMenu.SetItemShortcutDisabled(index, true);
	}

	/// <summary>
	/// Builds a <see cref="Shortcut"/> from the live InputMap binding(s) for <paramref name="action"/>.
	/// </summary>
	/// <param name="action">Project InputMap action name.</param>
	/// <returns>Shortcut with duplicated events, or empty when unbound.</returns>
	private static Shortcut CreateShortcutFromAction(string action)
	{
		var shortcut = new Shortcut();
		if (string.IsNullOrEmpty(action) || !InputMap.HasAction(action))
			return shortcut;

		var events = new Godot.Collections.Array();
		foreach (InputEvent ev in InputMap.ActionGetEvents(action))
		{
			if (ev == null) continue;
			events.Add((InputEvent)ev.Duplicate());
		}
		shortcut.Events = events;
		return shortcut;
	}

	/// <summary>
	/// If this shell is not already part of the selection, select it alone before context actions.
	/// Keeps multi-select when right-clicking a cue that is already selected.
	/// </summary>
	private void EnsureSelectedForContextMenu()
	{
		var cue = CueList.FetchCueFromId(CueId);
		if (cue == null || _globalData?.ShellSelection == null) return;

		var selected = ShellSelection.SelectedCues;
		if (selected != null && selected.Contains(cue))
			return;

		_globalData.ShellSelection.SelectIndividualShell(cue);
	}

	/// <summary>
	/// Dispatches context menu choices to the same GlobalSignals as the Input Map hotkeys.
	/// </summary>
	/// <param name="id"><see cref="ShellContextMenuId"/> value.</param>
	private void OnShellContextMenuIdPressed(long id)
	{
		if (_globalSignals == null) return;

		switch ((ShellContextMenuId)id)
		{
			case ShellContextMenuId.Cut:
				_globalSignals.EmitSignal(nameof(GlobalSignals.CutSelectedCues));
				break;
			case ShellContextMenuId.Copy:
				_globalSignals.EmitSignal(nameof(GlobalSignals.CopySelectedCues));
				break;
			case ShellContextMenuId.Paste:
				_globalSignals.EmitSignal(nameof(GlobalSignals.PasteCues));
				break;
			case ShellContextMenuId.Duplicate:
				_globalSignals.EmitSignal(nameof(GlobalSignals.DuplicateSelectedCues));
				break;
			case ShellContextMenuId.Delete:
				_globalSignals.EmitSignal(nameof(GlobalSignals.DeleteSelectedCues));
				break;
			case ShellContextMenuId.Group:
				_globalSignals.EmitSignal(nameof(GlobalSignals.GroupSelectedCues));
				break;
			case ShellContextMenuId.CreateCue:
				_globalSignals.EmitSignal(nameof(GlobalSignals.CreateCue));
				break;
		}
	}

	public void Focus()
	{
		Selected = true;
		RefreshShellChrome();
	}
	

	public void Deselect()
	{
		Selected = false;
		// Keep legacy overlays hidden; chrome is the PanelContainer style only.
		if (_shellPanel != null)
			_shellPanel.Visible = false;
		if (_groupPanel != null)
			_groupPanel.Visible = false;
		RefreshShellChrome();
	}

	public void Select()
	{
		Selected = true;
		if (_shellPanel != null)
			_shellPanel.Visible = false;
		if (_groupPanel != null)
			_groupPanel.Visible = false;
		RefreshShellChrome();
	}

	/// <summary>
	/// Global rectangle of this cue's own row (excludes nested child shells).
	/// Used by box-select hit testing so expanded groups do not absorb every nested hit via parent bounds.
	/// </summary>
	/// <returns>Row bounds in global coordinates.</returns>
	public Rect2 GetRowGlobalRect()
	{
		float h = ShellColumnLayout.RowMinHeight;
		if (_rowHBox != null && IsInstanceValid(_rowHBox) && _rowHBox.Size.Y > 1f)
			h = _rowHBox.Size.Y;
		return new Rect2(GlobalPosition, new Vector2(Size.X, h));
	}

	/// <summary>
	/// Ensures the per-row StyleBox exists with locked metrics (no layout jump on state change).
	/// </summary>
	private void EnsurePanelStyle()
	{
		if (_panelStyle != null) return;
		_panelStyle = new StyleBoxFlat();
		ApplyPanelMetricsForDepth();
	}

	/// <summary>
	/// Root shells keep L/R content margins; nested shells use zero horizontal inset so their
	/// trailing Pre/Dur/Post/Follow columns line up with the root grid (margins would stack
	/// per nest level and shift those fields left).
	/// </summary>
	private void ApplyPanelMetricsForDepth()
	{
		if (_panelStyle == null) return;
		GlobalStyles.ApplyShellChromeMetrics(_panelStyle);

		int depth = ComputeNestDepth();
		if (depth > 0)
		{
			_panelStyle.ContentMarginLeft = 0;
			_panelStyle.ContentMarginRight = 0;
			// Nested selection still uses a left accent via colour strip; skip L/R border inset.
			_panelStyle.BorderWidthLeft = 0;
			_panelStyle.BorderWidthRight = 0;
		}
	}

	/// <summary>
	/// Rebuilds shell body colour: zebra base × desaturated cue wash × hover/selection.
	/// Border width/margins stay fixed per depth (root vs nested).
	/// </summary>
	public void RefreshShellChrome()
	{
		EnsurePanelStyle();
		ApplyPanelMetricsForDepth();

		Color cueColor = _cue != null ? _cue.Color : Colors.Black;
		bool even = (_zebraIndex % 2) == 0;
		// Hover wash is suppressed while reordering (drop indicator is the visual guide).
		bool showHover = _hovered && !IsReorderActive();
		var state = Selected
			? GlobalStyles.ShellChromeState.Selected
			: (showHover ? GlobalStyles.ShellChromeState.Hover : GlobalStyles.ShellChromeState.Normal);

		_panelStyle.BgColor = GlobalStyles.ShellBackgroundFor(cueColor, even, state);
		// Nested shells: no side border (avoids extra inset); root keeps state border colour.
		_panelStyle.BorderColor = ComputeNestDepth() > 0
			? new Color(0, 0, 0, 0)
			: GlobalStyles.ShellBorderFor(state);
		AddThemeStyleboxOverride("panel", _panelStyle);
		// Hatch overlay is drawn in _Draw; keep it in sync with chrome refreshes.
		QueueRedraw();
	}

	/// <summary>
	/// Draws disarmed hatch behind shell text: one diagonal direction when disarmed,
	/// both directions (X hatch) when also skip-if-disarmed. Limited to the main row
	/// (not nested child shells under an expanded group).
	/// </summary>
	public override void _Draw()
	{
		base._Draw();
		if (_cue == null || _cue.Armed)
			return;

		float rowH = ShellColumnLayout.RowMinHeight;
		if (_rowHBox != null && _rowHBox.Size.Y > 1f)
			rowH = _rowHBox.Size.Y;

		float width = Size.X;
		if (width < 2f || rowH < 2f)
			return;

		// Subtle lines so text stays readable over the hatch.
		var lineColor = new Color(0.85f, 0.9f, 0.95f, 0.18f);
		const float lineWidth = 1.25f;
		const float spacing = 10f;

		// Primary diagonal set (top-left → bottom-right).
		DrawDiagonalHatch(width, rowH, spacing, lineColor, lineWidth, forward: true);

		// Second set completes X hatch when skip-if-disarmed is enabled.
		if (_cue.SkipIfDisarmed)
			DrawDiagonalHatch(width, rowH, spacing, lineColor, lineWidth, forward: false);
	}

	/// <summary>
	/// Draws parallel diagonal lines across a row-sized rectangle at the top of this shell.
	/// </summary>
	/// <param name="width">Row width in local coordinates.</param>
	/// <param name="height">Row height in local coordinates.</param>
	/// <param name="spacing">Distance between parallel hatch lines.</param>
	/// <param name="color">Line colour.</param>
	/// <param name="lineWidth">Stroke width.</param>
	/// <param name="forward">True for \ direction; false for / direction.</param>
	private void DrawDiagonalHatch(float width, float height, float spacing, Color color, float lineWidth, bool forward)
	{
		// Offset range covers full rectangle with parallel diagonals.
		float start = -height;
		float end = width + height;
		for (float offset = start; offset <= end; offset += spacing)
		{
			Vector2 a;
			Vector2 b;
			if (forward)
			{
				// Line family: y - x = c  →  points (offset, 0) through (offset+height, height)
				a = new Vector2(offset, 0f);
				b = new Vector2(offset + height, height);
			}
			else
			{
				// Line family: y + x = c
				a = new Vector2(offset, 0f);
				b = new Vector2(offset - height, height);
			}

			if (!ClipLineToRect(ref a, ref b, width, height))
				continue;
			DrawLine(a, b, color, lineWidth, antialiased: true);
		}
	}

	/// <summary>
	/// Liang–Barsky style clip of a line segment to the [0,width]×[0,height] rect.
	/// </summary>
	/// <returns>False if the segment lies entirely outside the rect.</returns>
	private static bool ClipLineToRect(ref Vector2 a, ref Vector2 b, float width, float height)
	{
		float x0 = a.X, y0 = a.Y, x1 = b.X, y1 = b.Y;
		float dx = x1 - x0;
		float dy = y1 - y0;
		float t0 = 0f;
		float t1 = 1f;

		// p/q edges: left, right, bottom, top
		if (!ClipEdge(-dx, x0, ref t0, ref t1)) return false;
		if (!ClipEdge(dx, width - x0, ref t0, ref t1)) return false;
		if (!ClipEdge(-dy, y0, ref t0, ref t1)) return false;
		if (!ClipEdge(dy, height - y0, ref t0, ref t1)) return false;

		a = new Vector2(x0 + t0 * dx, y0 + t0 * dy);
		b = new Vector2(x0 + t1 * dx, y0 + t1 * dy);
		return true;
	}

	private static bool ClipEdge(float p, float q, ref float t0, ref float t1)
	{
		if (System.Math.Abs(p) < 1e-8f)
			return q >= 0f;
		float r = q / p;
		if (p < 0f)
		{
			if (r > t1) return false;
			if (r > t0) t0 = r;
		}
		else
		{
			if (r < t0) return false;
			if (r < t1) t1 = r;
		}
		return true;
	}

	// Re-ordering functions

	/// <summary>
	/// Starts reorder on left-press. Accepts the event so BaseButton does not latch pressed;
	/// mouse-up is owned by <see cref="CueReorder"/> via CueList._Input.
	/// </summary>
	private void OnDragBarGuiInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mb)
			return;
		if (mb.ButtonIndex != MouseButton.Left || !mb.Pressed)
			return;

		// Prevent BaseButton internal press-attempt (would stick if mouse-up is handled globally).
		AcceptEvent();
		_dragButton?.SetPressedNoSignal(false);

		if (IsCueEditingLocked())
			return;
		if (_globalData?.Cuelist == null)
			return;
		_globalData.Cuelist.StartReorder(this);
	}

	/// <summary>
	/// Clears any residual pressed/focus state on the reorder grabber after a drag session.
	/// </summary>
	public void ReleaseDragGrabber()
	{
		if (_dragButton == null || !IsInstanceValid(_dragButton))
			return;

		_dragButton.KeepPressedOutside = false;
		_dragButton.SetPressedNoSignal(false);
		_dragButton.ButtonPressed = false;
		if (_dragButton.HasFocus())
			_dragButton.ReleaseFocus();
	}
}
