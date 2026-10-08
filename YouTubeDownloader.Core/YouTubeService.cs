using System.Net;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;
using YouTubeDownloader.Core.Mp4;

namespace YouTubeDownloader.Core;

/// <summary>
/// Native (pure-managed) YouTube access for the phone apps, where launching yt-dlp or ffmpeg
/// as a subprocess is impossible. Backed by YoutubeExplode.
///
/// Video: YouTube serves video and audio as separate streams, so this downloads an H.264 MP4
/// video stream (up to <see cref="MaxVideoHeight"/>) and an AAC audio stream and merges them
/// with <see cref="Mp4Muxer"/>, in plain C# with no re-encoding.
/// Audio: the best AAC stream as-is, saved as .m4a. No MP3, which would need ffmpeg.
/// </summary>
public sealed class YouTubeService
{
    // One connection pool for every client, with cookies off at this level: YoutubeExplode
    // keeps each client's cookies itself. Its default HttpClient has a shared cookie jar, which
    // made every new client come back as the same visitor. The same handler on every platform;
    // the phones' native handlers compressed by default, so this asks for it. RefusedLinkHandler
    // turns a refused stream link into an exception YoutubeExplode doesn't retry.
    private static readonly HttpClient Http = new(new RefusedLinkHandler(new SocketsHttpHandler
    {
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    }));

    // Each YoutubeClient is one anonymous YouTube visitor. YouTube decides per visitor whether
    // the stream links it hands out work: for some visitors (an experiment, seen as fexp
    // 52227522 in the links) every link answers 403, for the rest they all work. So on a 403
    // the client is replaced by a new visitor; see FetchManifestAsync.
    private YoutubeClient _youtube = new(Http);

    /// <summary>How many visitors to try before giving up on a 403.</summary>
    private const int MaxVisitors = 5;

    // The last video looked up, so a download right after GetVideoInfoAsync, or alongside it,
    // doesn't fetch the same watch page (about 1 MB) again. The task is kept, not the result,
    // so a lookup still running is shared.
    private (VideoId Id, Task<Video> Lookup)? _lastVideo;

    /// <summary>Tallest video to download, in pixels. H.264 on YouTube rarely goes above 1080.</summary>
    public int MaxVideoHeight { get; set; } = 1080;

