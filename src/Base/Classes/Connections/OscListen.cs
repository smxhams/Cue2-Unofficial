//==================================================================================//
// OscListen.cs                                                                     //
// This file is part of Cue2                                                        //
// http://cue2.live/                                                                //
//==================================================================================//

using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using Cue2.Shared;
using Godot;
using Godot.Collections;
using Rug.Osc;

namespace Cue2.Base.Classes.Connections;

/// <summary>
/// Normalized OSC input message used for monitoring, capture, and Input Map triggers.
/// </summary>
public readonly struct OscInputMessage
{
    public string Address { get; init; }
    public string ArgsDisplay { get; init; }
    public int ArgCount { get; init; }

    /// <summary>
    /// First argument coerced to double when possible (int/float/double/numeric string).
    /// Used by built-in commands for optional fade / seek values.
    /// </summary>
    public double? FirstFloat { get; init; }

    public bool IsValid => !string.IsNullOrEmpty(Address);
}

/// <summary>
/// Application-wide OSC receive service. Listens on a configurable UDP port, marshals
/// messages to the main thread for monitoring / capture / built-in show-control paths /
/// Input Map action triggers.
/// </summary>
/// <remarks>
/// Mirrors <see cref="MidiManager"/> patterns: monitor log signal, capture mode, and
/// show-scoped Input Map bindings. Receive runs on a background thread; all signals fire
/// on the Godot main thread via <see cref="_Process"/>.
/// Fixed built-in paths are documented in <see cref="BuiltInCommandCatalog"/>
/// (see also Settings → OSC Listener).
/// </remarks>
public partial class OscListen : Node
{
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;
    private InputActionsListener _inputActionsListener;

    private OscReceiver _receiver;
    private Thread _thread;
    private volatile bool _running;
    private int _port = 7001;
    private string _sessionName = string.Empty;
    private bool _enabled;
    private bool _monitorEnabled = true;
    private bool _isCapturing;

    /// <summary>InputMap action name → optional OSC binding (show-scoped).</summary>
    private readonly System.Collections.Generic.Dictionary<string, OscActionBinding> _inputMapBindings =
        new(StringComparer.Ordinal);

    private readonly ConcurrentQueue<OscInputMessage> _pendingMessages = new();
    private readonly ConcurrentQueue<string> _pendingLogLines = new();

    /// <summary>Maximum lines retained in the in-memory monitor buffer.</summary>
    public const int MaxMonitorLines = 500;

    // ── Public state ────────────────────────────────────────────────────────

    /// <summary>UDP port the receiver binds to (1–65535).</summary>
    public int Port
    {
        get => _port;
        set
        {
            int clamped = Math.Clamp(value, 1, 65535);
            if (_port == clamped) return;
            _port = clamped;
            if (_enabled)
                RestartReceiver();
            EmitSignal(SignalName.OscStateChanged);
        }
    }

    /// <summary>
    /// Optional session name for documentation / path examples (not used for filtering).
    /// </summary>
    public string SessionName
    {
        get => _sessionName;
        set
        {
            string next = value ?? string.Empty;
            if (_sessionName == next) return;
            _sessionName = next;
            EmitSignal(SignalName.OscStateChanged);
        }
    }

