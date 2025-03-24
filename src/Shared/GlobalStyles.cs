using Godot;
using System;
using System.Collections;

namespace Cue2.Shared;
public partial class GlobalStyles : Node
{

	private static StyleBoxFlat _hoverStyle = new StyleBoxFlat();
	private static StyleBoxFlat _focusedStyle = new StyleBoxFlat();
	public StyleBoxFlat NextStyle = new StyleBoxFlat();
	public StyleBoxFlat ActiveStyle = new StyleBoxFlat();
	public StyleBoxFlat DefaultStyle = new StyleBoxFlat();
	
	private static StyleBoxFlat _dangerStyle = new StyleBoxFlat();
	
	private static Color _highColor1 = new Color("#EB6F02");
	private static Color _highColor2 = new Color("#BA5E0B");
	private static Color _highColor3 = new Color("#974B08");
	private static Color _highColor4 = new Color("#693200");
	private static Color _highColor5 = new Color("#3E1D00");
	private static Color _lowColor1 = new Color("#03838F");
	private static Color _lowColor2 = new Color("#086871");
	private static Color _lowColor3 = new Color("#06545C");
	private static Color _lowColor4 = new Color("#013B40");
	private static Color _lowColor5 = new Color("#002326");

	
	public override void _Ready()
	{
		SetProcess(false); // This class is only for statics - disable process
		// Default Style
		ActiveStyle.BorderWidthBottom = 0;
		ActiveStyle.BorderWidthRight = 0;
		ActiveStyle.BorderWidthLeft = 0;
		ActiveStyle.BorderWidthTop = 0;
		//activeStyle.BorderColor = new Color("#974B08");
		//HoverStyle.BgColor = new Color((float)0.09,(float)0.09,(float)0.09,(float)0.6);


		// Hover Style
		_hoverStyle.BorderWidthBottom = 2;
		_hoverStyle.BorderWidthRight = 2;
		_hoverStyle.BorderWidthLeft = 2;
		_hoverStyle.BorderWidthTop = 2;
		_hoverStyle.BorderColor = new Color("#002326");
		_hoverStyle.BgColor = new Color((float)0.09,(float)0.09,(float)0.09,(float)0.6);

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
		
		//Danger Style
		_dangerStyle.BorderWidthBottom = 2;
		_dangerStyle.BorderWidthRight = 2;
		_dangerStyle.BorderWidthLeft = 2;
		_dangerStyle.BorderWidthTop = 2;
		_dangerStyle.BorderColor = _highColor2;
		_highColor5.A = 0.5f;
		_dangerStyle.BgColor = _highColor5;

	}


	public static StyleBoxFlat FocusedStyle()
	{
		return _focusedStyle;
	}

	public static StyleBoxFlat HoverStyle()
	{
		return _hoverStyle;
	}

	public static StyleBoxFlat DangerStyle()
	{
		return _dangerStyle;
	}
	
}
