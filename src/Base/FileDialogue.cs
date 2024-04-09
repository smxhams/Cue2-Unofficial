using Godot;
using System;
using System.IO;

public partial class FileDialogue : FileDialog
{

	private GlobalSignals _globalSignals;
	public override void _Ready()
	{
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
	private void _on_file_selected(String @path)
	{
		string extention = Path.GetExtension(@path);

		if (String.IsNullOrEmpty(extention) | extention != ".wav")
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.ErrorLog), "File select failed: " + extention + " Invalid File Type");
			return;
		}
		else
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.FileSelected), @path);
		}

	}
}
