namespace Cue2.Shared.Decoders;

/// <summary>
/// A single timed subtitle cue extracted from a text-based subtitle stream.
/// </summary>
public sealed class SubtitleCueEntry
{
    /// <summary>Display start time in media microseconds.</summary>
    public long StartUs { get; init; }

    /// <summary>Display end time in media microseconds.</summary>
    public long EndUs { get; init; }

    /// <summary>Plain (or lightly cleaned) text to show.</summary>
    public string Text { get; init; } = string.Empty;
}
