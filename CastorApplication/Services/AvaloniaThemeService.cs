using Avalonia;
using Avalonia.Styling;

namespace CastorApplication.Services;

public sealed class AvaloniaThemeService : IThemeService
{
    public bool IsLightTheme => Application.Current?.RequestedThemeVariant == ThemeVariant.Light;

    public void ApplyTheme(int selectedThemeIndex)
    {
        if (Application.Current is null) return;

        Application.Current.RequestedThemeVariant =
            selectedThemeIndex == 1 ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
