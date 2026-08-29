using Avalonia.Controls;
using Avalonia.Interactivity;
using CastorApplication.ViewModels;
using CastorApplication.Views.Dialogs;

namespace CastorApplication.Views;

public partial class ScenesView : UserControl
{
    public ScenesView()
    {
        InitializeComponent();
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
}