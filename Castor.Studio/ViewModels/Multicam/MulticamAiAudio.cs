namespace CastorApplication.ViewModels.Multicam;

/// <summary>An audio source of the workspace the AI can listen to, as the microphone of a scene.</summary>
public sealed record MulticamMicrophoneOption(Guid? SceneId, Guid? SourceId, string Label)
{
    public static MulticamMicrophoneOption None { get; } = new(null, null, "Aucun micro");

    public bool IsNone => SourceId is null;

    // The same source whatever its label: renaming it keeps it selected.
    public bool Equals(MulticamMicrophoneOption? other) =>
        other is not null && SceneId == other.SceneId && SourceId == other.SourceId;

    public override int GetHashCode() => HashCode.Combine(SceneId, SourceId);

    public override string ToString() => Label;
}

/// <summary>What the person in a scene does in a podcast, as the worker understands it.</summary>
public sealed record MulticamAiRoleOption(string Value, string Label)
{
    public static IReadOnlyList<MulticamAiRoleOption> Podcast { get; } =
    [
        new("", "Rôle auto"),
        new("host", "Animateur"),
        new("guest", "Invité"),
        new("guest2", "Invité 2"),
        new("wide", "Plan large"),
        new("host_zoom", "Animateur serré"),
    ];

    public override string ToString() => Label;
}
