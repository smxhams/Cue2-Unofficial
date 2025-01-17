using Godot;
using System;
using Cue2.Base.Classes;

namespace Cue2.UI;
public partial class ActiveCueProgressBar : ProgressBar
{
	[Export] private int PlaybackId { get; set; } = -1;
	private bool _mouseInSlider;

	private void _input(InputEvent @event) {
		if (_mouseInSlider == true && Input.IsMouseButtonPressed(MouseButton.Left))
		{
			SetValue(this);
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
	}
	private void mouse_entered()
	{
		_mouseInSlider = true;
		GD.Print("Mouse in slider");
	}
	
	private void mouse_extied()
	{
		_mouseInSlider = false;
		GD.Print("Mouse out of slider");
	}

}
