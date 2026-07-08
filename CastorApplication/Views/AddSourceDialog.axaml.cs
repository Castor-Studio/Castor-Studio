using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CastorApplication.ViewModels;

namespace CastorApplication.Views;

public partial class AddSourceDialog : Window
{
    public AddSourceDialog()
    {
        InitializeComponent();
    }

    public AddSourceDialog(AddSourceDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        // La fermeture (validation ou annulation) porte le résultat.
        viewModel.CloseRequested += result => Close(result);
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        // Première énumération asynchrone dès l'ouverture, et focus sur la
        // recherche pour pouvoir taper directement le nom cherché.
        if (DataContext is AddSourceDialogViewModel vm)
            _ = vm.RefreshCommand.ExecuteAsync(null);
        SearchBox.Focus();
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is AddSourceDialogViewModel vm && vm.SelectedItem != null)
            vm.ConfirmItem(vm.SelectedItem);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is AddSourceDialogViewModel vm)
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && vm.ConfirmCommand.CanExecute(null))
            {
                vm.ConfirmCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }
        base.OnKeyDown(e);
    }
}
