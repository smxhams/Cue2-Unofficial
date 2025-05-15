using Godot;
using System.IO;
using Cue2.Shared;

// This is a resource attached to:
// -OpenDialog: FileDialog (Found in Cue2Base scene)

namespace Cue2.Base;
public partial class OpenDialog : FileDialog
{
	private GlobalSignals _globalSignals;
	
	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		this.FileSelected += _onFileSelected;
	}
	
	private void _onFileSelected(string @path)
	{
		var extention = Path.GetExtension(@path);

		if (extention != ".c2")
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Not a valid extention: " + extention, 0);
			return;
		}
		_globalSignals.EmitSignal(nameof(GlobalSignals.OpenSelectedSession), @path);
	}

}
