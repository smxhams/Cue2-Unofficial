// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cue2.Domain.Cuelist;
using Cue2.Domain.Playback;
using Cue2.Domain.Devices;
using Cue2.Domain.ShowSettings;
using Cue2.Domain.Metadata;
using Cue2.Domain.Cues;
using Cue2.Domain.Connections;
using Cue2.Domain.Library;
using Cue2.Domain.Commands;
using Cue2.Services;
using Cue2.Media.Audio;
using Cue2.UI.Utilities;

namespace Cue2.UI.Inspectors;


/// <summary>
/// Inspector UI for managing audio components in cues. Handles file selection, playback settings,
/// and output patching. Supports multi-edit when Settings multi-edit is on and multiple cues are selected.
/// </summary>
/// <remarks>
/// Multi-edit targets are selected cues that have an audio component. Uniform values are shown;
/// mixed values are blank. Waveform and routing matrix reflect the primary (focused) target;
/// scalar edits (volume, pan, loop, times, fades, play count, output, file) apply to all targets.
/// History uses a cuelist snapshot when two or more targets change.
/// </remarks>
/// <summary>
/// Partial: Routing matrix, media path/health, multi-edit history helpers
/// </summary>
public partial class AudioInspector
{
    private async void BuildRoutingMatrix()
    {
        if (!IsInsideTree() || _routingMatrixGrid == null)
            return;

        int gen = _shellSelectGeneration;
        _routingInputLabels.Clear();
        foreach (var child in _routingMatrixGrid.GetChildren())
        {
            child.QueueFree();
        }

        if (_focusedAudioComponent == null)
        {
            GD.Print($"AudioInspector:BuildRoutingMatrix - No focused audio component");
            if (_routingContainer != null)
                _routingContainer.Visible = false;
            return;
        }

        var tree = GetTree();
        if (tree == null)
            return;

        await ToSignal(tree, "process_frame"); // Wait a frame for existing children to fully clear.
        if (!IsInsideTree())
            return;

        // Selection may have changed while waiting (multi-select focus flood).
        if (gen != _shellSelectGeneration || _focusedAudioComponent == null)
            return;
        if (_focusedAudioComponent.Metadata == null)
        {
            GD.Print("AudioInspector:BuildRoutingMatrix - Metadata not ready; skipping matrix.");
            if (_routingContainer != null)
                _routingContainer.Visible = false;
            return;
        }
        
        // Get ins and outs data
        var inputChannels = _focusedAudioComponent.Metadata.Channels;
        var inputLabels = GetChannelLabels(inputChannels, isInput: true);

        int outputChannels;
        List<string> outputLabels = new List<string>();
        
        // Prefer live Patch reference, then PatchId (default patch on create sets both).
        if (_focusedAudioComponent.Patch != null && GodotObject.IsInstanceValid(_focusedAudioComponent.Patch)
            && _focusedAudioComponent.PatchId != _focusedAudioComponent.Patch.Id)
        {
            _focusedAudioComponent.PatchId = _focusedAudioComponent.Patch.Id;
        }

        // Audio Output Patch
        if (_focusedAudioComponent.PatchId != -1 || _focusedAudioComponent.Patch != null)
        {
            AudioOutputPatch patch = _focusedAudioComponent.Patch;
            if (patch == null || !GodotObject.IsInstanceValid(patch))
            {
                _globalData.Settings.GetAudioOutputPatches()
                    .TryGetValue(_focusedAudioComponent.PatchId, out patch);
            }

            // Check if selected patch exists, if not clean the audio component of it.
            if (patch == null || !GodotObject.IsInstanceValid(patch))
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:BuildRoutingMatrix - Patch ID {_focusedAudioComponent.PatchId} not found, resetting output", 2);
                _focusedAudioComponent.Patch = null;
                _focusedAudioComponent.PatchId = -1;
                _focusedAudioComponent.Routing = null;
                PopulateOutputOptions(); // Refresh UI to reflect missing patch
                _routingContainer.Visible = false;
                return;
            }

            _focusedAudioComponent.Patch = patch;
            _focusedAudioComponent.PatchId = patch.Id;
            outputChannels = patch.Channels.Count;
            outputLabels = patch.Channels.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
        }
        
