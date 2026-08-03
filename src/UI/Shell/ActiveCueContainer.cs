// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

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
using Cue2.Services;
using Cue2.UI.Utilities;
using Godot;

namespace Cue2.UI.Shell;

public partial class ActiveCueContainer : PanelContainer
{
	private GlobalSignals _globalSignals;
	private GlobalData Gd;
	
	private static Dictionary<int, ICue> _activeCues = new Dictionary<int, ICue>();
	private static Dictionary<int, Node> _activeCueBars = new Dictionary<int, Node>();
	
	
	

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_globalSignals.CueGo += AddActiveCue;
		Gd = GetNode<Cue2.Services.GlobalData>("/root/GlobalData");
		
		GetNode<Button>("%ResumeAllButton").Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.ResumeAll));
		GetNode<Button>("%PauseAllButton").Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.PauseAll));
		GetNode<Button>("%StopAllButton").Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.StopAll));

		UiLocalizer.LocalizeTree(this);
		_globalSignals.LocaleChanged += OnLocaleChanged;
	}

	/// <inheritdoc />
	public override void _ExitTree()
	{
		if (_globalSignals != null)
		{
			_globalSignals.CueGo -= AddActiveCue;
			_globalSignals.LocaleChanged -= OnLocaleChanged;
		}
		base._ExitTree();
	}

	/// <summary>
	/// Re-localizes active-cue panel chrome when the UI language changes.
	/// </summary>
	/// <param name="localeCode">New locale code.</param>
	private void OnLocaleChanged(string localeCode)
	{
		if (!GodotObject.IsInstanceValid(this))
			return;
		UiLocalizer.LocalizeTree(this);
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
			
	}
	
	public void AddActiveCue(int playbackIndex, int cueId)
	{ //From global signal, emitted by shell_bar
		var cue = CueList.FetchCueFromId(cueId);
		_activeCues.Add(playbackIndex, cue);
		//LoadActiveCueBar(playbackIndex, cue);
		
	}

	public static void RemoveActiveCue(int playbackIndex)
	{
		_activeCues.Remove(playbackIndex);
		_activeCueBars[playbackIndex].CallDeferred("queue_free");
		_activeCueBars.Remove(playbackIndex);
	}

	
}
