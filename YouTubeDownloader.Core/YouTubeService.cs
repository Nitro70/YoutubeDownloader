using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Videos.Streams;

namespace YouTubeDownloader.Core;

/// <summary>
/// Native (pure-managed) YouTube access used by the iOS app, where launching
/// yt-dlp/ffmpeg as subprocesses is impossible. Backed by YoutubeExplode.
///
/// Limitations vs. the desktop yt-dlp engine:
///   * Only progressive (muxed) MP4 streams are offered for video — these
///     carry audio+video in one file, so no ffmpeg muxing is required. That
///     caps quality at whatever progressive itag YouTube serves (commonly
///     360p or 720p).
///   * "Audio" downloads the best audio-only stream as-is (usually M4A/AAC).
///     We do not transcode to MP3 because that would need ffmpeg.
/// </summary>
public sealed class YouTubeService
{
    private readonly YoutubeClient _youtube = new();

    public async Task<VideoInfo> GetVideoInfoAsync(string url, CancellationToken ct = default)
    {
        var video = await _youtube.Videos.GetAsync(url, ct);
        string? thumb = video.Thumbnails.Count > 0
            ? video.Thumbnails.GetWithHighestResolution().Url
            : null;

        return new VideoInfo(
            video.Id.Value,
            video.Title,
            video.Author.ChannelTitle,
            video.Duration,
            thumb);
    }

    /// <summary>
    /// Downloads <paramref name="url"/> into <paramref name="outputDirectory"/> and
    /// returns the full path to the saved file.
    /// </summary>
    /// <param name="customName">
    /// Optional base filename (no extension). When null/empty the video title is used.
    /// The extension is chosen from the selected stream's container.
    /// </param>
    public async Task<string> DownloadAsync(
        string url,
        DownloadKind kind,
        string outputDirectory,
        string? customName = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var video = await _youtube.Videos.GetAsync(url, ct);
        var manifest = await _youtube.Videos.Streams.GetManifestAsync(video.Id, ct);

        IStreamInfo stream = kind == DownloadKind.Audio
            ? SelectAudioStream(manifest)
            : SelectVideoStream(manifest);

        string baseName = string.IsNullOrWhiteSpace(customName)
            ? Sanitize(video.Title)
            : Sanitize(customName);

        string ext = stream.Container.Name;
        string path = Path.Combine(outputDirectory, $"{baseName}.{ext}");
        path = MakeUnique(path);

        Directory.CreateDirectory(outputDirectory);
        await _youtube.Videos.Streams.DownloadAsync(stream, path, progress, ct);
        return path;
    }

    private static IStreamInfo SelectVideoStream(StreamManifest manifest)
    {
        // Muxed = progressive (video+audio in one file). No ffmpeg needed.
        var muxed = manifest.GetMuxedStreams().ToList();
        if (muxed.Count == 0)
        {
            throw new InvalidOperationException(
                "No progressive (single-file) video stream is available for this video. " +
                "High-resolution streams require merging separate audio/video tracks, " +
                "which this mobile build can't do without ffmpeg.");
        }
        return muxed.GetWithHighestVideoQuality();
    }

    private static IStreamInfo SelectAudioStream(StreamManifest manifest)
    {
        var audio = manifest.GetAudioOnlyStreams().ToList();
        if (audio.Count == 0)
        {
            throw new InvalidOperationException("No audio-only stream is available for this video.");
        }
        // Prefer an mp4/m4a container so the file plays natively on iOS without transcoding.
        var m4a = audio.Where(s => s.Container == Container.Mp4).ToList();
        var pool = m4a.Count > 0 ? m4a : audio;
        return pool.GetWithHighestBitrate();
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "video";
        if (cleaned.Length > 120) cleaned = cleaned[..120].Trim();
        return cleaned;
    }

    private static string MakeUnique(string path)
    {
        if (!File.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
