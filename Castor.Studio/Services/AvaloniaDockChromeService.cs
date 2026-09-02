using Avalonia;

namespace CastorApplication.Services;

// Mirrors AvaloniaThemeService: the one place that reaches into Application.Current
// so the settings ViewModel never touches Avalonia types. Both keys are read through
// DynamicResource (our own by DockTheme.axaml for tool pane titles, Dock's own by its
// DocumentControl theme for the document tab strip), so writing them re-renders live.
public sealed class AvaloniaDockChromeService : IDockChromeService
{
    public const string ShowTitlesKey = "CastorDockShowTitles";
    private const string DocumentTabStripKey = "DockDocumentControlTabStripVisible";

    public void ApplyShowTitles(bool showTitles)
    {
        if (Application.Current is null) return;

        Application.Current.Resources[ShowTitlesKey] = showTitles;
        Application.Current.Resources[DocumentTabStripKey] = showTitles;
    }
}
