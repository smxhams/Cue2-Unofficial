//==================================================================================//
// MidiManager.cs                                                                   //
// This file is part of Cue2                                                        //
// http://cue2.live/                                                                //
//==================================================================================//

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Godot;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;

namespace Cue2.Shared;

/// <summary>
/// Normalized MIDI input message used for monitoring, capture, and cue triggers.
/// Channel is 1–16. Data1/Data2 are 0–127.
/// </summary>
public readonly struct MidiInputMessage
{
    public string DeviceName { get; init; }
    public MidiTriggerMessageType MessageType { get; init; }
    public int Channel { get; init; }
    public int Data1 { get; init; }
    public int Data2 { get; init; }

    public bool IsValid => Channel >= 1 && Channel <= 16 && Data1 >= 0 && Data1 <= 127;
}

/// <summary>
/// Application-wide MIDI input service (Melanchall.DryWetMidi).
/// Supports multiple session input devices: enumerate available ports, add/remove
/// devices to the session, open all when MIDI is enabled, and forward events for
/// monitoring and (later) cue triggers.
/// </summary>
/// <remarks>
/// DryWetMidi ships native libs that NuGet copies next to the managed assembly under
/// <c>.godot/mono/temp/bin/</c> (editor) or <c>data_Cue2_*/</c> (export). Godot's process
/// DLL search path may not include those folders, so we load natives via
/// <see cref="NativeLibPaths"/> (export dirs first, then <c>res://bin/{platform}/</c>).
/// </remarks>
public partial class MidiManager : Node
{
    private GlobalSignals _globalSignals;

    /// <summary>Device name → open input handle (only entries currently listening).</summary>
    private readonly Dictionary<string, InputDevice> _openDevices =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Device name → open output handle.</summary>
    private readonly Dictionary<string, OutputDevice> _openOutputs =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reverse map so ErrorOccurred / DeviceRemoved can resolve a name when
    /// <see cref="MidiDevice.Name"/> throws on a removed device.
    /// </summary>
    private readonly Dictionary<InputDevice, string> _openDeviceNames = new();
    private readonly Dictionary<OutputDevice, string> _openOutputNames = new();

    /// <summary>Session-configured input names (order preserved; may include offline devices).</summary>
    private readonly List<string> _sessionInputNames = new();

    /// <summary>Session-configured output names (order preserved; may include offline devices).</summary>
    private readonly List<string> _sessionOutputNames = new();

    /// <summary>InputMap action name → optional MIDI binding (show-scoped).</summary>
    private readonly Dictionary<string, MidiActionBinding> _inputMapBindings =
        new(StringComparer.Ordinal);

    private GlobalData _globalData;
    private InputActionsListener _inputActionsListener;

    private bool _midiEnabled;
    private bool _monitorEnabled = true;
    private bool _nativeReady;
    private static bool _resolverRegistered;
    private bool _isCapturing;
    private bool _watcherHooked;

    /// <summary>Main-thread work queued from DryWetMidi background callbacks.</summary>
    private readonly ConcurrentQueue<Action> _mainThreadActions = new();

    private readonly ConcurrentQueue<string> _pendingLogLines = new();
    private readonly ConcurrentQueue<MidiInputMessage> _pendingMessages = new();
    private readonly List<string> _availableInputNames = new();
    private readonly List<string> _availableOutputNames = new();

    /// <summary>Deferred note-offs: (device, channel0-15, note, fireAtTime).</summary>
    private readonly List<(string Device, int Channel0, int Note, double FireAt)> _pendingNoteOffs = new();

    /// <summary>Seconds between automatic availability polls (reconnect / unplug detection).</summary>
    private const double DevicePollIntervalSec = 1.5;

    private double _devicePollAccum;

    /// <summary>Maximum lines retained in the in-memory monitor buffer.</summary>
    public const int MaxMonitorLines = 500;

    /// <summary>
    /// When true, all session input devices that are present are opened and receive events.
    /// </summary>
    public bool MidiEnabled
    {
        get => _midiEnabled;
        set
        {
            if (_midiEnabled == value) return;
            _midiEnabled = value;
            if (_midiEnabled)
            {
                OpenAllSessionInputs();
                OpenAllSessionOutputs();
            }
            else
            {
                CloseAllInputs();
                CloseAllOutputs();
            }
            EmitSignal(SignalName.MidiStateChanged);
        }
    }

    /// <summary>
    /// When true, received MIDI events are queued for the monitor log UI.
    /// </summary>
    public bool MonitorEnabled
    {
        get => _monitorEnabled;
        set
        {
            if (_monitorEnabled == value) return;
            _monitorEnabled = value;
            EmitSignal(SignalName.MidiStateChanged);
        }
    }

    /// <summary>True when at least one session input is open and listening.</summary>
    public bool IsAnyInputOpen => _openDevices.Count > 0 &&
                                  _openDevices.Values.Any(d => d != null && d.IsListeningForEvents);

    /// <summary>Number of currently open listening input devices.</summary>
    public int OpenInputCount => _openDevices.Count(kv => IsOpenHandleHealthy(kv.Key));

    /// <summary>Number of currently open output devices.</summary>
    public int OpenOutputCount => _openOutputs.Count(kv => IsOpenOutputHealthy(kv.Key));

    /// <summary>Session-configured input device names (may include offline devices).</summary>
    public IReadOnlyList<string> SessionInputNames => _sessionInputNames;

    /// <summary>Session-configured output device names (may include offline devices).</summary>
    public IReadOnlyList<string> SessionOutputNames => _sessionOutputNames;

    /// <summary>Currently available system MIDI input names (last enumeration).</summary>
    public IReadOnlyList<string> AvailableInputNames => _availableInputNames;

    /// <summary>Currently available system MIDI output names (last enumeration).</summary>
    public IReadOnlyList<string> AvailableOutputNames => _availableOutputNames;

    /// <summary>
    /// Available system inputs that are not already in the session (for the Add dropdown).
    /// </summary>
    public IReadOnlyList<string> AvailableInputsNotInSession =>
        _availableInputNames
            .Where(n => !_sessionInputNames.Contains(n, StringComparer.OrdinalIgnoreCase))
            .ToList();

    /// <summary>
    /// Available system outputs that are not already in the session (for the Add dropdown).
    /// </summary>
    public IReadOnlyList<string> AvailableOutputsNotInSession =>
        _availableOutputNames
            .Where(n => !_sessionOutputNames.Contains(n, StringComparer.OrdinalIgnoreCase))
            .ToList();

    /// <summary>True when the platform native library was found and loaded successfully.</summary>
    public bool IsNativeReady => _nativeReady;

    /// <summary>Fired when device list or enable/monitor state changes (main thread).</summary>
    [Signal]
    public delegate void MidiStateChangedEventHandler();

    /// <summary>
    /// Fired on the main thread for each monitor line (timestamped MIDI summary).
    /// Only raised when <see cref="MonitorEnabled"/> is true.
    /// </summary>
    [Signal]
    public delegate void MidiMonitorLineEventHandler(string line);

    /// <summary>
    /// Fired once on the main thread when capture mode receives the next MIDI message.
    /// Args: deviceName, messageType (int), channel (1–16), data1, data2.
    /// </summary>
    [Signal]
    public delegate void MidiCapturedEventHandler(
        string deviceName, int messageType, int channel, int data1, int data2);

