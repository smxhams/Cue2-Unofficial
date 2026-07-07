using Godot;
using System;
using System.Collections.Generic;
using System.IO;
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

    private GlobalData _globalData;
    private VBoxContainer _fileMenuContainer;

    // Recent submenu (hover activated from File > Open Recent)
    private PanelContainer _recentMenuPanel;
    private VBoxContainer _recentContainer;

    // Timer for delayed close of submenus when mouse leaves the entire header menu area
    private Timer _menuHideTimer;

    private Label _titleLabel;
    
    
    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNode<GlobalData>("/root/GlobalData");

        _titleLabel = GetNode<Label>("%TitleLabel");
        UpdateTitle();

        _fileMenuContainer = GetNode<PanelContainer>("%DropMenuFile")
            .GetNode<VBoxContainer>("MarginContainer/dmFileContainer");

        _recentMenuPanel = GetNode<PanelContainer>("%DropMenuRecent");
        _recentContainer = _recentMenuPanel.GetNode<VBoxContainer>("MarginContainer/RecentContainer");

        // Create timer for delayed submenu close on mouse leave
        _menuHideTimer = new Timer { OneShot = true, WaitTime = 0.25f }; // 250ms delay for comfortable menu navigation
        _menuHideTimer.Timeout += OnMenuHideTimeout;
        AddChild(_menuHideTimer);
        
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

        _globalSignals.NewSession += UpdateTitle;
        _globalSignals.OpenSelectedSession += _ => CallDeferred(nameof(UpdateTitle));
        _globalSignals.Save += UpdateTitle;
        _globalSignals.SaveAs += UpdateTitle;
    
        // Drop down menu button behavior
        // File drop down
        GetNode<Button>("%FileNew").Pressed += () =>
        {
            _globalData.SessionName = null;
            _globalData.SessionPath = null;
            UpdateTitle();
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
        GetNode<Button>("%MainMenuFile").MouseExited += ScheduleMenuHide;
        GetNode<Button>("%MainMenuEdit").MouseEntered += _onMainMenuEditHover;
        GetNode<Button>("%MainMenuEdit").MouseExited += ScheduleMenuHide;
        GetNode<Button>("%MainMenuView").MouseEntered += _onMainMenuViewHover;
        GetNode<Button>("%MainMenuView").MouseExited += ScheduleMenuHide;

        GetNode<PanelContainer>("%DropMenuFile").MouseEntered += () => { CancelMenuHide(); _mouseInUi = true; };
        GetNode<PanelContainer>("%DropMenuFile").MouseExited += ScheduleMenuHide;
        GetNode<PanelContainer>("%DropMenuEdit").MouseEntered += () => { CancelMenuHide(); _mouseInUi = true; };
        GetNode<PanelContainer>("%DropMenuEdit").MouseExited += ScheduleMenuHide;
        GetNode<PanelContainer>("%DropMenuView").MouseEntered += () => { CancelMenuHide(); _mouseInUi = true; };
        GetNode<PanelContainer>("%DropMenuView").MouseExited += ScheduleMenuHide;

        // Recent submenu hover support
        GetNode<Button>("%FileOpenRecent").MouseEntered += _onFileOpenRecentHover;
        GetNode<Button>("%FileOpenRecent").MouseExited += ScheduleMenuHide;
        GetNode<PanelContainer>("%DropMenuRecent").MouseEntered += () => { CancelMenuHide(); _mouseInUi = true; };
        GetNode<PanelContainer>("%DropMenuRecent").MouseExited += ScheduleMenuHide;

        GetNode<Button>("%AboutButton").TooltipText += Version.FullVersionString;
        
        SyncHotkeys();
    }

    private void SyncHotkeys()
    {
        GetNode<Label>("%FileNewHotkey").Text = GlobalData.ParseHotkey("NewSession");
        GetNode<Label>("%FileSaveHotkey").Text = GlobalData.ParseHotkey("SaveSession");
        GetNode<Label>("%FileSaveAsHotkey").Text = GlobalData.ParseHotkey("SaveAsSession");
        GetNode<Label>("%FileOpenHotkey").Text = GlobalData.ParseHotkey("OpenSession");

        var settingsBtn = GetNode<Button>("%SettingsButton");
        string settingsHotkey = GlobalData.ParseHotkey("ToggleSettings");
        settingsBtn.TooltipText = "Settings" + (!string.IsNullOrEmpty(settingsHotkey) ? "\nHotkey: " + settingsHotkey : "");
    }

    public void UpdateTitle()
    {
        if (_titleLabel == null || _globalData == null) return;

        if (!string.IsNullOrEmpty(_globalData.SessionName))
        {
            _titleLabel.Text = $"Cue2 - {_globalData.SessionName}";
        }
        else
        {
            _titleLabel.Text = "Cue2";
        }
    }

    /// <summary>
    /// Clears all children from the recent submenu container (used before repopulating).
    /// </summary>
    private void ClearRecentSubmenu()
    {
        if (_recentContainer == null) return;
        foreach (Node child in _recentContainer.GetChildren())
        {
            if (IsInstanceValid(child))
                child.QueueFree();
        }
    }

    /// <summary>
    /// Populates the hover submenu (DropMenuRecent) with recent show files.
    /// Shows a "No recent files" message or the list + Clear option.
    /// </summary>
    private void PopulateRecentSubmenu()
    {
        ClearRecentSubmenu();
        if (_recentContainer == null || _globalData?.UserDataManager == null) return;

        var recents = _globalData.UserDataManager.GetRecentShowFiles();

        if (recents.Count == 0)
        {
            var label = new Label
            {
                Text = "(No recent files)",
                HorizontalAlignment = HorizontalAlignment.Left
            };
            label.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
            _recentContainer.AddChild(label);
            return;
        }

        // Small header for the submenu
        var header = new Label
        {
            Text = "Recent Files",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        header.AddThemeFontSizeOverride("font_size", 9);
        header.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        _recentContainer.AddChild(header);

        const int maxToShow = 8;
        int displayed = 0;
        foreach (var path in recents)
        {
            if (displayed >= maxToShow) break;
            displayed++;

            string displayName = Path.GetFileName(path);
            if (displayName.Length > 32)
                displayName = displayName.Substring(0, 29) + "…";

            var btn = new Button
            {
                Text = displayName,
                Alignment = HorizontalAlignment.Left,
                MouseFilter = Control.MouseFilterEnum.Pass,
                TooltipText = path
            };

            string captured = path;
            btn.Pressed += () => OnRecentFileSelected(captured);
            _recentContainer.AddChild(btn);
        }

        // Separator + Clear
        var sep = new HSeparator();
        _recentContainer.AddChild(sep);

        var clearBtn = new Button
        {
            Text = "Clear Recent",
            Alignment = HorizontalAlignment.Left,
            MouseFilter = Control.MouseFilterEnum.Pass
        };
        clearBtn.Pressed += OnClearRecents;
        _recentContainer.AddChild(clearBtn);
    }

    private void OnRecentFileSelected(string path)
    {
        _globalSignals.EmitSignal(nameof(GlobalSignals.OpenSelectedSession), path);
        _mainMenuButton.ButtonPressed = false; // close entire menu system
    }

    private void OnClearRecents()
    {
        _globalData?.UserDataManager?.ClearRecentShowFiles();
        PopulateRecentSubmenu(); // refresh submenu in place to reflect empty state
    }

    private void ShowRecentSubmenu()
    {
        if (_recentMenuPanel == null) return;

        // Position the recent submenu to the right of the File dropdown (improves on pure hardcoded offsets)
        var filePanel = GetNodeOrNull<PanelContainer>("%DropMenuFile");
        if (filePanel != null && filePanel.Visible)
        {
            // Use local position relative to our parent (same as other drops)
            _recentMenuPanel.Position = new Vector2(filePanel.Position.X + filePanel.Size.X, GetNode<Button>("%FileOpenRecent").Position.Y);
        }

        _recentMenuPanel.Visible = true;
        PopulateRecentSubmenu();
    }

    private void HideRecentSubmenu()
    {
        if (_recentMenuPanel != null)
        {
            _recentMenuPanel.Visible = false;
        }
    }

    /// <summary>
    /// Hides all dropdown panels. Called on full menu close.
    /// </summary>
    private void HideAllDropdowns()
    {
        GetNodeOrNull<PanelContainer>("%DropMenuFile")?.Hide();
        GetNodeOrNull<PanelContainer>("%DropMenuEdit")?.Hide();
        GetNodeOrNull<PanelContainer>("%DropMenuView")?.Hide();
        HideRecentSubmenu();
    }

    private void ScheduleMenuHide()
    {
        _menuHideTimer?.Stop();
        _menuHideTimer?.Start();
    }

    private void CancelMenuHide()
    {
        _menuHideTimer?.Stop();
    }

    private void OnMenuHideTimeout()
    {
        if (IsMouseOverMenuArea())
        {
            return; // mouse came back, keep open
        }

        HideAllDropdowns();
        _mouseInUi = false;
    }

    private bool IsMouseOverMenuArea()
    {
        var mousePos = GetViewport().GetMousePosition();

        if (IsControlUnderMouse("%MainMenuFile", mousePos)) return true;
        if (IsControlUnderMouse("%MainMenuEdit", mousePos)) return true;
        if (IsControlUnderMouse("%MainMenuView", mousePos)) return true;

        if (IsControlUnderMouse("%DropMenuFile", mousePos)) return true;
        if (IsControlUnderMouse("%DropMenuEdit", mousePos)) return true;
        if (IsControlUnderMouse("%DropMenuView", mousePos)) return true;

        if (IsControlUnderMouse("%FileOpenRecent", mousePos)) return true;
        if (IsControlUnderMouse("%DropMenuRecent", mousePos)) return true;

        return false;
    }

    private bool IsControlUnderMouse(string nodePath, Vector2 mousePos)
    {
        var ctrl = GetNodeOrNull<Control>(nodePath);
        if (ctrl == null || !ctrl.Visible) return false;
        return ctrl.GetGlobalRect().HasPoint(mousePos);
    }

    private void _onMainMenuFileHover()
    {
        CancelMenuHide();
        HideRecentSubmenu();
        GetNode<PanelContainer>("%DropMenuFile").Visible = true;
        GetNode<PanelContainer>("%DropMenuEdit").Visible = false;
        GetNode<PanelContainer>("%DropMenuView").Visible = false;
        _mouseInUi = true;
    }

    private void _onMainMenuEditHover()
    {
        CancelMenuHide();
        HideRecentSubmenu();
        GetNode<PanelContainer>("%DropMenuFile").Visible = false;
        GetNode<PanelContainer>("%DropMenuEdit").Visible = true;
        GetNode<PanelContainer>("%DropMenuView").Visible = false;
        _mouseInUi = true;
    }

    private void _onMainMenuViewHover()
    {
        CancelMenuHide();
        HideRecentSubmenu();
        GetNode<PanelContainer>("%DropMenuFile").Visible = false;
        GetNode<PanelContainer>("%DropMenuEdit").Visible = false;
        GetNode<PanelContainer>("%DropMenuView").Visible = true;
        _mouseInUi = true;
    }

    private void _onFileOpenRecentHover()
    {
        CancelMenuHide();
        // Keep the parent File menu visible while showing the hover submenu to the right
        GetNode<PanelContainer>("%DropMenuFile").Visible = true;
        GetNode<PanelContainer>("%DropMenuEdit").Visible = false;
        GetNode<PanelContainer>("%DropMenuView").Visible = false;
        ShowRecentSubmenu();
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
        if (@toggle == true)
        {
            GD.Print("MainTitleBarUI:OnTitleMainMenuToggled - Showing main menu");
            _mainMenu.Visible = true;
            _mainMenuActive = true;
            // Note: actual File submenu population is lazy on hover
        }
        else
        {
            GD.Print("MainTitleBarUI:OnTitleMainMenuToggled - Hiding main menu");
            _mainMenu.Visible = false;
            _mainMenuActive = false;
            _menuHideTimer?.Stop();
            HideAllDropdowns();
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
