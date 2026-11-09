using CastorApplication.Services.Config;

namespace Castor.Studio.Tests;

public sealed class JsonConfigServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"castor-config-{Guid.NewGuid():N}.json");

    public JsonConfigServiceTests()
    {
        File.WriteAllText(_path, """
            {
              "providers": {
                "twitch": { "clientId": "twitch-id", "scopes": [ "user:read:email" ] },
                "youtube": { "clientId": "", "scopes": [ "https://www.googleapis.com/auth/youtube" ] }
              }
            }
            """);
    }

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Environment_fills_in_the_client_kept_out_of_the_file()
    {
        var environment = new Dictionary<string, string>
        {
            ["CASTOR_YOUTUBE_CLIENT_ID"] = " yt-id ",
            ["CASTOR_YOUTUBE_CLIENT_SECRET"] = "yt-secret"
        };

        var config = new JsonConfigService(_path, name => environment.GetValueOrDefault(name));

        var youtube = config.GetProviderConfig("youtube");
        Assert.Equal("yt-id", youtube.ClientId);
        Assert.Equal("yt-secret", youtube.ClientSecret);
        Assert.Equal(["https://www.googleapis.com/auth/youtube"], youtube.Scopes);
    }

    [Fact]
    public void Without_environment_the_file_values_stay()
    {
        var config = new JsonConfigService(_path, _ => null);

        Assert.Equal("twitch-id", config.GetProviderConfig("twitch").ClientId);
        Assert.Null(config.GetProviderConfig("twitch").ClientSecret);
        Assert.Equal("", config.GetProviderConfig("youtube").ClientId);
    }

    [Fact]
    public void Blank_environment_values_do_not_erase_the_file()
    {
        var config = new JsonConfigService(_path, _ => "  ");

        Assert.Equal("twitch-id", config.GetProviderConfig("twitch").ClientId);
    }
}
