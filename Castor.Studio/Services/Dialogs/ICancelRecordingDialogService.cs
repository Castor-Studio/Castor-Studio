using Avalonia.Controls.ApplicationLifetimes;
using CastorApplication.ViewModels.Studio;
using CastorApplication.Views.Dialogs;

namespace CastorApplication.Services.Dialogs;

internal interface ICancelRecordingDialogService
{
    // True once the operator confirmed that the recording and its file can be thrown away.
    Task<bool> ConfirmAsync(string outputPath);
}

internal sealed class CancelRecordingDialogService(IClassicDesktopStyleApplicationLifetime desktop) : ICancelRecordingDialogService
{
    public async Task<bool> ConfirmAsync(string outputPath)
    {
        var owner = desktop.MainWindow ?? throw new InvalidOperationException("La fenêtre principale n'est pas disponible.");
        return await new CancelRecordingDialog(new CancelRecordingDialogViewModel(outputPath)).ShowDialog<bool>(owner);
    }
}
