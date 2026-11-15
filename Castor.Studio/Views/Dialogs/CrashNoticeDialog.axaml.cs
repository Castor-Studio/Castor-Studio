using Avalonia.Controls;
using Avalonia.Input;
using CastorApplication.ViewModels.Shell;

namespace CastorApplication.Views.Dialogs;

public partial class CrashNoticeDialog : Window
{
    public CrashNoticeDialog()
    {
        InitializeComponent();
    }

    public CrashNoticeDialog(CrashNoticeDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += Close;
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e) => BeginMoveDrag(e);

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is CrashNoticeDialogViewModel viewModel)
            viewModel.CloseRequested -= Close;
    }
}
