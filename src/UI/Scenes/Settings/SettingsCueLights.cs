using Godot;
using System;
using System.Threading.Tasks;
using Cue2.Base.Classes.Connections;
using Cue2.Shared;
using Cue2.UI.Utilities;

namespace Cue2.UI.Scenes.Settings;

/// <summary>
/// SettingsCueLights is the UI parent for the settings window.
/// It is responsible for user setting of generral cuelight options.
/// Adding new cuelight instances
/// </summary>
public partial class SettingsCueLights : ScrollContainer
{
    private GlobalData _globalData;
    private GlobalSignals _globalSignals;
    private CueLightManager _cueLightManager;
    private Base.Classes.Settings _settings;
    
    //UI
    private PackedScene _cueLightInstanceScene;
    
    private VBoxContainer _cueLightsContainer;
    
    private Button _newCueLightButton;

    private ColorPickerButton _idleColour;
    private ColorPickerButton _goColour;
    private ColorPickerButton _standbyColour;
    private ColorPickerButton _countInColour;

    private Button _testGoButton;
    private Button _testStandbyButton;
    private Button _testCountInButton;
    private Button _testIdentifyButton;
    
    
    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _cueLightManager = _globalData.CueLightManager;
        _settings = _globalData.Settings;

        _cueLightInstanceScene = SceneLoader.LoadPackedScene("uid://6vou7cplmgmo", out string _);

        VisibilityChanged += OnVisible;
        
        
        // UI
        _cueLightsContainer = GetNode<VBoxContainer>("%CueLightsContainer");
        
        _newCueLightButton = GetNode<Button>("%NewCueLightButton");
        _idleColour = GetNode<ColorPickerButton>("%IdleColour");
        _goColour = GetNode<ColorPickerButton>("%GoColour");
        _standbyColour = GetNode<ColorPickerButton>("%StandbyColour");
        _countInColour = GetNode<ColorPickerButton>("%CountInColour");
        
        _testGoButton = GetNode<Button>("%TestGoButton");
        _testStandbyButton = GetNode<Button>("%TestStandbyButton");
        _testCountInButton = GetNode<Button>("%TestCountInButton");
        _testIdentifyButton = GetNode<Button>("%TestIdentifyButton");
        
        
        
        OnVisible(); // Initial set data
        
        _idleColour.ColorChanged += async color => 
        {
            _settings.CueLightIdleColour = color;
            await UpdateAllCueLightColorsAsync();
        };
        _goColour.ColorChanged += async color => 
        {
            _settings.CueLightGoColour = color;
            await UpdateAllCueLightColorsAsync();
        };
        _standbyColour.ColorChanged += async color => 
        {
            _settings.CueLightStandbyColour = color;
            await UpdateAllCueLightColorsAsync();
        };
        _countInColour.ColorChanged += async color => 
        {
            _settings.CueLightCountInColour = color;
            await UpdateAllCueLightColorsAsync();
        };

        _testGoButton.Pressed += () => _cueLightManager.AllGo();
        _testStandbyButton.Pressed += () => _cueLightManager.AllStandby();
        _testCountInButton.Pressed += () => _cueLightManager.AllCountIn();
        _testIdentifyButton.Toggled += state => _cueLightManager.AllIdentify(state);
        
        
        _newCueLightButton.Pressed += NewCueLightButton;
        
