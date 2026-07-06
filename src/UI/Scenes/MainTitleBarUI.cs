using Godot;
using System;
using System.Threading.Tasks;
using Cue2.Base;
using Cue2.Shared;
using Cue2.UI.Scenes.SubWindows;
using SettingsWindow = Cue2.UI.Scenes.Settings.SettingsWindow;

namespace Cue2.UI.Scenes;

public partial class MainTitleBarUI : Control
{
    private GlobalSignals _globalSignals;
    private SettingsWindow _settingsWindow;
    private PackedScene _settingsWindowPackedScene = SceneLoader.LoadPackedScene("uid://cfw3syjm11bd6", out _);
    private AboutWindow _aboutWindow;
    private PackedScene _aboutWindowPackedScene = SceneLoader.LoadPackedScene("uid://82ylja0fq6y0", out _);

    private HBoxContainer _mainMenu;
    private Button _mainMenuButton;
    private bool _mainMenuActive = false;
    private bool _mouseInUi = false;
    
    
    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        
        GetNode<Button>("%TitleCue2Menu").Pressed += OnTitleCue2MenuPressed;
        GetNode<Button>("%TitleMainMenu").Toggled += OnTitleMainMenuToggled;
        GetNode<Button>("%WindowMinimizeButton").Pressed += OnWindowMinimizeButtonPressed;
        GetNode<Button>("%WindowExpandButton").Pressed += OnWindowExpandButtonPressed;
        GetNode<Button>("%ExitButton").Pressed += OnExitButtonPressed;

        GetNode<Button>("%SettingsButton").Toggled += OnSettingsButtonToggled;
        
        GetNode<Button>("%AboutButton").Toggled += OnAboutButtonPressed;

        _globalSignals.ToggleSettingsWindow += ToggleSettingsWindow;
        
        _mainMenu = GetNode<HBoxContainer>("%MainMenuContainer");
        _mainMenuButton = GetNode<Button>("%TitleMainMenu");
        
        GetNode<Button>("%TitleMainMenu").MouseEntered += () => _mainMenuButton.ButtonPressed = true;
    
