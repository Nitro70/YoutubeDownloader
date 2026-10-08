using System.Net;

namespace YouTubeDownloader.Core;

/// <summary>
/// YouTube's video servers refused a stream link (HTTP 403). Deliberately not an
/// <see cref="HttpRequestException"/>: YoutubeExplode retries those six times as the same
/// visitor, which can't help when the refusal is about the visitor.
/// </summary>
internal sealed class StreamLinkRefusedException()
    : Exception("YouTube refused the stream link (HTTP 403).");

/// <summary>
/// Turns a 403 for a stream link (googlevideo.com/videoplayback) into a
/// <see cref="StreamLinkRefusedException"/>. While the stream list is fetched, that drops a
/// refused visitor at the first refusal instead of after YoutubeExplode's retries; during a
/// download it fails at once instead of retrying a link that stays refused. Other
/// googlevideo requests, such as the DASH manifest (whose failure YoutubeExplode tolerates),
/// are left alone.
/// </summary>
internal sealed class RefusedLinkHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Forbidden
            && request.RequestUri is { } uri
            && uri.Host.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/videoplayback", StringComparison.Ordinal))
        {
            response.Dispose();
            throw new StreamLinkRefusedException();
        }
        return response;
    }
}
