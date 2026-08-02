//==================================================================================//
// SettingsOscConnections.cs                                                        //
// This file is part of Cue2                                                        //
// http://cue2.live/                                                                //
//==================================================================================//

using System;
using System.Text;
using Cue2.Domain.Connections;
using Cue2.Services;
using Godot;
using Rug.Osc;

namespace Cue2.UI.Settings;

/// <summary>
/// Settings panel for OSC send connections (top) and live send monitor log (bottom).
/// Layout mirrors <see cref="SettingsMidi"/>.
/// </summary>
public partial class SettingsOscConnections : Control
{
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;
    private HistoryManager _historyManager;
    private OscConnections _oscConnections;

    private Button _newOscButton;
    private VBoxContainer _connectionsContainer;
    private Label _nameLabel;
    private Label _interfaceLabel;
    private Label _destinationLabel;
    private Label _portLabel;
    private Label _statusLabel;

    private CheckBox _listenCheckBox;
    private Button _clearLogButton;
    private CodeEdit _monitorLog;
    private LineEdit _testPathLineEdit;
    private LineEdit _testArgsLineEdit;
    private Button _testSendButton;

    private bool _isSyncingUi;
    private readonly StringBuilder _logBuilder = new();
    private int _logLineCount;

    private const int MaxUiLogLines = OscConnections.MaxMonitorLines;

    private PackedScene _oscConnectionCardScene;

    public override void _Ready()
    {
        _globalSignals = GetNodeOrNull<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNodeOrNull<GlobalData>("/root/GlobalData");
        _historyManager = _globalData?.HistoryManager;
        _oscConnections = GetNodeOrNull<OscConnections>("/root/OscConnections");

        _newOscButton = GetNodeOrNull<Button>("%NewOscButton");
        _connectionsContainer = GetNodeOrNull<VBoxContainer>("%ConnectionsContainer");
        _nameLabel = GetNodeOrNull<Label>("%NameLabel");
        _interfaceLabel = GetNodeOrNull<Label>("%InterfaceLabel");
        _destinationLabel = GetNodeOrNull<Label>("%DestinationLabel");
        _portLabel = GetNodeOrNull<Label>("%PortLabel");
        _statusLabel = GetNodeOrNull<Label>("%StatusLabel");

        _listenCheckBox = GetNodeOrNull<CheckBox>("%ListenCheckBox");
        _clearLogButton = GetNodeOrNull<Button>("%ClearLogButton");
        _monitorLog = GetNodeOrNull<CodeEdit>("%MonitorLog");
        _testPathLineEdit = GetNodeOrNull<LineEdit>("%TestPathLineEdit");
        _testArgsLineEdit = GetNodeOrNull<LineEdit>("%TestArgsLineEdit");
        _testSendButton = GetNodeOrNull<Button>("%TestSendButton");

        _oscConnectionCardScene = SceneLoader.LoadPackedScene(
            "uid://b53mk1xolhtmv", out string err);
        if (_oscConnectionCardScene == null)
            GD.PrintErr($"SettingsOscConnections:_Ready - card scene: {err}");

        ConfigureMonitorLogStyle();

        if (_newOscButton != null)
            _newOscButton.Pressed += OnNewConnectionPressed;
        if (_listenCheckBox != null)
            _listenCheckBox.Toggled += OnListenToggled;
        if (_clearLogButton != null)
            _clearLogButton.Pressed += OnClearLogPressed;
        if (_testSendButton != null)
            _testSendButton.Pressed += OnTestSendPressed;
        if (_testPathLineEdit != null)
            _testPathLineEdit.TextSubmitted += _ => _testPathLineEdit.ReleaseFocus();
        if (_testArgsLineEdit != null)
            _testArgsLineEdit.TextSubmitted += _ =>
            {
                _testArgsLineEdit.ReleaseFocus();
                OnTestSendPressed();
            };

        if (_nameLabel != null) _nameLabel.Resized += UpdateUiColumns;
        if (_interfaceLabel != null) _interfaceLabel.Resized += UpdateUiColumns;
        if (_destinationLabel != null) _destinationLabel.Resized += UpdateUiColumns;
        if (_portLabel != null) _portLabel.Resized += UpdateUiColumns;

        if (_oscConnections != null)
        {
            _oscConnections.OscConnectionsStateChanged += OnConnectionsStateChanged;
            _oscConnections.OscSendMonitorLine += OnSendMonitorLine;
        }

        if (_historyManager != null)
            _historyManager.HistoryRestored += OnHistoryRestored;
        if (_globalSignals != null)
            _globalSignals.NewSession += OnNewSession;

        VisibilityChanged += OnVisibilityChanged;
        SyncFromModel();
    }

