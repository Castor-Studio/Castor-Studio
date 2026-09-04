using System.Windows.Input;

namespace CastorApplication.ViewModels.Shell;

// One entry of the navigation bar's "Panneaux" menu. The command travels with the entry, because
// a menu built from a list gives its items no other way to reach one.
public sealed class StudioPanelEntry(string title, ICommand show)
{
    public string Title { get; } = title;

    public ICommand Show { get; } = show;
}
