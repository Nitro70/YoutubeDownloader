namespace YouTubeDownloader.Core;

/// <summary>Lightweight, UI-friendly snapshot of a YouTube video's metadata.</summary>
public sealed record VideoInfo(
    string Id,
    string Title,
    string Author,
    TimeSpan? Duration,
    string? ThumbnailUrl)
{
    public string DurationDisplay =>
        Duration is { } d
            ? (d.TotalHours >= 1
                ? $"{(int)d.TotalHours}:{d.Minutes:D2}:{d.Seconds:D2}"
                : $"{d.Minutes}:{d.Seconds:D2}")
            : "Live / unknown";
}

/// <summary>What the user wants out of a download.</summary>
public enum DownloadKind
{
    /// <summary>Progressive MP4 (muxed video+audio) — single file, no ffmpeg needed.</summary>
    Video,

    /// <summary>Audio-only M4A (AAC) — single file, no transcode needed.</summary>
    Audio
}
