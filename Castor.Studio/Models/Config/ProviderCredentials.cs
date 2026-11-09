namespace CastorApplication.Models.Config;

// An OAuth client as credentials.json holds it, apart from the rest of the configuration.
public sealed class ProviderCredentials
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}