    /// <summary>True while waiting for the next MIDI message to capture for a cue trigger.</summary>
    public bool IsCapturing => _isCapturing;

    public override void _Ready()
    {
        _globalSignals = GetNodeOrNull<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNodeOrNull<GlobalData>("/root/GlobalData");
        _inputActionsListener = GetNodeOrNull<InputActionsListener>("/root/InputActionsListener");
        _nativeReady = EnsureNativeLibraryLoaded();
        if (_nativeReady)
        {
            HookDevicesWatcher();
            RefreshDeviceList();
        }
        else
            GD.PrintErr("MidiManager:_Ready - Native MIDI library unavailable; device list disabled.");
        GD.Print("MidiManager:_Ready - MIDI manager ready.");
    }

    public override void _Process(double delta)
    {
        // Marshal device-watcher / error callbacks onto the Godot main thread first.
        int actions = 0;
        while (actions < 32 && _mainThreadActions.TryDequeue(out Action action))
        {
            try { action?.Invoke(); }
            catch (Exception ex) { GD.PrintErr($"MidiManager:_Process - deferred action: {ex.Message}"); }
            actions++;
        }

        // Drain structured messages (capture + cue triggers + optional monitor lines).
        int drained = 0;
        while (drained < 64 && _pendingMessages.TryDequeue(out MidiInputMessage msg))
        {
            ProcessMainThreadMessage(msg);
            drained++;
        }

        // Drain extra monitor-only lines (open/close notices).
        while (drained < 80 && _pendingLogLines.TryDequeue(out string line))
        {
            EmitSignal(SignalName.MidiMonitorLine, line);
            drained++;
        }

        // Deferred note-offs for Note On components with a duration.
        ProcessPendingNoteOffs(delta);

        // Periodic reconcile: catches unplug/replug when DevicesWatcher is silent or incomplete
        // (Windows Equals/Name limitations, missed USB events, zombie open handles).
        if (_nativeReady && (_midiEnabled || _sessionInputNames.Count > 0 || _sessionOutputNames.Count > 0))
        {
            _devicePollAccum += delta;
            if (_devicePollAccum >= DevicePollIntervalSec)
            {
                _devicePollAccum = 0;
                ReconcileSessionDevices(emitStateIfChanged: true, reason: "poll");
            }
        }
    }

    public override void _ExitTree()
    {
        UnhookDevicesWatcher();
        CancelCapture();
        _pendingNoteOffs.Clear();
        CloseAllInputs();
        CloseAllOutputs();
    }

