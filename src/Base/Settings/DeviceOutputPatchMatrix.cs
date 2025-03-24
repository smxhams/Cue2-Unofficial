using Godot;
using System;


namespace Cue2.Base.Settings;
public partial class DeviceOutputPatchMatrix : Panel
{
    [Export]
    public string DeviceId { get; set; }
    [Export]
    public string DeviceName { get; set; }

    public override void _Ready()
    {
        if (HasNode("Label"))
        {

            GetNode<Label>("Label").Text = DeviceName;
        }
    }
}
