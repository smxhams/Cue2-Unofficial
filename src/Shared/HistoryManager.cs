using System;
using System.Collections.Generic;
using System.Linq;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Godot;
using Godot.Collections;

namespace Cue2.Shared;

/// <summary>
/// Scoped undo/redo for document data changes (cues, cuelist structure, show settings)
/// and cuelist selection. Playback and app preferences are not tracked.
/// </summary>
/// <remarks>
/// Prefer the narrowest scope:
/// <list type="bullet">
/// <item><see cref="RecordCueChange"/> — single cue property/component edits (no display reload)</item>
/// <item><see cref="RecordCuelistChange"/> — create/delete/reorder/group (rebuilds shells only)</item>
/// <item><see cref="RecordSettingsChange"/> — settings slice only (omit Displays unless needed)</item>
/// <item><see cref="RecordSelectionChange"/> — shell selection / focus only</item>
/// </list>
/// </remarks>
public partial class HistoryManager : Node
{
	/// <summary>
	/// History entry scope controls what is captured and how restore runs.
	/// </summary>
	public enum HistoryScope
	{
		/// <summary>Single cue <see cref="Cue.GetData"/> snapshot.</summary>
		Cue = 0,
		/// <summary>Full cuelist (order + all cues), no settings.</summary>
		Cuelist = 1,
		/// <summary>Settings keys only (optionally filtered).</summary>
		Settings = 2,
		/// <summary>Selected cue ids + focused cue only (no document mutation).</summary>
		Selection = 3
	}

	private GlobalData _globalData;
	private GlobalSignals _globalSignals;

	private readonly List<HistoryEntry> _undoStack = new();
	private readonly List<HistoryEntry> _redoStack = new();
	private bool _isRestoring;

	/// <summary>
	/// Active continuous-edit session key (typing / drag / spin). Only merges while this is set
	/// and matches the incoming key. Discrete records (null key) or <see cref="EndCoalesceSession"/>
	/// close the session so the next edit of the same field becomes a new undo step.
	/// </summary>
	private string _activeCoalesceKey;

	/// <summary>
	/// Raised when undo/redo availability or stack contents change (including new records).
	/// </summary>
	[Signal]
	public delegate void HistoryChangedEventHandler();

	/// <summary>
	/// Raised only after a successful Undo or Redo has finished applying restored state.
	/// Argument is <see cref="HistoryScope"/> as int so listeners can refresh only when relevant
	/// (e.g. settings panels should ignore cue undos so SpinBoxes are not re-synced spuriously).
	/// </summary>
	[Signal]
	public delegate void HistoryRestoredEventHandler(int scope);

	/// <summary>
	/// Whether there is at least one undo step available.
	/// </summary>
	public bool CanUndo => _undoStack.Count > 0;

	/// <summary>
	/// Whether there is at least one redo step available.
	/// </summary>
	public bool CanRedo => _redoStack.Count > 0;

	/// <summary>
	/// Maximum undo steps, from user preferences.
	/// </summary>
	public int MaxDepth
	{
		get
		{
			var depth = _globalData?.UserDataManager?.UndoDepth ?? UserDataManager.DefaultUndoDepth;
			return Math.Clamp(depth, UserDataManager.MinUndoDepth, UserDataManager.MaxUndoDepth);
		}
	}

	/// <summary>
	/// True while a restore is in progress (suppresses nested recording).
	/// </summary>
	public bool IsRestoring => _isRestoring;

	public override void _Ready()
	{
		_globalData = GetParent() as GlobalData ?? GetNode<GlobalData>("/root/GlobalData");
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

		_globalSignals.Undo += Undo;
		_globalSignals.Redo += Redo;
		_globalSignals.NewSession += Clear;

		GD.Print("HistoryManager:_Ready - Initialized (scoped history).");
	}

	public override void _ExitTree()
	{
		if (_globalSignals != null)
		{
			_globalSignals.Undo -= Undo;
			_globalSignals.Redo -= Redo;
			_globalSignals.NewSession -= Clear;
		}
	}

