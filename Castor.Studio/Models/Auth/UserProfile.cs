namespace CastorApplication.Models.Auth
{
    public sealed class UserProfile
    {
        public string Id { get; init; } = "";

        public string DisplayName { get; init; } = "";

        public string? AvatarUrl { get; init; }
    }
}
