namespace YouTubeDownloader.Core;

/// <summary>Lightweight, UI-friendly snapshot of a YouTube video's metadata.</summary>
public sealed record VideoInfo(
    string Id,
    string Title,
    string Author,
    TimeSpan? Duration,
    string? ThumbnailUrl)
{
    public string DurationDisplay => DurationFormat.Format(Duration);
}

/// <summary>One video from a YouTube search.</summary>
public sealed record SearchResult(
    string Id,
    string Url,
    string Title,
    string Author,
    TimeSpan? Duration,
    string? ThumbnailUrl)
{
    public string DurationDisplay => DurationFormat.Format(Duration);

    /// <summary>Second line for a result row: "Channel · 3:32".</summary>
    public string Details => $"{Author}  ·  {DurationDisplay}";
}

public static class DurationFormat
{
    /// <summary>"3:32", "1:02:05", or "Live" when YouTube reports no length.</summary>
    public static string Format(TimeSpan? duration) =>
        duration is { } d
            ? (d.TotalHours >= 1
                ? $"{(int)d.TotalHours}:{d.Minutes:D2}:{d.Seconds:D2}"
                : $"{d.Minutes}:{d.Seconds:D2}")
            : "Live";
}

/// <summary>What the user wants out of a download.</summary>
public enum DownloadKind
{
    /// <summary>Progressive MP4 (muxed video+audio): single file, no ffmpeg needed.</summary>
    Video,

    /// <summary>Audio-only M4A (AAC): single file, no transcode needed.</summary>
    Audio
}
