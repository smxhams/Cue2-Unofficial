using Godot;
using System;
using Cue2.Base.Classes;

namespace Cue2.ui;
public partial class ActiveCueBar : Control
{
	[Export] private int PlaybackId { get; set; } = -1;
	
	/*public override void _Process(double delta)
	{
		float pos = 0f;
		long length = 0;
		try
		{
			pos = Playback.GetMediaPosition(PlaybackId);
			length = Playback.GetMediaLength(PlaybackId);
		}
		catch (Exception e)
		{
			GD.Print("Can't get cue timing, possibly this script arrived earlier than cue loaded: ");
		}
		
		

		ParseTime(length);
		GetNode<ProgressBar>("Panel/MarginContainer/ProgressBar").Value = pos * 100;
		
		// Time elapsed
		var elapsedLong = Convert.ToInt64(pos * length);
		var elapsed = ParseTime(elapsedLong);
		GetNode<Label>("Panel/MarginContainer/ProgressBar/HBoxContainer/GridContainer/LabelTimeLeft").Text = elapsed;
		
		// Time remaining
		var remainingLong = length - elapsedLong;
		var remaining = ParseTime(remainingLong);
		GetNode<Label>("Panel/MarginContainer/ProgressBar/HBoxContainer/GridContainer/LabelTimeRight").Text = "-" + remaining;

	}

	private void _onActivePausePressed()
	{
		GD.Print("Pause: " + PlaybackId);
		Playback.Pause(PlaybackId);
	}

	private void _onActiveStopPressed()
	{
		GD.Print("Stop: " + PlaybackId);
		Playback.StopMedia(PlaybackId);
	}
	

	private string ParseTime(long time)
	{
		time = time * 10000;
		var timespan = TimeSpan.FromTicks(time);
		string strTime = timespan.ToString(@"hh\:mm\:ss");
		return strTime;
	}*/
}