    private void ProcessPendingNoteOffs(double delta)
    {
        if (_pendingNoteOffs.Count == 0) return;
        // FireAt is stored as remaining seconds.
        for (int i = _pendingNoteOffs.Count - 1; i >= 0; i--)
        {
            var (device, ch0, note, remaining) = _pendingNoteOffs[i];
            remaining -= delta;
            if (remaining > 0)
            {
                _pendingNoteOffs[i] = (device, ch0, note, remaining);
                continue;
            }

            _pendingNoteOffs.RemoveAt(i);
            try
            {
                if (_openOutputs.TryGetValue(device, out var outDev) && outDev != null)
                {
                    outDev.SendEvent(new NoteOffEvent((SevenBitNumber)note, (SevenBitNumber)0)
                    {
                        Channel = (FourBitNumber)Math.Clamp(ch0, 0, 15)
                    });
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"MidiManager:ProcessPendingNoteOffs - {device}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Begins capture mode: the next valid MIDI message is reported via <see cref="MidiCaptured"/>
    /// and does not fire cue triggers.
    /// </summary>
    public void StartCapture()
    {
        _isCapturing = true;
        GD.Print("MidiManager:StartCapture - Waiting for next MIDI message…");
        EnqueueMonitorLine("— Capture armed: waiting for MIDI…");
        EmitSignal(SignalName.MidiStateChanged);
    }

    /// <summary>
    /// Cancels an in-progress MIDI capture, if any.
    /// </summary>
    public void CancelCapture()
    {
        if (!_isCapturing) return;
        _isCapturing = false;
        EnqueueMonitorLine("— Capture cancelled");
        EmitSignal(SignalName.MidiStateChanged);
    }

    private void ProcessMainThreadMessage(MidiInputMessage msg)
    {
        if (!msg.IsValid) return;

        // Monitor log (always when enabled).
        if (_monitorEnabled)
        {
            string line = FormatMidiInputMessage(msg);
            EmitSignal(SignalName.MidiMonitorLine, line);
        }

        // Capture consumes the event for the UI and skips cue GO.
        if (_isCapturing)
        {
            _isCapturing = false;
            EmitSignal(SignalName.MidiCaptured,
                msg.DeviceName ?? string.Empty,
                (int)msg.MessageType,
                msg.Channel,
                msg.Data1,
                msg.Data2);
            EmitSignal(SignalName.MidiStateChanged);
            GD.Print($"MidiManager:ProcessMainThreadMessage - Captured {msg.MessageType} ch={msg.Channel} d1={msg.Data1} d2={msg.Data2}");
            return;
        }

        // App InputMap actions (Go, Save, …) then cue-specific MIDI triggers.
        TryFireInputMapBindings(msg);
        TryFireCueTriggers(msg);
    }

    /// <summary>
    /// Invokes project InputMap actions whose MIDI Input Map binding matches <paramref name="msg"/>.
    /// </summary>
    private void TryFireInputMapBindings(MidiInputMessage msg)
    {
        if (_inputMapBindings.Count == 0) return;

        // Lazy resolve listener if _Ready order differed.
        _inputActionsListener ??= GetNodeOrNull<InputActionsListener>("/root/InputActionsListener");
        if (_inputActionsListener == null) return;

        foreach (var kvp in _inputMapBindings)
        {
            var binding = kvp.Value;
            if (binding == null || !binding.HasBinding) continue;
            if (!binding.Matches(msg)) continue;

            string action = kvp.Key;
            GD.Print($"MidiManager:TryFireInputMapBindings - MIDI action '{action}' ← {binding.GetDisplay()}");
            if (_inputActionsListener.TryTriggerAction(action))
            {
                _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                    $"MIDI Input Map: {action} ← {binding.GetDisplay()}", (int)LogType.Info);
            }
        }
    }

    // ── MIDI Input Map (project actions) ────────────────────────────────────

    /// <summary>
    /// Returns a clone of the MIDI binding for <paramref name="actionName"/> (never null).
    /// </summary>
    public MidiActionBinding GetInputMapBinding(string actionName)
    {
        if (string.IsNullOrEmpty(actionName))
            return MidiActionBinding.Unbound();
        if (_inputMapBindings.TryGetValue(actionName, out var b) && b != null)
            return b.Clone();
        return MidiActionBinding.Unbound();
    }

    /// <summary>
    /// Replaces the MIDI binding for a project InputMap action.
    /// Pass an unbound binding (or null) to clear.
    /// </summary>
    public void SetInputMapBinding(string actionName, MidiActionBinding binding)
    {
        if (string.IsNullOrEmpty(actionName)) return;

        if (binding == null || !binding.HasBinding)
        {
            _inputMapBindings.Remove(actionName);
        }
        else
        {
            _inputMapBindings[actionName] = binding.Clone();
        }

        EmitSignal(SignalName.MidiStateChanged);
    }

    /// <summary>
    /// Finds another InputMap action that already uses the same MIDI pattern.
    /// </summary>
    /// <param name="excludeAction">Action being edited (ignored).</param>
    /// <param name="candidate">Proposed binding.</param>
    /// <returns>Conflicting action name, or null.</returns>
    public string FindConflictingInputMapAction(string excludeAction, MidiActionBinding candidate)
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

    private static bool BindingsConflict(MidiActionBinding a, MidiActionBinding b)
    {
        if (a.MessageType != b.MessageType) return false;
        // Channels conflict if equal, or either is "any" (0).
        if (a.Channel != 0 && b.Channel != 0 && a.Channel != b.Channel) return false;
        if (a.Data1 != b.Data1) return false;
        // If either ignores value, they conflict on type+channel+data1 alone.
        if (!a.MatchValue || !b.MatchValue) return true;
        return a.Data2 == b.Data2;
    }

    /// <summary>Serializes all MIDI Input Map bindings for history / showfile.</summary>
    public Godot.Collections.Dictionary GetInputMapBindingsData()
    {
        var dict = new Godot.Collections.Dictionary();
        foreach (var kvp in _inputMapBindings)
        {
            if (kvp.Value == null || !kvp.Value.HasBinding) continue;
            dict[kvp.Key] = kvp.Value.ToDict();
        }
        return dict;
    }

    /// <summary>Restores MIDI Input Map bindings from history / showfile.</summary>
    public void LoadInputMapBindingsData(Godot.Collections.Dictionary data)
    {
        _inputMapBindings.Clear();
        if (data == null)
        {
            EmitSignal(SignalName.MidiStateChanged);
            return;
        }

        foreach (var key in data.Keys)
        {
            string action = key.AsString();
            if (string.IsNullOrEmpty(action)) continue;
            if (data[key].VariantType != Variant.Type.Dictionary) continue;
            var binding = MidiActionBinding.FromDict(data[key].AsGodotDictionary());
            if (binding.HasBinding)
                _inputMapBindings[action] = binding;
        }

        EmitSignal(SignalName.MidiStateChanged);
        GD.Print($"MidiManager:LoadInputMapBindingsData - {_inputMapBindings.Count} binding(s)");
    }

    /// <summary>
    /// GO's every armed cue whose MIDI trigger matches <paramref name="msg"/>.
    /// </summary>
    private void TryFireCueTriggers(MidiInputMessage msg)
    {
        if (CueList.CueIndex == null || CueList.CueIndex.Count == 0) return;

        var executor = _globalData?.CueCommandExectutor;
        if (executor == null) return;

        foreach (Cue cue in CueList.CueIndex.Values)
        {
            if (cue == null || !cue.CanFireMidiTrigger) continue;
            if (!cue.MidiTriggerMatches(msg.MessageType, msg.Channel, msg.Data1, msg.Data2, msg.DeviceName))
                continue;

            GD.Print($"MidiManager:TryFireCueTriggers - MIDI GO: \"{cue.Name}\" (id={cue.Id})");
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"MIDI trigger: \"{cue.Name}\" ← {cue.GetMidiTriggerDisplay()}", (int)LogType.Info);
            executor.ActivateSequenceFrom(cue);
        }
    }

    private static string FormatMidiInputMessage(MidiInputMessage msg)
    {
        var sb = new StringBuilder(96);
        sb.Append(DateTime.Now.ToString("HH:mm:ss.fff"));
        sb.Append("  ");
        if (!string.IsNullOrEmpty(msg.DeviceName))
        {
            sb.Append('[');
            sb.Append(msg.DeviceName);
            sb.Append("] ");
        }

        switch (msg.MessageType)
        {
            case MidiTriggerMessageType.NoteOn:
                sb.Append($"NoteOn   ch={msg.Channel} note={msg.Data1} vel={msg.Data2}");
                break;
            case MidiTriggerMessageType.NoteOff:
                sb.Append($"NoteOff  ch={msg.Channel} note={msg.Data1} vel={msg.Data2}");
                break;
            case MidiTriggerMessageType.ControlChange:
                sb.Append($"CC       ch={msg.Channel} cc={msg.Data1} val={msg.Data2}");
                break;
            case MidiTriggerMessageType.ProgramChange:
                sb.Append($"Program  ch={msg.Channel} program={msg.Data1}");
                break;
            default:
                sb.Append($"{msg.MessageType} ch={msg.Channel} {msg.Data1} {msg.Data2}");
                break;
        }

        return sb.ToString();
    }

    // ── Native load (Godot path) ────────────────────────────────────────────

    /// <summary>
    /// Loads <c>Melanchall_DryWetMidi_Native*</c> via <see cref="NativeLibPaths"/> and
    /// registers a DllImport resolver so DryWetMidi P/Invoke finds it under Godot.
    /// </summary>
    private bool EnsureNativeLibraryLoaded()
    {
        try
        {
            RegisterDllImportResolver();

            string path = ResolveNativeLibraryPath(out string platformLabel);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                GD.PrintErr($"MidiManager:EnsureNativeLibraryLoaded - Native library missing for {platformLabel}. " +
                            "Expected Melanchall_DryWetMidi_Native* beside the app or under res://bin/ " +
                            "(see docs/export-packaging.md).");
                _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                    $"MIDI: native library not found for {platformLabel}", (int)LogType.Error);
                return false;
            }

            nint handle = NativeLibrary.Load(path);
            GD.Print($"MidiManager:EnsureNativeLibraryLoaded - Loaded {path} (handle={handle}) [{platformLabel}]");
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MidiManager:EnsureNativeLibraryLoaded - {ex.Message}");
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"MIDI: failed to load native library — {ex.Message}", (int)LogType.Error);
            return false;
        }
    }

    private static void RegisterDllImportResolver()
    {
        if (_resolverRegistered) return;
        try
        {
            Assembly dryWetAssembly = typeof(InputDevice).Assembly;
            NativeLibrary.SetDllImportResolver(dryWetAssembly, ResolveDryWetMidiNative);
            _resolverRegistered = true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MidiManager:RegisterDllImportResolver - {ex.Message}");
        }
    }

    private static IntPtr ResolveDryWetMidiNative(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (string.IsNullOrEmpty(libraryName) ||
            !libraryName.StartsWith("Melanchall_DryWetMidi_Native", StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        try
        {
            string path = ResolveNativeLibraryPath(out _);
            if (!string.IsNullOrEmpty(path) && File.Exists(path) &&
                NativeLibrary.TryLoad(path, out IntPtr handle))
            {
                return handle;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MidiManager:ResolveDryWetMidiNative - {ex.Message}");
        }

        return IntPtr.Zero;
    }

    private static string ResolveNativeLibraryPath(out string platformLabel)
    {
        string fileName = NativeLibPaths.GetDryWetMidiNativeFileName(out platformLabel);
        if (string.IsNullOrEmpty(fileName))
            return string.Empty;

        string platformDir = NativeLibPaths.GetPlatformDir(out _);
        string path = NativeLibPaths.FindLibraryFile(fileName, platformDir, out string foundDir, out var tried);
        if (string.IsNullOrEmpty(path))
        {
            GD.PrintErr(
                $"MidiManager:ResolveNativeLibraryPath - {fileName} not found. " +
                $"Tried: {NativeLibPaths.FormatTriedDirectories(tried)}");
        }
        else
        {
            GD.Print($"MidiManager:ResolveNativeLibraryPath - Found {fileName} in {foundDir}");
        }

        return path;
    }

    // ── Enumeration, hot-plug & session device list ─────────────────────────

    /// <summary>
    /// Re-enumerates system MIDI inputs and reconciles session open/closed state.
    /// Offline session devices stay listed; present ones reopen when MIDI is enabled.
    /// </summary>
    public void RefreshDeviceList()
    {
        ReconcileSessionDevices(emitStateIfChanged: true, reason: "refresh", forceStateEmit: true);
    }

    /// <summary>
    /// Subscribes to DryWetMidi <see cref="DevicesWatcher"/> for add/remove notifications.
    /// Callbacks may arrive off-thread and are marshalled to the main thread.
    /// </summary>
    private void HookDevicesWatcher()
    {
        if (_watcherHooked) return;
        try
        {
            DevicesWatcher.Instance.DeviceAdded += OnWatcherDeviceAdded;
            DevicesWatcher.Instance.DeviceRemoved += OnWatcherDeviceRemoved;
            _watcherHooked = true;
            GD.Print("MidiManager:HookDevicesWatcher - DevicesWatcher subscribed.");
        }
        catch (Exception ex)
        {
            // Platform may not support watching; poll still covers reconnect.
            GD.PrintErr($"MidiManager:HookDevicesWatcher - {ex.Message}");
            _watcherHooked = false;
        }
    }

    private void UnhookDevicesWatcher()
    {
        if (!_watcherHooked) return;
        try
        {
            DevicesWatcher.Instance.DeviceAdded -= OnWatcherDeviceAdded;
            DevicesWatcher.Instance.DeviceRemoved -= OnWatcherDeviceRemoved;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MidiManager:UnhookDevicesWatcher - {ex.Message}");
        }
        finally
        {
            _watcherHooked = false;
        }
    }

    private void OnWatcherDeviceAdded(object sender, DeviceAddedRemovedEventArgs e)
    {
        // May run on a non-Godot thread.
        string name = null;
        string kind = "device";
        try
        {
            if (e?.Device is InputDevice input)
            {
                name = SafeGetDeviceName(input);
                kind = "input";
            }
            else if (e?.Device is OutputDevice output)
            {
                name = SafeGetDeviceName(output);
                kind = "output";
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MidiManager:OnWatcherDeviceAdded - {ex.Message}");
        }

        string captured = name;
        string capturedKind = kind;
        _mainThreadActions.Enqueue(() =>
        {
            if (!string.IsNullOrEmpty(captured))
            {
                GD.Print($"MidiManager:OnWatcherDeviceAdded - {capturedKind} '{captured}'");
                EnqueueMonitorLine($"— {capturedKind} added: {captured}");
                _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                    $"MIDI: {capturedKind} connected — {captured}", (int)LogType.Info);
            }
            else
            {
                EnqueueMonitorLine("— MIDI device added");
            }

            ReconcileSessionDevices(emitStateIfChanged: true, reason: "device-added");
        });
    }

    private void OnWatcherDeviceRemoved(object sender, DeviceAddedRemovedEventArgs e)
    {
        // DeviceRemoved instances are non-interactable (Name throws). Match by reference
        // against our open handles; otherwise fall back to a full reconcile.
        string knownName = null;
        bool isOutput = false;
        if (e?.Device is InputDevice removedIn &&
            _openDeviceNames.TryGetValue(removedIn, out string mappedIn))
        {
            knownName = mappedIn;
        }
        else if (e?.Device is OutputDevice removedOut &&
                 _openOutputNames.TryGetValue(removedOut, out string mappedOut))
        {
            knownName = mappedOut;
            isOutput = true;
        }

        string captured = knownName;
        bool capturedIsOutput = isOutput;
        MidiDevice capturedDevice = e?.Device;
        _mainThreadActions.Enqueue(() =>
        {
            if (!string.IsNullOrEmpty(captured))
            {
                GD.Print($"MidiManager:OnWatcherDeviceRemoved - '{captured}'");
                EnqueueMonitorLine($"— Device removed: {captured}");
                _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                    $"MIDI: device disconnected — {captured}", (int)LogType.Warning);
                if (capturedIsOutput)
                    CloseOutput(captured, removeFromSession: false, logNotice: false);
                else
                    CloseInput(captured, removeFromSession: false, logNotice: false);
            }
            else if (capturedDevice is InputDevice inDev)
            {
                string byRef = FindOpenNameByInstance(inDev);
                if (!string.IsNullOrEmpty(byRef))
                {
                    EnqueueMonitorLine($"— Device removed: {byRef}");
                    CloseInput(byRef, removeFromSession: false, logNotice: false);
                }
            }
            else if (capturedDevice is OutputDevice outDev)
            {
                string byRef = FindOpenOutputNameByInstance(outDev);
                if (!string.IsNullOrEmpty(byRef))
                {
                    EnqueueMonitorLine($"— Output removed: {byRef}");
                    CloseOutput(byRef, removeFromSession: false, logNotice: false);
                }
            }

            ReconcileSessionDevices(emitStateIfChanged: true, reason: "device-removed");
        });
    }

    /// <summary>
    /// Re-scans available ports, closes handles for missing/dead devices, and reopens
    /// session devices that have returned while MIDI is enabled.
    /// </summary>
    /// <param name="emitStateIfChanged">Emit <see cref="MidiStateChanged"/> when open set or availability changes.</param>
    /// <param name="reason">Log tag for diagnostics.</param>
    /// <param name="forceStateEmit">Always emit state (e.g. user pressed Refresh).</param>
    private void ReconcileSessionDevices(bool emitStateIfChanged, string reason, bool forceStateEmit = false)
    {
        if (!_nativeReady && !(_nativeReady = EnsureNativeLibraryLoaded()))
        {
            if (forceStateEmit)
                EmitSignal(SignalName.MidiStateChanged);
            return;
        }

        var previousAvailableIn = new HashSet<string>(_availableInputNames, StringComparer.OrdinalIgnoreCase);
        var previousAvailableOut = new HashSet<string>(_availableOutputNames, StringComparer.OrdinalIgnoreCase);
        var previousOpenIn = new HashSet<string>(_openDevices.Keys, StringComparer.OrdinalIgnoreCase);
        var previousOpenOut = new HashSet<string>(_openOutputs.Keys, StringComparer.OrdinalIgnoreCase);

        EnumerateAvailableDevices();

        // Drop open inputs that disappeared or are no longer healthy.
        foreach (string openName in _openDevices.Keys.ToList())
        {
            bool stillAvailable = _availableInputNames.Contains(openName, StringComparer.OrdinalIgnoreCase);
            bool healthy = stillAvailable && IsOpenHandleHealthy(openName);

            if (!stillAvailable || !healthy)
            {
                GD.Print($"MidiManager:ReconcileSessionDevices[{reason}] - Closing input '{openName}' " +
                         $"(available={stillAvailable}, healthy={healthy})");
                if (!stillAvailable)
                {
                    EnqueueMonitorLine($"— Input offline: {openName}");
                    _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                        $"MIDI: input offline — {openName}", (int)LogType.Warning);
                }
                CloseInput(openName, removeFromSession: false, logNotice: stillAvailable);
            }
        }

        // Drop open outputs that disappeared or are unhealthy.
        foreach (string openName in _openOutputs.Keys.ToList())
        {
            bool stillAvailable = _availableOutputNames.Contains(openName, StringComparer.OrdinalIgnoreCase);
            bool healthy = stillAvailable && IsOpenOutputHealthy(openName);

            if (!stillAvailable || !healthy)
            {
                GD.Print($"MidiManager:ReconcileSessionDevices[{reason}] - Closing output '{openName}'");
                if (!stillAvailable)
                {
                    EnqueueMonitorLine($"— Output offline: {openName}");
                    _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                        $"MIDI: output offline — {openName}", (int)LogType.Warning);
                }
                CloseOutput(openName, removeFromSession: false, logNotice: stillAvailable);
            }
        }

        // Reopen session devices while MIDI is enabled.
        if (_midiEnabled)
        {
            foreach (string sessionName in _sessionInputNames.ToList())
            {
                if (!_availableInputNames.Contains(sessionName, StringComparer.OrdinalIgnoreCase))
                    continue;
                if (_openDevices.ContainsKey(sessionName) && IsOpenHandleHealthy(sessionName))
                    continue;

                if (_openDevices.ContainsKey(sessionName))
                    CloseInput(sessionName, removeFromSession: false, logNotice: false);

                if (OpenInput(sessionName))
                {
                    GD.Print($"MidiManager:ReconcileSessionDevices[{reason}] - Reopened input '{sessionName}'");
                    if (reason != "poll" || !previousOpenIn.Contains(sessionName))
                    {
                        _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                            $"MIDI: input reconnected — {sessionName}", (int)LogType.Info);
                    }
                }
            }

            foreach (string sessionName in _sessionOutputNames.ToList())
            {
                if (!_availableOutputNames.Contains(sessionName, StringComparer.OrdinalIgnoreCase))
                    continue;
                if (_openOutputs.ContainsKey(sessionName) && IsOpenOutputHealthy(sessionName))
                    continue;

                if (_openOutputs.ContainsKey(sessionName))
                    CloseOutput(sessionName, removeFromSession: false, logNotice: false);

                if (OpenOutput(sessionName))
                {
                    GD.Print($"MidiManager:ReconcileSessionDevices[{reason}] - Reopened output '{sessionName}'");
                    if (reason != "poll" || !previousOpenOut.Contains(sessionName))
                    {
                        _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                            $"MIDI: output reconnected — {sessionName}", (int)LogType.Info);
                    }
                }
            }
        }

        bool availabilityChanged =
            !previousAvailableIn.SetEquals(_availableInputNames) ||
            !previousAvailableOut.SetEquals(_availableOutputNames);
        bool openChanged =
            !previousOpenIn.SetEquals(_openDevices.Keys) ||
            !previousOpenOut.SetEquals(_openOutputs.Keys);
        if (forceStateEmit || (emitStateIfChanged && (availabilityChanged || openChanged)))
            EmitSignal(SignalName.MidiStateChanged);
    }

    /// <summary>
    /// True when the open handle exists and is still listening (not a zombie after unplug).
    /// </summary>
    private bool IsOpenHandleHealthy(string deviceName)
    {
        if (!_openDevices.TryGetValue(deviceName, out var device) || device == null)
            return false;
        try
        {
            return device.IsListeningForEvents;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enumerates system input/output names (disposes temp handles we do not own).
    /// </summary>
    private void EnumerateAvailableDevices()
    {
        _availableInputNames.Clear();
        _availableOutputNames.Clear();
        try
        {
            foreach (var device in InputDevice.GetAll())
            {
                try
                {
                    string name = SafeGetDeviceName(device);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (!_availableInputNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        _availableInputNames.Add(name);
                }
                finally
                {
                    if (device != null && !_openDeviceNames.ContainsKey(device))
                    {
                        try { device.Dispose(); }
                        catch { /* ignore */ }
                    }
                }
            }
            _availableInputNames.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (var device in OutputDevice.GetAll())
            {
                try
                {
                    string name = SafeGetDeviceName(device);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (!_availableOutputNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        _availableOutputNames.Add(name);
                }
                finally
                {
                    if (device != null && !_openOutputNames.ContainsKey(device))
                    {
                        try { device.Dispose(); }
                        catch { /* ignore */ }
                    }
                }
            }
            _availableOutputNames.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MidiManager:EnumerateAvailableDevices - {ex.Message}");
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"MIDI: failed to list devices — {ex.Message}", (int)LogType.Warning);
        }
    }

    private bool IsOpenOutputHealthy(string deviceName)
    {
        if (!_openOutputs.TryGetValue(deviceName, out var device) || device == null)
            return false;
        try
        {
            // Touch a cheap property; removed devices throw.
            _ = device.Name;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string SafeGetDeviceName(MidiDevice device)
    {
        if (device == null) return null;
        try { return device.Name; }
        catch { return null; }
    }

    private string FindOpenNameByInstance(InputDevice device)
    {
        if (device == null) return null;
        if (_openDeviceNames.TryGetValue(device, out string name))
            return name;
        foreach (var kvp in _openDevices)
        {
            if (ReferenceEquals(kvp.Value, device))
                return kvp.Key;
        }
        return null;
    }

    private string FindOpenOutputNameByInstance(OutputDevice device)
    {
        if (device == null) return null;
        if (_openOutputNames.TryGetValue(device, out string name))
            return name;
        foreach (var kvp in _openOutputs)
        {
            if (ReferenceEquals(kvp.Value, device))
                return kvp.Key;
        }
        return null;
    }

    /// <summary>True if a session output is open.</summary>
    public bool IsOutputOpen(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return false;
        return IsOpenOutputHealthy(deviceName);
    }

    /// <summary>True if an output name is currently visible to the system.</summary>
    public bool IsOutputAvailable(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return false;
        return _availableOutputNames.Contains(deviceName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds a system output device to the session. Opens immediately when MIDI is enabled.
    /// </summary>
    public bool AddOutputDevice(string deviceName)
    {
        string name = deviceName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name)) return false;
        if (_sessionOutputNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            return false;

        _sessionOutputNames.Add(name);
        GD.Print($"MidiManager:AddOutputDevice - Added '{name}' to session.");
        _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
            $"MIDI: added output '{name}'", (int)LogType.Info);

        if (_midiEnabled)
            OpenOutput(name);

        EmitSignal(SignalName.MidiStateChanged);
        return true;
    }

    /// <summary>
    /// Removes an output from the session and closes it if open.
    /// </summary>
    public bool RemoveOutputDevice(string deviceName)
    {
        string name = deviceName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name)) return false;

        int idx = _sessionOutputNames.FindIndex(n =>
            string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return false;

        CloseOutput(_sessionOutputNames[idx], removeFromSession: false);
        _sessionOutputNames.RemoveAt(idx);

        GD.Print($"MidiManager:RemoveOutputDevice - Removed '{name}' from session.");
        _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
            $"MIDI: removed output '{name}'", (int)LogType.Info);

        EmitSignal(SignalName.MidiStateChanged);
        return true;
    }

    /// <summary>
    /// Returns true if <paramref name="deviceName"/> is currently visible to the system.
    /// </summary>
    public bool IsDeviceAvailable(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return false;
        return _availableInputNames.Contains(deviceName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true if the named session device is open and listening.
    /// </summary>
    public bool IsDeviceOpen(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return false;
        return IsOpenHandleHealthy(deviceName);
    }

    /// <summary>
    /// Adds a system input device to the session. Opens it immediately when MIDI is enabled.
    /// No-op if already in the session or name is empty.
    /// </summary>
    /// <param name="deviceName">Device name from <see cref="AvailableInputNames"/>.</param>
    /// <returns><c>true</c> when the device was newly added.</returns>
    public bool AddInputDevice(string deviceName)
    {
        string name = deviceName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name)) return false;

        if (_sessionInputNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            return false;

        _sessionInputNames.Add(name);
        GD.Print($"MidiManager:AddInputDevice - Added '{name}' to session.");
        _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
            $"MIDI: added input '{name}'", (int)LogType.Info);

        if (_midiEnabled)
            OpenInput(name);

        EmitSignal(SignalName.MidiStateChanged);
        return true;
    }

    /// <summary>
    /// Removes a device from the session and closes it if open.
    /// </summary>
    /// <param name="deviceName">Session device name.</param>
    /// <returns><c>true</c> when the device was found and removed.</returns>
    public bool RemoveInputDevice(string deviceName)
    {
        string name = deviceName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name)) return false;

        int idx = _sessionInputNames.FindIndex(n =>
            string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return false;

        CloseInput(_sessionInputNames[idx], removeFromSession: false);
        _sessionInputNames.RemoveAt(idx);

        GD.Print($"MidiManager:RemoveInputDevice - Removed '{name}' from session.");
        _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
            $"MIDI: removed input '{name}'", (int)LogType.Info);

        EmitSignal(SignalName.MidiStateChanged);
        return true;
    }

    /// <summary>
    /// Clears the pending monitor queue (does not clear UI; Settings panel owns the CodeEdit).
    /// </summary>
    public void ClearPendingMonitorLines()
    {
        while (_pendingLogLines.TryDequeue(out _)) { }
    }

    // ── Serialization (show settings + undo slice) ───────────────────────────

    /// <summary>
    /// Serializes session MIDI configuration for the showfile / history.
    /// </summary>
    /// <returns>Dictionary with MidiEnabled, MonitorEnabled, and SessionInputs.</returns>
    public Godot.Collections.Dictionary GetData()
    {
        var dict = new Godot.Collections.Dictionary();
        dict["MidiEnabled"] = _midiEnabled;
        dict["MonitorEnabled"] = _monitorEnabled;
        var inputs = new Godot.Collections.Array();
        foreach (string name in _sessionInputNames)
            inputs.Add(name);
        dict["SessionInputs"] = inputs;
        var outputs = new Godot.Collections.Array();
        foreach (string name in _sessionOutputNames)
            outputs.Add(name);
        dict["SessionOutputs"] = outputs;
        return dict;
    }

    /// <summary>
    /// Restores session MIDI configuration from show load or undo/redo.
    /// Closes open ports, replaces the session list, then reopens when enabled.
    /// </summary>
    /// <param name="data">Dictionary previously produced by <see cref="GetData"/>.</param>
    public void LoadFromData(Godot.Collections.Dictionary data)
    {
        if (data == null) return;

        CancelCapture();
        CloseAllInputs();
        CloseAllOutputs();
        _sessionInputNames.Clear();
        _sessionOutputNames.Clear();

        if (data.TryGetValue("SessionInputs", out var inputsVar))
        {
            var arr = inputsVar.AsGodotArray();
            foreach (var item in arr)
            {
                string name = item.AsString()?.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                if (_sessionInputNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                _sessionInputNames.Add(name);
            }
        }

        if (data.TryGetValue("SessionOutputs", out var outputsVar))
        {
            var arr = outputsVar.AsGodotArray();
            foreach (var item in arr)
            {
                string name = item.AsString()?.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                if (_sessionOutputNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                _sessionOutputNames.Add(name);
            }
        }

        _monitorEnabled = !data.TryGetValue("MonitorEnabled", out var mon) || mon.AsBool();
        _midiEnabled = data.TryGetValue("MidiEnabled", out var en) && en.AsBool();

        // Re-enumerate so availability/open status is current after restore.
        if (_nativeReady || (_nativeReady = EnsureNativeLibraryLoaded()))
            RefreshDeviceListQuiet();

        if (_midiEnabled)
        {
            OpenAllSessionInputs();
            OpenAllSessionOutputs();
        }

        EmitSignal(SignalName.MidiStateChanged);
        GD.Print($"MidiManager:LoadFromData - Enabled={_midiEnabled}, " +
                 $"inputs={_sessionInputNames.Count}, outputs={_sessionOutputNames.Count}");
    }

    /// <summary>
    /// Clears session MIDI configuration for a new empty show.
    /// </summary>
    public void ResetToDefaults()
    {
        CancelCapture();
        _pendingNoteOffs.Clear();
        CloseAllInputs();
        CloseAllOutputs();
        _sessionInputNames.Clear();
        _sessionOutputNames.Clear();
        _inputMapBindings.Clear();
        _midiEnabled = false;
        _monitorEnabled = true;
        EmitSignal(SignalName.MidiStateChanged);
        GD.Print("MidiManager:ResetToDefaults - MIDI disabled, session devices and Input Map cleared.");
    }

    /// <summary>
    /// Re-enumerates available devices without emitting <see cref="MidiStateChanged"/>
    /// (used mid-load so a single state change fires at the end).
    /// </summary>
    private void RefreshDeviceListQuiet()
    {
        EnumerateAvailableDevices();
    }

    // ── Open / close ────────────────────────────────────────────────────────

    private void OpenAllSessionInputs()
    {
        if (!_nativeReady && !(_nativeReady = EnsureNativeLibraryLoaded()))
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "MIDI: cannot open inputs — native library not loaded.", (int)LogType.Error);
            return;
        }

        EnumerateAvailableDevices();

        if (_sessionInputNames.Count == 0)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "MIDI: enabled but no input devices in session.", (int)LogType.Info);
            return;
        }

        foreach (string name in _sessionInputNames.ToList())
            OpenInput(name);
    }

    private void OpenAllSessionOutputs()
    {
        if (!_nativeReady && !(_nativeReady = EnsureNativeLibraryLoaded()))
            return;

        EnumerateAvailableDevices();

        if (_sessionOutputNames.Count == 0)
            return;

        foreach (string name in _sessionOutputNames.ToList())
            OpenOutput(name);
    }

    private bool OpenOutput(string deviceName)
    {
        if (string.IsNullOrEmpty(deviceName)) return false;

        if (_openOutputs.TryGetValue(deviceName, out var existing))
        {
            if (IsOpenOutputHealthy(deviceName))
                return true;
            CloseOutput(deviceName, removeFromSession: false, logNotice: false);
        }

        if (!_nativeReady && !(_nativeReady = EnsureNativeLibraryLoaded()))
            return false;

        try
        {
            OutputDevice device;
            try
            {
                device = OutputDevice.GetByName(deviceName);
            }
            catch (Exception getEx)
            {
                GD.Print($"MidiManager:OpenOutput - GetByName('{deviceName}') failed: {getEx.Message}");
                return false;
            }

            if (device == null)
                return false;

            // Prepare for sending (DryWetMIDI opens on first send for some platforms;
            // explicit PrepareForEventsSending avoids first-message lag where available).
            try { device.PrepareForEventsSending(); }
            catch { /* optional API depending on version */ }

            _openOutputs[deviceName] = device;
            _openOutputNames[device] = deviceName;

            GD.Print($"MidiManager:OpenOutput - Opened '{deviceName}'.");
            EnqueueMonitorLine($"— Opened output: {deviceName}");
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MidiManager:OpenOutput - {deviceName}: {ex.Message}");
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"MIDI: failed to open output '{deviceName}' — {ex.Message}", (int)LogType.Warning);
            return false;
        }
    }

    private void CloseOutput(string deviceName, bool removeFromSession, bool logNotice = true)
    {
        if (string.IsNullOrEmpty(deviceName)) return;

        if (_openOutputs.TryGetValue(deviceName, out var device))
        {
            _openOutputs.Remove(deviceName);
            if (device != null)
                _openOutputNames.Remove(device);

            if (device != null)
            {
                try { device.Dispose(); }
                catch (Exception ex)
                {
                    GD.Print($"MidiManager:CloseOutput - Dispose '{deviceName}': {ex.Message}");
                }
            }

            if (logNotice)
                EnqueueMonitorLine($"— Closed output: {deviceName}");
        }

        // Cancel pending note-offs for this device.
        _pendingNoteOffs.RemoveAll(n =>
            string.Equals(n.Device, deviceName, StringComparison.OrdinalIgnoreCase));

        if (removeFromSession)
        {
            _sessionOutputNames.RemoveAll(n =>
                string.Equals(n, deviceName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void CloseAllOutputs()
    {
        foreach (string name in _openOutputs.Keys.ToList())
            CloseOutput(name, removeFromSession: false, logNotice: false);
        _pendingNoteOffs.Clear();
    }

    // ── MIDI send (cue components / panic) ──────────────────────────────────

    /// <summary>
    /// Sends a channel message to a session output device.
    /// Channel is 1–16. For Note On with <paramref name="noteDurationSeconds"/> &gt; 0, schedules Note Off.
    /// </summary>
    public bool SendMessage(
        string outputDeviceName,
        MidiTriggerMessageType type,
        int channel,
        int data1,
        int data2 = 0,
        double noteDurationSeconds = 0)
    {
        if (string.IsNullOrWhiteSpace(outputDeviceName)) return false;
        if (!_midiEnabled)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "MIDI send failed: MIDI is disabled.", (int)LogType.Warning);
            return false;
        }

        if (!_openOutputs.TryGetValue(outputDeviceName, out var device) || device == null)
        {
            // Try open if in session but not open.
            if (_sessionOutputNames.Contains(outputDeviceName, StringComparer.OrdinalIgnoreCase))
                OpenOutput(outputDeviceName);
            if (!_openOutputs.TryGetValue(outputDeviceName, out device) || device == null)
            {
                _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                    $"MIDI send failed: output '{outputDeviceName}' not open.", (int)LogType.Warning);
                return false;
            }
        }

        int ch0 = Math.Clamp(channel, 1, 16) - 1;
        data1 = Math.Clamp(data1, 0, 127);
        data2 = Math.Clamp(data2, 0, 127);

        try
        {
            MidiEvent ev = type switch
            {
                MidiTriggerMessageType.NoteOn => new NoteOnEvent((SevenBitNumber)data1, (SevenBitNumber)data2)
                {
                    Channel = (FourBitNumber)ch0
                },
                MidiTriggerMessageType.NoteOff => new NoteOffEvent((SevenBitNumber)data1, (SevenBitNumber)data2)
                {
                    Channel = (FourBitNumber)ch0
                },
                MidiTriggerMessageType.ControlChange => new ControlChangeEvent(
                    (SevenBitNumber)data1, (SevenBitNumber)data2)
                {
                    Channel = (FourBitNumber)ch0
                },
                MidiTriggerMessageType.ProgramChange => new ProgramChangeEvent((SevenBitNumber)data1)
                {
                    Channel = (FourBitNumber)ch0
                },
                _ => null
            };

            if (ev == null) return false;
            device.SendEvent(ev);

            if (type == MidiTriggerMessageType.NoteOn && noteDurationSeconds > 1e-6)
            {
                _pendingNoteOffs.Add((outputDeviceName, ch0, data1, noteDurationSeconds));
            }

            if (_monitorEnabled)
            {
                EnqueueMonitorLine(
                    $"→ [{outputDeviceName}] {type} ch{channel} d1={data1} d2={data2}");
            }

            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MidiManager:SendMessage - {outputDeviceName}: {ex.Message}");
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"MIDI send error on '{outputDeviceName}': {ex.Message}", (int)LogType.Error);
            return false;
        }
    }

    /// <summary>
    /// Sends All Notes Off (CC 123) and All Sound Off (CC 120) on all channels for all open outputs.
    /// </summary>
    public void PanicAllOutputs()
    {
        _pendingNoteOffs.Clear();
        foreach (var kvp in _openOutputs.ToList())
        {
            var device = kvp.Value;
            if (device == null) continue;
            try
            {
                for (int ch = 0; ch < 16; ch++)
                {
                    device.SendEvent(new ControlChangeEvent((SevenBitNumber)123, (SevenBitNumber)0)
                    {
                        Channel = (FourBitNumber)ch
                    });
                    device.SendEvent(new ControlChangeEvent((SevenBitNumber)120, (SevenBitNumber)0)
                    {
                        Channel = (FourBitNumber)ch
                    });
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"MidiManager:PanicAllOutputs - {kvp.Key}: {ex.Message}");
            }
        }
        EnqueueMonitorLine("— Panic: All Notes/Sound Off on all outputs");
        _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
            "MIDI panic: All Notes Off / All Sound Off sent", (int)LogType.Info);
    }

    /// <summary>
    /// Opens a named input if present. Returns <c>true</c> when listening successfully.
    /// </summary>
    private bool OpenInput(string deviceName)
    {
        if (string.IsNullOrEmpty(deviceName)) return false;

        if (_openDevices.TryGetValue(deviceName, out var existing))
        {
            if (IsOpenHandleHealthy(deviceName))
                return true;
            // Zombie handle after a partial disconnect — dispose and retry.
            CloseInput(deviceName, removeFromSession: false, logNotice: false);
        }

        if (!_nativeReady && !(_nativeReady = EnsureNativeLibraryLoaded()))
            return false;

        try
        {
            // GetByName throws or fails when the port is gone mid-call.
            InputDevice device;
            try
            {
                device = InputDevice.GetByName(deviceName);
            }
            catch (Exception getEx)
            {
                GD.Print($"MidiManager:OpenInput - GetByName('{deviceName}') failed: {getEx.Message}");
                return false;
            }

            if (device == null)
                return false;

            device.EventReceived += OnMidiEventReceived;
            device.ErrorOccurred += OnMidiErrorOccurred;
            device.StartEventsListening();

            _openDevices[deviceName] = device;
            _openDeviceNames[device] = deviceName;

            GD.Print($"MidiManager:OpenInput - Listening on '{deviceName}'.");
            EnqueueMonitorLine($"— Opened input: {deviceName}");
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MidiManager:OpenInput - {deviceName}: {ex.Message}");
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"MIDI: failed to open '{deviceName}' — {ex.Message}", (int)LogType.Warning);
            return false;
        }
    }

    /// <summary>
    /// Closes an open input handle. Safe if the device was already removed from the system.
    /// </summary>
    /// <param name="deviceName">Session / open map key.</param>
    /// <param name="removeFromSession">When true, also drop from the session list.</param>
    /// <param name="logNotice">When true, write a monitor line for the close.</param>
    private void CloseInput(string deviceName, bool removeFromSession, bool logNotice = true)
    {
        if (string.IsNullOrEmpty(deviceName)) return;

        if (_openDevices.TryGetValue(deviceName, out var device))
        {
            _openDevices.Remove(deviceName);
            if (device != null)
                _openDeviceNames.Remove(device);

            if (device != null)
            {
                try { device.EventReceived -= OnMidiEventReceived; }
                catch { /* device may already be invalid */ }
                try { device.ErrorOccurred -= OnMidiErrorOccurred; }
                catch { /* ignore */ }
                try
                {
                    if (device.IsListeningForEvents)
                        device.StopEventsListening();
                }
                catch { /* unplugged mid-listen */ }
                try { device.Dispose(); }
                catch (Exception ex)
                {
                    GD.Print($"MidiManager:CloseInput - Dispose '{deviceName}': {ex.Message}");
                }
            }

            if (logNotice)
                EnqueueMonitorLine($"— Closed input: {deviceName}");
        }

        if (removeFromSession)
        {
            _sessionInputNames.RemoveAll(n =>
                string.Equals(n, deviceName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void CloseAllInputs()
    {
        foreach (string name in _openDevices.Keys.ToList())
            CloseInput(name, removeFromSession: false, logNotice: false);
    }

    // ── Event handlers ──────────────────────────────────────────────────────

    private void OnMidiEventReceived(object sender, MidiEventReceivedEventArgs e)
    {
        if (e?.Event == null) return;

        string deviceName = ResolveSenderName(sender) ?? "?";

        // Structured messages drive capture, cue triggers, and monitor formatting.
        if (TryParseMidiEvent(deviceName, e.Event, out MidiInputMessage msg))
        {
            _pendingMessages.Enqueue(msg);
            return;
        }

        // Unhandled message types: monitor-only dump when listening.
        if (_monitorEnabled)
        {
            string line = $"{DateTime.Now:HH:mm:ss.fff}  [{deviceName}] {e.Event.EventType}  {e.Event}";
            _pendingLogLines.Enqueue(line);
        }
    }

    /// <summary>
    /// Resolves a device display name from a DryWetMidi callback sender without throwing
    /// if the device has already been removed from the system.
    /// </summary>
    private string ResolveSenderName(object sender)
    {
        if (sender is InputDevice input)
        {
            if (_openDeviceNames.TryGetValue(input, out string mapped) && !string.IsNullOrEmpty(mapped))
                return mapped;
            return SafeGetDeviceName(input);
        }

        if (sender is MidiDevice md)
            return SafeGetDeviceName(md);

        return null;
    }

    /// <summary>
    /// Maps a DryWetMidi event into a <see cref="MidiInputMessage"/> for supported trigger types.
    /// Note On with velocity 0 is treated as Note Off.
    /// </summary>
    private static bool TryParseMidiEvent(string deviceName, MidiEvent midiEvent, out MidiInputMessage msg)
    {
        msg = default;
        switch (midiEvent)
        {
            case NoteOnEvent noteOn when noteOn.Velocity == 0:
                msg = new MidiInputMessage
                {
                    DeviceName = deviceName,
                    MessageType = MidiTriggerMessageType.NoteOff,
                    Channel = noteOn.Channel + 1,
                    Data1 = noteOn.NoteNumber,
                    Data2 = 0
                };
                return true;
            case NoteOnEvent noteOn:
                msg = new MidiInputMessage
                {
                    DeviceName = deviceName,
                    MessageType = MidiTriggerMessageType.NoteOn,
                    Channel = noteOn.Channel + 1,
                    Data1 = noteOn.NoteNumber,
                    Data2 = noteOn.Velocity
                };
                return true;
            case NoteOffEvent noteOff:
                msg = new MidiInputMessage
                {
                    DeviceName = deviceName,
                    MessageType = MidiTriggerMessageType.NoteOff,
                    Channel = noteOff.Channel + 1,
                    Data1 = noteOff.NoteNumber,
                    Data2 = noteOff.Velocity
                };
                return true;
            case ControlChangeEvent cc:
                msg = new MidiInputMessage
                {
                    DeviceName = deviceName,
                    MessageType = MidiTriggerMessageType.ControlChange,
                    Channel = cc.Channel + 1,
                    Data1 = cc.ControlNumber,
                    Data2 = cc.ControlValue
                };
                return true;
            case ProgramChangeEvent pc:
                msg = new MidiInputMessage
                {
                    DeviceName = deviceName,
                    MessageType = MidiTriggerMessageType.ProgramChange,
                    Channel = pc.Channel + 1,
                    Data1 = pc.ProgramNumber,
                    Data2 = 0
                };
                return true;
            default:
                return false;
        }
    }

    private void OnMidiErrorOccurred(object sender, ErrorOccurredEventArgs e)
    {
        // Often fires when a device is unplugged while open. Marshal close to main thread.
        string deviceName = ResolveSenderName(sender) ?? "MIDI";
        string msg = e?.Exception?.Message ?? "Unknown MIDI device error";
        GD.PrintErr($"MidiManager:OnMidiErrorOccurred - [{deviceName}] {msg}");
        _pendingLogLines.Enqueue($"! ERROR [{deviceName}]: {msg}");

        string capturedName = deviceName;
        InputDevice capturedDevice = sender as InputDevice;
        _mainThreadActions.Enqueue(() =>
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"MIDI device error: [{capturedName}] {msg}", (int)LogType.Warning);

            string name = capturedName;
            if (string.IsNullOrEmpty(name) || name == "MIDI")
                name = FindOpenNameByInstance(capturedDevice) ?? name;

            if (!string.IsNullOrEmpty(name) && name != "MIDI" && _openDevices.ContainsKey(name))
            {
                EnqueueMonitorLine($"— Device error / offline: {name}");
                CloseInput(name, removeFromSession: false, logNotice: false);
                EmitSignal(SignalName.MidiStateChanged);
            }
            else
            {
                // Unknown which port failed — full reconcile.
                ReconcileSessionDevices(emitStateIfChanged: true, reason: "device-error");
            }
        });
    }

    private void EnqueueMonitorLine(string line)
    {
        if (!_monitorEnabled) return;
        string stamped = $"{DateTime.Now:HH:mm:ss.fff}  {line}";
        _pendingLogLines.Enqueue(stamped);
    }
}
