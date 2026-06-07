namespace Castor.Engine.Models;

public sealed class DiscoveredCamera
{
    public string Label { get; init; } = "";
    public string Ip { get; init; } = "";
    public string SuggestedUrl { get; init; } = "";
    public string Method { get; init; } = "";
}
