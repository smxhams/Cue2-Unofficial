using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Cue2.Base;
using Cue2.Shared;
using Cue2.UI.Scenes.SubWindows;
using Cue2.UI.Utilities;
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

    // Show Files submenu (hover activated from File > Show Files)
    private PanelContainer _showFilesMenuPanel;

    // Timer for delayed close of submenus when mouse leaves the entire header menu area
    private Timer _menuHideTimer;

    private Label _titleLabel;
    private Button _editUndoButton;
    private Button _editRedoButton;
    
    
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

        _showFilesMenuPanel = GetNodeOrNull<PanelContainer>("%DropMenuShowFiles");

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
            _globalData.SessionDir = null;
            _globalData.SessionAudioPath = null;
            _globalData.SessionVideoPath = null;
            _globalData.SessionImagesPath = null;
            _globalData.SessionWaveformsPath = null;
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

        // Edit drop down (Undo / Redo / Cut / Copy / Paste)
        _editUndoButton = GetNode<Button>("%EditUndo");
        _editRedoButton = GetNode<Button>("%EditRedo");
        _editUndoButton.Pressed += () =>
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Undo));
            _mainMenuButton.ButtonPressed = false;
        };
        _editRedoButton.Pressed += () =>
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Redo));
            _mainMenuButton.ButtonPressed = false;
        };
        var editCut = GetNodeOrNull<Button>("%EditCut");
        if (editCut != null)
        {
            editCut.Pressed += () =>
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.CutSelectedCues));
                _mainMenuButton.ButtonPressed = false;
            };
        }
        var editCopy = GetNodeOrNull<Button>("%EditCopy");
        if (editCopy != null)
        {
            editCopy.Pressed += () =>
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.CopySelectedCues));
                _mainMenuButton.ButtonPressed = false;
            };
        }
        var editPaste = GetNodeOrNull<Button>("%EditPaste");
        if (editPaste != null)
        {
            editPaste.Pressed += () =>
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.PasteCues));
                _mainMenuButton.ButtonPressed = false;
            };
        }
        if (_globalData.HistoryManager != null)
        {
            _globalData.HistoryManager.HistoryChanged += UpdateUndoRedoMenuState;
            // Hotkey labels may change when input map is undone/redone.
            _globalData.HistoryManager.HistoryRestored += OnHistoryRestored;
            UpdateUndoRedoMenuState();
        }

        // Non-submenu File rows: close any open flyout submenu on hover
        WireFileMenuItemHidesSubmenus("%FileNew");
        WireFileMenuItemHidesSubmenus("%FileSave");
        WireFileMenuItemHidesSubmenus("%FileSaveAs");
        WireFileMenuItemHidesSubmenus("%FileOpenSession");
        
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

        // Show Files submenu
        var showFilesBtn = GetNodeOrNull<Button>("%FileShowFiles");
        if (showFilesBtn != null)
        {
            showFilesBtn.MouseEntered += _onFileShowFilesHover;
            showFilesBtn.MouseExited += ScheduleMenuHide;
        }
        if (_showFilesMenuPanel != null)
        {
            _showFilesMenuPanel.MouseEntered += () => { CancelMenuHide(); _mouseInUi = true; };
            _showFilesMenuPanel.MouseExited += ScheduleMenuHide;
        }

        var copyMediaBtn = GetNodeOrNull<Button>("%ShowFilesCopyMedia");
        if (copyMediaBtn != null)
            copyMediaBtn.Pressed += OnShowFilesCopyMediaPressed;

        var checkPresenceBtn = GetNodeOrNull<Button>("%ShowFilesCheckPresence");
        if (checkPresenceBtn != null)
            checkPresenceBtn.Pressed += OnShowFilesCheckPresencePressed;

        var openFolderBtn = GetNodeOrNull<Button>("%ShowFilesOpenFolder");
        if (openFolderBtn != null)
            openFolderBtn.Pressed += OnShowFilesOpenFolderPressed;

        GetNode<Button>("%AboutButton").TooltipText += Version.FullVersionString;
        
        SyncHotkeys();
    }

    private void SyncHotkeys()
    {
        GetNode<Label>("%FileNewHotkey").Text = GlobalData.ParseHotkey("NewSession");
        GetNode<Label>("%FileSaveHotkey").Text = GlobalData.ParseHotkey("SaveSession");
        GetNode<Label>("%FileSaveAsHotkey").Text = GlobalData.ParseHotkey("SaveAsSession");
        GetNode<Label>("%FileOpenHotkey").Text = GlobalData.ParseHotkey("OpenSession");
        GetNode<Label>("%EditUndoHotkey").Text = GlobalData.ParseHotkey("Undo");
        GetNode<Label>("%EditRedoHotkey").Text = GlobalData.ParseHotkey("Redo");
        var editCutHk = GetNodeOrNull<Label>("%EditCutHotkey");
        if (editCutHk != null) editCutHk.Text = GlobalData.ParseHotkey("CutSelectedCues");
        var editCopyHk = GetNodeOrNull<Label>("%EditCopyHotkey");
        if (editCopyHk != null) editCopyHk.Text = GlobalData.ParseHotkey("CopySelectedCues");
        var editPasteHk = GetNodeOrNull<Label>("%EditPasteHotkey");
        if (editPasteHk != null) editPasteHk.Text = GlobalData.ParseHotkey("PasteCues");

        var settingsBtn = GetNode<Button>("%SettingsButton");
        string settingsHotkey = GlobalData.ParseHotkey("ToggleSettings");
        settingsBtn.TooltipText = "Settings" + (!string.IsNullOrEmpty(settingsHotkey) ? "\nHotkey: " + settingsHotkey : "");
    }

    private void OnHistoryRestored(int scope)
    {
        if (scope != (int)HistoryManager.HistoryScope.Settings) return;
        SyncHotkeys();
    }

    /// <summary>
    /// Enables or disables Edit → Undo/Redo based on history stack state.
    /// </summary>
    private void UpdateUndoRedoMenuState()
    {
        var history = _globalData?.HistoryManager;
        if (_editUndoButton != null)
            _editUndoButton.Disabled = history == null || !history.CanUndo;
        if (_editRedoButton != null)
            _editRedoButton.Disabled = history == null || !history.CanRedo;
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

    private void ShowShowFilesSubmenu()
    {
        if (_showFilesMenuPanel == null) return;

        var filePanel = GetNodeOrNull<PanelContainer>("%DropMenuFile");
        var showFilesBtn = GetNodeOrNull<Button>("%FileShowFiles");
        if (filePanel != null && filePanel.Visible && showFilesBtn != null)
        {
            // Align to the right of the File menu, next to the Show Files row
            _showFilesMenuPanel.Position = new Vector2(
                filePanel.Position.X + filePanel.Size.X,
                filePanel.Position.Y + showFilesBtn.Position.Y);
        }

        _showFilesMenuPanel.Visible = true;
    }

    private void HideShowFilesSubmenu()
    {
        if (_showFilesMenuPanel != null)
            _showFilesMenuPanel.Visible = false;
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
        HideShowFilesSubmenu();
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

        if (IsControlUnderMouse("%FileShowFiles", mousePos)) return true;
        if (IsControlUnderMouse("%DropMenuShowFiles", mousePos)) return true;

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
        HideShowFilesSubmenu();
        GetNode<PanelContainer>("%DropMenuFile").Visible = true;
        GetNode<PanelContainer>("%DropMenuEdit").Visible = false;
        GetNode<PanelContainer>("%DropMenuView").Visible = false;
        _mouseInUi = true;
    }

    private void _onMainMenuEditHover()
    {
        CancelMenuHide();
        HideRecentSubmenu();
        HideShowFilesSubmenu();
        GetNode<PanelContainer>("%DropMenuFile").Visible = false;
        GetNode<PanelContainer>("%DropMenuEdit").Visible = true;
        GetNode<PanelContainer>("%DropMenuView").Visible = false;
        _mouseInUi = true;
    }

    private void _onMainMenuViewHover()
    {
        CancelMenuHide();
        HideRecentSubmenu();
        HideShowFilesSubmenu();
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
        HideShowFilesSubmenu();
        ShowRecentSubmenu();
        _mouseInUi = true;
    }

    private void _onFileShowFilesHover()
    {
        CancelMenuHide();
        GetNode<PanelContainer>("%DropMenuFile").Visible = true;
        GetNode<PanelContainer>("%DropMenuEdit").Visible = false;
        GetNode<PanelContainer>("%DropMenuView").Visible = false;
        HideRecentSubmenu();
        ShowShowFilesSubmenu();
        _mouseInUi = true;
    }

    /// <summary>
    /// File menu items without a flyout submenu close any open submenu on hover
    /// (e.g. leave "Open Recent" → hover "Open" closes the recent list).
    /// </summary>
    private void WireFileMenuItemHidesSubmenus(string buttonPath)
    {
        var btn = GetNodeOrNull<Button>(buttonPath);
        if (btn == null) return;
        btn.MouseEntered += OnFileMenuPlainItemHover;
    }

    private void OnFileMenuPlainItemHover()
    {
        CancelMenuHide();
        HideRecentSubmenu();
        HideShowFilesSubmenu();
        GetNodeOrNull<PanelContainer>("%DropMenuFile")?.Show();
        _mouseInUi = true;
    }

    /// <summary>
    /// Manual media copy into show folders + relative path update (ignores auto-backup setting).
    /// </summary>
    private void OnShowFilesCopyMediaPressed()
    {
        _mainMenuButton.ButtonPressed = false;

        if (string.IsNullOrEmpty(_globalData?.SessionDir) || string.IsNullOrEmpty(_globalData.SessionPath))
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                "Cannot copy media: save the show first so a show folder exists.", 1);
            return;
        }

        var backup = GetNodeOrNull<MediaBackupManager>("/root/MediaBackupManager");
        if (backup == null)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Media backup service unavailable.", 2);
            return;
        }

        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Copying media files into show folder…", 0);
        backup.EnqueueShowMediaBackup(force: true);
    }

    /// <summary>
    /// Immediate full media presence check with summary log.
    /// </summary>
    private void OnShowFilesCheckPresencePressed()
    {
        _mainMenuButton.ButtonPressed = false;

        var health = GetNodeOrNull<MediaHealthService>("/root/MediaHealthService");
        if (health == null)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Media health service unavailable.", 2);
            return;
        }

        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Checking file presence…", 0);
        health.CheckAllMediaNow();
    }

    /// <summary>
    /// Opens the show session folder in the OS file browser (Explorer / Finder / etc.).
    /// </summary>
    private void OnShowFilesOpenFolderPressed()
    {
        _mainMenuButton.ButtonPressed = false;

        string dir = _globalData?.SessionDir;
        if (string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(_globalData?.SessionPath))
            dir = _globalData.SessionPath.GetBaseDir();

        if (string.IsNullOrEmpty(dir))
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                "No show folder open. Save the show first.", 1);
            return;
        }

        try
        {
            // Normalize separators for the OS shell
            string full = Path.GetFullPath(dir);
            if (!Directory.Exists(full))
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                    $"Show folder does not exist: {full}", 2);
                return;
            }

            // Godot OS.ShellOpen opens folders in the system file manager cross-platform
            Error err = OS.ShellOpen(full);
            if (err != Error.Ok)
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                    $"Failed to open show folder ({err}): {full}", 2);
                return;
            }

            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Opened show folder: {full}", 0);
            GD.Print($"MainTitleBarUI:OnShowFilesOpenFolderPressed - Opened {full}");
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Failed to open show folder: {ex.Message}", 2);
            GD.PrintErr($"MainTitleBarUI:OnShowFilesOpenFolderPressed - {ex.Message}");
        }
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
            if (_settingsWindow == null || !GodotObject.IsInstanceValid(_settingsWindow))
            {
                GD.Print("Loading settings window scene");
                _settingsWindow = _settingsWindowPackedScene.Instantiate<SettingsWindow>();
                // Keep hidden until SettingsWindow._Ready applies cached size/position (avoids flicker).
                _settingsWindow.Visible = false;
                _settingsWindow.TreeExiting += OnSettingsWindowClose;
                // Prefer hide-on-close so reopening does not re-instantiate every panel (main-thread stall).
                _settingsWindow.CloseRequested += OnSettingsCloseRequested;
                AddChild(_settingsWindow);
            }
            else
            {
                _settingsWindow.Show();
                _settingsWindow.GrabFocus();
            }
        }
        if (@toggle == false)
        {
            // Hide instead of free — avoids re-running all settings panel _Ready and
            // SubViewport setup while video is playing (presentation is main-thread).
            if (_settingsWindow != null && GodotObject.IsInstanceValid(_settingsWindow))
                _settingsWindow.Hide();
        }
    }

    private void OnSettingsCloseRequested()
    {
        if (_settingsWindow != null && GodotObject.IsInstanceValid(_settingsWindow))
            _settingsWindow.Hide();
        GetNode<Button>("%SettingsButton").ButtonPressed = false;
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
        var window = GetWindow();
        if (window != null && GodotObject.IsInstanceValid(window))
            window.Mode = Window.ModeEnum.Minimized;
    }

    /// <summary>
    /// Toggles maximized (not exclusive fullscreen). Fullscreen was incorrect for this
    /// control: it does not match double-click maximize and leaves a broken restore path
    /// when the user later edge-resizes the borderless window.
    /// </summary>
    private void OnWindowExpandButtonPressed()
    {
        // Prefer MainWindowHandles so normal geometry is flushed before maximize.
        if (GetParent() is MainWindowHandles handles)
        {
            handles.ToggleMaximizeFromChrome();
            return;
        }

        var window = GetWindow();
        if (window == null || !GodotObject.IsInstanceValid(window))
            return;

        UiUtilities.ToggleMaximize(window);
    }
    private void OnExitButtonPressed()
    {
        GetTree().Root.PropagateNotification((int)NotificationWMCloseRequest);
        Task.Delay(100);
        GetTree().Quit();
    }
}
