using System.Net;
using YoutubeExplode.Exceptions;

namespace YouTubeDownloader.Core;

/// <summary>
/// Readable error messages for the phone apps. iOS release builds strip the runtime's own
/// exception text down to resource keys (a failed request reads
/// "net_http_message_not_success_statuscode"), so network errors are described here.
/// </summary>
public static class ErrorText
{
    public static string Describe(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } =>
            "YouTube refused the request (HTTP 403). Try again.",
        HttpRequestException { StatusCode: { } status } =>
            $"YouTube answered with an error (HTTP {(int)status}). Try again.",
        HttpRequestException =>
            "Couldn't reach YouTube. Check the connection and try again.",
        HttpIOException =>
            "The connection to YouTube dropped. Try again.",
        TaskCanceledException { InnerException: TimeoutException } =>
            "YouTube took too long to answer. Try again.",
        RequestLimitExceededException =>
            "YouTube is limiting requests from this network. Try again later.",
        _ => ex.Message,
    };
}
