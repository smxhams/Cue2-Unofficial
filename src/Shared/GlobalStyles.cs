using Godot;
using System;
using System.Collections;

namespace Cue2.Shared;
public partial class GlobalStyles : Node
{
	private Theme _theme;

	private static StyleBoxFlat _hoverStyle = new StyleBoxFlat();
	private static StyleBoxFlat _focusedStyle = new StyleBoxFlat();
	/// <summary>Default shell-row panel (keeps content inset so borders stay visible).</summary>
	private static StyleBoxFlat _shellRowStyle = new StyleBoxFlat();
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
	public static Color Success = new Color("#9aff92");

	// List zebra base colours (also used for blank space under the cuelist).
	public static Color ZebraEven = new Color(0.145f, 0.155f, 0.165f, 1f);
	public static Color ZebraOdd = new Color(0.105f, 0.112f, 0.120f, 1f);

	/// <summary>Main title bar background (edit/show share the same bar; accent is the window border).</summary>
	public static Color TitleBarEditMode = new Color(0.059f, 0.059f, 0.059f, 1f);

	/// <summary>Legacy title-bar show tint (unused; show mode accents the window border).</summary>
	public static Color TitleBarShowMode = new Color(HighColor5.R, HighColor5.G, HighColor5.B, 1f);

	/// <summary>Title label colour (unchanged between edit and show mode).</summary>
	public static Color TitleBarLabelEditMode = new Color(0.75f, 0.78f, 0.80f, 1f);

	/// <summary>Legacy title label show accent (unused; show mode accents the window border).</summary>
	public static Color TitleBarLabelShowMode = HighColor1;

	/// <summary>
	/// Main window border in Edit Mode (matches Cue2Base.tscn Border StyleBox default).
	/// </summary>
	public static Color WindowBorderEditMode = new Color(0.00392157f, 0.231373f, 0.25098f, 1f);

	/// <summary>
	/// Main window border in Show Mode — warm HighColor5 accent for live-performance visibility.
	/// </summary>
	public static Color WindowBorderShowMode = HighColor5;

	// List zebra styles (legacy StyleBox accessors)
	private static StyleBoxFlat _evenRowStyle;
	private static StyleBoxFlat _oddRowStyle;
	
	// Fonts and text colors
	public static Color SoftFontColor = new Color("#45606b"); 
	public static Color DisabledColor = new Color("#1d1d1d");

	/// <summary>Shell chrome interaction state (metrics stay fixed; only colours change).</summary>
	public enum ShellChromeState
	{
		Normal,
		Hover,
		Selected
	}

	
	public override void _Ready()
	{
		_theme = GetTree().Root.GetTheme();
		
		SetProcess(false); // This class is only for statics - disable process
		
		// Shell rows: ALL states share identical border/margin metrics so hover/select
		// never resizes the row (avoids cuelist jitter). Only colors change.
		// Vertical margins stay 0 so adjacent ColorPanels meet with no gap.
		// Base styles kept for non-shell callers; ShellBar builds per-row mixed colours.
		ConfigureShellRowStyle(
			_shellRowStyle,
			borderColor: new Color(0, 0, 0, 0),
			bgColor: ZebraOdd);

		ConfigureShellRowStyle(
			_hoverStyle,
			borderColor: new Color(0.15f, 0.75f, 0.85f, 0.85f),
			bgColor: ZebraEven.Lightened(0.08f));

		ConfigureShellRowStyle(
			NextStyle,
			borderColor: new Color("#06545C"),
			bgColor: new Color(0.024f, 0.329f, 0.361f, 0.25f));

		// Strong selection outline (still 1px L/R so layout does not jump).
		ConfigureShellRowStyle(
			_focusedStyle,
			borderColor: new Color(0.25f, 0.92f, 1.0f, 1f),
			bgColor: new Color(0.08f, 0.42f, 0.48f, 0.55f));

		ConfigureShellRowStyle(
			ActiveStyle,
			borderColor: new Color("#974B08"),
			bgColor: new Color(0.592f, 0.294f, 0.031f, 0.55f));
		
		ConfigureShellRowStyle(
			_dangerStyle,
			borderColor: HighColor2,
			bgColor: new Color(HighColor5.R, HighColor5.G, HighColor5.B, 0.5f));
		
		
		// Zebra rows
		_evenRowStyle = new StyleBoxFlat();
		_evenRowStyle.BgColor = ZebraEven;
		
		_oddRowStyle = new StyleBoxFlat();
		_oddRowStyle.BgColor = ZebraOdd;
	}


