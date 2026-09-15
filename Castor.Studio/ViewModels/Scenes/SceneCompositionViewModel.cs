using CastorApplication.Models.Studio;
using CastorApplication.Services.Studio;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Scenes;

/// <summary>
/// La composition d'une scène telle que le moteur la détient, relue en continu : quelles
/// sources sont composées, et quel rectangle chacune occupe. Aucune de ces valeurs n'est
/// décidée ni ajustée ici — une valeur retenue localement finirait par décrire autre chose
/// que ce qui est réellement rendu.
/// </summary>
/// <remarks>
/// Ce qui en est fait — les cadres peints sur l'aperçu — est tracé par le moteur lui-même :
/// sa surface est une fenêtre native, rien de ce que dessine Avalonia ne peut s'y poser.
/// </remarks>
public partial class SceneCompositionViewModel : ViewModelBase
{
    private readonly ISourceRuntime _sourceRuntime;
    private SceneItemViewModel? _scene;

    /// <summary>
    /// Les sources que le moteur compose, dans l'ordre du dessin : de l'arrière-plan vers le
    /// premier plan. C'est son empilement, parcouru dans le sens où il peint.
    /// </summary>
    public IReadOnlyList<SourceTransform> Sources { get; private set; } = [];

    [ObservableProperty] private string _status = "";

    internal SceneCompositionViewModel(ISourceRuntime sourceRuntime) => _sourceRuntime = sourceRuntime;

    /// <summary>Suit désormais cette scène, à partir de zéro.</summary>
    internal void ShowScene(SceneItemViewModel? scene)
    {
        _scene = scene;
        Sources = [];
        Refresh();
    }

    /// <summary>
    /// Relit la composition dans le moteur. Appelée après chaque geste et à intervalle
    /// régulier par l'aperçu : c'est ce qui fait suivre les cadres quand une transformation
    /// change du côté du moteur, sans que l'interface en soit prévenue.
    /// </summary>
    public void Refresh()
    {
        var scene = _scene;
        if (scene == null)
        {
            Sources = [];
            Status = "";
            return;
        }

        var result = _sourceRuntime.GetSceneComposition(scene.Id);
        if (result.Status == StudioRuntimeStatus.Unavailable)
        {
            // Un moteur globalement indisponible est déjà annoncé par l'aperçu : ici, il n'y
            // a simplement rien de composé.
            Sources = [];
            Status = "";
            return;
        }

        if (!result.IsSuccess)
        {
            // Lecture refusée : les derniers cadres restent en place plutôt que de
            // disparaître sur un incident passager, mais le refus se dit.
            Status = result.Message;
            return;
        }

        Status = "";
        Sources = Drawable(result.Composition.Sources);
    }

    // Le moteur rend sa pile du premier plan vers l'arrière-plan ; elle est parcourue à
    // l'envers parce qu'on peint de l'arrière vers l'avant. Une nouvelle liste à chaque
    // lecture : celle-ci est relue image par image par le thread graphique du moteur, elle
    // ne doit plus changer une fois donnée.
    private static IReadOnlyList<SourceTransform> Drawable(IReadOnlyList<SourceTransform> transforms)
    {
        var drawable = new List<SourceTransform>(transforms.Count);
        for (var index = transforms.Count - 1; index >= 0; index--)
        {
            var transform = transforms[index];
            // Une source que le moteur ne compose pas — masquée, ou sans image comme une
            // source audio — n'a aucun cadre à montrer.
            if (!transform.IsVisible || transform.Width <= 0 || transform.Height <= 0) continue;

            drawable.Add(transform);
        }

        return drawable;
    }
}