        // Load cue-lights
        var cueLights = _cueLightManager.GetCueLights();
        foreach (var cueLight in cueLights)
        {
            PanelContainer instance = _cueLightInstanceScene.Instantiate<PanelContainer>();
            _cueLightsContainer.AddChild(instance);
            instance.Name = cueLight.Id.ToString();
            SetUpInstance(instance, cueLight);
        }
        
    }

    private async void NewCueLightButton()
    {
        var cueLight = _cueLightManager.CreateCueLight();
        await cueLight.ConnectAsync();
        PanelContainer instance = _cueLightInstanceScene.Instantiate<PanelContainer>();
        _cueLightsContainer.AddChild(instance);
        instance.Name = cueLight.Id.ToString();
        SetUpInstance(instance, cueLight);
    }

    private void SetUpInstance(PanelContainer instance, CueLight cueLight)
    {
        var nameLineEdit = instance.GetNode<LineEdit>("%NameLineEdit");
        nameLineEdit.Text = cueLight.Name;
        nameLineEdit.TextSubmitted += text =>
        {
            cueLight.Name = text;
            nameLineEdit.ReleaseFocus();
        }; 
        
        var collapseButton = instance.GetNode<Button>("%CueLightCollapseButton");
        collapseButton.Icon = GetThemeIcon("Right", "AtlasIcons");
        var configAccordian = instance.GetNode<HBoxContainer>("%ConfigAccordian");
        configAccordian.Visible = false;
        
        
        var deleteButton = instance.GetNode<Button>("%DeleteButton");
        deleteButton.Icon = GetThemeIcon("DeleteBin", "AtlasIcons");
        deleteButton.Pressed += async () =>
        {
            await DeleteCueLight(instance, cueLight);
        };
        
        
        var connectionStatusColourRect = instance.GetNode<ColorRect>("%ConnectionStatusColourRect");
        connectionStatusColourRect.Color = cueLight.CueLightIsConnected ? Colors.Green : Colors.Red;
        var checkConnectionButton = instance.GetNode<Button>("%CheckConnectionButton");
        checkConnectionButton.Icon = GetThemeIcon("Refresh", "AtlasIcons");
        checkConnectionButton.Pressed += () =>
        {
            connectionStatusColourRect.Color = cueLight.CueLightIsConnected ? Colors.Green : Colors.Red;
            connectionStatusColourRect.TooltipText = cueLight.CueLightIsConnected ? "Connected" : "Disconnected";
        };
        
        var identifyButton = instance.GetNode<Button>("%IdentifyButton");
        identifyButton.Toggled += async state => await cueLight.IdentifyAsync(state);
        
        
        var ipLineEdit = instance.GetNode<LineEdit>("%IpLineEdit");
        ipLineEdit.Text = cueLight.IpAddress;
        ipLineEdit.TextSubmitted += text =>
        {
            string cleanedIp =  UiUtilities.VerifyIpInput(text, _globalSignals);
            if (cleanedIp != null)
            {
                cueLight.SetIpAddressAsync(cleanedIp);
                ipLineEdit.Text = cleanedIp;
            }
            ipLineEdit.Text = cueLight.IpAddress;
            ipLineEdit.ReleaseFocus();
        };

    }

    private async Task DeleteCueLight(PanelContainer instance, CueLight cueLight)
    {
        try
        {
            // Disconnect the CueLight if connected
            if (cueLight.CueLightIsConnected)
            {
                await cueLight.DisconnectAsync();
                GD.Print($"SettingsCueLights:DeleteCueLightAsync - Disconnected CueLight {cueLight.Id} ({cueLight.Name})");
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                    $"Disconnected CueLight {cueLight.Id} ({cueLight.Name})", 0);
            }
            // Remove UI instance
            instance.QueueFree(); // Marks the UI instance for deletion

            // Remove from CueLightManager
            _cueLightManager.DeleteCueLight(cueLight);
            
            
            GD.Print($"SettingsCueLights:DeleteCueLightAsync - Deleted CueLight {cueLight.Id} ({cueLight.Name}) and its UI instance");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Deleted CueLight {cueLight.Id} ({cueLight.Name}) and its UI instance", 0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"SettingsCueLights:DeleteCueLightAsync - Error deleting CueLight {cueLight.Id}: {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Error deleting CueLight {cueLight.Id}: {ex.Message}", 2);
        }
    }
    
    private async Task UpdateAllCueLightColorsAsync()
    {
        try
        {
            var cueLights = _cueLightManager.GetCueLights();
            foreach (var cueLight in cueLights)
            {
                if (cueLight.CueLightIsConnected)
                {
                    await cueLight.ConfigureColorsAsync(
                        _settings.CueLightIdleColour, 
                        _settings.CueLightGoColour, 
                        _settings.CueLightStandbyColour, 
                        _settings.CueLightCountInColour,
                        _settings.CueLightBrightness);
                    GD.Print($"SettingsCueLights:UpdateAllCueLightColorsAsync - Updated colors for CueLight {cueLight.Id} ({cueLight.Name})");
                    _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                        $"Updated colors for CueLight {cueLight.Id} ({cueLight.Name})", 0);
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"SettingsCueLights:UpdateAllCueLightColorsAsync - Error updating colors: {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"Error updating cue light colors: {ex.Message}", 2);
        }
    }
    
    
    private void OnVisible()
    {
        if (Visible)
        {
            _idleColour.Color = _settings.CueLightIdleColour;
            _goColour.Color = _settings.CueLightGoColour;
            _standbyColour.Color = _settings.CueLightStandbyColour;
            _countInColour.Color = _settings.CueLightCountInColour;
        }
    }
}
