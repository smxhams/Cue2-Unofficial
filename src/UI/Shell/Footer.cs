using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Cue2.Services;

namespace Cue2.UI.Shell;


/// <summary>
/// Main application footer bar: device status, connections, log readout,
/// total cue count, background process progress, and CPU/memory usage.
/// </summary>
public partial class Footer : Control
{
    private GlobalSignals _globalSignals;
    
    private List<string> _last5Logs = new List<string>();
    private Node _logWindow;

    // Ui
    private Label _totalCuesLabel;
    private Label _cpuUsageLabel;
    private Label _memoryUsageLabel;
    private Control _processProgressHost;
    private ProgressBar _bkgProcessStatusBar;
    private Label _processStatusLabel;
    private Control _processSeparator;
    private Button _logCountButton;
    private string _logPrintoutBaseTooltip = "Log";
    
    private Timer _updateTimer;
    private Timer _processHideTimer;

    // Process resource tracking (CPU / memory)
    private Process _currentProcess;
    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuSampleUtc;
    private bool _hasCpuBaseline;

    // Devices status
    private GlobalData _globalData;
    private AudioDevices _audioDevices;
    private DisplaysManager _displaysManager;
    private Button _devicesFooterButton;
    
    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

        _globalSignals.LogUpdated += _updateLog;
        
