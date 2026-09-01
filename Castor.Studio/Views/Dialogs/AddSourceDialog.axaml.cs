using Avalonia.Controls;
using Avalonia.Input;
using CastorApplication.ViewModels.Scenes;

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
        viewModel.CloseRequested += result => Close(result);
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is AddSourceDialogViewModel vm)
            _ = vm.RefreshCommand.ExecuteAsync(null);

        SearchBox.Focus();
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}