	/// <summary>
	/// Fixed geometry for every shell-row state (must stay identical across hover/select).
	/// Left/right border only; top/bottom margins 0 so color strips stack flush.
	/// </summary>
	private const int ShellBorderSide = 1;
	private const int ShellContentPadX = 2;

	/// <summary>
	/// Builds a shell-row StyleBox. Metrics are locked; only colors differ per state.
	/// </summary>
	private static void ConfigureShellRowStyle(
		StyleBoxFlat style,
		Color borderColor,
		Color bgColor)
	{
		if (style == null) return;
		ApplyShellChromeMetrics(style);
		style.BorderColor = borderColor;
		style.BgColor = bgColor;
	}

	/// <summary>
	/// Applies locked shell metrics (call when mutating a per-row StyleBox's colours only).
	/// </summary>
	public static void ApplyShellChromeMetrics(StyleBoxFlat style)
	{
		if (style == null) return;

		style.BorderWidthLeft = ShellBorderSide;
		style.BorderWidthRight = ShellBorderSide;
		style.BorderWidthTop = 0;
		style.BorderWidthBottom = 0;

		style.ContentMarginLeft = ShellBorderSide + ShellContentPadX;
		style.ContentMarginRight = ShellBorderSide + ShellContentPadX;
		style.ContentMarginTop = 0;
		style.ContentMarginBottom = 0;

		style.CornerRadiusTopLeft = 0;
		style.CornerRadiusTopRight = 0;
		style.CornerRadiusBottomLeft = 0;
		style.CornerRadiusBottomRight = 0;
		style.AntiAliasing = false;
	}

	/// <summary>
	/// Desaturates <paramref name="cueColor"/> toward luminance (wash for shell backgrounds).
	/// </summary>
	/// <param name="cueColor">Cue accent colour.</param>
	/// <param name="amount">0 = original, 1 = full grey.</param>
	public static Color Desaturate(Color cueColor, float amount = 0.6f)
	{
		amount = Mathf.Clamp(amount, 0f, 1f);
		float lum = cueColor.R * 0.299f + cueColor.G * 0.587f + cueColor.B * 0.114f;
		return new Color(
			Mathf.Lerp(cueColor.R, lum, amount),
			Mathf.Lerp(cueColor.G, lum, amount),
			Mathf.Lerp(cueColor.B, lum, amount),
			1f);
	}

	/// <summary>
	/// Mixes zebra base with a desaturated cue wash for the shell body.
	/// </summary>
	public static Color MixZebraAndCue(Color zebra, Color cueColor)
	{
		// Soft, darkened, desaturated cue tint over zebra.
		Color tint = Desaturate(cueColor, 0.55f).Darkened(0.28f);
		return zebra.Lerp(tint, 0.40f);
	}

	/// <summary>
	/// Final shell background for zebra index + cue colour + interaction state.
	/// </summary>
	public static Color ShellBackgroundFor(Color cueColor, bool evenZebra, ShellChromeState state)
	{
		Color zebra = evenZebra ? ZebraEven : ZebraOdd;
		Color mixed = MixZebraAndCue(zebra, cueColor);

		return state switch
		{
			// Strong cyan lift so selection reads clearly over cue wash.
			ShellChromeState.Selected => mixed
				.Lerp(new Color(0.12f, 0.62f, 0.72f), 0.52f)
				.Lightened(0.06f),
			ShellChromeState.Hover => mixed
				.Lightened(0.07f)
				.Lerp(new Color(0.08f, 0.40f, 0.45f), 0.18f),
			_ => mixed
		};
	}

	/// <summary>
	/// Shell border colour for interaction state (width stays fixed at 1px L/R).
	/// </summary>
	public static Color ShellBorderFor(ShellChromeState state)
	{
		return state switch
		{
			ShellChromeState.Selected => new Color(0.35f, 0.95f, 1.0f, 1f),
			ShellChromeState.Hover => new Color(0.15f, 0.70f, 0.80f, 0.9f),
			_ => new Color(0f, 0f, 0f, 0f)
		};
	}

	/// <summary>Default unselected shell row chrome (stable content inset).</summary>
	public static StyleBoxFlat ShellRowStyle()
	{
		return _shellRowStyle;
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
