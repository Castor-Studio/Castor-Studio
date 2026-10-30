using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CastorApplication.ViewModels.Shell;

namespace CastorApplication.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var screen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
        var screenSize = screen != null
            ? new Size(screen.WorkingArea.Width / screen.Scaling, screen.WorkingArea.Height / screen.Scaling)
            : new Size(1280, 800);

        (DataContext as MainViewModel)?.ApplyScreenSize(screenSize);

        // Panels detached in the previous session reopen after the first layout pass, once the
        // dock control is attached and can be resolved as their owner window.
        Dispatcher.UIThread.Post(
            () => (DataContext as MainViewModel)?.PresentFloatingPanels(),
            DispatcherPriority.Background);

        if (DataContext is MainViewModel viewModel) _ = viewModel.LoadSavedScenesAsync();
    }

    private bool _closeConfirmed;
    private bool _isConfirmingClose;

    // The close button, Alt+F4 and the Quitter menu all land here. Asking is asynchronous, so
    // the first close is always cancelled and the window closes itself again once allowed.
    // A Windows shutdown is not held back: there is no waiting on a dialog then.
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || e.Cancel || e.CloseReason == WindowCloseReason.OSShutdown
            || DataContext is not MainViewModel viewModel)
            return;

        e.Cancel = true;
        if (_isConfirmingClose) return;

        _isConfirmingClose = true;
        try
        {
            if (!await viewModel.ConfirmCloseAsync()) return;
        }
        finally
        {
            _isConfirmingClose = false;
        }

        _closeConfirmed = true;
        Close();
    }
}
