using System.Collections.Concurrent;

namespace CastorApplication.Services.Studio;

/// <summary>
/// Une pastille rendue par l'interface, prête pour le moteur : pixels BGRA non prémultipliés,
/// à la taille de l'écran (pixels physiques).
/// </summary>
internal sealed record PreviewBadgeImage(int Width, int Height, byte[] Pixels);

/// <summary>
/// Les pastilles que le moteur pose sur l'aperçu. Il ne sait pas écrire : c'est l'interface
/// qui les rend, dans sa propre typographie, et les dépose ici. Le thread graphique demande
/// celle qui lui manque, l'interface la rend à son tour, et elle apparaît à l'image suivante.
/// </summary>
internal static class PreviewBadges
{
    private static readonly ConcurrentDictionary<string, PreviewBadgeImage> Images = new();
    private static readonly ConcurrentDictionary<string, byte> Requested = new();

    /// <summary>
    /// Rend une pastille, appelé depuis le thread graphique : il doit seulement poster le
    /// rendu vers l'interface, jamais l'attendre.
    /// </summary>
    public static Action<string>? Renderer { get; set; }

    /// <summary>La pastille de ce texte, ou rien si elle n'est pas encore rendue (elle est alors demandée).</summary>
    public static PreviewBadgeImage? Get(string label)
    {
        if (Images.TryGetValue(label, out var image)) return image;
        if (Renderer is { } renderer && Requested.TryAdd(label, 0)) renderer(label);
        return null;
    }

    public static void Put(string label, PreviewBadgeImage image) => Images[label] = image;

    /// <summary>
    /// Oublie les pastilles rendues, pour qu'elles soient refaites — à une autre mise à
    /// l'échelle de l'écran, par exemple.
    /// </summary>
    public static void Clear()
    {
        Images.Clear();
        Requested.Clear();
    }
}
