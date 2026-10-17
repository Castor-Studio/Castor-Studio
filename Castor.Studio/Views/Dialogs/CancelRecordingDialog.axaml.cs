using System;
using Avalonia.Controls;
using Avalonia.Input;
using CastorApplication.ViewModels.Studio;

namespace CastorApplication.Views.Dialogs;

public partial class CancelRecordingDialog : Window
{
    public CancelRecordingDialog()
    {
        InitializeComponent();
    }

    public CancelRecordingDialog(CancelRecordingDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e) => BeginMoveDrag(e);

    private void OnCloseRequested(bool confirmed) => Close(confirmed);

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is CancelRecordingDialogViewModel viewModel)
            viewModel.CloseRequested -= OnCloseRequested;
    }
}
