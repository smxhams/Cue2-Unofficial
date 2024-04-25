using Godot;
using System;
using System.Collections;

public partial class GlobalStyles : Node
{

	public StyleBoxFlat hoverStyle = new StyleBoxFlat();
	public StyleBoxFlat selectedStyle = new StyleBoxFlat();
	public StyleBoxFlat nextStyle = new StyleBoxFlat();
	public StyleBoxFlat activeStyle = new StyleBoxFlat();
	public StyleBoxFlat defaultStyle = new StyleBoxFlat();
	// Called when the node enters the scene tree for the first time.
	
	public override void _Ready()
	{
		// Default Style
		activeStyle.BorderWidthBottom = 0;
		activeStyle.BorderWidthRight = 0;
		activeStyle.BorderWidthLeft = 0;
		activeStyle.BorderWidthTop = 0;
		//activeStyle.BorderColor = new Color("#974B08");
		hoverStyle.BgColor = new Color((float)0.09,(float)0.09,(float)0.09,(float)0.6);


		// Hover Style
		hoverStyle.BorderWidthBottom = 2;
		hoverStyle.BorderWidthRight = 2;
		hoverStyle.BorderWidthLeft = 2;
		hoverStyle.BorderWidthTop = 2;
		hoverStyle.BorderColor = new Color("#002326");
		hoverStyle.BgColor = new Color((float)0.09,(float)0.09,(float)0.09,(float)0.6);

		// Next Style
		nextStyle.BorderWidthBottom = 2;
		nextStyle.BorderWidthRight = 2;
		nextStyle.BorderWidthLeft = 2;
		nextStyle.BorderWidthTop = 2;
		nextStyle.BorderColor = new Color("#06545C");
		nextStyle.BgColor = new Color((float)0.024,(float)0.329,(float)0.361,(float)0.2);

		// Selected Style
		selectedStyle.BorderWidthBottom = 2;
		selectedStyle.BorderWidthRight = 2;
		selectedStyle.BorderWidthLeft = 2;
		selectedStyle.BorderWidthTop = 2;
		selectedStyle.BorderColor = new Color("#06545C");
		selectedStyle.BgColor = new Color((float)0.09,(float)0.09,(float)0.09,(float)0.6);

		// Ative Style
		activeStyle.BorderWidthBottom = 2;
		activeStyle.BorderWidthRight = 2;
		activeStyle.BorderWidthLeft = 2;
		activeStyle.BorderWidthTop = 2;
		activeStyle.BorderColor = new Color("#974B08");
		activeStyle.BgColor = new Color((float)0.592,(float)0.294,(float)0.031,(float)0.6);

	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
