using Godot;
using System;
using System.Collections;

namespace Cue2.Shared;
public partial class GlobalStyles : Node
{
	private Theme _theme;

	private static StyleBoxFlat _hoverStyle = new StyleBoxFlat();
	private static StyleBoxFlat _focusedStyle = new StyleBoxFlat();
	public StyleBoxFlat NextStyle = new StyleBoxFlat();
	public StyleBoxFlat ActiveStyle = new StyleBoxFlat();
	public StyleBoxFlat DefaultStyle = new StyleBoxFlat();
	
	private static StyleBoxFlat _dangerStyle = new StyleBoxFlat();
	
	public static Color HighColor1 = new Color("#EB6F02");
	public static Color HighColor2 = new Color("#BA5E0B");
	public static Color HighColor3 = new Color("#974B08");
	public static Color HighColor4 = new Color("#693200");
	public static Color HighColor5 = new Color("#3E1D00");
	public static Color LowColor1 = new Color("#03838F");
	public static Color LowColor2 = new Color("#086871");
	public static Color LowColor3 = new Color("#06545C");
	public static Color LowColor4 = new Color("#013B40");
	public static Color LowColor5 = new Color("#002326");
	
	public static Color Danger = new Color("#ff806f"); 
	public static Color Warning = new Color("#ffb45d");

	// List zebra styles
	private static StyleBoxFlat _evenRowStyle;
	private static StyleBoxFlat _oddRowStyle;
	
	// Fonts and text colors
	public static Color SoftFontColor = new Color("#45606b"); 
	public static Color DisabledColor = new Color("#1d1d1d");

	
	public override void _Ready()
	{
		_theme = GetTree().Root.GetTheme();
		
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

		// Active Style
		ActiveStyle.BorderWidthBottom = 2;
		ActiveStyle.BorderWidthRight = 2;
		ActiveStyle.BorderWidthLeft = 2;
		ActiveStyle.BorderWidthTop = 2;
		ActiveStyle.BorderColor = new Color("#974B08");
		ActiveStyle.BgColor = new Color((float)0.592,(float)0.294,(float)0.031,(float)0.6);
		
		// Danger Style
		_dangerStyle.BorderWidthBottom = 2;
		_dangerStyle.BorderWidthRight = 2;
		_dangerStyle.BorderWidthLeft = 2;
		_dangerStyle.BorderWidthTop = 2;
		_dangerStyle.BorderColor = HighColor2;
		HighColor5.A = 0.5f;
		_dangerStyle.BgColor = HighColor5;
		
		
		// Zebra rows
		_evenRowStyle = new StyleBoxFlat();
		_evenRowStyle.BgColor = new Color(0.4f, 0.4f, 0.4f, 0.05f); // Soft lightening
		
		_oddRowStyle = new StyleBoxFlat();
		_oddRowStyle.BgColor = new Color(0f, 0f, 0f, 0.05f); // Soft darkening
		
		/*// Scan for existing Labels at startup
		ScanForLabels(GetTree().Root);
				
		// Listen for new nodes added dynamically
		GetTree().NodeAdded += OnNodeAdded;*/
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
	
	public static StyleBoxFlat EvenRowStyle()
	{
		return _evenRowStyle;
	}
	
	public static StyleBoxFlat OddRowStyle()
	{
		return _oddRowStyle;
	}
	
	
	/*/// <summary>
	/// Recursively scans the scene tree for Label nodes and applies the default font color override.
	/// </summary>
	/// <param name="node">The starting node to scan from.</param>
	private void ScanForLabels(Node node)
	{
		if (node is Label label)
		{
			ApplyLabelColor(label);
		}

		foreach (Node child in node.GetChildren())
		{
			ScanForLabels(child);
		}
	}
	
	
	/// <summary>
	/// Handles newly added nodes. If it's a Label, applies the default font color override.
	/// </summary>
	/// <param name="node">The newly added node.</param>
	private void OnNodeAdded(Node node)
	{
		if (node is Label label)
		{
			ApplyLabelColor(label);
		}
	}

	/// <summary>
	/// Applies the default font color override to a Label, with error handling.
	/// </summary>
	/// <param name="label">The Label to modify.</param>
	private void ApplyLabelColor(Label label)
	{
		try
		{
			if (label == null)
			{
				return;
			}

			label.AddThemeColorOverride("font_color", SoftFontColor);
			GD.Print($"GlobalStyles:ApplyLabelColor - Applied color {SoftFontColor} to Label '{label.Name}' in '{label.GetPath()}'.");  // Debug print with script/function prefix //!!!
		}
		catch (Exception ex)
		{
			return;
		}
	}*/
	
	
}
