using CastorApplication.Models.Studio;

namespace CastorApplication.ViewModels.Studio;

// IconPath : icône 24x24 au trait (style Tabler), comme SourceIcons. Dans la barre, seule
// l'icône s'affiche ; le libellé sert d'infobulle et de nom pour les lecteurs d'écran.
public sealed record SceneTransitionOption(SceneTransitionKind Kind, string Label, string IconPath)
{
    public static IReadOnlyList<SceneTransitionOption> All { get; } =
    [
        new(SceneTransitionKind.Cut, "Coupe",
            "M3 7a3 3 0 1 0 6 0a3 3 0 1 0 -6 0 M3 17a3 3 0 1 0 6 0a3 3 0 1 0 -6 0 M8.6 8.6l10.4 10.4 M8.6 15.4l10.4 -10.4"),
        new(SceneTransitionKind.Fade, "Fondu",
            "M4 12a6 6 0 1 0 12 0a6 6 0 1 0 -12 0 M8 12a6 6 0 1 0 12 0a6 6 0 1 0 -12 0"),
        new(SceneTransitionKind.FadeToBlack, "Fondu au noir",
            "M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0 M12 17a5 5 0 0 0 0 -10z"),
        new(SceneTransitionKind.Swipe, "Balayage",
            "M4 12h10 M4 12l4 4 M4 12l4 -4 M20 4v16"),
        new(SceneTransitionKind.Slide, "Glissement",
            "M3 7h18 M6 10l-3 -3l3 -3 M3 17h18 M6 20l-3 -3l3 -3")
    ];

    // Des durées toutes prêtes plutôt qu'une saisie libre : en plein live, on choisit, on ne tape pas.
    public static IReadOnlyList<int> Durations { get; } = [150, 300, 500, 750, 1000, 1500, 2000];

    public static SceneTransitionOption For(SceneTransitionKind kind) =>
        All.FirstOrDefault(option => option.Kind == kind) ?? All[1];

    // Une durée réglée à la main dans le fichier de settings s'affiche sur la plus proche.
    public static int NearestDuration(int durationMs) =>
        Durations.MinBy(duration => Math.Abs(duration - durationMs));

    public static int StepOf(int durationMs) => IndexOf(NearestDuration(durationMs));

    public static int DurationAt(int step) => Durations[Math.Clamp(step, 0, Durations.Count - 1)];

    private static int IndexOf(int duration)
    {
        for (var index = 0; index < Durations.Count; index++)
            if (Durations[index] == duration) return index;
        return 0;
    }
}
