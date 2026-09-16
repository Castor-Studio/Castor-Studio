using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CastorApplication.ViewModels.Scenes;

namespace CastorApplication.Views;

public partial class ScenesView : UserControl
{
    public ScenesView()
    {
        InitializeComponent();
    }

    private void OnSourceNameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: SourceItemViewModel source }) return;
        if (DataContext is not ScenesViewModel viewModel) return;

        viewModel.BeginRenameSourceCommand.Execute(source);
        e.Handled = true;
    }

    // The field appears when the rename starts: take the focus and select the name so typing
    // replaces it. Posted, because the box can only be focused once it is laid out.
    private void OnSourceRenameBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != IsVisibleProperty || e.NewValue is not true || sender is not TextBox box) return;

        Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            box.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    private void OnSourceRenameBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { IsVisible: true, DataContext: SourceItemViewModel { IsRenaming: true } }) return;
        if (DataContext is ScenesViewModel viewModel)
            viewModel.ConfirmRenameSourceCommand.Execute(null);
    }
}