	/// <summary>
	/// Records a single-cue checkpoint before a property or component mutation.
	/// </summary>
	/// <param name="cueId">Cue being edited.</param>
	/// <param name="description">Human-readable description.</param>
	/// <param name="coalesceKey">
	/// Optional key for a continuous edit session (typing, drag, spin). While the same session
	/// is open, further records with this key do not push steps. Pass null for discrete commits
	/// so each change is one undo step. Call <see cref="EndCoalesceSession"/> when the continuous
	/// interaction ends (focus exit / mouse up).
	/// </param>
	public void RecordCueChange(int cueId, string description, string coalesceKey = null)
	{
		if (_isRestoring) return;
		if (_globalData?.Cuelist == null) return;

		var cue = CueList.FetchCueFromId(cueId);
		if (cue == null)
		{
			GD.PrintErr($"HistoryManager:RecordCueChange - Cue {cueId} not found; skipping.");
			return;
		}

		if (ShouldCoalesce(coalesceKey))
			return;

		try
		{
			PushUndo(CaptureCueEntry(cueId, description ?? "Edit cue", coalesceKey), coalesceKey);
		}
		catch (Exception ex)
		{
			LogRecordFailure(ex);
		}
	}

	/// <summary>
	/// Records a full cuelist checkpoint before structural mutations (create/delete/reorder/group).
	/// Does not capture or restore show settings / displays.
	/// </summary>
	/// <param name="description">Human-readable description.</param>
	/// <param name="coalesceKey">Optional continuous-session key; usually null for structural ops.</param>
	public void RecordCuelistChange(string description, string coalesceKey = null)
	{
		if (_isRestoring) return;
		if (_globalData?.Cuelist == null) return;
		if (ShouldCoalesce(coalesceKey)) return;

		try
		{
			PushUndo(CaptureCuelistEntry(description ?? "Edit cuelist"), coalesceKey);
		}
		catch (Exception ex)
		{
			LogRecordFailure(ex);
		}
	}

	/// <summary>
	/// Records a settings checkpoint. Pass <paramref name="keys"/> to capture only those keys
	/// (e.g. "StopFadeDuration") so restore will not reload displays or unrelated systems.
	/// When <paramref name="keys"/> is empty, captures the full settings dictionary.
	/// </summary>
	/// <param name="description">Human-readable description.</param>
	/// <param name="coalesceKey">Optional continuous-session key (e.g. spinning a value).</param>
	/// <param name="keys">Optional subset of settings keys to store.</param>
	public void RecordSettingsChange(string description, string coalesceKey = null, params string[] keys)
	{
		if (_isRestoring) return;
		if (_globalData?.Settings == null) return;
		if (ShouldCoalesce(coalesceKey)) return;

		try
		{
			var slice = _globalData.Settings.CaptureHistorySlice(keys);
			// Scalar general-settings slices are cloned without JSON (avoids empty/corrupt snapshots).
			var stored = IsScalarSettingsSlice(keys)
				? CloneScalarSettingsSlice(slice)
				: DeepCloneDictionary(slice);

			if (stored == null || stored.Count == 0)
			{
				GD.PrintErr($"HistoryManager:RecordSettingsChange - Empty settings slice for '{description}'; keys={string.Join(",", keys ?? System.Array.Empty<string>())}");
				return;
			}

			var (selectedIds, focusedId) = CaptureSelectionState();
			var entry = new HistoryEntry(
				description ?? "Edit settings",
				coalesceKey,
				HistoryScope.Settings,
				-1,
				null,
				null,
				stored,
				selectedIds,
				focusedId);

			PushUndo(entry, coalesceKey);
		}
		catch (Exception ex)
		{
			LogRecordFailure(ex);
		}
	}

