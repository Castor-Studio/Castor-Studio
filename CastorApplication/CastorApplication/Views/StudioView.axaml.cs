using Avalonia.Controls;
using CastorApplication.Factories;
using CastorApplication.ViewModels;

namespace CastorApplication.Views;

public partial class StudioView : UserControl
{
    public StudioView()
    {
        InitializeComponent();

        // 1. Créer le ViewModel
        var vm = new StudioViewModel();

        // 2. Utiliser la Factory pour générer le Layout
        var factory = new StudioDockFactory(vm);
        var layout = factory.CreateLayout();

        // 3. Initialiser le Layout (très important pour le drag & drop)
        factory.InitLayout(layout);

        // 4. Donner le layout au ViewModel et lier le DataContext
        vm.Layout = layout;
        this.DataContext = vm;
    }
}