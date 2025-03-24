using Godot;
using System;

public partial class DeviceOutputChannelUI : Panel
{
    [Export]
    public int DeviceCId { get; set; }

    [Export]
    public int DeviceChannel { get; set; }
}
