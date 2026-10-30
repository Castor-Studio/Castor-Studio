using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CastorApplication.Services.Studio;

namespace CastorApplication.Controls;

/// <summary>
/// Rend les pastilles que le moteur pose sur l'aperçu, dans le style de celle qui nomme la
/// scène : même fond voilé, même bordure, même texte. La zone d'aperçu est noire en clair
/// comme en sombre, ses couleurs sont donc celles du thème sombre (Styles/Colors.axaml).
/// </summary>
internal static class PreviewBadgeRenderer
{
    private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#AA0B0B12"));
    private static readonly IBrush Border = new SolidColorBrush(Color.Parse("#2a2a38"));   // AppBorder
    private static readonly IBrush Foreground = new SolidColorBrush(Color.Parse("#c4c4d4")); // VideoFgBrush
    private static readonly FontFamily Inter = new("fonts:Inter#Inter");

    private static double _scaling = 1;

    /// <summary>
    /// Branche le rendu des pastilles à cette mise à l'échelle de l'écran. Une autre échelle
    /// que la précédente fait refaire celles déjà rendues.
    /// </summary>
    public static void Attach(double scaling)
    {
        if (scaling <= 0) scaling = 1;
        if (PreviewBadges.Renderer != null && scaling == _scaling) return;

        _scaling = scaling;
        PreviewBadges.Clear();
        PreviewBadges.Renderer = label => Dispatcher.UIThread.Post(() => PreviewBadges.Put(label, Render(label, _scaling)));
    }

    private static PreviewBadgeImage Render(string label, double scaling)
    {
        var badge = new Border
        {
            Padding = new Thickness(8, 3),
            CornerRadius = new CornerRadius(5),
            Background = Background,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = label,
                FontFamily = Inter,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = Foreground
            }
        };
        badge.Measure(Size.Infinity);
        badge.Arrange(new Rect(badge.DesiredSize));

        var size = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(badge.DesiredSize.Width * scaling)),
            Math.Max(1, (int)Math.Ceiling(badge.DesiredSize.Height * scaling)));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling));
        bitmap.Render(badge);

        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        Unpremultiply(pixels);
        return new PreviewBadgeImage(size.Width, size.Height, pixels);
    }

    // Avalonia rend en alpha prémultiplié ; l'effet par défaut de libobs mélange en alpha
    // droit. Sans ce retour, les bords adoucis du texte et des coins s'assombriraient.
    private static void Unpremultiply(byte[] pixels)
    {
        for (var index = 0; index < pixels.Length; index += 4)
        {
            var alpha = pixels[index + 3];
            if (alpha is 0 or 255) continue;

            pixels[index] = (byte)Math.Min(255, pixels[index] * 255 / alpha);
            pixels[index + 1] = (byte)Math.Min(255, pixels[index + 1] * 255 / alpha);
            pixels[index + 2] = (byte)Math.Min(255, pixels[index + 2] * 255 / alpha);
        }
    }
}
