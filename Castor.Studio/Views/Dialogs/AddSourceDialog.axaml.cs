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

        // S'abonner à l'événement de fermeture du ViewModel
        if (DataContext is AddSourceDialogViewModel vm)
        {
            vm.CloseRequested += OnCloseRequested;
            _ = vm.RefreshCommand.ExecuteAsync(null);
        }
    }

    // Adapté au délégué Action<AddSourceResult?> du ViewModel
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
            vm.CloseRequested -= OnCloseRequested;   // ← nécessite un abonnement par méthode nommée dans le constructeur
        }

        DataContext = null;
        Content = null;
    }
}
