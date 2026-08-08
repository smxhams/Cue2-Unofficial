// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Godot;
using Godot.Collections;

using Cue2.Domain.Connections;
using Cue2.Domain.Cues;
using Cue2.Domain.Library;
using Cue2.UI.Shell;
using Cue2.Services;
using Cue2.UI.Popups;
using Cue2.UI.Utilities;

// This script is attached to the cuelist in main UI
// Originator
namespace Cue2.Domain.Cuelist;

/// <summary>
/// Manages the main cue list UI, including creation, removal, drag-and-drop reordering
/// (with support for nesting/grouping), box multi-select, and save/load of cue hierarchy and order.
/// Partial: ResetCuelist, GetData, LoadData, StructureCuelist
/// </summary>
public partial class CueList
{
	/// <summary>
	/// Serializes the entire cuelist (cues + top-level order) for session save.
	/// </summary>
	/// <returns>Dictionary containing "Cues" and "CueOrder".</returns>
	public Dictionary GetData()
	{
		var saveTable = new Dictionary();
		var cues = new Dictionary();
		var cueOrder = GetCueOrder();
		saveTable.Add("CueOrder", cueOrder);
		foreach (var cue in CueIndex.Values)
		{
			var cueData = cue.GetData();
			
			cues.Add(cue.Id, cueData);
		}
		saveTable.Add("Cues", cues);
		return saveTable;
	}

	/// <summary>
	/// Returns a position-to-cueId map for the top-level cues only (used for save/load order).
	/// Child ordering is maintained via each Cue's ChildCues list.
	/// </summary>
	public Godot.Collections.Dictionary<int, int> GetCueOrder()
	{
		var cueOrder = new Godot.Collections.Dictionary<int, int>();
		for (int i = 0; i < _cueContainer.GetChildren().Count; i++)
		{
			var cueId = _cueContainer.GetChild(i).Get("CueId");
			cueOrder.Add(i, (int)cueId);
		}

		return cueOrder;
	}

	/// <summary>
	/// Loads cues from serialized data, creates shells, links components (patches, OSC, cue lights),
	/// then applies saved cue order via StructureCuelist.
	/// </summary>
	/// <param name="cueData">The "cues" sub-dictionary from session save.</param>
	public void LoadData(Dictionary cueData)
	{
		_globalSignals?.EmitSignal(nameof(GlobalSignals.Log), "CueList:LoadData - Loading Cues", (int)LogType.Info);

		if (cueData.TryGetValue("Cues", out var cues))
		{
			foreach (var cue in (Dictionary)cues)
			{
				var asDict = cue.Value.AsGodotDictionary();
				var cueDict = new Dictionary();
				foreach (var key in asDict.Keys)
				{
					var value = asDict[key];
					string keyStr = key.ToString();
						
					cueDict[keyStr] = value;
				} 
				Cue newCue = CreateCue(cueDict);
				
				// Patches are instantiated in load sequence seperate form cues. Once patchs and cues are created they
				// need to be linked.
				var newCueAudioComponent = newCue.GetAudioComponent();
				if (newCueAudioComponent != null)
				{
					var patches = _globalData.Settings.GetAudioOutputPatches();
					patches.TryGetValue(newCueAudioComponent.PatchId, out var patch);
					if (patch != null)
					{
						newCueAudioComponent.Patch = patch;
					}
				}
				
				var newCueVideoComponent = newCue.GetVideoComponent();
				if (newCueVideoComponent != null)
				{
					var patches = _globalData.Settings.GetAudioOutputPatches();
					patches.TryGetValue(newCueVideoComponent.PatchId, out var patch);
					if (patch != null)
					{
						newCueVideoComponent.Patch = patch;
					}
				}

				var cueLightComps = newCue.GetCueLightComponents();
				if (cueLightComps != null)
				{
					foreach (var cueLightComp in cueLightComps)
					{
						var cuelight = _globalData.CueLightManager.GetCueLight(cueLightComp.CueLightId);
						cueLightComp.CueLight = cuelight;
					}
				}
				
				var oscComponents = newCue.GetOscComponents();
				if (oscComponents != null)
				{
					foreach (var oscComp in oscComponents)
					{
						var oscConnection = OscConnections.GetCueOscConnection(oscComp.OscConnectionId);
						oscComp.OscConnection = oscConnection;
					}
				}


			}
		}

		if (cueData.TryGetValue("CueOrder", out var order))
		{
			var cueOrder = new Godot.Collections.Dictionary<int, int>();
			foreach (var cue in (Godot.Collections.Dictionary)order)
			{
				cueOrder.Add((int)cue.Key, (int)cue.Value);
				//GD.Print(cue.Key + " <-order cue -> " + (int)cue.Value);
			}
			StructureCuelist(cueOrder);	
		}
	}
	
	private void StructureCuelist(Godot.Collections.Dictionary<int, int> cueOrder)
	{
		// Key is child order, value is cueId
		foreach (Cue cue in CueIndex.Values)
		{
			// Assign child shellbars to parents (reparent only; ordering done after).
			// Intentionally silent: load/rebuild can reparent many cues and would spam the log.
			if (cue.ParentId != -1)
			{
				var parentShell = FetchCueFromId(cue.ParentId)?.ShellBar;
				var childContainer = parentShell?.GetNode<VBoxContainer>("%ShellChildContainer");
				if (childContainer != null && cue.ShellBar != null && cue.ShellBar.GetParent() != childContainer)
				{
					cue.ShellBar.Reparent(childContainer);
				}
			}
		}

		// Reorder children inside each group container to exactly match the persisted ChildCues order.
		foreach (Cue cue in CueIndex.Values)
		{
			if (cue.ChildCues.Count == 0) continue;
			var container = cue.ShellBar?.ShellChildContainer;
			if (container == null) continue;

			// Build lookup of current child shells by id for fast placement
			var shellsById = new System.Collections.Generic.Dictionary<int, ShellBar>();
			foreach (var ch in container.GetChildren())
			{
				if (ch is ShellBar s) shellsById[s.CueId] = s;
			}

			int pos = 0;
			foreach (int childId in cue.ChildCues)
			{
				if (shellsById.TryGetValue(childId, out var sb))
				{
					container.MoveChild(sb, pos);
					pos++;
				}
			}
		}

		// Order top-level shells
		for (int i = 0; i < cueOrder.Count; i++)
		{
			if (!CueIndex.TryGetValue(cueOrder[i], out var cue)) continue;
			var shell = cue.ShellBar as ShellBar;
			if (shell == null || cue.ParentId != -1) continue;
			// Use direct call; defer only if needed for init timing (kept simple here)
			_cueContainer.MoveChild(shell, i);
		}

		// Refresh collapse/expand buttons and visibility for any groups after load structure
		// (SetCue ran early; child shells have now been moved into containers)
		foreach (var cue in CueIndex.Values)
		{
			if (cue.ChildCues.Count > 0 && cue.ShellBar != null)
			{
				cue.ShellBar.RelationshipChanged();
			}
		}

		RefreshShellZebra();
	}
}
