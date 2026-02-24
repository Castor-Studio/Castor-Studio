using CastorApplication.Factories; // Assure-toi que c'est le bon namespace
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Serializer;
using System.ComponentModel;
using System.IO;

namespace CastorApplication.ViewModels;

public partial class StudioViewModel : ViewModelBase
{
    [ObservableProperty]
    private IRootDock? _layout;

    public StudioViewModel()
    {
        // 1. On crée la factory en lui passant 'this'
        var factory = new StudioDockFactory(this);

        // 2. On essaie de charger le layout existant
        if (File.Exists("layout.json"))
        {
            LoadLayout(factory);
        }

        // 3. Si le chargement a échoué ou que le fichier n'existait pas
        if (Layout == null)
        {
            Layout = factory.CreateLayout();
            if (Layout != null)
            {
                factory.InitLayout(Layout);

                // TEST : On force une sauvegarde immédiate
                SaveLayout();
            }
        }

        if (Layout is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged += (s, e) =>
            {
                // On sauvegarde dès qu'un truc bouge
                SaveLayout();
            };
        }
    }

    public void SaveLayout()
    {
        if (Layout != null)
        {
            // On n'utilise plus le constructeur avec typeof(IRootDock)
            var serializer = new DockSerializer();
            var json = serializer.Serialize(Layout);
            File.WriteAllText("layout.json", json);
        }
    }

    public void LoadLayout(IFactory factory)
    {
        if (File.Exists("layout.json"))
        {
            var json = File.ReadAllText("layout.json");
            var serializer = new DockSerializer();
            // On précise le type ici au moment de la désérialisation
            var layout = serializer.Deserialize<IRootDock>(json);

            if (layout != null)
            {
                Layout = layout;
                factory.InitLayout(Layout);
            }
        }
    }
}