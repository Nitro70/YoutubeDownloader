namespace YouTubeDownloader.Core;

/// <summary>Tells a pasted link apart from typed search words.</summary>
public static class YouTubeInput
{
    // YouTube links people type or copy without the "https://" in front.
    private static readonly string[] BareYouTubePrefixes =
    {
        "youtube.com/", "www.youtube.com/", "m.youtube.com/", "music.youtube.com/", "youtu.be/"
    };

    /// <summary>
    /// True when <paramref name="text"/> is a link: any absolute http(s) URL as-is, or a
    /// YouTube link typed without "https://" (which gets it added). Anything else is
    /// search words.
    /// </summary>
    public static bool TryGetUrl(string? text, out string url)
    {
        url = string.Empty;
        string t = text?.Trim() ?? string.Empty;
        if (t.Length == 0) return false;

        if (Uri.TryCreate(t, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            url = t;
            return true;
        }

        if (t.Any(char.IsWhiteSpace)) return false;

        foreach (string prefix in BareYouTubePrefixes)
        {
            if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate("https://" + t, UriKind.Absolute, out _))
            {
                url = "https://" + t;
                return true;
            }
        }
        return false;
    }
}
