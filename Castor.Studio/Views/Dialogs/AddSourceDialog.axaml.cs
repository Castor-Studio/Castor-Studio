using Avalonia.Controls;
using Avalonia.Input;
using CastorApplication.ViewModels;
using CastorApplication.ViewModels.Scenes;
using System;

namespace CastorApplication.Views.Dialogs;

public partial class AddSourceDialog : Window
{
    public AddSourceDialog()
    {
        InitializeComponent();
    }

    public AddSourceDialog(AddSourceDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Subscribe to the ViewModel's close event
        if (DataContext is AddSourceDialogViewModel vm)
        {
            vm.CloseRequested += OnCloseRequested;
            _ = vm.RefreshCommand.ExecuteAsync(null);
        }
    }

    // Adapted to the ViewModel's Action<AddSourceResult?> delegate
    private void OnCloseRequested(AddSourceResult? result)
    {
        Close(result);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);   // ← en premier : laisse Avalonia nettoyer l'arbre encore complet

        PointerPressed -= OnHeaderPointerPressed;
        if (DataContext is AddSourceDialogViewModel vm)
        {
            vm.CloseRequested -= OnCloseRequested;   // ← requires a named-method subscription in the constructor
            vm.ScanNetworkCommand.Cancel();
        }

        DataContext = null;
        Content = null;
    }
}
