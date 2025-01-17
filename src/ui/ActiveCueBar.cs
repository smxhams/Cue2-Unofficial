using Godot;
using System;
using Cue2.Base.Classes;

namespace Cue2.ui;
public partial class ActiveCueBar : Control
{
	[Export] private int PlaybackId { get; set; } = -1;
	private static bool _mouseInSlider;
	
	public override void _Process(double delta)
	{
		var pos = Playback.GetMediaPosition(PlaybackId);
		var length = Playback.GetMediaLength(PlaybackId);

		parseTime(length);
		GetNode<ProgressBar>("Panel/MarginContainer/ProgressBar").Value = pos * 100;
		
		// Time elapsed
		var elapsedLong = Convert.ToInt64(pos * length);
		var elapsed = parseTime(elapsedLong);
		GetNode<Label>("Panel/MarginContainer/ProgressBar/HBoxContainer/GridContainer/LabelTimeLeft").Text = elapsed;
		
		// Time remaining
		var remainingLong = length - elapsedLong;
		var remaining = parseTime(remainingLong);
		GetNode<Label>("Panel/MarginContainer/ProgressBar/HBoxContainer/GridContainer/LabelTimeRight").Text = "-" + remaining;

	}

	
	
	/*private void _input(InputEvent @event) {
		if (_mouseInSlider == true && Input.IsMouseButtonPressed(MouseButton.Left))
		{
			SetValue(GetNode<ProgressBar>("Panel/MarginContainer/ProgressBar"));
			GD.Print("Moving slider: " + PlaybackId);
		}
	}

	private void SetValue(ProgressBar slider)
	{
		var ratio = RatioInBody(slider);
		slider.Value = ratio * slider.MaxValue;
		Playback.SetMediaPosition(PlaybackId, (float)ratio);
	}
	
	private double RatioInBody(ProgressBar slider)
	{
		var posClicked = GetLocalMousePosition() - slider.GetRect().Position;
		double ratio = posClicked.X / slider.GetRect().Size.X;
		if (ratio > 1.0)
		{
			ratio = 1.0;
		}
		else if (ratio < 0.0)
		{
			ratio = 0.0;
		}

		return ratio;
	}*/

	private string parseTime(long time)
	{
		time = time * 10000;
		var timespan = TimeSpan.FromTicks(time);
		string strTime = timespan.ToString(@"hh\:mm\:ss");
		return strTime;
	}
}
