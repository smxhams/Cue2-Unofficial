//==================================================================================//
// SettingsOscListen.cs                                                             //
// This file is part of Cue2                                                        //
// http://cue2.live/                                                                //
//==================================================================================//

using System;
using System.Net.NetworkInformation;
using System.Text;
using Cue2.Base.Classes.Connections;
using Cue2.Shared;
using Godot;

namespace Cue2.UI.Scenes.Settings;

/// <summary>
/// Settings panel for OSC receive: enable/port/session (top) and live monitor log (bottom).
/// Layout mirrors <see cref="SettingsMidi"/>.
/// </summary>
public partial class SettingsOscListen : Control
{
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;
    private HistoryManager _historyManager;
    private OscListen _oscListen;

    private CheckButton _enabledCheckButton;
    private Label _ipAddressLabel;
    private Label _statusLabel;
    private LineEdit _sessionNameLineEdit;
    private LineEdit _portLineEdit;

    private CheckBox _listenCheckBox;
    private Button _clearLogButton;
    private CodeEdit _monitorLog;

    private bool _isSyncingUi;
    private bool _portEditing;
    private bool _sessionNameEditing;
    private readonly StringBuilder _logBuilder = new();
    private int _logLineCount;

    private const int MaxUiLogLines = OscListen.MaxMonitorLines;

    public override void _Ready()
    {
        _globalSignals = GetNodeOrNull<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNodeOrNull<GlobalData>("/root/GlobalData");
        _historyManager = _globalData?.HistoryManager;
        _oscListen = GetNodeOrNull<OscListen>("/root/OscListen");

        _enabledCheckButton = GetNodeOrNull<CheckButton>("%EnabledCheckButton");
        _ipAddressLabel = GetNodeOrNull<Label>("%IpAddressLabel");
        _statusLabel = GetNodeOrNull<Label>("%StatusLabel");
        _sessionNameLineEdit = GetNodeOrNull<LineEdit>("%SessionNameLineEdit");
        _portLineEdit = GetNodeOrNull<LineEdit>("%PortLineEdit");

        _listenCheckBox = GetNodeOrNull<CheckBox>("%ListenCheckBox");
        _clearLogButton = GetNodeOrNull<Button>("%ClearLogButton");
        _monitorLog = GetNodeOrNull<CodeEdit>("%MonitorLog");

        // Fixed built-in command catalog (read-only; generated from OscListen.BuiltInCommandCatalog).
        PopulateBuiltInCommandList();

        ConfigureMonitorLogStyle();

        if (_enabledCheckButton != null)
            _enabledCheckButton.Toggled += OnEnabledToggled;
        if (_portLineEdit != null)
            _portLineEdit.EditingToggled += OnPortEditingToggled;
        if (_sessionNameLineEdit != null)
            _sessionNameLineEdit.EditingToggled += OnSessionNameEditingToggled;
        if (_listenCheckBox != null)
            _listenCheckBox.Toggled += OnListenToggled;
        if (_clearLogButton != null)
            _clearLogButton.Pressed += OnClearLogPressed;

        if (_oscListen != null)
        {
            _oscListen.OscStateChanged += OnOscStateChanged;
            _oscListen.OscMonitorLine += OnOscMonitorLine;
        }

        if (_historyManager != null)
            _historyManager.HistoryRestored += OnHistoryRestored;
        if (_globalSignals != null)
            _globalSignals.NewSession += OnNewSession;

        VisibilityChanged += OnVisibilityChanged;

        DisplayIpAddresses();
        SyncFromModel();
    }

    public override void _ExitTree()
    {
        VisibilityChanged -= OnVisibilityChanged;

        if (_historyManager != null)
            _historyManager.HistoryRestored -= OnHistoryRestored;
        if (_globalSignals != null)
            _globalSignals.NewSession -= OnNewSession;

        if (_oscListen != null)
        {
            _oscListen.OscStateChanged -= OnOscStateChanged;
            _oscListen.OscMonitorLine -= OnOscMonitorLine;
        }

        if (_enabledCheckButton != null)
            _enabledCheckButton.Toggled -= OnEnabledToggled;
        if (_portLineEdit != null)
            _portLineEdit.EditingToggled -= OnPortEditingToggled;
        if (_sessionNameLineEdit != null)
            _sessionNameLineEdit.EditingToggled -= OnSessionNameEditingToggled;
        if (_listenCheckBox != null)
            _listenCheckBox.Toggled -= OnListenToggled;
        if (_clearLogButton != null)
            _clearLogButton.Pressed -= OnClearLogPressed;

        base._ExitTree();
    }

    private void OnVisibilityChanged()
    {
        if (!Visible) return;
        DisplayIpAddresses();
        SyncFromModel();
        // Buffered lines keep accumulating while hidden; push them into the CodeEdit now.
        RefreshMonitorLogUi(stickToBottom: true);
    }

    private void OnOscStateChanged()
    {
        if (_historyManager?.IsRestoring == true) return;
        if (Visible)
            SyncFromModel();
    }