	/// <summary>
	/// After an external settings import (e.g. loading a .c2settings file).
	/// Relinks cue components when audio patches were replaced and notifies UI panels
	/// via <see cref="HistoryRestored"/> (same path as undo/redo of settings).
	/// </summary>
	/// <remarks>
	/// Call after <see cref="RecordSettingsChange"/> + <see cref="Settings.ApplyPartialFromHistory"/>.
	/// Does not push history — the caller is responsible for the pre-change memento.
	/// </remarks>
	/// <param name="keys">Settings keys that were applied (used to decide whether to relink patches).</param>
	public void NotifySettingsApplied(params string[] keys)
	{
		if (keys != null && keys.Any(k => string.Equals(k, "AudioPatch", StringComparison.Ordinal)))
			RelinkAllCueComponents();

		EmitSignal(SignalName.HistoryChanged);
		EmitSignal(SignalName.HistoryRestored, (int)HistoryScope.Settings);
		GD.Print($"HistoryManager:NotifySettingsApplied - keys=[{string.Join(", ", keys ?? System.Array.Empty<string>())}]");
	}

	/// <summary>
	/// Ends a continuous edit session so the next change (even with the same field key)
	/// becomes a new undo step. Call on text focus exit, mouse-up after a drag, etc.
	/// </summary>
	/// <param name="coalesceKey">
	/// If set, only ends the session when it matches the active key.
	/// If null, ends any active continuous session.
	/// </param>
	public void EndCoalesceSession(string coalesceKey = null)
	{
		if (string.IsNullOrEmpty(_activeCoalesceKey))
			return;
		if (coalesceKey != null && _activeCoalesceKey != coalesceKey)
			return;

		_activeCoalesceKey = null;
	}

	/// <summary>
	/// Records a selection/focus checkpoint before a pure selection change (click, range, next/prev).
	/// Does not capture cue data or settings. Call before mutating <see cref="ShellSelection"/>.
	/// </summary>
	/// <param name="description">Human-readable description (e.g. "Select cue").</param>
	/// <param name="coalesceKey">Optional continuous-session key; usually null for discrete clicks.</param>
	public void RecordSelectionChange(string description, string coalesceKey = null)
	{
		if (_isRestoring) return;
		if (ShouldCoalesce(coalesceKey)) return;

		try
		{
			PushUndo(CaptureSelectionEntry(description ?? "Select cues", coalesceKey), coalesceKey);
		}
		catch (Exception ex)
		{
			LogRecordFailure(ex);
		}
	}

	/// <summary>
	/// Obsolete full-document capture. Prefer scoped APIs. Kept as cuelist+settings fallback.
	/// </summary>
	[Obsolete("Use RecordCueChange / RecordCuelistChange / RecordSettingsChange for scoped history.")]
	public void RecordState(string description, string coalesceKey = null)
	{
		// Structural default: cuelist only (avoids display flicker). Callers should migrate.
		RecordCuelistChange(description, coalesceKey);
	}

