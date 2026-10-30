using System.Collections.Concurrent;

namespace CastorApplication.Services.Studio;

/// <summary>
/// A badge rendered by the interface, ready for the engine: non-premultiplied BGRA pixels,
/// at screen size (physical pixels).
/// </summary>
internal sealed record PreviewBadgeImage(int Width, int Height, byte[] Pixels);

/// <summary>
/// The badges the engine lays on the preview. It cannot write: the interface
/// renders them, in its own typography, and drops them here. The graphics thread requests
/// the one it lacks, the interface renders it in turn, and it appears on the next frame.
/// </summary>
internal static class PreviewBadges
{
    private static readonly ConcurrentDictionary<string, PreviewBadgeImage> Images = new();
    private static readonly ConcurrentDictionary<string, byte> Requested = new();

    /// <summary>
    /// Renders a badge, called from the graphics thread: it must only post the
    /// rendering to the interface, never wait for it.
    /// </summary>
    public static Action<string>? Renderer { get; set; }

    /// <summary>This text's badge, or nothing if not rendered yet (it is then requested).</summary>
    public static PreviewBadgeImage? Get(string label)
    {
        if (Images.TryGetValue(label, out var image)) return image;
        if (Renderer is { } renderer && Requested.TryAdd(label, 0)) renderer(label);
        return null;
    }

    public static void Put(string label, PreviewBadgeImage image) => Images[label] = image;

    /// <summary>
    /// Forgets the rendered badges so they get redone — at another screen
    /// scale, for example.
    /// </summary>
    public static void Clear()
    {
        Images.Clear();
        Requested.Clear();
    }
}