    private void OnHistoryRestored(int scope)
    {
        if (!GodotObject.IsInstanceValid(this)) return;
        if (scope != (int)HistoryManager.HistoryScope.Settings) return;
        SyncFromModel();
    }

    private void OnNewSession()
    {
        if (!GodotObject.IsInstanceValid(this)) return;
        OnClearLogPressed();
        SyncFromModel();
    }

    private void RecordHistory(string description)
    {
        if (_historyManager == null || _historyManager.IsRestoring) return;
        _historyManager.RecordSettingsChange(description, null, "OscListen");
    }

    /// <summary>
    /// Builds the read-only built-in command list from <see cref="OscListen.BuiltInCommandCatalog"/>.
    /// </summary>
    private void PopulateBuiltInCommandList()
    {
        var list = GetNodeOrNull<VBoxContainer>("%CommandsList");
        if (list == null) return;

        foreach (Node child in list.GetChildren())
            child.QueueFree();

        string lastCategory = null;
        foreach (var cmd in OscListen.BuiltInCommandCatalog)
        {
            if (cmd.Category != lastCategory)
            {
                lastCategory = cmd.Category;
                var cat = new Label
                {
                    Text = lastCategory,
                    TooltipText = $"{lastCategory} commands"
                };
                cat.AddThemeFontSizeOverride("font_size", 11);
                cat.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f, 1f));
                // Small top gap between categories
                if (list.GetChildCount() > 0)
                {
                    var spacer = new Control { CustomMinimumSize = new Vector2(0, 4) };
                    list.AddChild(spacer);
                }
                list.AddChild(cat);
            }

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.TooltipText = cmd.Description;

            var pattern = new LineEdit
            {
                Text = cmd.Pattern,
                Editable = false,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SelectAllOnFocus = true,
                TooltipText = cmd.Description,
                CustomMinimumSize = new Vector2(180, 0),
            };
            pattern.AddThemeFontSizeOverride("font_size", 11);
            row.AddChild(pattern);

