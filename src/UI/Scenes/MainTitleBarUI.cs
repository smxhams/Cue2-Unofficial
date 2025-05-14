using Godot;
using System;
using System.Threading.Tasks;
using Cue2.Shared;

namespace Cue2.UI;

public partial class MainTitleBarUI : Control
{
    private GlobalSignals _globalSignals;
    private Node _settingsWindow;

    private HBoxContainer _mainMenu;
    private Button _mainMenuButton;
    private bool _mainMenuActive = false;
    private bool _mouseInUi = false;
    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        
        GetNode<Button>("%TitleCue2Menu").Pressed += _onTitleCue2MenuPressed;
        GetNode<Button>("%TitleMainMenu").Toggled += _onTitleMainMenuToggled;
        GetNode<Button>("%TitleHelpMenu").Pressed += _onTitleHelpMenuPressed;
        GetNode<Button>("%SettingsButton").Toggled += _onSettingsButtonToggled;
        GetNode<Button>("%WindowMinimizeButton").Pressed += _onWindowMinimizeButtonPressed;
        GetNode<Button>("%WindowExpandButton").Pressed += _onWindowExpandButtonPressed;
        GetNode<Button>("%ExitButton").Pressed += _onExitButtonPressed;

        _globalSignals.CloseSettingsWindow += _closeSettingsWindow;

        _mainMenu = GetNode<HBoxContainer>("%MainMenuContainer");
        _mainMenuButton = GetNode<Button>("%TitleMainMenu");
        
        GetNode<Button>("%TitleMainMenu").MouseEntered += () => _mainMenuButton.ButtonPressed = true;
    
        // Drop down menu button behavior
        // File drop down
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
        
        _syncHotkeys();
    }

    private void _syncHotkeys()
    {
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

    private void _onTitleCue2MenuPressed()
    {
        throw new NotImplementedException();
    }

    private void _onTitleMainMenuToggled(Boolean @toggle)
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



    private void _onTitleHelpMenuPressed()
    {
        throw new NotImplementedException();
    }


    
    private void _onSettingsButtonToggled(Boolean @toggle)
    {
        if (@toggle == true){
            if (_settingsWindow == null)
            {
                GD.Print("Loading settings window scene");
                _settingsWindow = SceneLoader.LoadScene("uid://cfw3syjm11bd6", out string error); // Loads settings window
                AddChild(_settingsWindow);
            }
            else {
                _settingsWindow.GetWindow().Show();
            }
        }
        if (@toggle == false){
            _settingsWindow.GetWindow().Hide();
        }
    }
    private void _closeSettingsWindow()
    {
        GetNode<Button>("%SettingsButton").ButtonPressed = false;
    }
    
    private void _onWindowMinimizeButtonPressed()
    {
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Minimized, GetWindow().GetWindowId());
    }
    private void _onWindowExpandButtonPressed()
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
    private void _onExitButtonPressed()
    {
        GetTree().Root.PropagateNotification((int)NotificationWMCloseRequest);
        Task.Delay(100);
        GetTree().Quit();
    }
}
