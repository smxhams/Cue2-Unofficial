using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Godot;

namespace Cue2.Shared;

/// <summary>
/// Kind of media health problem tracked for a cue.
/// </summary>
public enum MediaHealthIssueKind
{
    /// <summary>Referenced media file does not exist on disk.</summary>
    FileMissing = 0
}

/// <summary>
/// Media health issue currently associated with a cue.
/// </summary>
public sealed class CueMediaIssue
{
    /// <summary>Issue category.</summary>
    public MediaHealthIssueKind Kind { get; init; }

    /// <summary>Stored media path(s) involved (as on the cue component).</summary>
    public IReadOnlyList<string> Paths { get; init; } = Array.Empty<string>();

    /// <summary>User-facing tooltip / log text (one line per path).</summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Tracks media file health (e.g. missing URLs) for cues.
/// Sources: low-frequency background scan, playback failures, path assignment.
/// Emits <see cref="GlobalSignals.CueMediaHealthChanged"/> when a cue's issue state changes.
/// Logs each specific missing path only once until resolved and missing again.
/// </summary>
public partial class MediaHealthService : Node
{
    /// <summary>Seconds between background full scans (kept light).</summary>
    public const double CheckIntervalSeconds = 12.0;

    private GlobalData _globalData;
    private GlobalSignals _globalSignals;

    /// <summary>cueId → active issue (currently one aggregated FileMissing issue per cue).</summary>
    private readonly Dictionary<int, CueMediaIssue> _issues = new();

    /// <summary>
    /// Keys already logged for a missing path so we do not spam the log on every scan.
    /// Format: <c>{cueId}|missing|{normalizedStoredPath}</c>
    /// </summary>
    private readonly HashSet<string> _loggedMissingKeys = new(StringComparer.OrdinalIgnoreCase);

    private Timer _timer;
    private int _scanCursor;

    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

        _timer = new Timer
        {
            WaitTime = CheckIntervalSeconds,
            OneShot = false,
            Autostart = true
        };
        AddChild(_timer);
        _timer.Timeout += OnBackgroundScanTick;

        GD.Print("MediaHealthService:_Ready - Periodic media health scan enabled.");
    }

    /// <summary>
    /// Returns true if the cue currently has a known media health issue.
    /// </summary>
    public bool HasIssue(int cueId) => _issues.ContainsKey(cueId);

    /// <summary>
    /// Tries to get the current issue for a cue.
    /// </summary>
    public bool TryGetIssue(int cueId, out CueMediaIssue issue) =>
        _issues.TryGetValue(cueId, out issue);

    /// <summary>
    /// Tooltip text for the shell-bar issue indicator, or empty if healthy.
    /// </summary>
    public string GetIssueTooltip(int cueId) =>
        _issues.TryGetValue(cueId, out var issue) ? issue.Message : string.Empty;

