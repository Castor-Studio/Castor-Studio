using Avalonia.Controls;
using CastorApplication.ViewModels;
using System.ComponentModel;

namespace CastorApplication.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // Cette méthode est appelée automatiquement quand on ferme la fenêtre
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // On récupère le ViewModel de la fenêtre
        if (DataContext is MainWindowViewModel mwvm)
        {
            // On demande au StudioViewModel de sauvegarder son layout
            // (Note: Assure-toi que ShowStudio a été appelé au moins une fois)
            mwvm.ShowStudioCommand.Execute(null); // Force l'accès au VM si besoin

            // Si tu as bien exposé ton champ _studioPage ou une propriété StudioPage
            // On appelle la sauvegarde :
            // mwvm.StudioPage?.SaveLayout(); 
            // Note : Adapte selon le nom de la propriété que tu as créée pour le Studio
        }
        base.OnClosing(e);
    }
}