using Avalonia.Controls.ApplicationLifetimes;
using CastorApplication.ViewModels.Scenes;
using CastorApplication.Views.Dialogs;

namespace CastorApplication.Services.Dialogs;

internal interface IAddSourceDialogService
{
    Task<AddSourceResult?> ShowAsync(AddSourceDialogViewModel viewModel);
}

internal sealed class AddSourceDialogService(IClassicDesktopStyleApplicationLifetime desktop) : IAddSourceDialogService
{
    public async Task<AddSourceResult?> ShowAsync(AddSourceDialogViewModel viewModel)
    {
        var owner = desktop.MainWindow ?? throw new InvalidOperationException("La fenêtre principale n'est pas disponible.");
        return await new AddSourceDialog(viewModel).ShowDialog<AddSourceResult?>(owner);
    }
}
