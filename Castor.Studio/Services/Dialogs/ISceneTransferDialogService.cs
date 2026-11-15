using Avalonia.Controls.ApplicationLifetimes;
using CastorApplication.ViewModels.Scenes;
using CastorApplication.Views.Dialogs;

namespace CastorApplication.Services.Dialogs;

internal interface ISceneTransferDialogService
{
    // True once the operator confirmed; the view model then holds the checked scenes.
    Task<bool> ShowAsync(SceneTransferDialogViewModel viewModel);
}

internal sealed class SceneTransferDialogService(IClassicDesktopStyleApplicationLifetime desktop) : ISceneTransferDialogService
{
    public async Task<bool> ShowAsync(SceneTransferDialogViewModel viewModel)
    {
        var owner = desktop.MainWindow ?? throw new InvalidOperationException("La fenêtre principale n'est pas disponible.");
        return await new SceneTransferDialog(viewModel).ShowDialog<bool>(owner);
    }
}
