using Avalonia.Controls;
using CastorApplication.ViewModels;

namespace CastorApplication.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel mwvm)
        {
            mwvm.StudioPage.SaveLayout();
        }
        base.OnClosing(e);
    }
}
