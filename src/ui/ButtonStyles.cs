using Cue2.Shared;
using Godot;

namespace Cue2.UI;

public partial class ButtonStyles : Button
{
	private StyleBoxFlat _hoverStyle = GlobalStyles.HoverStyle();
	private void _onMouseEntered()
	{
		//this.AddThemeStyleboxOverride("panel", _hoverStyle);
	}
	private void _onMouseExited()
	{
		//this.RemoveThemeStyleboxOverride("panel");
	}
}