        // Direct output
        else if (!string.IsNullOrEmpty(_focusedAudioComponent.DirectOutput))
        {
            var device = _audioDevices.OpenAudioDevice(_focusedAudioComponent.DirectOutput, out var _);
            if (device == null)
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:BuildRoutingMatrix - Direct output device not found: {_focusedAudioComponent.DirectOutput}", 2);
                _focusedAudioComponent.DirectOutput = null;
                PopulateOutputOptions(); // Refresh UI to reflect missing output
                _routingContainer.Visible = false;
                return;
            }
            outputChannels = device.Channels;
            for (int i = 0; i < outputChannels; i++)
            {
                outputLabels.Add($"Channel {i}");
            }
        }
        else
        {
            GD.Print($"AudioInspector:BuildRoutingMatrix - No output selected");
            _routingContainer.Visible = false;
            return; // No output selected
        }

        
        // Validate routing (CuePatch) matches what is expected
        var routing = _focusedAudioComponent.Routing;
        bool needsUpdate = routing == null ||
                           routing.OutputChannels != outputChannels ||
                           !routing.OutputLabels.SequenceEqual(outputLabels) ||
                           routing.InputChannels != inputChannels ||
                           !routing.InputLabels.SequenceEqual(inputLabels);
        
        if (needsUpdate)
        {
            // Preserve old volumes if possible
            var oldRouting = routing;

            // Create new CuePatch with current dimensions
            routing = new CuePatch(inputChannels, inputLabels, outputChannels, outputLabels);
            _focusedAudioComponent.Routing = routing;

            if (oldRouting != null)
            {
                // Copy over existing volumes for overlapping channels
                int copyInputs = Math.Min(oldRouting.InputChannels, inputChannels);
                int copyOutputs = Math.Min(oldRouting.OutputChannels, outputChannels);

                for (int i = 0; i < copyInputs; i++)
                {
                    for (int j = 0; j < copyOutputs; j++)
                    {
                        routing.SetVolume(i, j, oldRouting.GetVolume(i, j));
                    }
                }
            }

            GD.Print($"AudioInspector:BuildRoutingMatrix - Resized/created CuePatch to inputs: {inputChannels}, outputs: {outputChannels}"); //!!!
        }
        
        
        
        // Set grid columns: outputChannels + 1 (for input labels)
        _routingMatrixGrid.Columns = outputChannels + 1;
        
        // Add header row: empty + output labels
        _routingMatrixGrid.AddChild(new Label { Text = ""}); // Corner
        foreach (var outLabel in outputLabels)
        {
            var label = new Label { Text = outLabel };
            _routingMatrixGrid.AddChild(label);
        }
        
        // Add rows: input label (+ pan status for stereo) + volume fields
        string panStatus = inputChannels == 2
            ? UiUtilities.FormatPan(_focusedAudioComponent.Pan)
            : null;
        for (int row = 0; row < inputChannels; row++)
        {
            string labelText = inputLabels[row];
            if (panStatus != null && row < 2)
                labelText = $"{labelText} ({panStatus})";
            var inLabel = new Label { Text = labelText };
            _routingMatrixGrid.AddChild(inLabel);
            _routingInputLabels.Add(inLabel);
            
            for (int col = 0; col < outputChannels; col++)
            {
                var volumeEdit = new LineEdit();
                var linearVol = _focusedAudioComponent.Routing.GetVolume(row, col);
                if (linearVol > 0.0f)
                {
                    var dbVol = UiUtilities.LinearToDb(linearVol);
                    volumeEdit.Text = $"{dbVol}dB";
                }

                var row1 = row;
                var col1 = col;
                volumeEdit.TextSubmitted += (string newText) => OnMatrixVolumeSubmitted(newText, volumeEdit, row1, col1);
                volumeEdit.FocusExited += () => OnMatrixVolumeSubmitted(volumeEdit.Text, volumeEdit, row1, col1);
                LineEditDbDragSlider.EnableVolume(volumeEdit);
                _routingMatrixGrid.AddChild(volumeEdit);
            }
        }
        _routingContainer.Visible = true;

    }

    /// <summary>
    /// Handles matrix volume submission. Converts dB to linear and updates CuePatch.
    /// </summary>
    /// <param name="text">Submitted text.</param>
    /// <param name="textField">LineEdit field.</param>
    /// <param name="inputCh">Input channel index.</param>
    /// <param name="outputCh">Output channel index.</param>
    private void OnMatrixVolumeSubmitted(string text, LineEdit textField, int inputCh, int outputCh)
    {
        if (_focusedCue == null || _focusedAudioComponent?.Routing == null || textField == null)
            return;
        if (_globalData?.HistoryManager?.IsRestoring == true)
            return;

        GD.Print($"AudioInspector:OnMatrixVolumeSubmitted - In {inputCh}. Out {outputCh}");
        try
        {
            float dbValue;
            if (string.IsNullOrWhiteSpace((text ?? string.Empty).Replace("dB", "").Trim()))
            {
                dbValue = -60.0f;
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:OnMatrixVolumeSubmitted - Blank input treated as OFF for In {inputCh}, Out {outputCh}", 0);
            }
            else if (!float.TryParse((text ?? string.Empty).Replace("dB", "").Trim(), out dbValue))
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:OnMatrixVolumeSubmitted - Invalid matrix volume: {text}", 1);
                return;
            }

            float linear = (float)UiUtilities.DbToLinear(dbValue.ToString());
            float current = _focusedAudioComponent.Routing.GetVolume(inputCh, outputCh);
            if (Math.Abs(current - linear) < 1e-6f)
            {
                if (linear > 0.0f)
                    textField.Text = $"{UiUtilities.LinearToDb(linear)}dB";
                if (textField.HasFocus())
                    textField.ReleaseFocus();
                return;
            }

            // Discrete cell commit — each matrix cell change is its own undo step.
            _globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit audio routing volume");
            _focusedAudioComponent.Routing.SetVolume(inputCh, outputCh, linear);
            if (linear > 0.0f)
            {
                var dbReturn = UiUtilities.LinearToDb(linear);
                textField.Text = $"{dbReturn}dB";
            }
            else
            {
                textField.Text = string.Empty;
            }
            if (textField.HasFocus())
                textField.ReleaseFocus();
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:OnMatrixVolumeSubmitted - Error: {ex.Message}", 2);
        } 
    }



    /// <summary>
    /// Gets standard channel labels based on count. For inputs (audio file) or outputs (patch/device).
    /// </summary>
    /// <param name="count">Number of channels.</param>
    /// <param name="isInput">True for input labels.</param>
    /// <returns>List of labels.</returns>
    private List<string> GetChannelLabels(int count, bool isInput) // New helper
    {
        return count switch
        {
            1 => new List<string> { "Mono" },
            2 => new List<string> { "Left", "Right" },
            4 => new List<string> { "Front Left", "Front Right", "Rear Left", "Rear Right" }, // Quad
            6 => new List<string> { "Front Left", "Front Right", "Center", "LFE", "Surround Left", "Surround Right" }, // 5.1
            8 => new List<string> { "Front Left", "Front Right", "Center", "LFE", "Surround Left", "Surround Right", "Surround Back Left", "Surround Back Right" }, // 7.1
            _ => Enumerable.Range(1, count).Select(i => $"Ch {i}").ToList() // Fallback for others
        };
    }
    

    private StyleBoxFlat _fileUrlMissingStyle;
    private bool _fileUrlMissing;
    private Button _deleteAudioComponentButton;

    /// <summary>
    /// Refreshes the file URL field when media paths are rewritten (e.g. after show-local backup).
    /// </summary>
    private void RefreshMediaPathDisplay()
    {
        if (!IsInsideTree() || _fileUrl == null || _focusedAudioComponent == null)
            return;

        string path = _focusedAudioComponent.AudioFile ?? string.Empty;
        if (!string.Equals(_fileUrl.Text, path, StringComparison.Ordinal))
            _fileUrl.Text = path;

        // Re-check missing state after path rewrite / backup (autoloads via SceneTree root).
        GetTree()?.Root?.GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")
            ?.CheckCue(_focusedCue?.Id ?? -1);
        ApplyFileUrlMissingStyleFromHealth();
    }

    private void OnCueMediaHealthChanged(int cueId, bool hasIssue, string message)
    {
        if (_focusedCue == null || _focusedCue.Id != cueId)
            return;
        // Only style this inspector's URL if *audio* is among the missing paths
        ApplyFileUrlMissingStyleFromHealth();
    }

    /// <summary>
    /// Styles the audio URL field only when this cue's audio path is reported missing
    /// (not when only video/other media is missing).
    /// </summary>
    private void ApplyFileUrlMissingStyleFromHealth()
    {
        if (!IsInsideTree())
            return;

        if (_focusedCue == null || _focusedAudioComponent == null ||
            string.IsNullOrWhiteSpace(_focusedAudioComponent.AudioFile))
        {
            ApplyFileUrlMissingStyle(false, null);
            return;
        }

        var health = GetTree()?.Root?.GetNodeOrNull<MediaHealthService>("/root/MediaHealthService");
        bool missing = health != null && health.IsPathMissing(_focusedCue.Id, _focusedAudioComponent.AudioFile);
        ApplyFileUrlMissingStyle(missing, missing ? "File Missing" : null);
    }

    /// <summary>
    /// Applies or clears italic + red border styling on the URL field for missing media.
    /// </summary>
    private void ApplyFileUrlMissingStyle(bool missing, string tooltip)
    {
        _fileUrlMissingStyle ??= InspectorMediaUrlStyle.CreateMissingStyle();
        InspectorMediaUrlStyle.Apply(_fileUrl, _fileUrlMissingStyle, missing, tooltip);
        _fileUrlMissing = missing;
    }

    /// <summary>
    /// Targets for the next edit: multi-edit subset, or the single focused audio component.
    /// </summary>
    private List<(Cue Cue, AudioComponent Component)> GetAudioTargets()
    {
        if (_isMultiEdit)
            return _audioTargets ?? new List<(Cue, AudioComponent)>();
        if (_focusedCue != null && _focusedAudioComponent != null)
            return new List<(Cue, AudioComponent)> { (_focusedCue, _focusedAudioComponent) };
        return new List<(Cue, AudioComponent)>();
    }

    private bool UseMultiHistory() => GetAudioTargets().Count > 1;

    /// <summary>
    /// Records history before mutating audio targets (cuelist when multi).
    /// </summary>
    private void RecordAudioHistory(string singleDescription, string coalesceKey = null)
    {
        var targets = GetAudioTargets();
        if (targets.Count == 0)
            return;
        InspectorMultiEditSupport.RecordBeforeEdit(
            _globalData,
            UseMultiHistory(),
            targets[^1].Cue,
            singleDescription,
            "Multi-edit " + singleDescription,
            coalesceKey);
    }

    private string AudioCoalesceKey(string field) =>
        UseMultiHistory()
            ? $"multi:audio:{field}"
            : (_focusedCue != null ? $"cue:{_focusedCue.Id}:audio:{field}" : null);

    /// <summary>
    /// Called when a cue shell is selected. Updates UI based on presence of AudioComponent,
    /// including multi-edit when multiple cues are selected.
    /// </summary>
    /// <param name="cueId">The ID of the selected cue.</param>
}
