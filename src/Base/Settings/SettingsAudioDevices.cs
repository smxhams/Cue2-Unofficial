using System.Collections.Generic;
using System.Linq;
using Cue2.Base.Classes;
using Cue2.Base.Classes.Devices;
using Cue2.Shared;
using Godot;
using LibVLCSharp.Shared;

namespace Cue2.Base.Settings;

public partial class SettingsAudioDevices : ScrollContainer
{
	private GlobalData _globalData;

	private OptionButton _deviceOptionsDropMenu;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		
		_deviceOptionsDropMenu = GetNode<OptionButton>("VBoxContainer/VBoxContainer/AudioOutputOptions");
		
		VisibilityChanged += DisplayExistingDevices;
		VisibilityChanged += LoadAvailibleDeviceDropMenu;

	}

	private void LoadAvailibleDeviceDropMenu()
	{
		_globalData.Playback.GetAvailibleAudioDevices();
		
		// We check a repopulate all options each call - this empties prior options
		_deviceOptionsDropMenu.Clear();
		
		var devices = _globalData.Playback.GetAvailibleAudioDevices();
		foreach (var device in devices)
		{
			_deviceOptionsDropMenu.AddItem(device.Description);
		}

		
	}
	
	
	private void DisplayExistingDevices()
	{
		if (!Visible) return; 
		var visible = Visible;


		List<AudioDevice> deviceList = _globalData.Devices.GetAudioDevices();
		if (deviceList == null || !deviceList.Any())
		{
			GetNode<Label>("VBoxContainer/LabelDeviceQuantity").Text = "There are currently no audio devices";
			return;
		}

		foreach (AudioDevice device in deviceList)
		{
			var deviceToolboxScene = GD.Load<PackedScene>("res://src/UI/AudioDeviceToolbox.tscn");
			var deviceToolbox = deviceToolboxScene.Instantiate();
			GetNode<VBoxContainer>("VBoxContainer/VBoxContainer").AddChild(deviceToolbox);
			deviceToolbox.GetNode<Label>("Panel/VBoxContainer/HBoxContainer/Label").Text = device.Name;
		}

		GetNode<Label>("VBoxContainer/LabelDeviceQuantity").Text = "There are " + deviceList.Count + " devices";



	}

	private void _onAddDevicePressed()
	{
		var deviceName = _deviceOptionsDropMenu.GetItemText(_deviceOptionsDropMenu.Selected);
		_globalData.Devices.CreateAudioDevice(deviceName);
		DisplayExistingDevices();
	}
}