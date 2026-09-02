using System;
using Avalonia;
using Avalonia.Controls;
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
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
    }
}
