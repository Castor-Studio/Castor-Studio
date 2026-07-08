using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Castor.Engine.Models;
using CastorApplication.ViewModels;

namespace CastorApplication.Views;

public partial class ScenesView : UserControl
{
    private const string SceneDragFormat = "application/x-castor-scene-id";

    public ScenesView()
    {
        InitializeComponent();
    }

    /// <summary>Entrée dans le champ « Nouvelle scène » : crée la scène et
    /// ferme la popup (équivalent du bouton CRÉER LA SCÈNE).</summary>
    private void OnCreateSceneKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ScenesViewModel vm) return;
        vm.CreateSceneCommand.Execute(null);
        CloseParentFlyout(sender);
        e.Handled = true;
    }

    /// <summary>Entrée dans le champ de renommage : applique le nouveau nom
    /// et ferme la popup (équivalent du bouton RENOMMER).</summary>
    private void OnRenameSceneKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ScenesViewModel vm) return;
        vm.ConfirmRenameSceneCommand.Execute(null);
        CloseParentFlyout(sender);
        e.Handled = true;
    }

    /// <summary>Ferme le flyout contenant le bouton cliqué — les boutons de
    /// validation des popups laissaient le flyout ouvert après action.</summary>
    private void OnCloseFlyoutClick(object? sender, RoutedEventArgs e) => CloseParentFlyout(sender);

    private static void CloseParentFlyout(object? sender)
    {
        // Le contenu d'un flyout vit dans un PopupRoot séparé : le Popup hôte
        // n'est pas un ancêtre VISUEL, il faut remonter l'arbre LOGIQUE.
        if (sender is not Control control) return;
        control.FindLogicalAncestorOfType<Popup>()?.Close();
    }

    /// <summary>Ouvre le dialogue modal « Ajouter une source » et applique
    /// le résultat à la scène sélectionnée.</summary>
    private async void OnAddSourceClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ScenesViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var dialog = new AddSourceDialog(vm.CreateAddSourceDialog());
        var result = await dialog.ShowDialog<AddSourceResult?>(owner);

        if (result != null)
            await vm.ApplyAddSourceResultAsync(result);
    }

    private async void OnSceneDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: SceneItem scene } control) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;

        var data = new DataObject();
        data.Set(SceneDragFormat, scene.Id);

        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
    }

    private void OnSceneRowDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(SceneDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
    }

    private void OnSceneRowDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control { DataContext: SceneItem targetScene } || DataContext is not ScenesViewModel vm) return;
        if (e.Data.Get(SceneDragFormat) is not Guid draggedId) return;

        var scenes = vm.Scenes;
        var sourceIndex = -1;
        for (var i = 0; i < scenes.Count; i++)
        {
            if (scenes[i].Id == draggedId)
            {
                sourceIndex = i;
                break;
            }
        }

        var targetIndex = scenes.IndexOf(targetScene);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex) return;

        scenes.Move(sourceIndex, targetIndex);
    }
}