    /// <summary>
    /// True when the given stored path is currently tracked as missing for this cue.
    /// Used by inspectors so audio vs video URL fields style independently.
    /// </summary>
    public bool IsPathMissing(int cueId, string storedPath)
    {
        if (cueId < 0 || string.IsNullOrWhiteSpace(storedPath))
            return false;
        if (!_issues.TryGetValue(cueId, out var issue) ||
            issue.Kind != MediaHealthIssueKind.FileMissing ||
            issue.Paths == null)
            return false;

        return issue.Paths.Any(p => string.Equals(p, storedPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reports that a media file path is missing for a cue (playback or other hard failure).
    /// UI is updated once; log only if this cue+path has not been logged yet.
    /// </summary>
    /// <param name="cueId">Cue id.</param>
    /// <param name="storedUrl">Path as stored on the component (relative or absolute).</param>
    public void ReportFileMissing(int cueId, string storedUrl)
    {
        if (cueId < 0 || string.IsNullOrWhiteSpace(storedUrl))
            return;

        // Merge with any other missing paths already tracked for this cue
        var paths = new List<string>();
        if (_issues.TryGetValue(cueId, out var existing) &&
            existing.Kind == MediaHealthIssueKind.FileMissing &&
            existing.Paths != null)
        {
            paths.AddRange(existing.Paths);
        }

        if (!paths.Any(p => string.Equals(p, storedUrl, StringComparison.OrdinalIgnoreCase)))
            paths.Add(storedUrl);

        ApplyMissingIssue(cueId, paths);
    }

    /// <summary>
    /// Re-evaluates all media paths on a cue and updates/clears the issue state.
    /// Call after path assignment or when a cue is selected.
    /// </summary>
    public void CheckCue(int cueId)
    {
        if (CueList.CueIndex == null || !CueList.CueIndex.TryGetValue(cueId, out var cue) || cue == null)
        {
            ClearIssue(cueId);
            return;
        }

        CheckCue(cue);
    }

    /// <summary>
    /// Re-evaluates media paths for a cue instance.
    /// </summary>
    public void CheckCue(Cue cue)
    {
        if (cue == null)
            return;

        var missing = CollectMissingPaths(cue);
        if (missing.Count == 0)
            ClearIssue(cue.Id);
        else
            ApplyMissingIssue(cue.Id, missing);
    }

    /// <summary>
    /// Clears any media health issue for the cue and notifies UI.
    /// </summary>
    public void ClearIssue(int cueId)
    {
        if (!_issues.Remove(cueId))
            return;

        // Allow future re-log if the same path goes missing again after being healthy
        _loggedMissingKeys.RemoveWhere(k => k.StartsWith($"{cueId}|missing|", StringComparison.Ordinal));

        _globalSignals?.EmitSignal(nameof(GlobalSignals.CueMediaHealthChanged), cueId, false, string.Empty);
    }

    /// <summary>
    /// Quiet full re-check of all media cues (no summary log). Call after media copies finish.
    /// </summary>
    public void RecheckAllQuiet()
    {
        if (CueList.CueIndex == null || CueList.CueIndex.Count == 0)
            return;

        foreach (var cue in CueList.CueIndex.Values)
        {
            if (cue == null || !HasMediaComponent(cue))
                continue;
            CheckCue(cue);
        }
    }

    /// <summary>
    /// Clears all tracked issues (e.g. new session).
    /// </summary>
    public void ClearAll()
    {
        var ids = _issues.Keys.ToList();
        _issues.Clear();
        _loggedMissingKeys.Clear();
        foreach (int id in ids)
            _globalSignals.EmitSignal(nameof(GlobalSignals.CueMediaHealthChanged), id, false, string.Empty);
    }

    /// <summary>
    /// Immediately checks every cue with media (not round-robin).
    /// Updates shell/inspector UI via existing signals, logs individual new missing paths,
    /// and emits a summary log: "All files present" or "N file(s) missing".
    /// </summary>
    /// <returns>Number of missing media path references found.</returns>
    public int CheckAllMediaNow()
    {
        if (CueList.CueIndex == null || CueList.CueIndex.Count == 0)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "File presence: no cues to check.", 0);
            return 0;
        }

        int totalMediaPaths = 0;
        int missingPaths = 0;
        int cuesWithMedia = 0;

        foreach (var cue in CueList.CueIndex.Values)
        {
            if (cue == null || !HasMediaComponent(cue))
                continue;

            cuesWithMedia++;

            var audio = cue.GetAudioComponent();
            if (audio != null && !string.IsNullOrWhiteSpace(audio.AudioFile))
            {
                totalMediaPaths++;
                if (!MediaFileExists(audio.AudioFile))
                    missingPaths++;
            }

            var video = cue.GetVideoComponent();
            if (video != null && !string.IsNullOrWhiteSpace(video.VideoFile))
            {
                totalMediaPaths++;
                if (!MediaFileExists(video.VideoFile))
                    missingPaths++;
            }

            // Apply UI/log side-effects for this cue
            CheckCue(cue);
        }

        if (totalMediaPaths == 0)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "File presence: no media files referenced.", 0);
        }
        else if (missingPaths == 0)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"All files present ({totalMediaPaths} media path(s) on {cuesWithMedia} cue(s)).", 0);
        }
        else
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"{missingPaths} file(s) missing (of {totalMediaPaths} media path(s) on {cuesWithMedia} cue(s)).",
                1);
        }

        GD.Print($"MediaHealthService:CheckAllMediaNow - total={totalMediaPaths} missing={missingPaths} cues={cuesWithMedia}");
        return missingPaths;
    }

    private void OnBackgroundScanTick()
    {
        if (CueList.CueIndex == null || CueList.CueIndex.Count == 0)
            return;

        // Round-robin a small batch each tick to keep cost minimal
        var cues = CueList.CueIndex.Values.Where(c => c != null).ToList();
        if (cues.Count == 0)
            return;

        const int batchSize = 8;
        int checkedCount = 0;
        int start = _scanCursor % cues.Count;

        for (int i = 0; i < cues.Count && checkedCount < batchSize; i++)
        {
            int index = (start + i) % cues.Count;
            var cue = cues[index];
            // Only spend File.Exists on cues that actually have media
            if (!HasMediaComponent(cue))
                continue;

            CheckCue(cue);
            checkedCount++;
        }

        _scanCursor = (start + Math.Max(1, checkedCount)) % Math.Max(1, cues.Count);
    }

    private static bool HasMediaComponent(Cue cue)
    {
        var audio = cue.GetAudioComponent();
        if (audio != null && !string.IsNullOrWhiteSpace(audio.AudioFile))
            return true;
        var video = cue.GetVideoComponent();
        if (video != null && !string.IsNullOrWhiteSpace(video.VideoFile))
            return true;
        return false;
    }

    private List<string> CollectMissingPaths(Cue cue)
    {
        var missing = new List<string>();

        var audio = cue.GetAudioComponent();
        if (audio != null && !string.IsNullOrWhiteSpace(audio.AudioFile))
        {
            if (!MediaFileExists(audio.AudioFile))
                missing.Add(audio.AudioFile);
        }

        var video = cue.GetVideoComponent();
        if (video != null && !string.IsNullOrWhiteSpace(video.VideoFile))
        {
            if (!MediaFileExists(video.VideoFile))
                missing.Add(video.VideoFile);
        }

        return missing;
    }

    private bool MediaFileExists(string storedPath) =>
        MediaPaths.Exists(storedPath, _globalData?.SessionDir);

    private void ApplyMissingIssue(int cueId, List<string> missingPaths)
    {
        if (missingPaths == null || missingPaths.Count == 0)
        {
            ClearIssue(cueId);
            return;
        }

        // Stable order for message comparison
        var ordered = missingPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string message = string.Join("\n", ordered.Select(p => $"File Missing: {p}"));

        bool stateChanged = true;
        if (_issues.TryGetValue(cueId, out var previous) &&
            previous.Kind == MediaHealthIssueKind.FileMissing &&
            string.Equals(previous.Message, message, StringComparison.Ordinal))
        {
            stateChanged = false;
        }

        _issues[cueId] = new CueMediaIssue
        {
            Kind = MediaHealthIssueKind.FileMissing,
            Paths = ordered,
            Message = message
        };

        // Log each path once
        foreach (string path in ordered)
        {
            string key = $"{cueId}|missing|{path}";
            if (_loggedMissingKeys.Add(key))
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                    $"Media missing on cue {cueId}: {path}", 1);
                GD.Print($"MediaHealthService:ApplyMissingIssue - Logged missing file cue={cueId} path={path}");
            }
        }

        if (stateChanged)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.CueMediaHealthChanged), cueId, true, message);
        }
    }
}
