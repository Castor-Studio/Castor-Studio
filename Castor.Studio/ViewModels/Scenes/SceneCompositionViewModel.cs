using CastorApplication.Models.Studio;
using CastorApplication.Services.Studio;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Scenes;

/// <summary>
/// Ce qu'un point du canvas offre à saisir : une source à déplacer, ou une poignée de la
/// source choisie à tirer.
/// </summary>
public readonly record struct CompositionTarget(CompositionGestureKind Kind, CompositionHandle? Handle);

/// <summary>
/// La composition d'une scène telle que le moteur la détient, relue en continu : quelles
/// sources sont composées, et quel rectangle chacune occupe. Aucune de ces valeurs n'est
/// décidée ni ajustée ici — une valeur retenue localement finirait par décrire autre chose
/// que ce qui est réellement rendu.
/// </summary>
/// <remarks>
/// Ce qui en est fait — les cadres peints sur l'aperçu — est tracé par le moteur lui-même :
/// sa surface est une fenêtre native, rien de ce que dessine Avalonia ne peut s'y poser.
/// <para>
/// Un geste (déplacer, étirer, rogner) ne fait pas exception : il propose un placement, le
/// moteur l'écrit ou le refuse, et c'est sa réponse qui reste. La seule chose montrée avant
/// lui est le cadre de la source saisie, le temps qu'il confirme.
/// </para>
/// </remarks>
public partial class SceneCompositionViewModel : ViewModelBase
{
    /// <summary>
    /// Écart minimal entre deux écritures pendant un geste : une image à 60 i/s. Le pointeur
    /// bouge bien plus souvent que le moteur ne compose ; lui écrire chaque mouvement ne
    /// montrerait rien de plus.
    /// </summary>
    public static readonly TimeSpan WriteInterval = TimeSpan.FromMilliseconds(16);

    private readonly ISourceRuntime _sourceRuntime;
    private readonly TimeProvider _time;
    private SceneItemViewModel? _scene;
    private Guid? _selectedSourceId;
    private Gesture? _gesture;

    // Un geste en cours : ce qu'il saisit, d'où il part, ce qu'il demande, et ce que le
    // moteur en a déjà écrit.
    private sealed class Gesture
    {
        public required Guid SceneId { get; init; }
        public required CompositionGestureKind Kind { get; init; }
        public required CompositionHandle? Handle { get; init; }
        public required SourceTransform Start { get; init; }
        public required double OriginX { get; init; }
        public required double OriginY { get; init; }
        public SourcePlacement Requested { get; set; }
        public bool HasPendingWrite { get; set; }
        public bool HasWritten { get; set; }
        public long? LastWrite { get; set; }
    }

    /// <summary>
    /// Ce que le moteur doit tracer par-dessus son image : les sources qu'il compose, dans
    /// l'ordre du dessin — de l'arrière-plan vers le premier plan —, et celle qui est
    /// choisie. Chacune porte la couleur sous laquelle la liste des sources la nomme.
    /// </summary>
    public CompositionOverlay Overlay { get; private set; } = CompositionOverlay.Empty;

    /// <summary>La source choisie, telle que le moteur la compose à cet instant.</summary>
    public SourceTransform? Selected => Overlay.Selected?.Transform;

    /// <summary>Si un geste est en cours sur une source.</summary>
    public bool IsGesturing => _gesture != null;

    [ObservableProperty] private string _status = "";

