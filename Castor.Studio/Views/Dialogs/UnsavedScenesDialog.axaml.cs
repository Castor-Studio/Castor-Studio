using System;
using Avalonia.Controls;
using Avalonia.Input;
using CastorApplication.ViewModels.Shell;

namespace CastorApplication.Views.Dialogs;

public partial class UnsavedScenesDialog : Window
{
    public UnsavedScenesDialog()
    {
        InitializeComponent();
    }

    public UnsavedScenesDialog(UnsavedScenesDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e) => BeginMoveDrag(e);

    private void OnCloseRequested(UnsavedScenesChoice choice) => Close(choice);

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is UnsavedScenesDialogViewModel viewModel)
            viewModel.CloseRequested -= OnCloseRequested;
    }
}
