using Avalonia.Controls.ApplicationLifetimes;
using CastorApplication.ViewModels.Shell;
using CastorApplication.Views.Dialogs;

namespace CastorApplication.Services.Dialogs;

internal interface IUnsavedScenesDialogService
{
    Task<UnsavedScenesChoice> AskAsync();
}

internal sealed class UnsavedScenesDialogService(IClassicDesktopStyleApplicationLifetime desktop) : IUnsavedScenesDialogService
{
    // Closed any other way than with one of its buttons, the dialog answers Cancel.
    public async Task<UnsavedScenesChoice> AskAsync()
    {
        var owner = desktop.MainWindow ?? throw new InvalidOperationException("La fenêtre principale n'est pas disponible.");
        var choice = await new UnsavedScenesDialog(new UnsavedScenesDialogViewModel()).ShowDialog<UnsavedScenesChoice?>(owner);
        return choice ?? UnsavedScenesChoice.Cancel;
    }
}
