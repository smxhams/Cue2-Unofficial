using Godot;
using System;
using System.Collections;

namespace Cue2.Shared;
public partial class GlobalStyles : Node
{

	public StyleBoxFlat HoverStyle = new StyleBoxFlat();
	private static StyleBoxFlat _focusedStyle = new StyleBoxFlat();
	public StyleBoxFlat NextStyle = new StyleBoxFlat();
	public StyleBoxFlat ActiveStyle = new StyleBoxFlat();
	public StyleBoxFlat DefaultStyle = new StyleBoxFlat();
	// Called when the node enters the scene tree for the first time.
	
	public override void _Ready()
	{
		// Default Style
		ActiveStyle.BorderWidthBottom = 0;
		ActiveStyle.BorderWidthRight = 0;
		ActiveStyle.BorderWidthLeft = 0;
		ActiveStyle.BorderWidthTop = 0;
		//activeStyle.BorderColor = new Color("#974B08");
		HoverStyle.BgColor = new Color((float)0.09,(float)0.09,(float)0.09,(float)0.6);


		// Hover Style
		HoverStyle.BorderWidthBottom = 2;
		HoverStyle.BorderWidthRight = 2;
		HoverStyle.BorderWidthLeft = 2;
		HoverStyle.BorderWidthTop = 2;
		HoverStyle.BorderColor = new Color("#002326");
		HoverStyle.BgColor = new Color((float)0.09,(float)0.09,(float)0.09,(float)0.6);

		// Next Style
		NextStyle.BorderWidthBottom = 2;
		NextStyle.BorderWidthRight = 2;
		NextStyle.BorderWidthLeft = 2;
		NextStyle.BorderWidthTop = 2;
		NextStyle.BorderColor = new Color("#06545C");
		NextStyle.BgColor = new Color((float)0.024,(float)0.329,(float)0.361,(float)0.2);

		// Selected Style
		_focusedStyle.BorderWidthBottom = 2;
		_focusedStyle.BorderWidthRight = 2;
		_focusedStyle.BorderWidthLeft = 2;
		_focusedStyle.BorderWidthTop = 2;
		_focusedStyle.BorderColor = new Color("#06545C");
		_focusedStyle.BgColor = new Color((float)0.09,(float)0.09,(float)0.09,(float)0.6);

		// Ative Style
		ActiveStyle.BorderWidthBottom = 2;
		ActiveStyle.BorderWidthRight = 2;
		ActiveStyle.BorderWidthLeft = 2;
		ActiveStyle.BorderWidthTop = 2;
		ActiveStyle.BorderColor = new Color("#974B08");
		ActiveStyle.BgColor = new Color((float)0.592,(float)0.294,(float)0.031,(float)0.6);

	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public static StyleBoxFlat FocusedStyle()
	{
		return _focusedStyle;
	}
}
