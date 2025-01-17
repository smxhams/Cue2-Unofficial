using Godot;
using System;
using Hardware.Info;

public partial class BottomRightContainer : HBoxContainer
{
	private static IHardwareInfo _hardwareInfo;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		try
		{
			_hardwareInfo = new HardwareInfo();
			_hardwareInfo.RefreshAll();
		}
		catch (Exception ex)
		{
			Console.WriteLine(ex);
		}
		GD.Print(_hardwareInfo.OperatingSystem);
		GD.Print(_hardwareInfo.MemoryStatus);
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