    public async Task<VideoInfo> GetVideoInfoAsync(string url, CancellationToken ct = default)
    {
        var video = await GetVideoAsync(url, ct);
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
    /// Searches YouTube and returns up to <paramref name="maxResults"/> plain videos, best
    /// match first. Channels, playlists and shelves are left out, as are ads.
    /// </summary>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, int maxResults = 10, CancellationToken ct = default)
    {
        var results = new List<SearchResult>();
        if (string.IsNullOrWhiteSpace(query) || maxResults <= 0) return results;

        await foreach (var v in _youtube.Search.GetVideosAsync(query.Trim(), ct))
        {
            string? thumb = v.Thumbnails.Count > 0
                ? v.Thumbnails.GetWithHighestResolution().Url
                : null;
            results.Add(new SearchResult(
                v.Id.Value, v.Url, v.Title, v.Author.ChannelTitle, v.Duration, thumb));
            if (results.Count >= maxResults) break;
        }
        return results;
    }

    /// <summary>
    /// Downloads <paramref name="url"/> into <paramref name="outputDirectory"/> and
    /// returns the full path to the saved file.
    /// </summary>
    /// <param name="customName">
    /// Optional base filename (no extension). When null/empty the video title is used.
    /// The extension is chosen from the selected stream's container.
    /// </param>
    /// <param name="status">
    /// What it is doing before the download starts, as a short line for the user.
    /// </param>
    public async Task<string> DownloadAsync(
        string url,
        DownloadKind kind,
        string outputDirectory,
        string? customName = null,
        IProgress<double>? progress = null,
        IProgress<string>? status = null,
        CancellationToken ct = default)
    {
        status?.Report("Finding the video…");
        var video = await GetVideoAsync(url, ct);
        status?.Report("Getting the video's streams…");
        var manifest = await FetchManifestAsync(video.Id, status, ct);

        string baseName = string.IsNullOrWhiteSpace(customName)
            ? Sanitize(video.Title)
            : Sanitize(customName);
        Directory.CreateDirectory(outputDirectory);

        if (kind == DownloadKind.Audio)
        {
            var audio = SelectAudioStream(manifest);
            // An audio-only MP4 is an M4A; name it that way so players treat it as audio.
            string ext = audio.Container == Container.Mp4 ? "m4a" : audio.Container.Name;
            string audioPath = MakeUnique(Path.Combine(outputDirectory, $"{baseName}.{ext}"));
            await DownloadOneAsync(audio, audioPath, Scaled(progress, 0, 1), ct);
            return audioPath;
        }

        var (videoStream, audioStream) = SelectVideoAndAudio(manifest);
        string output = MakeUnique(Path.Combine(outputDirectory, $"{baseName}.mp4"));

        if (audioStream is null)
        {
            // A combined stream, for the rare video YouTube still offers one for.
            await DownloadOneAsync(videoStream, output, Scaled(progress, 0, 1), ct);
            return output;
        }

        string tempBase = Path.Combine(Path.GetTempPath(), $"ytd-{video.Id}-{Guid.NewGuid():N}");
        string videoTemp = tempBase + ".video.mp4";
        string audioTemp = tempBase + ".audio.m4a";
        try
        {
            // Progress: the two downloads share 0 to 95 % by size, merging takes the rest.
            double videoBytes = videoStream.Size.Bytes;
            double audioBytes = audioStream.Size.Bytes;
            double total = Math.Max(1, videoBytes + audioBytes);
            double videoShare = 0.95 * videoBytes / total;
            double audioShare = 0.95 * audioBytes / total;

            await _youtube.Videos.Streams.DownloadAsync(videoStream, videoTemp, Scaled(progress, 0, videoShare), ct);
            await _youtube.Videos.Streams.DownloadAsync(audioStream, audioTemp, Scaled(progress, videoShare, audioShare), ct);
            await Mp4Muxer.MuxAsync(videoTemp, audioTemp, output, Scaled(progress, 0.95, 0.05), ct);
            return output;
        }
        finally
        {
            TryDelete(videoTemp);
            TryDelete(audioTemp);
        }
    }

    private Task<Video> GetVideoAsync(string url, CancellationToken ct)
    {
        var id = VideoId.Parse(url);
        if (_lastVideo is not { } last || last.Id != id || last.Lookup.IsFaulted || last.Lookup.IsCanceled)
        {
            // Not cancelled with the caller: someone else may be waiting for the same lookup.
            last = (id, _youtube.Videos.GetAsync(id, CancellationToken.None).AsTask());
            _lastVideo = last;
        }
        return last.Lookup.WaitAsync(ct);
    }

    /// <summary>
    /// Fetches the stream list, starting over as a new visitor each time YouTube answers 403.
    /// The visitor is baked into the stream links (googlevideo gets no cookies), so any client
    /// can download them afterwards.
    /// </summary>
    private async Task<StreamManifest> FetchManifestAsync(
        VideoId videoId, IProgress<string>? status, CancellationToken ct)
    {
        for (int visitor = 1; ; visitor++)
        {
            var youtube = _youtube;
            try
            {
                return await youtube.Videos.Streams.GetManifestAsync(videoId, ct);
            }
            catch (Exception ex) when (ex is StreamLinkRefusedException
                                       || ex is HttpRequestException { StatusCode: HttpStatusCode.Forbidden })
            {
                // Replace it unless another call already has, also when giving up, so that
                // trying again starts as a new visitor. The old client is dropped, not
                // disposed: a search may still be using it.
                Interlocked.CompareExchange(ref _youtube, new YoutubeClient(Http), youtube);
                if (visitor >= MaxVisitors) throw;
                status?.Report($"YouTube turned this session away, trying a new one ({visitor + 1} of {MaxVisitors})…");
            }
        }
    }

    /// <summary>Downloads one stream, removing the partial file if it fails or is cancelled.</summary>
    private async Task DownloadOneAsync(IStreamInfo stream, string path, IProgress<double>? progress, CancellationToken ct)
    {
        try
        {
            await _youtube.Videos.Streams.DownloadAsync(stream, path, progress, ct);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    private (IStreamInfo Video, IStreamInfo? Audio) SelectVideoAndAudio(StreamManifest manifest)
    {
        // H.264 in MP4 plays everywhere (iPhone Photos, Android galleries); VP9 and AV1 don't.
        var h264 = manifest.GetVideoOnlyStreams()
            .Where(s => s.Container == Container.Mp4
                        && s.VideoCodec.StartsWith("avc1", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var video = h264
            .Where(s => s.VideoQuality.MaxHeight <= MaxVideoHeight)
            .OrderByDescending(s => s.VideoQuality).ThenByDescending(s => s.Bitrate)
            .FirstOrDefault()
            ?? h264.OrderBy(s => s.VideoQuality).FirstOrDefault();

        var aac = manifest.GetAudioOnlyStreams()
            .Where(s => s.Container == Container.Mp4
                        && s.AudioCodec.StartsWith("mp4a", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var audio = aac
            .OrderByDescending(s => s.AudioCodec.Equals("mp4a.40.2", StringComparison.OrdinalIgnoreCase)) // AAC-LC first
            .ThenByDescending(s => s.Bitrate)
            .FirstOrDefault();

        if (video != null && audio != null) return (video, audio);

        var muxed = manifest.GetMuxedStreams().Where(s => s.Container == Container.Mp4).ToList();
        if (muxed.Count > 0) return (muxed.GetWithHighestVideoQuality(), null);

        throw new InvalidOperationException(
            "YouTube offers no H.264 video with AAC audio for this video, so it can't be saved as an MP4 here.");
    }

    private static IProgress<double>? Scaled(IProgress<double>? progress, double start, double share) =>
        progress is null ? null : new ScaledProgress(progress, start, share);

    /// <summary>
    /// Maps 0..1 progress of one step onto its slice of the overall 0..1, and only passes on
    /// changes of at least 0.2 % (a 1080p download otherwise sends thousands of UI updates).
    /// </summary>
    private sealed class ScaledProgress : IProgress<double>
    {
        private readonly IProgress<double> _inner;
        private readonly double _start, _share;
        private double _last = -1;

        public ScaledProgress(IProgress<double> inner, double start, double share)
        {
            _inner = inner;
            _start = start;
            _share = share;
        }

        public void Report(double value)
        {
            double v = Math.Clamp(value, 0, 1);
            if (v < 1 && v - _last < 0.002) return;
            _last = v;
            _inner.Report(_start + v * _share);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Temp leftovers are harmless; the OS clears the temp folder eventually.
        }
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