	/// <summary>
	/// Restores the previous scoped state from the undo stack.
	/// </summary>
	public void Undo()
	{
		if (!CanUndo || _isRestoring) return;

		HistoryEntry target = null;
		HistoryEntry redoEntry = null;
		bool popped = false;
		try
		{
			_activeCoalesceKey = null;
			target = _undoStack[^1];

			// Capture redo state BEFORE popping, so a capture failure cannot discard the undo entry.
			redoEntry = CaptureCurrentForScope(target);

			_undoStack.RemoveAt(_undoStack.Count - 1);
			popped = true;
			_redoStack.Add(redoEntry);

			RestoreEntry(target);

			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
				$"Undo: {target.Description}", (int)LogType.Info);
			GD.Print($"HistoryManager:Undo - '{target.Description}' scope={target.Scope} (undo={_undoStack.Count}, redo={_redoStack.Count})");
			EmitSignal(SignalName.HistoryChanged);
			EmitSignal(SignalName.HistoryRestored, (int)target.Scope);
		}
		catch (Exception ex)
		{
			// Restore stack integrity if we already moved the entry.
			if (popped && target != null)
			{
				if (_redoStack.Count > 0 && ReferenceEquals(_redoStack[^1], redoEntry))
					_redoStack.RemoveAt(_redoStack.Count - 1);
				_undoStack.Add(target);
			}

			GD.PrintErr($"HistoryManager:Undo - Failed: {ex.Message}\n{ex.StackTrace}");
			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
				$"Undo failed: {ex.Message}", (int)LogType.Error);
			_isRestoring = false;
			EmitSignal(SignalName.HistoryChanged);
		}
	}

	/// <summary>
	/// Re-applies a previously undone scoped state.
	/// </summary>
	public void Redo()
	{
		if (!CanRedo || _isRestoring) return;

		HistoryEntry target = null;
		HistoryEntry undoEntry = null;
		bool popped = false;
		try
		{
			_activeCoalesceKey = null;
			target = _redoStack[^1];

			undoEntry = CaptureCurrentForScope(target);

			_redoStack.RemoveAt(_redoStack.Count - 1);
			popped = true;
			_undoStack.Add(undoEntry);
			TrimToMaxDepth();

			RestoreEntry(target);

			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
				$"Redo: {target.Description}", (int)LogType.Info);
			GD.Print($"HistoryManager:Redo - '{target.Description}' scope={target.Scope} (undo={_undoStack.Count}, redo={_redoStack.Count})");
			EmitSignal(SignalName.HistoryChanged);
			EmitSignal(SignalName.HistoryRestored, (int)target.Scope);
		}
		catch (Exception ex)
		{
			if (popped && target != null)
			{
				if (_undoStack.Count > 0 && ReferenceEquals(_undoStack[^1], undoEntry))
					_undoStack.RemoveAt(_undoStack.Count - 1);
				_redoStack.Add(target);
			}

			GD.PrintErr($"HistoryManager:Redo - Failed: {ex.Message}\n{ex.StackTrace}");
			_globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
				$"Redo failed: {ex.Message}", (int)LogType.Error);
			_isRestoring = false;
			EmitSignal(SignalName.HistoryChanged);
		}
	}

	/// <summary>
	/// Clears both undo and redo stacks (e.g. on New Session or Open Session).
	/// </summary>
	public void Clear()
	{
		_activeCoalesceKey = null;
		if (_undoStack.Count == 0 && _redoStack.Count == 0) return;

		_undoStack.Clear();
		_redoStack.Clear();
		EmitSignal(SignalName.HistoryChanged);
		GD.Print("HistoryManager:Clear - History cleared.");
	}

	/// <summary>
	/// Trims the undo stack to the current <see cref="MaxDepth"/> and clears redo when shrinking.
	/// </summary>
	public void TrimToMaxDepth()
	{
		int max = MaxDepth;
		bool changed = false;

		while (_undoStack.Count > max)
		{
			_undoStack.RemoveAt(0);
			changed = true;
		}

		if (changed && _redoStack.Count > 0)
		{
			_redoStack.Clear();
			changed = true;
		}

		if (changed)
			EmitSignal(SignalName.HistoryChanged);
	}

	/// <summary>
	/// True only while a continuous session is open for this key (typing/drag/spin).
	/// Discrete records pass null and never coalesce.
	/// </summary>
	private bool ShouldCoalesce(string coalesceKey)
	{
		return !string.IsNullOrEmpty(coalesceKey)
		       && !string.IsNullOrEmpty(_activeCoalesceKey)
		       && _activeCoalesceKey == coalesceKey;
	}

	private void PushUndo(HistoryEntry entry, string coalesceKey)
	{
		_undoStack.Add(entry);
		_redoStack.Clear();
		// Open or close continuous session: null key = discrete commit, seals any prior session.
		_activeCoalesceKey = string.IsNullOrEmpty(coalesceKey) ? null : coalesceKey;
		TrimToMaxDepth();
		EmitSignal(SignalName.HistoryChanged);
		GD.Print($"HistoryManager:Push - '{entry.Description}' scope={entry.Scope} (undo={_undoStack.Count})");
	}

	private void LogRecordFailure(Exception ex)
	{
		GD.PrintErr($"HistoryManager:Record - Failed: {ex.Message}");
		_globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
			$"HistoryManager: Failed to record history: {ex.Message}", (int)LogType.Error);
	}

	/// <summary>
	/// Captures live state matching the scope of an existing history entry (for the opposite stack).
	/// </summary>
	private HistoryEntry CaptureCurrentForScope(HistoryEntry template)
	{
		return template.Scope switch
		{
			HistoryScope.Cue => CaptureCueEntry(template.CueId, template.Description, null),
			HistoryScope.Cuelist => CaptureCuelistEntry(template.Description),
			HistoryScope.Settings => CaptureSettingsEntry(
				template.Description,
				// Capture the same key set that was stored on the template (never full GetData).
				CaptureSettingsMatchingKeys(template.SettingsData)),
			HistoryScope.Selection => CaptureSelectionEntry(template.Description, null),
			_ => throw new InvalidOperationException($"Unknown history scope: {template.Scope}")
		};
	}

	private HistoryEntry CaptureCueEntry(int cueId, string description, string coalesceKey)
	{
		var cue = CueList.FetchCueFromId(cueId);
		if (cue == null)
			throw new InvalidOperationException($"Cannot capture history for missing cue {cueId}");

		var data = DeepCloneDictionary(cue.GetData());
		// Waveform peak caches are large and regenerate on demand; omit from history snapshots
		// so JSON cloning stays reliable and memory stays bounded.
		StripWaveformPayloads(data);

		var (selectedIds, focusedId) = CaptureSelectionState();
		return new HistoryEntry(
			description,
			coalesceKey,
			HistoryScope.Cue,
			cueId,
			data,
			null,
			null,
			selectedIds,
			focusedId);
	}

	private HistoryEntry CaptureCuelistEntry(string description)
	{
		var cues = DeepCloneDictionary(_globalData.Cuelist.GetData());
		if (cues.TryGetValue("Cues", out var cuesVariant) && cuesVariant.VariantType == Variant.Type.Dictionary)
		{
			foreach (var kv in (Dictionary)cuesVariant)
			{
				if (kv.Value.VariantType == Variant.Type.Dictionary)
					StripWaveformPayloads(kv.Value.AsGodotDictionary());
			}
		}

		var (selectedIds, focusedId) = CaptureSelectionState();
		return new HistoryEntry(
			description,
			null,
			HistoryScope.Cuelist,
			-1,
			null,
			cues,
			null,
			selectedIds,
			focusedId);
	}

	private HistoryEntry CaptureSettingsEntry(string description, Dictionary settingsData)
	{
		var (selectedIds, focusedId) = CaptureSelectionState();
		return new HistoryEntry(
			description,
			null,
			HistoryScope.Settings,
			-1,
			null,
			null,
			settingsData,
			selectedIds,
			focusedId);
	}

	private HistoryEntry CaptureSelectionEntry(string description, string coalesceKey)
	{
		var (selectedIds, focusedId) = CaptureSelectionState();
		return new HistoryEntry(
			description,
			coalesceKey,
			HistoryScope.Selection,
			-1,
			null,
			null,
			null,
			selectedIds,
			focusedId);
	}

	/// <summary>
	/// Snapshots current shell multi-selection (ordered) and focused cue id for history restore.
	/// </summary>
	private (int[] SelectedCueIds, int FocusedCueId) CaptureSelectionState()
	{
		int focusedId = _globalData?.FocusedCue ?? -1;
		var selected = ShellSelection.SelectedCues;
		if (selected == null || selected.Count == 0)
			return (System.Array.Empty<int>(), focusedId);

		// Preserve list order — last entry is "most recently selected" (paste/duplicate anchor).
		var ids = new int[selected.Count];
		int n = 0;
		foreach (var cue in selected)
		{
			if (cue == null) continue;
			ids[n++] = cue.Id;
		}
		if (n == ids.Length)
			return (ids, focusedId);
		if (n == 0)
			return (System.Array.Empty<int>(), focusedId);
		var trimmed = new int[n];
		System.Array.Copy(ids, trimmed, n);
		return (trimmed, focusedId);
	}

	/// <summary>
	/// Restores shell selection and focus from a history memento after model apply.
	/// Missing cue ids (deleted / not yet recreated) are skipped.
	/// </summary>
	private void RestoreSelection(HistoryEntry entry)
	{
		if (entry == null) return;

		// Drop current selection chrome (shells may be freed after cuelist rebuild).
		foreach (var cue in ShellSelection.SelectedCues.ToList())
		{
			if (cue?.ShellBar != null && IsInstanceValid(cue.ShellBar))
				cue.ShellBar.Deselect();
		}
		ShellSelection.SelectedCues.Clear();

		if (entry.SelectedCueIds != null)
		{
			foreach (int id in entry.SelectedCueIds)
			{
				var cue = CueList.FetchCueFromId(id);
				if (cue?.ShellBar == null || !IsInstanceValid(cue.ShellBar))
					continue;
				cue.ShellBar.Select();
				ShellSelection.SelectedCues.Add(cue);
			}
		}

		int focusId = entry.FocusedCueId;
		if (focusId >= 0 && CueList.FetchCueFromId(focusId) == null)
			focusId = -1;
		if (focusId < 0 && ShellSelection.SelectedCues.Count > 0)
			focusId = ShellSelection.SelectedCues[^1].Id;

		// Always publish so FocusedCue / inspectors match selection (including empty).
		_globalSignals?.EmitSignal(nameof(GlobalSignals.ShellFocused), focusId);
	}

	/// <summary>
	/// Clears embedded WaveformData from a cue snapshot dictionary (in place).
	/// </summary>
	private static void StripWaveformPayloads(Dictionary cueData)
	{
		if (cueData == null || !cueData.ContainsKey("Components")) return;
		var comps = cueData["Components"].AsGodotArray();
		foreach (var compVar in comps)
		{
			if (compVar.VariantType != Variant.Type.Dictionary) continue;
			var comp = compVar.AsGodotDictionary();
			if (comp.ContainsKey("WaveformData"))
				comp["WaveformData"] = System.Array.Empty<byte>();
		}
	}

	/// <summary>
	/// Captures current settings values for exactly the keys present in a previous settings snapshot.
	/// Never falls back to full GetData() (that can throw / hang and used to discard undo entries).
	/// </summary>
	private Dictionary CaptureSettingsMatchingKeys(Dictionary previousSlice)
	{
		if (previousSlice == null || previousSlice.Count == 0)
			return new Dictionary();

		var keys = previousSlice.Keys.Select(k => k.AsString()).Where(k => !string.IsNullOrEmpty(k)).ToArray();
		if (keys.Length == 0)
			return new Dictionary();

		var slice = _globalData.Settings.CaptureHistorySlice(keys);
		return IsScalarSettingsSlice(keys)
			? CloneScalarSettingsSlice(slice)
			: DeepCloneDictionary(slice);
	}

	private static readonly HashSet<string> ScalarSettingsKeys = new(StringComparer.Ordinal)
	{
		"UiScale",
		"GoScale",
		"WaveformResolution",
		"StopFadeDuration",
		"MediaBackupEnabled",
		"MultiEditEnabled",
		"SelectNewCues",
		"ShowTimelineWaveforms",
		"OutputBackgroundColor",
		"VideoQualityMode",
		"VideoPreviewQuality",
		"OutputVSyncMode",
		"AudioLatencyMode",
		"AudioDeclickMs",
		"AudioMasterVolume"
	};

	private static bool IsScalarSettingsSlice(string[] keys)
	{
		if (keys == null || keys.Length == 0) return false;
		foreach (var key in keys)
		{
			if (string.IsNullOrEmpty(key) || !ScalarSettingsKeys.Contains(key))
				return false;
		}
		return true;
	}

	private static bool NeedsDeepSettingsClone(Dictionary settingsData)
	{
		if (settingsData == null) return false;
		foreach (var k in settingsData.Keys)
		{
			string key = k.AsString();
			if (string.IsNullOrEmpty(key) || !ScalarSettingsKeys.Contains(key))
				return true;
		}
		return false;
	}

	/// <summary>
	/// Copies primitive settings values without JSON (stable for floats/bools/ints).
	/// </summary>
	private static Dictionary CloneScalarSettingsSlice(Dictionary source)
	{
		var clone = new Dictionary();
		if (source == null) return clone;
		foreach (var kvp in source)
		{
			string key = kvp.Key.AsString();
			if (string.IsNullOrEmpty(key)) continue;
			// Re-box as plain managed values so later restore does not depend on Variant lifetime.
			var v = kvp.Value;
			clone[key] = v.VariantType switch
			{
				Variant.Type.Float => v.AsSingle(),
				Variant.Type.Int => v.AsInt32(),
				Variant.Type.Bool => v.AsBool(),
				Variant.Type.String => v.AsString(),
				_ => v
			};
		}
		return clone;
	}

	private void RestoreEntry(HistoryEntry entry)
	{
		_isRestoring = true;
		try
		{
			switch (entry.Scope)
			{
				case HistoryScope.Cue:
					_globalData.Cuelist.ApplyCueHistorySnapshot(entry.CueId, DeepCloneDictionary(entry.CueData));
					break;

				case HistoryScope.Cuelist:
					_globalData.Cuelist.ApplyCuelistHistorySnapshot(DeepCloneDictionary(entry.CuesData));
					break;

				case HistoryScope.Settings:
					// Scalar slices are already plain dictionaries; deep-clone nested snapshots (InputMap, patches…).
					var settingsData = entry.SettingsData;
					if (settingsData != null && NeedsDeepSettingsClone(settingsData))
						settingsData = DeepCloneDictionary(settingsData);
					else if (settingsData != null)
						settingsData = CloneScalarSettingsSlice(settingsData);

					_globalData.Settings.ApplyPartialFromHistory(settingsData);
					// Patches are freed and recreated — re-link cue components and refresh inspectors
					// that list patch names / hold live AudioOutputPatch references.
					if (entry.SettingsData != null && entry.SettingsData.ContainsKey("AudioPatch"))
						RelinkAllCueComponents();
					// Displays / AudioPatch inspector refresh happens via RestoreSelection → ShellFocused.
					break;

				case HistoryScope.Selection:
					// Selection-only memento — no document apply.
					break;
			}

			// Always re-apply selection last so cuelist rebuilds have live shells to select,
			// and so property undos put the user back on the cues they had selected.
			// Selection-scope undos only run this step.
			RestoreSelection(entry);
		}
		finally
		{
			_isRestoring = false;
		}
	}

	private void RelinkAllCueComponents()
	{
		if (_globalData?.Cuelist == null) return;
		foreach (var cue in CueList.CueIndex.Values)
			_globalData.Cuelist.RelinkCueComponents(cue);
	}

	/// <summary>
	/// Deep-clones a Godot dictionary via JSON round-trip so history never aliases live models.
	/// </summary>
	private static Dictionary DeepCloneDictionary(Dictionary source)
	{
		if (source == null) return new Dictionary();

		string json = Json.Stringify(source);
		using var parser = new Json();
		var err = parser.Parse(json);
		if (err != Error.Ok)
			throw new InvalidOperationException($"History deep-clone JSON parse failed: {err}");

		return parser.Data.AsGodotDictionary();
	}

	/// <summary>
	/// One scoped memento entry on the undo/redo stack.
	/// </summary>
	private sealed class HistoryEntry
	{
		public string Description { get; }
		public string CoalesceKey { get; }
		public HistoryScope Scope { get; }
		public int CueId { get; }
		public Dictionary CueData { get; }
		public Dictionary CuesData { get; }
		public Dictionary SettingsData { get; }

		/// <summary>Ordered selected cue ids at capture time (last = most recent selection).</summary>
		public int[] SelectedCueIds { get; }

		/// <summary>Focused cue id at capture time, or -1.</summary>
		public int FocusedCueId { get; }

		public HistoryEntry(
			string description,
			string coalesceKey,
			HistoryScope scope,
			int cueId,
			Dictionary cueData,
			Dictionary cuesData,
			Dictionary settingsData,
			int[] selectedCueIds,
			int focusedCueId)
		{
			Description = description ?? "Edit";
			CoalesceKey = coalesceKey;
			Scope = scope;
			CueId = cueId;
			CueData = cueData;
			CuesData = cuesData;
			SettingsData = settingsData;
			SelectedCueIds = selectedCueIds ?? System.Array.Empty<int>();
			FocusedCueId = focusedCueId;
		}
	}
}
