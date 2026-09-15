using Avalonia.Controls;
using Avalonia.Threading;
using CastorApplication.ViewModels.Scenes;

namespace CastorApplication.Controls;

public partial class SceneCompositionCanvas : UserControl
{
    // Rien ne prévient l'interface qu'une transformation a bougé du côté du moteur : le
    // canvas le relit à cadence fixe. Assez souvent pour que le décalage ne se voie pas,
    // assez rarement pour que la lecture ne pèse pas sur le rendu.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(250);

    private readonly DispatcherTimer _refreshTimer;

    public SceneCompositionCanvas()
    {
        InitializeComponent();

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefreshInterval };
        _refreshTimer.Tick += (_, _) => Refresh();

        AttachedToVisualTree += (_, _) =>
        {
            Refresh();
            _refreshTimer.Start();
        };
        DetachedFromVisualTree += (_, _) => _refreshTimer.Stop();
    }

    private void Refresh()
    {
        // Le canvas cède sa place à l'aperçu direct sans quitter l'arbre visuel : tant qu'il
        // n'est pas montré, il n'y a rien à relire.
        if (!IsEffectivelyVisible) return;

        (DataContext as SceneCompositionViewModel)?.Refresh();
    }
}
