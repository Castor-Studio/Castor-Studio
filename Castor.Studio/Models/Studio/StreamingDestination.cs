namespace CastorApplication.Models.Studio;

// Platforms Castor can go live on. The ids are the auth provider ids, so a connected account
// in IProviderStore names its destination directly.
public static class StreamingProviders
{
    public const string Twitch = "twitch";
    public const string YouTube = "youtube";

    // Order of preference when several accounts are connected and none is chosen.
    public static IReadOnlyList<string> All { get; } = [Twitch, YouTube];

    public static string DisplayName(string providerId) => providerId switch
    {
        Twitch => "Twitch",
        YouTube => "YouTube",
        _ => providerId
    };
}

// Where the live output pushes to. Twitch resolves its ingest server itself ("auto"); the other
// platforms hand Castor the server address along with the key.
public sealed record StreamingDestination(string ProviderId, string Server, string StreamKey)
{
    public string DisplayName => StreamingProviders.DisplayName(ProviderId);

    public bool UsesPlatformServer => ProviderId == StreamingProviders.Twitch;

    // The key must never end up in a log, and a record prints every member by default.
    public override string ToString() => $"{DisplayName} ({Server})";
}
