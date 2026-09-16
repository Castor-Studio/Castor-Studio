using Avalonia.Controls;
using Avalonia.Input;
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