    public override void _ExitTree()
    {
        VisibilityChanged -= OnVisibilityChanged;

        if (_historyManager != null)
            _historyManager.HistoryRestored -= OnHistoryRestored;
        if (_globalSignals != null)
            _globalSignals.NewSession -= OnNewSession;

        if (_oscConnections != null)
        {
            _oscConnections.OscConnectionsStateChanged -= OnConnectionsStateChanged;
            _oscConnections.OscSendMonitorLine -= OnSendMonitorLine;
        }

        if (_newOscButton != null)
            _newOscButton.Pressed -= OnNewConnectionPressed;
        if (_listenCheckBox != null)
            _listenCheckBox.Toggled -= OnListenToggled;
        if (_clearLogButton != null)
            _clearLogButton.Pressed -= OnClearLogPressed;

        base._ExitTree();
    }

    private void OnVisibilityChanged()
    {
        if (!Visible) return;
        SyncFromModel();
        // Buffered lines keep accumulating while hidden; push them into the CodeEdit now.
        RefreshMonitorLogUi(stickToBottom: true);
    }

    private void OnConnectionsStateChanged()
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
        ClearConnectionCards();
        OnClearLogPressed();
        SyncFromModel();
    }

    private void RecordHistory(string description)
    {
        if (_historyManager == null || _historyManager.IsRestoring) return;
        _historyManager.RecordSettingsChange(description, null, "OscConnections");
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

        // Slightly cooler green than MIDI to distinguish send vs receive monitors.
        _monitorLog.AddThemeColorOverride("font_color", new Color(0.7f, 0.9f, 0.95f, 1f));
        _monitorLog.AddThemeColorOverride("font_readonly_color", new Color(0.7f, 0.9f, 0.95f, 1f));
        _monitorLog.AddThemeColorOverride("caret_color", new Color(0.4f, 0.7f, 0.8f, 0.6f));
        _monitorLog.AddThemeColorOverride("background_color", new Color(0.05f, 0.05f, 0.05f, 1f));
        _monitorLog.AddThemeFontSizeOverride("font_size", 12);
    }

    private void SyncFromModel()
    {
        _isSyncingUi = true;
        try
        {
            if (_listenCheckBox != null && _oscConnections != null)
                _listenCheckBox.SetPressedNoSignal(_oscConnections.MonitorEnabled);

            RebuildConnectionCards();
            UpdateStatusLabel();
        }
        finally
        {
            _isSyncingUi = false;
        }
    }

    private void RebuildConnectionCards()
    {
        if (_connectionsContainer == null || _oscConnectionCardScene == null) return;

        ClearConnectionCards();

        var list = OscConnections.Connections;
        if (list == null || list.Count == 0)
        {
            var empty = new Label
            {
                Text = "No OSC connections — create one above.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            empty.AddThemeFontSizeOverride("font_size", 11);
            empty.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f, 1f));
            _connectionsContainer.AddChild(empty);
            return;
        }

        var ratios = GetColumnRatios();
        foreach (var connection in list)
        {
            if (connection == null || !GodotObject.IsInstanceValid(connection)) continue;
            var card = _oscConnectionCardScene.Instantiate<SettingsOscConnectionCard>();
            _connectionsContainer.AddChild(card);
            card.SetCueOscConnection(connection);
            card.HistoryRecordRequested += OnCardHistoryRequested;
            if (ratios.Count > 0)
                card.UpdateRatios(ratios);
        }
    }

    private void ClearConnectionCards()
    {
        if (_connectionsContainer == null) return;
        foreach (var child in _connectionsContainer.GetChildren())
        {
            if (child is SettingsOscConnectionCard card)
                card.HistoryRecordRequested -= OnCardHistoryRequested;
            _connectionsContainer.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void OnCardHistoryRequested(string description)
    {
        RecordHistory(description);
    }

    private void UpdateStatusLabel()
    {
        if (_statusLabel == null) return;
        int count = OscConnections.Connections?.Count ?? 0;
        int open = 0;
        if (OscConnections.Connections != null)
        {
            foreach (var c in OscConnections.Connections)
            {
                if (c != null && GodotObject.IsInstanceValid(c) && c.IsSenderOpen)
                    open++;
            }
        }
        _statusLabel.Text = count == 0
            ? "No connections"
            : $"{open}/{count} connected";
    }

    private void OnNewConnectionPressed()
    {
        if (_historyManager?.IsRestoring == true) return;
        RecordHistory("Add OSC connection");
        OscConnections.CreateConnection();
        // Rebuild via OscConnectionsStateChanged
    }

    private void OnTestSendPressed()
    {
        var list = OscConnections.Connections;
        if (list == null || list.Count == 0)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "OSC test: no connections", (int)LogType.Warning);
            return;
        }

        string path = (_testPathLineEdit?.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(path))
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "OSC test: enter a path", (int)LogType.Warning);
            return;
        }
        if (!path.StartsWith("/"))
            path = "/" + path;

        try
        {
            string args = _testArgsLineEdit?.Text ?? string.Empty;
            OscMessage msg = string.IsNullOrWhiteSpace(args)
                ? new OscMessage(path)
                : OscMessageUtil.BuildMessage(path, args);
            // Send on all open connections so multi-dest shows can verify.
            foreach (var conn in list)
            {
                if (conn == null || !GodotObject.IsInstanceValid(conn)) continue;
                conn.SendMessage(msg);
            }
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"OSC test: {path} → {list.Count} connection(s)", (int)LogType.Info);
        }
        catch (Exception ex)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"OSC test failed: {ex.Message}", (int)LogType.Error);
        }
    }

    private void OnListenToggled(bool pressed)
    {
        if (_isSyncingUi || _oscConnections == null) return;
        if (_historyManager?.IsRestoring == true) return;
        if (_oscConnections.MonitorEnabled == pressed) return;

        RecordHistory(pressed ? "Enable OSC send monitor" : "Disable OSC send monitor");
        _oscConnections.MonitorEnabled = pressed;
    }

    private void OnClearLogPressed()
    {
        _logBuilder.Clear();
        _logLineCount = 0;
        if (_monitorLog != null)
            _monitorLog.Text = string.Empty;
        _oscConnections?.ClearPendingMonitorLines();
    }

    private void OnSendMonitorLine(string line)
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

    private Godot.Collections.Dictionary GetColumnRatios()
    {
        if (_nameLabel == null || _interfaceLabel == null || _destinationLabel == null || _portLabel == null)
            return new Godot.Collections.Dictionary();

        float totalWidth = _nameLabel.Size.X + _interfaceLabel.Size.X
            + _destinationLabel.Size.X + _portLabel.Size.X;
        if (totalWidth <= 0) return new Godot.Collections.Dictionary();

        var ratios = new Godot.Collections.Dictionary();
        ratios["Name"] = _nameLabel.Size.X / totalWidth;
        ratios["Interface"] = _interfaceLabel.Size.X / totalWidth;
        ratios["Destination"] = _destinationLabel.Size.X / totalWidth;
        ratios["Port"] = _portLabel.Size.X / totalWidth;
        return ratios;
    }

    private void UpdateUiColumns()
    {
        var ratios = GetColumnRatios();
        if (ratios.Count == 0 || _connectionsContainer == null) return;
        foreach (var child in _connectionsContainer.GetChildren())
        {
            if (child is SettingsOscConnectionCard card)
                card.UpdateRatios(ratios);
        }
    }
}
