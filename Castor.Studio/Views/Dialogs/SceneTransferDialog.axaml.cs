using System;
using Avalonia.Controls;
using Avalonia.Input;
using CastorApplication.ViewModels.Scenes;

namespace CastorApplication.Views.Dialogs;

public partial class SceneTransferDialog : Window
{
    public SceneTransferDialog()
    {
        InitializeComponent();
    }

    public SceneTransferDialog(SceneTransferDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e) => BeginMoveDrag(e);

    private void OnCloseRequested(bool confirmed) => Close(confirmed);

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is SceneTransferDialogViewModel viewModel)
            viewModel.CloseRequested -= OnCloseRequested;
    }
}