    /// <summary>When true, the UDP receiver is open and processing messages.</summary>
    public bool OscListenEnabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (_enabled)
                StartListening();
            else
                StopListening();
            EmitSignal(SignalName.OscStateChanged);
        }
    }

    /// <summary>When true, received OSC messages are queued for the monitor log UI.</summary>
    public bool MonitorEnabled
    {
        get => _monitorEnabled;
        set
        {
            if (_monitorEnabled == value) return;
            _monitorEnabled = value;
            EmitSignal(SignalName.OscStateChanged);
        }
    }

    /// <summary>True while the background receiver thread is active.</summary>
    public bool IsListening => _running && _receiver != null;

    /// <summary>True while waiting for the next OSC message to capture.</summary>
    public bool IsCapturing => _isCapturing;

    // ── Signals ─────────────────────────────────────────────────────────────

    /// <summary>Fired when enable/port/session/monitor/bindings change (main thread).</summary>
    [Signal]
    public delegate void OscStateChangedEventHandler();

    /// <summary>
    /// Fired on the main thread for each monitor line (timestamped OSC summary).
    /// Only raised when <see cref="MonitorEnabled"/> is true (or for system notices).
    /// </summary>
    [Signal]
    public delegate void OscMonitorLineEventHandler(string line);

    /// <summary>
    /// Fired once on the main thread when capture mode receives the next OSC message.
    /// Args: address, argsDisplay.
    /// </summary>
    [Signal]
    public delegate void OscCapturedEventHandler(string address, string argsDisplay);

    // ── Lifecycle ───────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _globalSignals = GetNodeOrNull<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNodeOrNull<GlobalData>("/root/GlobalData");
        _inputActionsListener = GetNodeOrNull<InputActionsListener>("/root/InputActionsListener");
        GD.Print("OscListen:_Ready - OSC listener ready.");
        if (_enabled)
            StartListening();
    }

    public override void _Process(double delta)
    {
        int drained = 0;
        while (drained < 64 && _pendingMessages.TryDequeue(out OscInputMessage msg))
        {
            ProcessMainThreadMessage(msg);
            drained++;
        }

        while (drained < 80 && _pendingLogLines.TryDequeue(out string line))
        {
            EmitSignal(SignalName.OscMonitorLine, line);
            drained++;
        }
    }

    public override void _ExitTree()
    {
        StopListening();
    }

    // ── Capture ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Begins capture mode: the next valid OSC message is reported via <see cref="OscCaptured"/>
    /// and does not fire Input Map actions.
    /// </summary>
    public void StartCapture()
    {
        _isCapturing = true;
        GD.Print("OscListen:StartCapture - Waiting for next OSC message…");
        EnqueueMonitorLine("— Capture armed: waiting for OSC…");
        EmitSignal(SignalName.OscStateChanged);
    }

    /// <summary>Cancels an in-progress OSC capture, if any.</summary>
    public void CancelCapture()
    {
        if (!_isCapturing) return;
        _isCapturing = false;
        EnqueueMonitorLine("— Capture cancelled");
        EmitSignal(SignalName.OscStateChanged);
    }

    /// <summary>Clears any pending monitor log lines that have not yet been emitted.</summary>
    public void ClearPendingMonitorLines()
    {
        while (_pendingLogLines.TryDequeue(out _)) { }
    }

    // ── Message processing ──────────────────────────────────────────────────

    private void ProcessMainThreadMessage(OscInputMessage msg)
    {
        if (!msg.IsValid) return;

        if (_monitorEnabled)
        {
            string line = FormatOscInputMessage(msg);
            EmitSignal(SignalName.OscMonitorLine, line);
        }

        if (_isCapturing)
        {
            _isCapturing = false;
            EmitSignal(SignalName.OscCaptured, msg.Address, msg.ArgsDisplay ?? string.Empty);
            EmitSignal(SignalName.OscStateChanged);
            GD.Print($"OscListen:ProcessMainThreadMessage - Captured {msg.Address} {msg.ArgsDisplay}");
            return;
        }

        // Fixed show-control paths take precedence over user Input Map bindings.
        // Implementation lives in OscListen.BuiltInCommands.cs.
        if (TryFireBuiltInCommands(msg))
            return;

        TryFireInputMapBindings(msg);
    }

    /// <summary>
    /// Invokes project InputMap actions whose OSC Input Map binding matches <paramref name="msg"/>.
    /// </summary>
    private void TryFireInputMapBindings(OscInputMessage msg)
    {
        if (_inputMapBindings.Count == 0) return;

        _inputActionsListener ??= GetNodeOrNull<InputActionsListener>("/root/InputActionsListener");
        if (_inputActionsListener == null) return;

        foreach (var kvp in _inputMapBindings)
        {
            var binding = kvp.Value;
            if (binding == null || !binding.HasBinding) continue;
            if (!binding.Matches(msg.Address, msg.ArgsDisplay)) continue;

            string action = kvp.Key;
            GD.Print($"OscListen:TryFireInputMapBindings - OSC action '{action}' ← {binding.GetDisplay()}");
            if (_inputActionsListener.TryTriggerAction(action))
            {
                _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                    $"OSC Input Map: {action} ← {binding.GetDisplay()}", (int)LogType.Info);
            }
        }
    }

    // ── OSC Input Map ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns a clone of the OSC binding for <paramref name="actionName"/> (never null).
    /// </summary>
    public OscActionBinding GetInputMapBinding(string actionName)
    {
        if (string.IsNullOrEmpty(actionName))
            return OscActionBinding.Unbound();
        if (_inputMapBindings.TryGetValue(actionName, out var b) && b != null)
            return b.Clone();
        return OscActionBinding.Unbound();
    }

    /// <summary>
    /// Replaces the OSC binding for a project InputMap action.
    /// Pass an unbound binding (or null) to clear.
    /// </summary>
    public void SetInputMapBinding(string actionName, OscActionBinding binding)
    {
        if (string.IsNullOrEmpty(actionName)) return;

        if (binding == null || !binding.HasBinding)
            _inputMapBindings.Remove(actionName);
        else
            _inputMapBindings[actionName] = binding.Clone();

        EmitSignal(SignalName.OscStateChanged);
    }

    /// <summary>
    /// Finds another InputMap action that already uses the same OSC pattern.
    /// </summary>
    public string FindConflictingInputMapAction(string excludeAction, OscActionBinding candidate)
    {
        if (candidate == null || !candidate.HasBinding) return null;

        foreach (var kvp in _inputMapBindings)
        {
            if (string.Equals(kvp.Key, excludeAction, StringComparison.Ordinal)) continue;
            var other = kvp.Value;
            if (other == null || !other.HasBinding) continue;
            if (BindingsConflict(candidate, other))
                return kvp.Key;
        }
        return null;
    }

    private static bool BindingsConflict(OscActionBinding a, OscActionBinding b)
    {
        if (!string.Equals(a.Address, b.Address, StringComparison.Ordinal)) return false;
        // If either ignores args, they conflict on address alone.
        if (!a.MatchArgs || !b.MatchArgs) return true;
        return string.Equals(a.ArgsDisplay ?? string.Empty, b.ArgsDisplay ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>Serializes all OSC Input Map bindings for history / showfile.</summary>
    public Dictionary GetInputMapBindingsData()
    {
        var dict = new Dictionary();
        foreach (var kvp in _inputMapBindings)
        {
            if (kvp.Value == null || !kvp.Value.HasBinding) continue;
            dict[kvp.Key] = kvp.Value.ToDict();
        }
        return dict;
    }

    /// <summary>Restores OSC Input Map bindings from history / showfile.</summary>
    public void LoadInputMapBindingsData(Dictionary data)
    {
        _inputMapBindings.Clear();
        if (data == null)
        {
            EmitSignal(SignalName.OscStateChanged);
            return;
        }

        foreach (var key in data.Keys)
        {
            string action = key.AsString();
            if (string.IsNullOrEmpty(action)) continue;
            if (data[key].VariantType != Variant.Type.Dictionary) continue;
            var binding = OscActionBinding.FromDict(data[key].AsGodotDictionary());
            if (binding.HasBinding)
                _inputMapBindings[action] = binding;
        }

        EmitSignal(SignalName.OscStateChanged);
        GD.Print($"OscListen:LoadInputMapBindingsData - {_inputMapBindings.Count} binding(s)");
    }

    // ── Receiver ────────────────────────────────────────────────────────────

    private void StartListening()
    {
        if (_running) return;
        try
        {
            _receiver = new OscReceiver(_port);
            _receiver.Connect();
            GD.Print($"OscListen:StartListening - Receiver connected on port {_port}");
            EnqueueMonitorLine($"— Listening on UDP port {_port}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"OscListen:StartListening - Failed to connect: {ex.Message}");
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"OSC Listener: failed to bind port {_port}: {ex.Message}", (int)LogType.Error);
            _receiver = null;
            _enabled = false;
            return;
        }

        _running = true;
        _thread = new Thread(ReceiveLoop)
        {
            IsBackground = true,
            Name = "Cue2-OscListen"
        };
        _thread.Start();
    }

    private void StopListening()
    {
        if (!_running && _receiver == null) return;
        _running = false;
        try
        {
            _receiver?.Close();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"OscListen:StopListening - Close: {ex.Message}");
        }

        try
        {
            _thread?.Join(1500);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"OscListen:StopListening - Join: {ex.Message}");
        }

        _receiver = null;
        _thread = null;
        EnqueueMonitorLine("— Listener stopped");
        GD.Print("OscListen:StopListening - Stopped listening");
    }

    private void RestartReceiver()
    {
        StopListening();
        if (_enabled)
            StartListening();
    }

    private void ReceiveLoop()
    {
        GD.Print("OscListen:ReceiveLoop - Thread started");
        while (_running)
        {
            try
            {
                var receiver = _receiver;
                if (receiver == null) break;

                OscPacket packet = receiver.Receive();
                if (!_running) break;

                if (packet is OscMessage oscMessage)
                {
                    _pendingMessages.Enqueue(FromOscMessage(oscMessage));
                }
                else if (packet is OscBundle bundle)
                {
                    foreach (var nested in bundle)
                    {
                        if (nested is OscMessage nestedMsg)
                            _pendingMessages.Enqueue(FromOscMessage(nestedMsg));
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_running) break;
                // OscReceiver throws when closed; avoid spamming if shutting down.
                GD.PrintErr($"OscListen:ReceiveLoop - {ex.Message}");
                EnqueueMonitorLine($"— Receive error: {ex.Message}");
                break;
            }
        }
        GD.Print("OscListen:ReceiveLoop - Thread exiting");
    }

    private void EnqueueMonitorLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        string stamped = $"{DateTime.Now:HH:mm:ss.fff}  {line}";
        _pendingLogLines.Enqueue(stamped);
    }

    private static string FormatOscInputMessage(OscInputMessage msg)
    {
        var sb = new StringBuilder(96);
        sb.Append(DateTime.Now.ToString("HH:mm:ss.fff"));
        sb.Append("  ");
        sb.Append(msg.Address);
        if (!string.IsNullOrEmpty(msg.ArgsDisplay))
        {
            sb.Append("  ");
            sb.Append(msg.ArgsDisplay);
        }
        return sb.ToString();
    }

    /// <summary>Builds a normalized <see cref="OscInputMessage"/> from a Rug.Osc packet.</summary>
    private static OscInputMessage FromOscMessage(OscMessage message)
    {
        return new OscInputMessage
        {
            Address = message?.Address ?? string.Empty,
            ArgsDisplay = FormatArgs(message),
            ArgCount = message?.Count ?? 0,
            FirstFloat = TryCoerceFirstFloat(message)
        };
    }

    /// <summary>Formats OSC message arguments for monitor / binding display.</summary>
    public static string FormatArgs(OscMessage message)
    {
        if (message == null || message.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        for (int i = 0; i < message.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            object arg = message[i];
            sb.Append(FormatArg(arg));
        }
        return sb.ToString();
    }

    private static string FormatArg(object arg)
    {
        if (arg == null) return "null";
        return arg switch
        {
            string s => $"\"{s}\"",
            float f => f.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
            double d => d.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => arg.ToString()
        };
    }

    private static double? TryCoerceFirstFloat(OscMessage message)
    {
        if (message == null || message.Count == 0) return null;
        object arg = message[0];
        return arg switch
        {
            float f => f,
            double d => d,
            int i => i,
            long l => l,
            string s when double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v) => v,
            _ => null
        };
    }

    // ── Serialization ───────────────────────────────────────────────────────

    /// <summary>Serializes listener enable/port/session for the showfile.</summary>
    public Dictionary GetData()
    {
        var saveDict = new Dictionary();
        saveDict["OscListenEnabled"] = _enabled;
        saveDict["Port"] = _port;
        saveDict["SessionName"] = _sessionName ?? string.Empty;
        saveDict["MonitorEnabled"] = _monitorEnabled;
        return saveDict;
    }

    /// <summary>Restores listener state from showfile / history.</summary>
    public void LoadFromData(Dictionary data)
    {
        if (data == null) return;

        if (data.TryGetValue("Port", out var portVar))
            _port = Math.Clamp(portVar.AsInt32(), 1, 65535);

        if (data.TryGetValue("SessionName", out var sessionVar))
            _sessionName = sessionVar.AsString() ?? string.Empty;

        if (data.TryGetValue("MonitorEnabled", out var monVar))
            _monitorEnabled = monVar.AsBool();

        bool wantEnabled = data.TryGetValue("OscListenEnabled", out var enVar) && enVar.AsBool();
        // Apply enable last so port is correct before bind.
        if (wantEnabled != _enabled || (wantEnabled && !_running))
        {
            _enabled = wantEnabled;
            if (_enabled)
            {
                StopListening();
                StartListening();
            }
            else
            {
                StopListening();
            }
        }

        EmitSignal(SignalName.OscStateChanged);
        GD.Print($"OscListen:LoadFromData - enabled={_enabled} port={_port}");
    }

    /// <summary>Restores OSC listen defaults for a new empty session.</summary>
    public void ResetToDefaults()
    {
        _isCapturing = false;
        _enabled = false;
        StopListening();
        _port = 7001;
        _sessionName = string.Empty;
        _monitorEnabled = true;
        _inputMapBindings.Clear();
        EmitSignal(SignalName.OscStateChanged);
        GD.Print("OscListen:ResetToDefaults - OSC listen disabled, port 7001.");
    }

    // ── Compatibility helpers (static-style call sites) ──────────────────────

    /// <summary>Enables or disables the listener (instance API).</summary>
    public void SetEnabled(bool enabled) => OscListenEnabled = enabled;

    /// <summary>Sets the listen port and restarts if active.</summary>
    public void SetPort(int port) => Port = port;
}
