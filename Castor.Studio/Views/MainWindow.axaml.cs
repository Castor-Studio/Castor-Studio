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
    }
}