            list.AddChild(row);
        }
    }

    private void ConfigureMonitorLogStyle()
    {
        if (_monitorLog == null) return;

        _monitorLog.Editable = false;
        _monitorLog.ContextMenuEnabled = true;
        _monitorLog.GuttersDrawLineNumbers = false;
        _monitorLog.ScrollPastEndOfFile = false;
        _monitorLog.WrapMode = TextEdit.LineWrappingMode.None;
        _monitorLog.CaretBlink = false;
        _monitorLog.CaretType = TextEdit.CaretTypeEnum.Line;

        var bg = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.05f, 0.05f, 1f),
            BorderColor = new Color(0.22f, 0.22f, 0.22f, 1f),
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
        };
        bg.SetBorderWidthAll(1);
        bg.SetCornerRadiusAll(3);
        _monitorLog.AddThemeStyleboxOverride("normal", bg);
        _monitorLog.AddThemeStyleboxOverride("focus", bg);
        _monitorLog.AddThemeStyleboxOverride("read_only", bg);

        _monitorLog.AddThemeColorOverride("font_color", new Color(0.75f, 0.95f, 0.75f, 1f));
        _monitorLog.AddThemeColorOverride("font_readonly_color", new Color(0.75f, 0.95f, 0.75f, 1f));
        _monitorLog.AddThemeColorOverride("caret_color", new Color(0.4f, 0.8f, 0.4f, 0.6f));
        _monitorLog.AddThemeColorOverride("background_color", new Color(0.05f, 0.05f, 0.05f, 1f));
        _monitorLog.AddThemeFontSizeOverride("font_size", 12);
    }

    private void SyncFromModel()
    {
        if (_oscListen == null) return;

        _isSyncingUi = true;
        try
        {
            if (_enabledCheckButton != null)
                _enabledCheckButton.SetPressedNoSignal(_oscListen.OscListenEnabled);

            if (_portLineEdit != null && !_portEditing)
                _portLineEdit.Text = _oscListen.Port.ToString();

            if (_sessionNameLineEdit != null && !_sessionNameEditing)
                _sessionNameLineEdit.Text = _oscListen.SessionName ?? string.Empty;

            if (_listenCheckBox != null)
                _listenCheckBox.SetPressedNoSignal(_oscListen.MonitorEnabled);

            UpdateStatusLabel();
        }
        finally
        {
            _isSyncingUi = false;
        }
    }

    private void UpdateStatusLabel()
    {
        if (_statusLabel == null || _oscListen == null) return;

        if (!_oscListen.OscListenEnabled)
        {
            _statusLabel.Text = "Listener off";
            return;
        }

        _statusLabel.Text = _oscListen.IsListening
            ? $"Listening · UDP {_oscListen.Port}"
            : $"Enabled · port {_oscListen.Port} (not bound)";
    }

    private void OnEnabledToggled(bool pressed)
    {
        if (_isSyncingUi || _oscListen == null) return;
        if (_historyManager?.IsRestoring == true) return;
        if (_oscListen.OscListenEnabled == pressed) return;

        RecordHistory(pressed ? "Enable OSC Listener" : "Disable OSC Listener");
        _oscListen.OscListenEnabled = pressed;
        UpdateStatusLabel();
    }

    private void OnListenToggled(bool pressed)
    {
        if (_isSyncingUi || _oscListen == null) return;
        if (_historyManager?.IsRestoring == true) return;
        if (_oscListen.MonitorEnabled == pressed) return;

        RecordHistory(pressed ? "Enable OSC receive monitor" : "Disable OSC receive monitor");
        _oscListen.MonitorEnabled = pressed;
    }

    private void OnPortEditingToggled(bool editing)
    {
        if (editing)
        {
            _portEditing = true;
            return;
        }

        if (!_portEditing) return;
        _portEditing = false;
        _portLineEdit?.ReleaseFocus();
        SubmitPort();
    }

    private void SubmitPort()
    {
        if (_oscListen == null || _portLineEdit == null) return;
        if (_historyManager?.IsRestoring == true) return;

        if (int.TryParse(_portLineEdit.Text, out int port) && port >= 1 && port <= 65535)
        {
            if (_oscListen.Port == port) return;
            RecordHistory($"Set OSC listen port to {port}");
            _oscListen.Port = port;
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"OSC Listener: port set to {port}", (int)LogType.Info);
        }
        else
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "OSC Listener: invalid port (1–65535).", (int)LogType.Warning);
            _portLineEdit.Text = _oscListen.Port.ToString();
        }
    }

    private void OnSessionNameEditingToggled(bool editing)
    {
        if (editing)
        {
            _sessionNameEditing = true;
            return;
        }

        if (!_sessionNameEditing) return;
        _sessionNameEditing = false;
        _sessionNameLineEdit?.ReleaseFocus();
        SubmitSessionName();
    }

    private void SubmitSessionName()
    {
        if (_oscListen == null || _sessionNameLineEdit == null) return;
        if (_historyManager?.IsRestoring == true) return;

        string sessionName = _sessionNameLineEdit.Text.Trim();

        // Empty is allowed (optional label).
        if (!string.IsNullOrEmpty(sessionName))
        {
            if (sessionName.Length > 20)
            {
                _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                    "OSC Listener: session name max 20 characters.", (int)LogType.Warning);
                _sessionNameLineEdit.Text = _oscListen.SessionName;
                return;
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(sessionName, @"^[a-zA-Z0-9_]+$"))
            {
                _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                    "OSC Listener: session name may only use letters, numbers, and underscores.",
                    (int)LogType.Warning);
                _sessionNameLineEdit.Text = _oscListen.SessionName;
                return;
            }
        }

        if (_oscListen.SessionName == sessionName) return;
        RecordHistory("Set OSC session name");
        _oscListen.SessionName = sessionName;
    }

    private void OnClearLogPressed()
    {
        _logBuilder.Clear();
        _logLineCount = 0;
        if (_monitorLog != null)
            _monitorLog.Text = string.Empty;
        _oscListen?.ClearPendingMonitorLines();
    }

    private void OnOscMonitorLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return;

        if (_logLineCount >= MaxUiLogLines)
        {
            string current = _logBuilder.ToString();
            int cut = 0;
            int drops = MaxUiLogLines / 4;
            for (int i = 0; i < drops; i++)
            {
                int next = current.IndexOf('\n', cut);
                if (next < 0)
                {
                    cut = current.Length;
                    break;
                }
                cut = next + 1;
            }
            _logBuilder.Clear();
            if (cut < current.Length)
                _logBuilder.Append(current, cut, current.Length - cut);
            _logLineCount = Math.Max(0, _logLineCount - drops);
        }

        if (_logBuilder.Length > 0)
            _logBuilder.Append('\n');
        _logBuilder.Append(line);
        _logLineCount++;

        if (!Visible) return;

        bool stickToBottom = true;
        if (_monitorLog != null && _monitorLog.GetLineCount() > 0)
        {
            int lastVisible = _monitorLog.GetLastFullVisibleLine();
            stickToBottom = lastVisible >= _monitorLog.GetLineCount() - 2;
        }

        RefreshMonitorLogUi(stickToBottom);
    }

    /// <summary>
    /// Pushes the in-memory monitor buffer into the CodeEdit (e.g. when the panel opens).
    /// </summary>
    private void RefreshMonitorLogUi(bool stickToBottom)
    {
        if (_monitorLog == null) return;
        _monitorLog.Text = _logBuilder.ToString();
        if (stickToBottom && _monitorLog.GetLineCount() > 0)
            _monitorLog.SetCaretLine(_monitorLog.GetLineCount());
    }

    private void DisplayIpAddresses()
    {
        if (_ipAddressLabel == null) return;

        var ipAddresses = new System.Collections.Generic.List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        ipAddresses.Add(ip.Address.ToString());
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"SettingsOscListen:DisplayIpAddresses - {ex.Message}");
        }

        _ipAddressLabel.Text = ipAddresses.Count > 0
            ? string.Join("  ·  ", ipAddresses)
            : "(none found)";
    }
}
