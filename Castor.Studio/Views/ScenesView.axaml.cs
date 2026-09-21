using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CastorApplication.ViewModels.Scenes;

namespace CastorApplication.Views;

public partial class ScenesView : UserControl
{
    // La source en cours de glissement. Le presse-papier de drag ne transporte que du texte,
    // et le drop a besoin du ViewModel lui-même, pas d'un identifiant à re-résoudre.
    private SourceItemViewModel? _draggedSource;

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

    // Seule la poignée déclenche le glissement : le reste de la ligne garde ses boutons
    // cliquables (boucle, retrait, montée/descente).
    private async void OnSourceHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: SourceItemViewModel source }) return;
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;

        _draggedSource = source;

        var payload = new DataTransfer();
        payload.Add(DataTransferItem.CreateText(source.Id.ToString()));

        try
        {
            await DragDrop.DoDragDropAsync(e, payload, DragDropEffects.Move);
        }
        finally
        {
            _draggedSource = null;
        }
    }

    private void OnSourceDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = _draggedSource != null ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnSourceDrop(object? sender, DragEventArgs e)
    {
        if (_draggedSource == null) return;
        if (sender is not Control { DataContext: SourceItemViewModel target }) return;
        if (DataContext is not ScenesViewModel viewModel) return;

        viewModel.MoveSourceHere(_draggedSource, target);
        e.Handled = true;
    }
}
