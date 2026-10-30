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
/// How the program output (live, recording) goes from one scene to the next.
/// </summary>
public sealed record SceneTransition(SceneTransitionKind Kind, int DurationMs)
{
    public const int MinDurationMs = 50;
    public const int MaxDurationMs = 5000;
    public const int DefaultDurationMs = 300;

    public static SceneTransition Default { get; } = new(SceneTransitionKind.Fade, DefaultDurationMs);

    /// <summary>
    /// Brings a setting read from disk back to what the interface can offer: an unknown type
    /// returns to the default, an out-of-range duration is clamped.
    /// </summary>
    public static SceneTransition From(SceneTransitionKind kind, int durationMs) => new(
        Enum.IsDefined(kind) ? kind : Default.Kind,
        Math.Clamp(durationMs, MinDurationMs, MaxDurationMs));

    /// <summary>A cut is not animated: the next scene appears at once.</summary>
    public bool IsAnimated => Kind != SceneTransitionKind.Cut;
}
