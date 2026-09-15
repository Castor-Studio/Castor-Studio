using System.Collections.ObjectModel;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Studio;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Scenes;

/// <summary>
/// Canvas de composition d'une scène : ce que le moteur compose, relu chez lui et redessiné
/// tel quel. Aucune position, aucune taille n'est décidée ni retenue ici — une valeur
/// conservée localement finirait par ne plus décrire ce qui est réellement rendu.
/// </summary>
public partial class SceneCompositionViewModel : ViewModelBase
{
    private readonly ISourceRuntime _sourceRuntime;
    private SceneItemViewModel? _scene;
    private bool _canvasComesFromEngine;

    /// <summary>
    /// Les calques dans l'ordre où ils se peignent, de l'arrière-plan vers le premier plan.
    /// C'est l'empilement du moteur, simplement parcouru dans le sens du dessin.
    /// </summary>
    public ObservableCollection<CompositionLayerViewModel> Layers { get; } = [];

    [ObservableProperty] private double _canvasWidth = 1920;
    [ObservableProperty] private double _canvasHeight = 1080;
    [ObservableProperty] private bool _hasLayers;
    [ObservableProperty] private string _status = "";

    /// <summary>Ce qu'il y a à lire quand le canvas est vide, et pourquoi il l'est.</summary>
    [ObservableProperty] private string _placeholder = "";

    internal SceneCompositionViewModel(ISourceRuntime sourceRuntime) => _sourceRuntime = sourceRuntime;

    /// <summary>Dessine désormais cette scène, à partir de zéro.</summary>
    internal void ShowScene(SceneItemViewModel? scene)
    {
        _scene = scene;
        Clear();
        Refresh();
    }

    /// <summary>
    /// Taille de canvas à utiliser tant que le moteur n'en donne aucune — le temps qu'il
    /// démarre, ou s'il reste indisponible. Dès qu'il en rend une, c'est la sienne qui vaut.
    /// </summary>
    internal void UseFallbackCanvas(int width, int height)
    {
        if (_canvasComesFromEngine || width <= 0 || height <= 0) return;

        CanvasWidth = width;
        CanvasHeight = height;
    }

    /// <summary>
    /// Relit la composition dans le moteur et réaligne le canvas dessus. Appelé après chaque
    /// geste et à intervalle régulier par la vue : c'est ce qui fait suivre le canvas quand
    /// une transformation change du côté du moteur, sans que l'interface en soit prévenue.
    /// </summary>
    public void Refresh()
    {
        UpdatePlaceholder();

        var scene = _scene;
        if (scene == null)
        {
            Clear();
            Status = "";
            return;
        }

        var result = _sourceRuntime.GetSceneComposition(scene.Id);
        if (result.Status == StudioRuntimeStatus.Unavailable)
        {
            // Un moteur globalement indisponible est déjà annoncé par l'aperçu : ici, il n'y
            // a simplement rien de composé à montrer.
            Clear();
            Status = "";
            return;
        }

        if (!result.IsSuccess)
        {
            // Lecture refusée : on garde le dernier dessin obtenu plutôt que de vider le
            // canvas sur un incident passager, mais on le dit.
            Status = result.Message;
            return;
        }

        Status = "";
        if (result.Composition.CanvasWidth > 0 && result.Composition.CanvasHeight > 0)
        {
            _canvasComesFromEngine = true;
            CanvasWidth = result.Composition.CanvasWidth;
            CanvasHeight = result.Composition.CanvasHeight;
        }

        Synchronize(scene, result.Composition.Sources);
    }

    // Le moteur rend sa pile du premier plan vers l'arrière-plan ; elle est parcourue à
    // l'envers parce qu'un canvas se peint de l'arrière vers l'avant. Les calques existants
    // sont déplacés et remis à jour sur place : le canvas se relit plusieurs fois par
    // seconde, tout reconstruire à chaque fois se verrait.
    private void Synchronize(SceneItemViewModel scene, IReadOnlyList<SourceTransform> transforms)
    {
        var target = 0;
        for (var index = transforms.Count - 1; index >= 0; index--)
        {
            var transform = transforms[index];
            // Une source que le moteur ne compose pas — masquée, ou sans image comme une
            // source audio — n'a aucun rectangle à dessiner.
            if (!transform.IsVisible || transform.Width <= 0 || transform.Height <= 0) continue;

            var source = FindSource(scene, transform.SourceId);
            var name = source?.Name ?? "Source";
            var color = source?.Color ?? "#5b8def";

            var existing = IndexOfLayer(transform.SourceId, target);
            if (existing < 0)
            {
                Layers.Insert(target, new CompositionLayerViewModel(transform, name, color));
            }
            else
            {
                if (existing != target) Layers.Move(existing, target);
                Layers[target].Apply(transform, name, color);
            }

            target++;
        }

        while (Layers.Count > target) Layers.RemoveAt(Layers.Count - 1);
        HasLayers = Layers.Count > 0;
    }

    private void Clear()
    {
        Layers.Clear();
        HasLayers = false;
    }

    private void UpdatePlaceholder() =>
        Placeholder = !_sourceRuntime.IsAvailable
            ? _sourceRuntime.UnavailableMessage
            : _scene == null
                ? "Aucune scène sélectionnée."
                : "Aucune source composée dans cette scène.";

    private int IndexOfLayer(Guid sourceId, int from)
    {
        for (var index = from; index < Layers.Count; index++)
        {
            if (Layers[index].SourceId == sourceId) return index;
        }

        return -1;
    }

    private static SourceItemViewModel? FindSource(SceneItemViewModel scene, Guid sourceId)
    {
        foreach (var source in scene.Sources)
        {
            if (source.Id == sourceId) return source;
        }

        return null;
    }
}
