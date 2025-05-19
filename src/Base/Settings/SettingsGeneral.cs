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
        
        GetNode<OptionButton>("%GoScaleOptionButton").ItemSelected += _scaleGoButton;
        
        //GetNode<OptionButton>("%SaveFilterOptionButton").selec
        _syncSettings();
    }
    
    private void _syncSettings()
    {
        GetNode<LineEdit>("%UiScaleNum").Text = _globalData.Settings.UiScale*100 + "%";
        GetNode<HSlider>("%UiScaleSlider").Value = _globalData.Settings.UiScale * 100f;
        GetNode<OptionButton>("%GoScaleOptionButton").Selected = (int)_globalData.Settings.GoScale;
    }
    
    private void _scaleGoButton(long index)
    {
        index = (int)index;
        switch (index)
        {
            case 0: _globalData.Settings.GoScale = 0.5f; break;
            case 1: _globalData.Settings.GoScale = 1.0f; break;
            case 2: _globalData.Settings.GoScale = 2.0f; break;
            case 3: _globalData.Settings.GoScale = 4.0f; break;
            case 4: _globalData.Settings.GoScale = 8.0f; break;
            case 5: _globalData.Settings.GoScale = 32.0f; break;
            default: _globalData.Settings.GoScale = 1.0f; break;
        }

        _globalSignals.EmitSignal(nameof(GlobalSignals.GoScaleChanged), _globalData.Settings.GoScale);
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