        // Drop down menu button behavior
        // File drop down
        GetNode<Button>("%FileNew").Pressed += () =>
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.NewSession));
            _mainMenuButton.ButtonPressed = false;
        };
        GetNode<Button>("%FileSave").Pressed += () =>
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Save));
            _mainMenuButton.ButtonPressed = false;
        };
        GetNode<Button>("%FileSaveAs").Pressed += () =>
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.SaveAs));
            _mainMenuButton.ButtonPressed = false;
        };
        GetNode<Button>("%FileOpenSession").Pressed += () =>
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.OpenSession));
            _mainMenuButton.ButtonPressed = false;
        };
        
        // Mouse over behavior
        GetNode<Button>("%MainMenuFile").MouseEntered += _onMainMenuFileHover;
        GetNode<Button>("%MainMenuFile").MouseExited += () => _mouseInUi = false;
        GetNode<Button>("%MainMenuEdit").MouseEntered += _onMainMenuEditHover;
        GetNode<Button>("%MainMenuEdit").MouseExited += () => _mouseInUi = false;
        GetNode<Button>("%MainMenuView").MouseEntered += _onMainMenuViewHover;
        GetNode<Button>("%MainMenuView").MouseExited += () => _mouseInUi = false;
        GetNode<PanelContainer>("%DropMenuFile").MouseEntered += () => _mouseInUi = true;
        GetNode<PanelContainer>("%DropMenuFile").MouseExited += () => _mouseInUi = false;
        GetNode<PanelContainer>("%DropMenuEdit").MouseEntered += () => _mouseInUi = true;
        GetNode<PanelContainer>("%DropMenuEdit").MouseExited += () => _mouseInUi = false;
        GetNode<PanelContainer>("%DropMenuView").MouseEntered += () => _mouseInUi = true;
        GetNode<PanelContainer>("%DropMenuView").MouseExited += () => _mouseInUi = false;

        GetNode<Button>("%AboutButton").TooltipText += Version.FullVersionString;
        
        SyncHotkeys();
    }

    private void SyncHotkeys()
    {
        GetNode<Label>("%FileNewHotkey").Text = GlobalData.ParseHotkey("NewSession");
        GetNode<Label>("%FileSaveHotkey").Text = GlobalData.ParseHotkey("SaveSession");
        GetNode<Label>("%FileSaveAsHotkey").Text = GlobalData.ParseHotkey("SaveAsSession");
        GetNode<Label>("%FileOpenHotkey").Text = GlobalData.ParseHotkey("OpenSession");
    }
    

    private void _onMainMenuFileHover()
    {
        GetNode<PanelContainer>("%DropMenuFile").Visible = true;
        GetNode<PanelContainer>("%DropMenuEdit").Visible = false;
        GetNode<PanelContainer>("%DropMenuView").Visible = false;
        _mouseInUi = true;
    }
    private void _onMainMenuEditHover()
    {
        GetNode<PanelContainer>("%DropMenuFile").Visible = false;
        GetNode<PanelContainer>("%DropMenuEdit").Visible = true;
        GetNode<PanelContainer>("%DropMenuView").Visible = false;
        _mouseInUi = true;
    }
    private void _onMainMenuViewHover()
    {
        GetNode<PanelContainer>("%DropMenuFile").Visible = false;
        GetNode<PanelContainer>("%DropMenuEdit").Visible = false;
        GetNode<PanelContainer>("%DropMenuView").Visible = true;
        _mouseInUi = true;
    }
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseEvent && mouseEvent.ButtonIndex == MouseButton.Left && mouseEvent.Pressed)
        {
            
            // Closes UI if mouse clicked while not inside main menu
            if (_mouseInUi == false && _mainMenuActive == true)
            {
                _mainMenuButton.ButtonPressed = false;
                
            }
        }
    }

    private void OnTitleCue2MenuPressed()
    {
        throw new NotImplementedException();
    }

    private void OnTitleMainMenuToggled(Boolean @toggle)
    {
        GD.Print("Main Menu");
        if (@toggle == true)
        {
            GD.Print("Hiding main menu");
            _mainMenu.Visible = true;
            _mainMenuActive = true;
        }
        else
        {
            GD.Print("Showing Main Menu");
            _mainMenu.Visible = false;
            _mainMenuActive = false;
            GetNode<PanelContainer>("%DropMenuFile").Visible = false;
            GetNode<PanelContainer>("%DropMenuEdit").Visible = false;
            GetNode<PanelContainer>("%DropMenuView").Visible = false;
        }

    }



    private void OnAboutButtonPressed(Boolean toggle)
    {
        if (toggle)
        {
            if (_aboutWindow == null)
            {
                _aboutWindow = _aboutWindowPackedScene.Instantiate<AboutWindow>();
                _aboutWindow.TreeExiting += OnAboutWindowExiting;
                AddChild(_aboutWindow);
            }
            else
            {
                _aboutWindow.Show();
            }
        }
        else
        {
            _aboutWindow?.QueueFree();
        }
    }

    private void OnAboutWindowExiting()
    {
        _aboutWindow = null;
        GetNode<Button>("%AboutButton").ButtonPressed = false;
        
    }


    
    private void OnSettingsButtonToggled(Boolean @toggle)
    {
        if (@toggle == true){
            if (_settingsWindow == null)
            {
                GD.Print("Loading settings window scene");
                _settingsWindow = _settingsWindowPackedScene.Instantiate<SettingsWindow>();
                _settingsWindow.TreeExiting += OnSettingsWindowClose;
                AddChild(_settingsWindow);
            }
            else
            {
                _settingsWindow.GetWindow().Show();
            }
        }
        if (@toggle == false)
        {
            _settingsWindow?.QueueFree();
        }
    }

    private void OnSettingsWindowClose()
    {
        _settingsWindow = null; 
        GetNode<Button>("%SettingsButton").ButtonPressed = false;
    }

    private void ToggleSettingsWindow()
    {
        var btn = GetNodeOrNull<Button>("%SettingsButton");
        if (btn != null)
        {
            btn.ButtonPressed = !btn.ButtonPressed;
        }
    }
    
    
    private void OnWindowMinimizeButtonPressed()
    {
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Minimized, GetWindow().GetWindowId());
    }
    private void OnWindowExpandButtonPressed()
    {
        var windowNumber = GetWindow().GetWindowId();
        if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Fullscreen){
            GD.Print("Maximise");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen, windowNumber);
        }
        else {
            GD.Print("Minimise");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed, windowNumber);
            //DisplayServer.WindowSetSize(new Vector2I(600,400), window_number);
        }
    }
    private void OnExitButtonPressed()
    {
        GetTree().Root.PropagateNotification((int)NotificationWMCloseRequest);
        Task.Delay(100);
        GetTree().Quit();
    }
}
