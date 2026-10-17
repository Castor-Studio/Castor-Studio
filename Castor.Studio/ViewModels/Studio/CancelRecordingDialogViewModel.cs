using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels.Studio;

public sealed partial class CancelRecordingDialogViewModel(string outputPath) : ViewModelBase
{
    public string Title => "Annuler l'enregistrement";
    public string Message => "L'enregistrement va s'arrêter et son fichier sera supprimé définitivement.";
    public string OutputPath { get; } = outputPath;

    public event Action<bool>? CloseRequested;

    [RelayCommand]
    private void Dismiss() => CloseRequested?.Invoke(false);

    [RelayCommand]
    private void Confirm() => CloseRequested?.Invoke(true);
}
