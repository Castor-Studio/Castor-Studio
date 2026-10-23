using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels.Studio;

public sealed partial class CancelRecordingDialogViewModel(IReadOnlyList<string> outputPaths) : ViewModelBase
{
    public string Title => OutputPaths.Count == 1 ? "Annuler l'enregistrement" : "Annuler les enregistrements";

    public string Message => OutputPaths.Count == 1
        ? "L'enregistrement va s'arrêter et son fichier sera supprimé définitivement."
        : $"Les {OutputPaths.Count} enregistrements vont s'arrêter et leurs fichiers seront supprimés définitivement.";

    public string ConfirmLabel => OutputPaths.Count == 1 ? "Supprimer l'enregistrement" : "Supprimer les enregistrements";
    public string DismissLabel => OutputPaths.Count == 1 ? "Continuer l'enregistrement" : "Continuer les enregistrements";

    public IReadOnlyList<string> OutputPaths { get; } = outputPaths;

    public event Action<bool>? CloseRequested;

    [RelayCommand]
    private void Dismiss() => CloseRequested?.Invoke(false);

    [RelayCommand]
    private void Confirm() => CloseRequested?.Invoke(true);
}
