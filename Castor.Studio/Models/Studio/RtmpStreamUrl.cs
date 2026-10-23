namespace CastorApplication.Models.Studio;

// Address of an RTMP camera stream. Its path usually ends with the stream key, so only the
// scheme and the server are ever shown or written to a message.
public static class RtmpStreamUrl
{
    public const int DefaultPort = 1935;

    // A server alone is not enough: libobs would keep reconnecting to a stream that does not exist.
    public static bool IsValid(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && uri.Scheme is "rtmp" or "rtmps"
        && uri.Host.Length > 0
        && uri.AbsolutePath.Trim('/').Length > 0;

    public static string Describe(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.Host.Length > 0
            ? $"{uri.Scheme}://{uri.Authority}"
            : "";
}
