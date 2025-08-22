using Godot;
using System;
using System.Collections.Generic;
using Cue2.Base.Classes;
using Cue2.Shared;

namespace Cue2.Base;

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
		Gd = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData");
		
		GetNode<Button>("%ResumeAllButton").Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.ResumeAll));
		GetNode<Button>("%PauseAllButton").Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.PauseAll));
		GetNode<Button>("%StopAllButton").Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.StopAll));
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
