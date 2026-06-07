namespace CastorApplication.Services;

public interface IThemeService
{
    bool IsLightTheme { get; }
    void ApplyTheme(int selectedThemeIndex);
}
