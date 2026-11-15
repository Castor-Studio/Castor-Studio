using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CastorApplication.Models.Studio;

namespace CastorApplication.Converters;

// A StreamingProviders id to the platform logo shown on Settings › Comptes, so a platform looks
// the same wherever Castor lists it.
public sealed class PlatformLogoConverter : IValueConverter
{
    public static readonly PlatformLogoConverter Instance = new();

    private static readonly Dictionary<string, Bitmap> Logos = [];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string providerId || !StreamingProviders.All.Contains(providerId)) return null;

        if (!Logos.TryGetValue(providerId, out var logo))
        {
            Logos[providerId] = logo = new Bitmap(AssetLoader.Open(
                new Uri($"avares://Castor-Studio/Assets/Platforms/{providerId}-logo.png")));
        }
        return logo;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
