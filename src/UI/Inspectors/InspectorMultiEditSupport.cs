using System;
using System.Collections.Generic;
using System.Linq;
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

namespace Cue2.UI.Inspectors;

/// <summary>
/// Shared multi-edit helpers for component inspectors (and shell-style history).
/// </summary>
/// <remarks>
/// Multi-edit is active when Settings → Multi-edit is on and more than one cue is selected.
/// Component targets are the subset of the selection that actually has that component type.
/// History uses a cuelist snapshot when applying to two or more cues so undo restores together.
/// Field display policy: show a value when all targets agree; otherwise blank / mixed.
/// </remarks>
public static class InspectorMultiEditSupport
{
    /// <summary>Placeholder for mixed / multi LineEdit fields.</summary>
    public const string MultiPlaceholder = "Multiple selected";

    /// <summary>
    /// True when the show setting allows multi-edit and more than one cue is selected.
    /// </summary>
    public static bool ShouldUseMultiEdit(GlobalData globalData)
    {
        if (globalData?.Settings == null || !globalData.Settings.MultiEditEnabled)
            return false;
        return ShellSelection.SelectedCues != null && ShellSelection.SelectedCues.Count > 1;
    }

    /// <summary>
    /// Snapshot of currently selected non-null cues (selection order).
    /// </summary>
    public static List<Cue> GetSelectedCues()
    {
        if (ShellSelection.SelectedCues == null || ShellSelection.SelectedCues.Count == 0)
            return new List<Cue>();
        return ShellSelection.SelectedCues.Where(c => c != null).ToList();
    }

    /// <summary>
    /// Collects selected cues that have a component of type <typeparamref name="T"/>.
    /// </summary>
    /// <param name="getter">Resolves the component from a cue (may return null).</param>
    /// <returns>Ordered list of cue + component pairs.</returns>
    public static List<(Cue Cue, T Component)> CollectComponentTargets<T>(Func<Cue, T> getter)
        where T : class
    {
        var result = new List<(Cue, T)>();
        if (getter == null)
            return result;

        foreach (var cue in GetSelectedCues())
        {
            var component = getter(cue);
            if (component != null)
                result.Add((cue, component));
        }

        return result;
    }

    /// <summary>
    /// Records history before a mutation: cuelist scope when <paramref name="multiHistory"/>,
    /// otherwise single-cue scope for <paramref name="primaryCue"/>.
    /// </summary>
    public static void RecordBeforeEdit(
        GlobalData globalData,
        bool multiHistory,
        Cue primaryCue,
        string singleDescription,
        string multiDescription,
        string coalesceKey = null)
    {
        var history = globalData?.HistoryManager;
        if (history == null || history.IsRestoring)
            return;

        if (multiHistory)
            history.RecordCuelistChange(multiDescription ?? singleDescription, coalesceKey);
        else if (primaryCue != null)
            history.RecordCueChange(primaryCue.Id, singleDescription, coalesceKey);
    }

    /// <summary>
    /// Ends a coalesce session for multi or single-cue continuous edits.
    /// </summary>
    public static void EndCoalesce(
        GlobalData globalData,
        bool multiHistory,
        Cue primaryCue,
        string multiKey,
        string singleKey)
    {
        var history = globalData?.HistoryManager;
        if (history == null)
            return;

        if (multiHistory)
            history.EndCoalesceSession(multiKey);
        else if (!string.IsNullOrEmpty(singleKey))
            history.EndCoalesceSession(singleKey);
    }

    /// <summary>
    /// True when every value equals the first under <see cref="EqualityComparer{T}.Default"/>.
    /// </summary>
    public static bool TryGetUniform<T>(IEnumerable<T> values, out T uniform)
    {
        uniform = default;
        if (values == null)
            return false;

        bool any = false;
        var comparer = EqualityComparer<T>.Default;
        foreach (var v in values)
        {
            if (!any)
            {
                uniform = v;
                any = true;
                continue;
            }

            if (!comparer.Equals(uniform, v))
                return false;
        }

        return any;
    }

    /// <summary>
    /// Uniform float check with absolute epsilon.
    /// </summary>
    public static bool TryGetUniformFloat(IEnumerable<float> values, out float uniform, float epsilon = 1e-5f)
    {
        uniform = 0f;
        bool any = false;
        foreach (var v in values)
        {
            if (!any)
            {
                uniform = v;
                any = true;
                continue;
            }

            if (Mathf.Abs(uniform - v) > epsilon)
                return false;
        }

        return any;
    }

    /// <summary>
    /// Uniform double check with absolute epsilon.
    /// </summary>
    public static bool TryGetUniformDouble(IEnumerable<double> values, out double uniform, double epsilon = 1e-9)
    {
        uniform = 0;
        bool any = false;
        foreach (var v in values)
        {
            if (!any)
            {
                uniform = v;
                any = true;
                continue;
            }

            if (Math.Abs(uniform - v) > epsilon)
                return false;
        }

        return any;
    }

    /// <summary>
    /// Uniform Godot color check via <see cref="Color.IsEqualApprox(Color)"/>.
    /// </summary>
    public static bool TryGetUniformColor(IEnumerable<Color> values, out Color uniform)
    {
        uniform = default;
        bool any = false;
        foreach (var v in values)
        {
            if (!any)
            {
                uniform = v;
                any = true;
                continue;
            }

            if (!uniform.IsEqualApprox(v))
                return false;
        }

        return any;
    }

    /// <summary>
    /// Uniform string (ordinal) check; null and empty are distinct.
    /// </summary>
    public static bool TryGetUniformString(IEnumerable<string> values, out string uniform)
    {
        uniform = null;
        bool any = false;
        foreach (var v in values)
        {
            if (!any)
            {
                uniform = v;
                any = true;
                continue;
            }

            if (!string.Equals(uniform, v, StringComparison.Ordinal))
                return false;
        }

        return any;
    }

    /// <summary>
    /// Clears a SpinBox's text to represent a mixed multi-edit value (value kept for range).
    /// </summary>
    public static void ClearSpinBoxText(SpinBox spin)
    {
        if (spin == null)
            return;
        var line = spin.GetLineEdit();
        if (line != null)
            line.Text = string.Empty;
    }

    /// <summary>
    /// Short header for multi component editing (e.g. "MULTI-EDIT AUDIO (3/5)").
    /// </summary>
    public static string FormatComponentMultiHeader(string componentLabel, int withComponent, int selectedCount)
    {
        return $"MULTI-EDIT {componentLabel.ToUpperInvariant()} ({withComponent}/{selectedCount})";
    }

    /// <summary>
    /// Tooltip listing which selected cues include the component.
    /// </summary>
    public static string FormatComponentMultiTooltip(
        string componentLabel,
        IReadOnlyList<(Cue Cue, object Component)> targets,
        int selectedCount)
    {
        if (targets == null || targets.Count == 0)
            return $"None of the {selectedCount} selected cue(s) have a {componentLabel} component.";

        string ids = string.Join(", ", targets.Select(t => t.Cue?.Id ?? -1));
        return $"Editing {componentLabel} on {targets.Count} of {selectedCount} selected cue(s).\nCue IDs: {ids}";
    }
}