    internal SceneCompositionViewModel(ISourceRuntime sourceRuntime, TimeProvider? time = null)
    {
        _sourceRuntime = sourceRuntime;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Suit désormais cette scène, à partir de zéro.</summary>
    internal void ShowScene(SceneItemViewModel? scene)
    {
        // Un geste appartient à la scène où il a commencé : ce qu'il a demandé est écrit
        // avant de la quitter, plutôt que perdu en route.
        FinishGesture(reconcile: false);
        _scene = scene;
        // Changer de scène, c'est changer de sources : garder le choix précédent ferait
        // rouvrir la suivante avec une sélection qui n'est plus à elle.
        _selectedSourceId = null;
        Publish([]);
        Refresh();
    }

    /// <summary>
    /// Choisit la source visée à ce point du canvas, ou aucune si le point ne tombe sur
    /// aucune. Le choix se porte sur celle qui est devant : c'est celle que l'opérateur
    /// voit à cet endroit.
    /// </summary>
    public void SelectAt(double canvasX, double canvasY)
    {
        var source = SourceAt(canvasX, canvasY);
        if (source == null)
        {
            ClearSelection();
            return;
        }

        _selectedSourceId = source.SourceId;
        Publish(Overlay.Sources);
    }

    /// <summary>Ne choisit plus aucune source : l'overlay ne montre que les cadres.</summary>
    public void ClearSelection()
    {
        if (_selectedSourceId == null) return;

        _selectedSourceId = null;
        Publish(Overlay.Sources);
    }

    /// <summary>
    /// Ce qu'un geste commencé à ce point saisirait : une poignée de la source choisie, à
    /// <paramref name="handleTolerance"/> près, sinon la source qui est devant. Les poignées
    /// passent avant tout : elles sont peintes par-dessus les autres cadres.
    /// </summary>
    public CompositionTarget? TargetAt(double canvasX, double canvasY, double handleTolerance, bool crop)
    {
        var selected = Selected;
        if (selected != null)
        {
            var handle = CompositionGeometry.HandleAt(selected, canvasX, canvasY, handleTolerance);
            if (handle != null)
                return new CompositionTarget(crop ? CompositionGestureKind.Crop : CompositionGestureKind.Resize, handle);
        }

        return SourceAt(canvasX, canvasY) != null
            ? new CompositionTarget(CompositionGestureKind.Move, null)
            : null;
    }

    /// <summary>
    /// Commence un geste à ce point du canvas. Sur une poignée de la source choisie, il
    /// l'étire — ou la rogne quand <paramref name="crop"/> est demandé. Sur une source, il
    /// la choisit et la déplace. À côté de toute source, il ne choisit plus rien.
    /// </summary>
    /// <returns>Ce que le geste a saisi, ou rien.</returns>
    public CompositionTarget? BeginGesture(double canvasX, double canvasY, double handleTolerance, bool crop)
    {
        FinishGesture(reconcile: false);

        var scene = _scene;
        var target = TargetAt(canvasX, canvasY, handleTolerance, crop);
        if (scene == null || target == null)
        {
            ClearSelection();
            return null;
        }

        var start = target.Value.Kind == CompositionGestureKind.Move
            ? SourceAt(canvasX, canvasY)!
            : Selected!;

        _selectedSourceId = start.SourceId;
        _gesture = new Gesture
        {
            SceneId = scene.Id,
            Kind = target.Value.Kind,
            Handle = target.Value.Handle,
            Start = start,
            OriginX = canvasX,
            OriginY = canvasY,
            Requested = start.Placement
        };
        Publish(Overlay.Sources);
        return target;
    }

    /// <summary>
    /// Le pointeur est maintenant à ce point du canvas. Le cadre suit tout de suite ; le
    /// moteur, lui, reçoit au plus une écriture par <see cref="WriteInterval"/>, la dernière
    /// demande restant en attente de <see cref="FlushGesture"/> ou de la fin du geste.
    /// </summary>
    /// <param name="keepAspectRatio">Si un angle garde les proportions de la source.</param>
    public void UpdateGesture(double canvasX, double canvasY, bool keepAspectRatio)
    {
        var gesture = _gesture;
        if (gesture == null) return;

        var deltaX = canvasX - gesture.OriginX;
        var deltaY = canvasY - gesture.OriginY;
        var requested = gesture.Kind switch
        {
            CompositionGestureKind.Resize => CompositionGeometry.Resize(
                gesture.Start, gesture.Handle!.Value, deltaX, deltaY, keepAspectRatio),
            CompositionGestureKind.Crop => CompositionGeometry.Crop(
                gesture.Start, gesture.Handle!.Value, deltaX, deltaY),
            _ => CompositionGeometry.Move(gesture.Start, deltaX, deltaY)
        };
        if (requested == gesture.Requested) return;

        gesture.Requested = requested;
        gesture.HasPendingWrite = true;
        Publish(Overlay.Sources);

        if (IsDue(gesture))
            Write(gesture);
    }

    /// <summary>
    /// Écrit la dernière demande du geste si elle attend encore et que l'intervalle est
    /// passé : c'est ce qui rattrape un pointeur qui s'arrête entre deux écritures.
    /// </summary>
    public void FlushGesture()
    {
        var gesture = _gesture;
        if (gesture is not { HasPendingWrite: true }) return;
        if (!IsDue(gesture)) return;

        Write(gesture);
    }

    /// <summary>
    /// Termine le geste : sa dernière demande est écrite, puis tout est relu dans le moteur.
    /// Ce qui reste à l'écran est ce qu'il a confirmé, pas ce qui a été demandé.
    /// </summary>
    public void EndGesture() => FinishGesture(reconcile: true);

    /// <summary>
    /// Abandonne le geste : la source reprend dans le moteur le placement qu'elle avait
    /// quand il a commencé.
    /// </summary>
    public void CancelGesture()
    {
        var gesture = _gesture;
        if (gesture == null) return;

        _gesture = null;
        var restored = Restore(gesture);
        Refresh();
        if (!restored.IsSuccess) Status = restored.Message;
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
            Publish([]);
            Status = "";
            return;
        }

        var result = _sourceRuntime.GetSceneComposition(scene.Id);
        if (result.Status == StudioRuntimeStatus.Unavailable)
        {
            // Un moteur globalement indisponible est déjà annoncé par l'aperçu : ici, il n'y
            // a simplement rien de composé.
            Publish([]);
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
        Publish(Drawable(scene, result.Composition.Sources));
    }

    private void FinishGesture(bool reconcile)
    {
        var gesture = _gesture;
        if (gesture == null) return;

        if (gesture.HasPendingWrite && !Write(gesture)) return;

        _gesture = null;
        if (reconcile) Refresh();
    }

    // Une écriture refusée arrête le geste là : la source reprend dans le moteur le
    // placement d'avant le geste, et l'écran se relit sur ce que le moteur détient. Rien
    // ne reste de la demande refusée, ni à l'écran, ni dans le moteur.
    private bool Write(Gesture gesture)
    {
        var result = _sourceRuntime.SetSourceTransform(gesture.SceneId, gesture.Start.SourceId, gesture.Requested);
        gesture.LastWrite = _time.GetTimestamp();
        gesture.HasPendingWrite = false;
        if (result.IsSuccess)
        {
            gesture.HasWritten = true;
            return true;
        }

        _gesture = null;
        Restore(gesture);
        Refresh();
        Status = result.Message;
        return false;
    }

    // La première demande d'un geste part tout de suite : c'est elle qui fait sentir que la
    // source a été saisie.
    private bool IsDue(Gesture gesture) =>
        gesture.LastWrite is not { } lastWrite || _time.GetElapsedTime(lastWrite) >= WriteInterval;

    private SourceTransformResult Restore(Gesture gesture) =>
        gesture.HasWritten
            ? _sourceRuntime.SetSourceTransform(gesture.SceneId, gesture.Start.SourceId, gesture.Start.Placement)
            : SourceTransformResult.Success(gesture.Start);

    // La source qui est devant à ce point du canvas. La liste va de l'arrière-plan vers le
    // premier plan : on la remonte pour rencontrer d'abord ce qui est dessus.
    private SourceTransform? SourceAt(double canvasX, double canvasY)
    {
        var sources = Overlay.Sources;
        for (var index = sources.Count - 1; index >= 0; index--)
        {
            var source = sources[index].Transform;
            if (CompositionGeometry.Contains(source, canvasX, canvasY)) return source;
        }

        return null;
    }

    // La sélection est retenue par identifiant, jamais par rectangle : c'est ce qui la fait
    // suivre la source quand le moteur la déplace, et ce qui la laisse tomber d'elle-même
    // quand la source cesse d'être composée — retirée, masquée, ou scène changée.
    private void Publish(IReadOnlyList<OverlaySource> sources)
    {
        sources = WithGesture(sources);
        var selected = Find(sources, _selectedSourceId);
        if (selected == null) _selectedSourceId = null;
        Overlay = new CompositionOverlay(sources, selected);
    }

    // Pendant un geste, la source saisie est montrée là où il la demande, sans attendre
    // l'écriture suivante ni la relecture du moteur : c'est ce qui rend le cadre collé au
    // pointeur. Les autres sources restent ce que le moteur en dit.
    private IReadOnlyList<OverlaySource> WithGesture(IReadOnlyList<OverlaySource> sources)
    {
        var gesture = _gesture;
        if (gesture == null) return sources;

        var shown = CompositionGeometry.Preview(gesture.Start, gesture.Requested);
        var result = new List<OverlaySource>(sources.Count);
        foreach (var source in sources)
        {
            result.Add(source.Transform.SourceId == gesture.Start.SourceId
                ? source with { Transform = shown }
                : source);
        }

        return result;
    }

    private static OverlaySource? Find(IReadOnlyList<OverlaySource> sources, Guid? sourceId)
    {
        if (sourceId == null) return null;

        foreach (var source in sources)
        {
            if (source.Transform.SourceId == sourceId) return source;
        }

        return null;
    }

    // Le moteur rend sa pile du premier plan vers l'arrière-plan ; elle est parcourue à
    // l'envers parce qu'on peint de l'arrière vers l'avant. Chaque source repart avec la
    // couleur que la liste lui donne : sur une composition qui se chevauche, c'est ce qui
    // dit quel cadre est quelle ligne. Une nouvelle liste à chaque lecture, puisque le
    // thread graphique du moteur relit image par image celle qu'on lui a donnée.
    private static IReadOnlyList<OverlaySource> Drawable(
        SceneItemViewModel scene,
        IReadOnlyList<SourceTransform> transforms)
    {
        var drawable = new List<OverlaySource>(transforms.Count);
        for (var index = transforms.Count - 1; index >= 0; index--)
        {
            var transform = transforms[index];
            // Une source que le moteur ne compose pas — masquée, ou sans image comme une
            // source audio — n'a aucun cadre à montrer.
            if (!transform.IsVisible || transform.Width <= 0 || transform.Height <= 0) continue;

            drawable.Add(new OverlaySource(transform, TintOf(scene, transform.SourceId)));
        }

        return drawable;
    }

    private static OverlayTint TintOf(SceneItemViewModel scene, Guid sourceId)
    {
        foreach (var source in scene.Sources)
        {
            if (source.Id == sourceId) return OverlayTint.Parse(source.Color);
        }

        return OverlayTint.Default;
    }
}
