using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CastorApplication.Controls;
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
        // A source's menu on the picture also carries the list actions: a source has one menu,
        // wherever it is clicked.
        ScenePreview.SourceActions = SourceListActions;
    }

    // Right-click on a row: the source is selected on the preview and its menu opens, the same
    // as on the picture, composition commands included when the engine composes it.
    private void OnSourceContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { DataContext: SourceItemViewModel source } row) return;
        if (DataContext is not ScenesViewModel viewModel || source.IsRenaming) return;

        var composition = viewModel.Composition;
        composition.Select(source.Id);
        SourceMenu.Create(composition, source, SourceListActions(source.Id)).Open(row);
        e.Handled = true;
    }

    // What can be done from the list: rename, a file's loop, remove. Remove comes last and in
    // red, like "Supprimer" in a scene's menu.
    private IEnumerable<Control> SourceListActions(Guid sourceId)
    {
        if (DataContext is not ScenesViewModel viewModel) yield break;
        if (viewModel.SelectedScene?.Sources.FirstOrDefault(item => item.Id == sourceId) is not { } source) yield break;

        yield return SourceMenu.Item("Renommer", CompositionIcons.Rename,
            () => viewModel.BeginRenameSourceCommand.Execute(source));
        if (source.IsFileSource)
        {
            yield return SourceMenu.Item("Lire en boucle", CompositionIcons.Loop,
                () => viewModel.ToggleMediaLoopCommand.Execute(source), isChecked: source.Loop);
        }

        yield return new Separator();
        yield return SourceMenu.Item("Retirer de la scène", CompositionIcons.Remove,
            () => viewModel.RemoveSourceCommand.Execute(source), isDanger: true);
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
