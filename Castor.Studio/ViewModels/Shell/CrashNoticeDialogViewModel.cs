using CastorApplication.Services.Diagnostics;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels.Shell;

// Shown once at startup when the previous run did not close normally: the tester learns where
// the report is, so it can come with the bug report.
public sealed partial class CrashNoticeDialogViewModel : ViewModelBase
{
    private readonly Action<string> _showInExplorer;

    public CrashNoticeDialogViewModel(UncleanExit exit, string logsDirectory, Action<string>? showInExplorer = null)
    {
        Exit = exit;
        LogsDirectory = logsDirectory;
        _showInExplorer = showInExplorer ?? AppInfo.ShowInExplorer;
    }

    public UncleanExit Exit { get; }
    public string LogsDirectory { get; }

    public string Title => "Fermeture inattendue";

    public string Message => Exit.ReportPath != null
        ? $"Castor Studio s'est arrêté à cause d'une erreur lors de la session du {StartedAtText}. "
            + "Un rapport a été enregistré : joignez-le à votre retour pour nous aider à corriger le problème."
        : $"Castor Studio ne s'est pas fermé normalement lors de la session du {StartedAtText} "
            + "(plantage ou arrêt forcé). Le journal de cette session peut nous aider à comprendre ce qui s'est passé.";

    // The file to attach: the report when there is one, otherwise the folder of the day's logs.
    public string ShownPath => Exit.ReportPath ?? LogsDirectory;

    private string StartedAtText => Exit.StartedAt.ToLocalTime().ToString("dd/MM/yyyy à HH:mm");

    public event Action? CloseRequested;

    [RelayCommand]
    private void ShowFile()
    {
        _showInExplorer(ShownPath);
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Dismiss() => CloseRequested?.Invoke();
}
