using System.Globalization;
using System.Text.RegularExpressions;
using YoutubeExplode.Videos;

namespace YouTubeDownloader.Core;

/// <summary>
/// Links to the project's player page on GitHub Pages, which plays a video full-window through
/// YouTube's embedded player. A plain embed link no longer plays when typed into a browser
/// (YouTube answers "Error 153" without a Referer), so this is the link to hand out instead.
/// </summary>
public static class PlayerLink
{
    public const string PageUrl = "https://nitro70.github.io/YoutubeDownloader/";

    private static readonly Regex LivePath = new(@"youtube\.com/live/([A-Za-z0-9_-]{11})", RegexOptions.IgnoreCase);
    private static readonly Regex Duration = new(@"^(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?$");

    /// <summary>
    /// The player page link for a YouTube video link, keeping its start time (t or start).
    /// False for search words or any link that isn't to a single video.
    /// </summary>
    public static bool TryCreate(string? text, out string link)
    {
        link = string.Empty;
        if (!YouTubeInput.TryGetUrl(text, out string url)) return false;

        string? id = VideoId.TryParse(url)?.Value;
        id ??= LivePath.Match(url) is { Success: true } m ? m.Groups[1].Value : null;
        if (id is null) return false;

        link = PageUrl + "?v=" + id;
        if (StartSeconds(url) is int start && start > 0) link += "&t=" + start.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static int? StartSeconds(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        foreach (string part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string key = part[..eq];
            if (key is "t" or "start") return ParseTime(Uri.UnescapeDataString(part[(eq + 1)..]));
        }
        return null;
    }

    // 90, 90s, 1m30s, 1h2m3s
    private static int? ParseTime(string value)
    {
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds)) return seconds;
        var m = Duration.Match(value);
        if (value.Length == 0 || !m.Success) return null;
        int Part(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture) : 0;
        return Part(1) * 3600 + Part(2) * 60 + Part(3);
    }
}
