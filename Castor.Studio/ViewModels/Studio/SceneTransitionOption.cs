using CastorApplication.Models.Studio;

namespace CastorApplication.ViewModels.Studio;

public sealed record SceneTransitionOption(SceneTransitionKind Kind, string Label)
{
    public static IReadOnlyList<SceneTransitionOption> All { get; } =
    [
        new(SceneTransitionKind.Cut, "Coupe"),
        new(SceneTransitionKind.Fade, "Fondu"),
        new(SceneTransitionKind.FadeToBlack, "Fondu au noir"),
        new(SceneTransitionKind.Swipe, "Balayage"),
        new(SceneTransitionKind.Slide, "Glissement")
    ];

    // Des durées toutes prêtes plutôt qu'une saisie libre : en plein live, on choisit, on ne tape pas.
    public static IReadOnlyList<int> Durations { get; } = [150, 300, 500, 750, 1000, 1500, 2000];

    public static SceneTransitionOption For(SceneTransitionKind kind) =>
        All.FirstOrDefault(option => option.Kind == kind) ?? All[1];

    // Une durée réglée à la main dans le fichier de settings s'affiche sur la plus proche.
    public static int NearestDuration(int durationMs) =>
        Durations.MinBy(duration => Math.Abs(duration - durationMs));
}
