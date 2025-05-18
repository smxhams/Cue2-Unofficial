using Godot;
using System;
using Cue2.Shared;

namespace Cue2.Base.Settings;

public partial class SettingsGeneral : ScrollContainer
{
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;
    public override void _Ready()
    {
        GD.Print("Settingsd General Init");
        
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        
        GetNode<HSlider>("%UiScaleSlider").ValueChanged += _onUiScaleSliderValueChanged;
        GetNode<HSlider>("%UiScaleSlider").DragEnded += _ApplyUiScaleFromSlider;
        GetNode<LineEdit>("%UiScaleNum").TextSubmitted += _ApplyUiScaleFromText;


    }

    private void _ApplyUiScaleFromText(string input)
    {
        string cleaned = input.Replace("%", "").Trim();

        if (!float.TryParse(cleaned, out float value))
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Invalid value for UI Scale entered", 1);
            GetNode<LineEdit>("%UiScaleNum").Text = _globalData.Settings.UiScale + "%";
        }

        value = Mathf.Clamp(value, 50f, 200f);
        GetNode<LineEdit>("%UiScaleNum").Text = value + "%";
        var scaleFactor = value / 100f;
        _globalData.Settings.UiScale = scaleFactor;
        _globalSignals.EmitSignal(nameof(GlobalSignals.UiScaleChanged), scaleFactor);
        
    }

    private void _ApplyUiScaleFromSlider(bool _)
    {
        var value = GetNode<HSlider>("%UiScaleSlider").Value;
        var scaleFactor = (float)(value / 100f);
        _globalData.Settings.UiScale = scaleFactor;
        _globalSignals.EmitSignal(nameof(GlobalSignals.UiScaleChanged), scaleFactor);
    }

    private void _onUiScaleSliderValueChanged(double value)
    {
        GetNode<LineEdit>("%UiScaleNum").Text = value + "%";
    }
}
