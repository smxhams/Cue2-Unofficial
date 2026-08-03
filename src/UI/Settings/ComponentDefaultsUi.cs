// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using System;
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
using Godot;
using AppSettings = Cue2.Domain.ShowSettings.Settings;

namespace Cue2.UI.Settings;

/// <summary>
/// Shared OptionButton population for component-default audio output and target layer controls.
/// </summary>
/// <remarks>
/// Metadata tokens:
/// audio — <c>preferred</c>, <c>none</c>, <c>patch:{id}</c>, <c>direct:{name}</c>;
/// layer — <c>first</c>, <c>none</c>, <c>layer:{id}</c>.
/// </remarks>
public static class ComponentDefaultsUi
{
    /// <summary>
    /// Rebuilds an audio-output OptionButton from live patches and devices, then selects the stored default.
    /// </summary>
    public static void PopulateAudioOutputOption(
        OptionButton button,
        AppSettings settings,
        AudioDevices audioDevices,
        ComponentAudioOutputDefaultMode mode,
        int patchId,
        string directOutput)
    {
        if (button == null) return;

        button.SetBlockSignals(true);
        try
        {
            button.Clear();

            AddTokenItem(button, "Preferred (Default Patch)", "preferred");
            AddTokenItem(button, "No output", "none");

            if (settings != null)
            {
                foreach (var kv in settings.GetAudioOutputPatches())
                {
                    var patch = kv.Value;
                    if (patch == null || !GodotObject.IsInstanceValid(patch)) continue;
                    AddTokenItem(button, $"Patch: {patch.Name}", $"patch:{patch.Id}");
                }
            }

            if (audioDevices != null)
            {
                foreach (var name in audioDevices.GetAvailableAudioDeviceNames())
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    AddTokenItem(button, $"Direct: {name}", $"direct:{name}");
                }
            }

            string want = EncodeAudioOutput(mode, patchId, directOutput);
            int selected = FindTokenIndex(button, want);

            // Missing stored patch/device — add a visible placeholder so the choice is not silently rewritten.
            if (selected < 0)
            {
                if (mode == ComponentAudioOutputDefaultMode.Patch && patchId >= 0)
                {
                    AddTokenItem(button, $"!!! Missing patch id {patchId}", $"patch:{patchId}");
                    selected = button.ItemCount - 1;
                }
                else if (mode == ComponentAudioOutputDefaultMode.Direct
                         && !string.IsNullOrEmpty(directOutput))
                {
                    AddTokenItem(button, $"!!! Missing: {directOutput}", $"direct:{directOutput}");
                    selected = button.ItemCount - 1;
                }
                else
                {
                    selected = 0; // Preferred
                }
            }

            button.Selected = selected;
        }
        finally
        {
            button.SetBlockSignals(false);
        }
    }

    /// <summary>
    /// Reads the selected audio-output default from an OptionButton.
    /// </summary>
    public static void ReadAudioOutputSelection(
        OptionButton button,
        out ComponentAudioOutputDefaultMode mode,
        out int patchId,
        out string directOutput)
    {
        mode = ComponentAudioOutputDefaultMode.Preferred;
        patchId = -1;
        directOutput = string.Empty;
        if (button == null || button.Selected < 0) return;

        string token = button.GetItemMetadata(button.Selected).AsString();
        DecodeAudioOutput(token, out mode, out patchId, out directOutput);
    }

    /// <summary>
    /// Rebuilds a target-layer OptionButton from live layers, then selects the stored default.
    /// </summary>
    public static void PopulateTargetLayerOption(
        OptionButton button,
        ComponentTargetLayerDefaultMode mode,
        int layerId)
    {
        if (button == null) return;

        button.SetBlockSignals(true);
        try
        {
            button.Clear();
            AddTokenItem(button, "First available layer", "first");
            AddTokenItem(button, "No output", "none");

            if (DisplaysManager.Layers != null)
            {
                foreach (var layer in DisplaysManager.Layers)
                {
                    if (layer == null) continue;
                    string name = string.IsNullOrEmpty(layer.LayerName)
                        ? $"Layer {layer.LayerId}"
                        : layer.LayerName;
                    AddTokenItem(button, name, $"layer:{layer.LayerId}");
                }
            }

            string want = EncodeTargetLayer(mode, layerId);
            int selected = FindTokenIndex(button, want);

            if (selected < 0)
            {
                if (mode == ComponentTargetLayerDefaultMode.Layer && layerId >= 0)
                {
                    AddTokenItem(button, $"!!! Missing layer {layerId}", $"layer:{layerId}");
                    selected = button.ItemCount - 1;
                }
                else
                {
                    selected = 0; // First available
                }
            }

            button.Selected = selected;
        }
        finally
        {
            button.SetBlockSignals(false);
        }
    }

    /// <summary>
    /// Reads the selected target-layer default from an OptionButton.
    /// </summary>
    public static void ReadTargetLayerSelection(
        OptionButton button,
        out ComponentTargetLayerDefaultMode mode,
        out int layerId)
    {
        mode = ComponentTargetLayerDefaultMode.FirstAvailable;
        layerId = -1;
        if (button == null || button.Selected < 0) return;

        string token = button.GetItemMetadata(button.Selected).AsString();
        DecodeTargetLayer(token, out mode, out layerId);
    }

    /// <summary>
    /// True when audio output defaults match system factory (Preferred, empty patch/direct).
    /// </summary>
    public static bool IsAudioOutputAtSystem(
        ComponentAudioOutputDefaultMode mode,
        int patchId,
        string directOutput)
    {
        return mode == ComponentAudioOutputDefaultMode.Preferred
               && patchId < 0
               && string.IsNullOrEmpty(directOutput);
    }

    /// <summary>
    /// True when target-layer defaults match system factory (First available).
    /// </summary>
    public static bool IsTargetLayerAtSystem(
        ComponentTargetLayerDefaultMode mode,
        int layerId)
    {
        return mode == ComponentTargetLayerDefaultMode.FirstAvailable && layerId < 0;
    }

    public static string EncodeAudioOutput(
        ComponentAudioOutputDefaultMode mode,
        int patchId,
        string directOutput)
    {
        return mode switch
        {
            ComponentAudioOutputDefaultMode.None => "none",
            ComponentAudioOutputDefaultMode.Patch => $"patch:{patchId}",
            ComponentAudioOutputDefaultMode.Direct => $"direct:{directOutput ?? string.Empty}",
            _ => "preferred"
        };
    }

    public static void DecodeAudioOutput(
        string token,
        out ComponentAudioOutputDefaultMode mode,
        out int patchId,
        out string directOutput)
    {
        mode = ComponentAudioOutputDefaultMode.Preferred;
        patchId = -1;
        directOutput = string.Empty;
        if (string.IsNullOrEmpty(token)) return;

        if (token == "none")
        {
            mode = ComponentAudioOutputDefaultMode.None;
            return;
        }

        if (token.StartsWith("patch:", StringComparison.Ordinal))
        {
            mode = ComponentAudioOutputDefaultMode.Patch;
            if (int.TryParse(token.AsSpan("patch:".Length), out int id))
                patchId = id;
            return;
        }

        if (token.StartsWith("direct:", StringComparison.Ordinal))
        {
            mode = ComponentAudioOutputDefaultMode.Direct;
            directOutput = token.Substring("direct:".Length);
            return;
        }

        mode = ComponentAudioOutputDefaultMode.Preferred;
    }

    public static string EncodeTargetLayer(ComponentTargetLayerDefaultMode mode, int layerId)
    {
        return mode switch
        {
            ComponentTargetLayerDefaultMode.None => "none",
            ComponentTargetLayerDefaultMode.Layer => $"layer:{layerId}",
            _ => "first"
        };
    }

    public static void DecodeTargetLayer(
        string token,
        out ComponentTargetLayerDefaultMode mode,
        out int layerId)
    {
        mode = ComponentTargetLayerDefaultMode.FirstAvailable;
        layerId = -1;
        if (string.IsNullOrEmpty(token)) return;

        if (token == "none")
        {
            mode = ComponentTargetLayerDefaultMode.None;
            return;
        }

        if (token.StartsWith("layer:", StringComparison.Ordinal))
        {
            mode = ComponentTargetLayerDefaultMode.Layer;
            if (int.TryParse(token.AsSpan("layer:".Length), out int id))
                layerId = id;
            return;
        }

        mode = ComponentTargetLayerDefaultMode.FirstAvailable;
    }

    private static void AddTokenItem(OptionButton button, string label, string token)
    {
        int index = button.ItemCount;
        button.AddItem(label);
        button.SetItemMetadata(index, token);
    }

    private static int FindTokenIndex(OptionButton button, string token)
    {
        for (int i = 0; i < button.ItemCount; i++)
        {
            if (button.GetItemMetadata(i).AsString() == token)
                return i;
        }
        return -1;
    }
}
