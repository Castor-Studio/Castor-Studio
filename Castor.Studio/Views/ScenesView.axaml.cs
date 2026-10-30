using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CastorApplication.ViewModels.Scenes;

namespace CastorApplication.Views;

public partial class ScenesView : UserControl
{
    // The source being dragged. The drag clipboard only carries text,
    // and the drop needs the ViewModel itself, not an id to resolve again.
    private SourceItemViewModel? _draggedSource;

    public ScenesView()
    {
        InitializeComponent();
    }

    // The canvas toolbar: its commands go through the engine like the source's
    // menu, then the preview receives at once what remains.
    private void Compose(Action<SceneCompositionViewModel> command)
    {
        if (DataContext is not ScenesViewModel viewModel) return;

        command(viewModel.Composition);
        ScenePreview.ShowComposition();
    }

    private void OnCenterSourceClick(object? sender, RoutedEventArgs e) => Compose(c => c.CenterSelected());

    private void OnFitSourceClick(object? sender, RoutedEventArgs e) => Compose(c => c.FitSelected());

    private void OnCropSourceClick(object? sender, RoutedEventArgs e) => Compose(c => c.ToggleCropMode());

    private void OnResetSourceClick(object? sender, RoutedEventArgs e) => Compose(c => c.ResetSelected());

    private void OnRotateRightClick(object? sender, RoutedEventArgs e) => Compose(c => c.RotateSelected(90));

    private void OnRotateLeftClick(object? sender, RoutedEventArgs e) => Compose(c => c.RotateSelected(-90));

    // The slider also follows the selected source: only a gap from what it already shows is
    // an operator request.
    private void OnZoomSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (DataContext is not ScenesViewModel viewModel) return;
        if (Math.Abs(e.NewValue - viewModel.Composition.SelectedZoomFactor) < 0.005) return;

        Compose(c => c.ZoomSelected(e.NewValue));
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

    // Only the handle starts the drag: the rest of the row keeps its buttons
    // clickable (loop, remove, up/down).
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
