namespace CastorApplication.Models.Studio;

public enum SceneTransitionKind
{
    Cut,
    Fade,
    FadeToBlack,
    Swipe,
    Slide
}

/// <summary>
/// Comment la sortie programme (live, enregistrement) passe d'une scène à la suivante.
/// </summary>
public sealed record SceneTransition(SceneTransitionKind Kind, int DurationMs)
{
    public const int MinDurationMs = 50;
    public const int MaxDurationMs = 5000;
    public const int DefaultDurationMs = 300;

    public static SceneTransition Default { get; } = new(SceneTransitionKind.Fade, DefaultDurationMs);

    /// <summary>
    /// Ramène un réglage lu sur disque dans ce que l'interface sait proposer : un type
    /// inconnu redevient le défaut, une durée hors bornes est bornée.
    /// </summary>
    public static SceneTransition From(SceneTransitionKind kind, int durationMs) => new(
        Enum.IsDefined(kind) ? kind : Default.Kind,
        Math.Clamp(durationMs, MinDurationMs, MaxDurationMs));

    /// <summary>Une coupe ne s'anime pas : la scène suivante s'affiche d'un coup.</summary>
    public bool IsAnimated => Kind != SceneTransitionKind.Cut;
}