        _logCountButton = GetNode<Button>("%LogCountButton");
        
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _audioDevices = GetNode<AudioDevices>("/root/AudioDevices");
        _displaysManager = GetNode<DisplaysManager>("/root/DisplaysManager");
        _devicesFooterButton = GetNode<Button>("%DevicesFooterButton");
        _devicesFooterButton.TooltipText = "Devices";
        _devicesFooterButton.Pressed += () => _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "Test log", new Random().Next(0,5));
        _devicesFooterButton.MouseEntered += UpdateDevicesFooterTooltip;
        
        _globalSignals.AudioDevicesChanged += UpdateDevicesFooterTooltip;
        _globalSignals.DisplaysChanged += UpdateDevicesFooterTooltip;
        
        _logCountButton.Toggled += OnLogCountToggled;

        _globalSignals.ToggleLogWindow += ToggleLogWindow;

        _syncHotkeys();
        
        _totalCuesLabel = GetNodeOrNull<Label>("%TotalCuesLabel");
        _cpuUsageLabel = GetNode<Label>("%CpuUsageLabel");
        _memoryUsageLabel = GetNode<Label>("%MemoryUsageLabel");

        // Background process progress (media backup, cuelist bulk ops) — left of total cues / CPU
        _processProgressHost = GetNodeOrNull<Control>("%ProcessProgressHost");
        _bkgProcessStatusBar = GetNodeOrNull<ProgressBar>("%BkgProcessStatusBar");
        _processStatusLabel = GetNodeOrNull<Label>("%ProcessStatusLabel");
        _processSeparator = GetNodeOrNull<Control>("%VSeparator5");
        SetProcessProgressVisible(false);
        _globalSignals.MediaBackupProgress += OnMediaBackupProgress;
        _globalSignals.MediaBackupCompleted += OnMediaBackupCompleted;
        _globalSignals.BackgroundProcessProgress += OnBackgroundProcessProgress;
        _globalSignals.BackgroundProcessCompleted += OnBackgroundProcessCompleted;
        _globalSignals.TotalCuesChanged += OnTotalCuesChanged;

        try
        {
            _currentProcess = Process.GetCurrentProcess();
            _lastCpuTime = _currentProcess.TotalProcessorTime;
            _lastCpuSampleUtc = DateTime.UtcNow;
            _hasCpuBaseline = true;
        }
        catch (Exception ex)
        {
            GD.Print($"Footer:_Ready - Failed to open current process for resource tracking: {ex.Message}");
            _hasCpuBaseline = false;
        }
        
        _updateTimer = new Timer();
        AddChild(_updateTimer);
        _updateTimer.WaitTime = 1.0; // Match OBS-style ~1s resource refresh
        _updateTimer.Start();
        _updateTimer.Timeout += UpdateResourceUsage;

        UpdateDevicesFooterTooltip(); // initial status
        // Prefer live cuelist count if already available (session may load after footer ready)
        int initialTotal = _globalData?.Cuelist?.TotalCueCount ?? _globalData?.CueTotal ?? 0;
        UpdateTotalCuesLabel(initialTotal);
        UpdateResourceUsage(); // initial CPU/MEM read
    }

    /// <summary>
    /// Handles <see cref="GlobalSignals.TotalCuesChanged"/> and refreshes the footer readout.
    /// </summary>
    /// <param name="total">New total cue count for the show.</param>
    private void OnTotalCuesChanged(int total)
    {
        UpdateTotalCuesLabel(total);
    }

    /// <summary>
    /// Sets the footer total-cues label text (e.g. "42 total cues").
    /// </summary>
    /// <param name="total">Number of cues currently in the show.</param>
    private void UpdateTotalCuesLabel(int total)
    {
        if (_totalCuesLabel == null)
            return;

        int safeTotal = Math.Max(0, total);
        _totalCuesLabel.Text = $"{safeTotal} total cues";
        _totalCuesLabel.TooltipText =
            safeTotal == 1
                ? "1 cue in the show"
                : $"{safeTotal} total cues in the show";
    }

    /// <summary>
    /// Updates the process progress bar overlay and tooltip during media backup.
    /// </summary>
    private void OnMediaBackupProgress(float percent, bool busy, string statusText, string originPath, string destPath, int completedCount, int totalCount)
    {
        string origin = string.IsNullOrEmpty(originPath) ? "…" : originPath;
        string dest = string.IsNullOrEmpty(destPath) ? "…" : destPath;
        string detail = $"{origin} → {dest}";
        ApplyProcessProgress(percent, busy, statusText, detail, completedCount, totalCount);
    }

    private void OnMediaBackupCompleted()
    {
        CompleteProcessProgress("Copying 100%");
    }

    /// <summary>
    /// Updates the process progress bar for generic background work (e.g. bulk cuelist ops).
    /// </summary>
    private void OnBackgroundProcessProgress(float percent, bool busy, string statusText, string detail, int completedCount, int totalCount)
    {
        ApplyProcessProgress(percent, busy, statusText, detail ?? string.Empty, completedCount, totalCount);
    }

    private void OnBackgroundProcessCompleted()
    {
        string finalLabel = _processStatusLabel?.Text;
        if (string.IsNullOrEmpty(finalLabel) || finalLabel.EndsWith("0%", StringComparison.Ordinal))
            finalLabel = "Done";
        // Prefer a 100% style label when possible
        if (_bkgProcessStatusBar != null && _bkgProcessStatusBar.Value < 100.0)
            _bkgProcessStatusBar.Value = 100.0;
        CompleteProcessProgress(finalLabel);
    }

    /// <summary>
    /// Shared footer progress UI for media backup and generic background tasks.
    /// </summary>
    private void ApplyProcessProgress(float percent, bool busy, string statusText, string detail, int completedCount, int totalCount)
    {
        if (_bkgProcessStatusBar == null)
            return;

        // Stay visible for the whole batch; completed handler hides after a short delay
        if (busy || totalCount > 0)
            SetProcessProgressVisible(true);

        // Cancel a pending hide if a new process starts (or continues)
        if (busy)
            _processHideTimer?.Stop();

        _bkgProcessStatusBar.MaxValue = 100.0;
        _bkgProcessStatusBar.Value = percent;

        string label = string.IsNullOrEmpty(statusText) ? $"{percent:F0}%" : statusText;
        if (_processStatusLabel != null)
            _processStatusLabel.Text = label;

        string tip = $"{percent:F0}%";
        if (!string.IsNullOrEmpty(detail))
            tip += $"\n{detail}";
        if (totalCount > 0)
            tip += $"\n{completedCount}/{totalCount}";

        _bkgProcessStatusBar.TooltipText = tip;
        if (_processProgressHost != null)
            _processProgressHost.TooltipText = tip;
        if (_processStatusLabel != null)
            _processStatusLabel.TooltipText = tip;
    }

    private void CompleteProcessProgress(string finalStatusText)
    {
        if (_bkgProcessStatusBar != null)
            _bkgProcessStatusBar.Value = 100.0;
        if (_processStatusLabel != null && !string.IsNullOrEmpty(finalStatusText))
            _processStatusLabel.Text = finalStatusText;

        // Reuse a single one-shot timer so rapid batches don't stack hide callbacks
        if (_processHideTimer == null)
        {
            _processHideTimer = new Timer { WaitTime = 0.6, OneShot = true };
            _processHideTimer.Timeout += OnProcessHideTimeout;
            AddChild(_processHideTimer);
        }
        else
        {
            _processHideTimer.Stop();
        }

        _processHideTimer.Start();
    }

    private void OnProcessHideTimeout()
    {
        SetProcessProgressVisible(false);
        if (_bkgProcessStatusBar != null)
        {
            _bkgProcessStatusBar.Value = 0;
            _bkgProcessStatusBar.TooltipText = "Background process";
        }
        if (_processStatusLabel != null)
            _processStatusLabel.Text = string.Empty;
    }

    private void SetProcessProgressVisible(bool visible)
    {
        if (_processProgressHost != null)
            _processProgressHost.Visible = visible;
        if (_processSeparator != null)
            _processSeparator.Visible = visible;
    }

    private void _updateLog(String @printout, int @type)
    {
        var logPrintout = GetNode<Button>("%LogPrintout");
        logPrintout.Text = @printout;
        if (type == 0) logPrintout.RemoveThemeColorOverride("font_color");
        if (type == 1) logPrintout.AddThemeColorOverride("font_color", GlobalStyles.Warning);
        if (type == 2) logPrintout.AddThemeColorOverride("font_color", GlobalStyles.Danger);
        if (type == 3) logPrintout.AddThemeColorOverride("font_color", GlobalStyles.Danger);
        _logCountButton.Text = "Log " + EventLogger.GetLogCount().ToString();
        
        _last5Logs.Add(@printout);
        if (_last5Logs.Count > 5)
        {
            _last5Logs.RemoveAt(0);
        }
        
        //Update log tooltip to show last 5 logs
        logPrintout.TooltipText = _logPrintoutBaseTooltip + "\n\nLast 5 log messages:\n";
        foreach (var log in _last5Logs)
        {    
            logPrintout.TooltipText += log + "\n";
        }
    }

    /// <summary>
    /// Updates footer CPU and memory readouts for the Cue2 process (OBS-style status).
    /// CPU is process usage as a percent of total machine capacity (all logical processors).
    /// Memory is the process working set.
    /// </summary>
    private void UpdateResourceUsage()
    {
        if (_cpuUsageLabel == null || _memoryUsageLabel == null)
            return;

        if (_currentProcess == null)
        {
            _cpuUsageLabel.Text = "CPU --.-%";
            _memoryUsageLabel.Text = "MEM -- MB";
            return;
        }

        try
        {
            _currentProcess.Refresh();

            // Memory: working set (physical RAM used by this process)
            double memMb = _currentProcess.WorkingSet64 / (1024.0 * 1024.0);
            double memPrivateMb = _currentProcess.PrivateMemorySize64 / (1024.0 * 1024.0);
            _memoryUsageLabel.Text = $"MEM {memMb:F0} MB";
            _memoryUsageLabel.TooltipText =
                $"Memory usage (this process)\n" +
                $"Working set: {memMb:F1} MB\n" +
                $"Private: {memPrivateMb:F1} MB";

            // CPU: delta of TotalProcessorTime over wall-clock interval, normalized by core count
            TimeSpan currentCpu = _currentProcess.TotalProcessorTime;
            DateTime nowUtc = DateTime.UtcNow;

            if (!_hasCpuBaseline)
            {
                _lastCpuTime = currentCpu;
                _lastCpuSampleUtc = nowUtc;
                _hasCpuBaseline = true;
                _cpuUsageLabel.Text = "CPU --.-%";
                _cpuUsageLabel.TooltipText = "CPU usage (this process)\nSampling…";
                return;
            }

            double cpuDeltaMs = (currentCpu - _lastCpuTime).TotalMilliseconds;
            double wallDeltaMs = (nowUtc - _lastCpuSampleUtc).TotalMilliseconds;

            _lastCpuTime = currentCpu;
            _lastCpuSampleUtc = nowUtc;

            if (wallDeltaMs <= 0.0)
            {
                _cpuUsageLabel.Text = "CPU --.-%";
                return;
            }

            // Normalize by logical processor count so 100% ≈ full machine (Task Manager / OBS style)
            int coreCount = Math.Max(1, System.Environment.ProcessorCount);
            double cpuPercent = Math.Clamp((cpuDeltaMs / wallDeltaMs) * 100.0 / coreCount, 0.0, 100.0);

            _cpuUsageLabel.Text = $"CPU {cpuPercent:F1}%";
            _cpuUsageLabel.TooltipText =
                $"CPU usage (this process)\n" +
                $"{cpuPercent:F1}% of total system capacity\n" +
                $"Logical processors: {coreCount}";
        }
        catch (Exception ex)
        {
            GD.Print($"Footer:UpdateResourceUsage - {ex.Message}");
            _cpuUsageLabel.Text = "CPU --.-%";
            _memoryUsageLabel.Text = "MEM -- MB";
        }
    }
    
    
    private void OnLogCountToggled(Boolean @toggle)
    {
        if (@toggle == true){
            if (_logWindow == null)
            {
                GD.Print("Loading settings window scene");
                _logWindow = SceneLoader.LoadScene("uid://cg8mrxu40hjf", out string error); // Loads settings window
                _logWindow.TreeExiting += OnLogWindowClosed;
                AddChild(_logWindow);
            }
            else {
                _logWindow.GetWindow().Show();
            }
        }
        if (@toggle == false)
        {
            _logWindow?.QueueFree();
        }
    }

    private void OnLogWindowClosed()
    {
        GD.Print($"Footer:OnLogWindowClosed");
        _logWindow = null;
        _logCountButton.ButtonPressed = false;
    }

    private void ToggleLogWindow()
    {
        _logCountButton.ButtonPressed = !_logCountButton.ButtonPressed;
    }

    private void _syncHotkeys()
    {
        string logHotkey = GlobalData.ParseHotkey("ToggleLog");
        _logCountButton.TooltipText = "Log, click to open full log" + (!string.IsNullOrEmpty(logHotkey) ? "\nHotkey: " + logHotkey : "");

        var logPrintout = GetNode<Button>("%LogPrintout");
        _logPrintoutBaseTooltip = "Log";
        if (!string.IsNullOrEmpty(logHotkey))
            _logPrintoutBaseTooltip += "\nHotkey: " + logHotkey;
        logPrintout.TooltipText = _logPrintoutBaseTooltip;
    }

    /// <summary>
    /// Updates the DevicesFooterButton tooltip and color with the status of used audio devices and video outputs.
    /// Green (🟢) = connected/available.
    /// Red (🔴) = configured/used but target not currently available.
    /// Tints the button green (Success) when all are OK, red (Danger) if any problems.
    /// </summary>
    private void UpdateDevicesFooterTooltip()
    {
        if (_devicesFooterButton == null) return;

        var audioStatuses = _audioDevices?.GetUsedAudioDeviceStatuses() ?? new Dictionary<string, bool>();
        var videoStatuses = _displaysManager?.GetVideoOutputStatuses() ?? new Dictionary<string, bool>();

        if (audioStatuses.Count == 0 && videoStatuses.Count == 0)
        {
            _devicesFooterButton.TooltipText = "Devices\n\nNo audio or video devices are currently used or configured.";
            _devicesFooterButton.AddThemeColorOverride("font_color", GlobalStyles.Success);
            return;
        }

        bool hasProblem = false;
        string tooltip = "Devices:\n";

        if (audioStatuses.Count > 0)
        {
            tooltip += "\nAudio Devices:\n";
            foreach (var entry in audioStatuses.OrderBy(e => e.Key))
            {
                bool connected = entry.Value;
                if (!connected) hasProblem = true;

                string indicator = connected ? "🟢 " : "🔴 ";
                string status = connected ? "connected" : "being used but not connected";
                tooltip += $"{indicator}{entry.Key} ({status})\n";
            }
        }

        if (videoStatuses.Count > 0)
        {
            tooltip += "\nVideo Outputs:\n";
            foreach (var entry in videoStatuses.OrderBy(e => e.Key))
            {
                bool connected = entry.Value;
                if (!connected) hasProblem = true;

                string indicator = connected ? "🟢 " : "🔴 ";
                string status = connected ? "connected" : "target monitor unavailable";
                tooltip += $"{indicator}{entry.Key} ({status})\n";
            }
        }

        _devicesFooterButton.TooltipText = tooltip.TrimEnd('\n', '\r');

        if (hasProblem)
        {
            _devicesFooterButton.AddThemeColorOverride("font_color", GlobalStyles.Danger);
        }
        else
        {
            _devicesFooterButton.AddThemeColorOverride("font_color", GlobalStyles.Success);
        }
    }
}
