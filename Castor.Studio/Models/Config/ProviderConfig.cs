namespace CastorApplication.Models.Config
{
    public class ProviderConfig
    {
        public string ClientId { get; set; } = string.Empty;
        public string? ClientSecret { get; set; } = null;
        public string[] Scopes { get; set; } = [];
    }
}
