using System;
using Avalonia.Controls;
using Avalonia.Input;
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
