using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels.Shell;

public enum UnsavedScenesChoice
{
    Save,
    DontSave,
    Cancel
}

// Asked when the app is closed with scene changes not saved yet. Anything but an explicit
// choice (Escape, the close button) keeps the app open.
public sealed partial class UnsavedScenesDialogViewModel : ViewModelBase
{
    public string Title => "Scènes non enregistrées";
    public string Message => "Les scènes ont été modifiées. Voulez-vous les enregistrer avant de quitter ?";

    public event Action<UnsavedScenesChoice>? CloseRequested;

    [RelayCommand]
    private void Save() => CloseRequested?.Invoke(UnsavedScenesChoice.Save);

    [RelayCommand]
    private void DontSave() => CloseRequested?.Invoke(UnsavedScenesChoice.DontSave);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(UnsavedScenesChoice.Cancel);
}
