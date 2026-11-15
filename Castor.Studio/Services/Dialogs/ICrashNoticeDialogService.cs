using Avalonia.Controls.ApplicationLifetimes;
using CastorApplication.Services.Diagnostics;
using CastorApplication.ViewModels.Shell;
using CastorApplication.Views.Dialogs;

namespace CastorApplication.Services.Dialogs;

internal interface ICrashNoticeDialogService
{
    Task ShowAsync(UncleanExit exit);
}

internal sealed class CrashNoticeDialogService(IClassicDesktopStyleApplicationLifetime desktop) : ICrashNoticeDialogService
{
    public async Task ShowAsync(UncleanExit exit)
    {
        var owner = desktop.MainWindow ?? throw new InvalidOperationException("La fenêtre principale n'est pas disponible.");
        await new CrashNoticeDialog(new CrashNoticeDialogViewModel(exit, AppLog.Directory)).ShowDialog(owner);
    }
}
