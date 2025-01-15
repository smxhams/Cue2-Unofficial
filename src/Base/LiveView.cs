using Godot;
using System;
using Cue2.Shared;


public partial class LiveView : PanelContainer
{
	private GlobalSignals _globalSignals;
	public Cue2.Shared.GlobalData Gd;
	

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_globalSignals.CueGo += AddLiveCue;
		Gd = GetNode<Cue2.Shared.GlobalData>("/root/GlobalData");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		foreach (int cue in Gd.LiveCues)
		{
			ProgressBar progressBar = GetNode<ProgressBar>("VBoxContainer2/HBoxContainer/ProgressBar");
			float progress = Gd.Playback.GetProgress(cue);
			progressBar.Value = progress;
		}
	}
	
	private void AddLiveCue(int cueId)
	{ //From global signal, emitted by shell_bar
		GD.Print("New Live Cue: " + cueId);
	}

	private void _on_h_slider_value_changed(float @value)
	{
		foreach (int cue in Gd.LiveCues)
		{
			bool success = Gd.Playback.SetProgress(cue, @value);		
		}	

	}
}
