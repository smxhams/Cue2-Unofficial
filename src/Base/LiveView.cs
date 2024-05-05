using Godot;
using System;



public partial class LiveView : PanelContainer
{
	private GlobalSignals _globalSignals;
	public GlobalData _gd;

	public GlobalMediaPlayerManager mediaManager;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		_globalSignals.CueGo += AddLiveCue;
		_gd = GetNode<GlobalData>("/root/GlobalData");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		foreach (int cue in _gd.liveCues)
		{
			ProgressBar progressBar = GetNode<ProgressBar>("VBoxContainer2/HBoxContainer/ProgressBar");
			float progress = _gd.mediaManager.GetProgress(cue);
			progressBar.Value = progress;
		}
	}
	
	private void AddLiveCue(int @cueID)
	{ //From global signal, emitted by shell_bar
		GD.Print("New Live Cue: " + @cueID);
	}

	private void _on_h_slider_value_changed(float @value)
	{
		foreach (int cue in _gd.liveCues)
		{
			bool success = _gd.mediaManager.SetProgress(cue, @value);		
		}	

	}
}
