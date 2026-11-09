using CastorApplication.Services.Config;

namespace Castor.Studio.Tests;

public sealed class JsonConfigServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"castor-config-{Guid.NewGuid():N}");
    private readonly string _path;
    private readonly string _credentialsPath;

    public JsonConfigServiceTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "config.json");
        _credentialsPath = Path.Combine(_directory, JsonConfigService.CredentialsFileName);
        File.WriteAllText(_path, """
            {
              "providers": {
                "twitch": { "clientId": "twitch-id", "scopes": [ "user:read:email" ] },
                "youtube": { "clientId": "", "scopes": [ "https://www.googleapis.com/auth/youtube" ] }
              }
            }
            """);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private JsonConfigService Load(Func<string, string?> environment) => new(_path, _credentialsPath, environment);

    private void WriteCredentials() => File.WriteAllText(_credentialsPath, """
        {
          "youtube": { "clientId": "file-id", "clientSecret": "file-secret" },
          "unknown": { "clientId": "ignored" }
        }
        """);

    [Fact]
    public void Environment_fills_in_the_client_kept_out_of_the_file()
    {
        var environment = new Dictionary<string, string>
        {
            ["CASTOR_YOUTUBE_CLIENT_ID"] = " yt-id ",
            ["CASTOR_YOUTUBE_CLIENT_SECRET"] = "yt-secret"
        };

        var config = Load(name => environment.GetValueOrDefault(name));

        var youtube = config.GetProviderConfig("youtube");
        Assert.Equal("yt-id", youtube.ClientId);
        Assert.Equal("yt-secret", youtube.ClientSecret);
        Assert.Equal(["https://www.googleapis.com/auth/youtube"], youtube.Scopes);
    }

    [Fact]
    public void Without_environment_nor_credentials_the_config_values_stay()
    {
        var config = Load(_ => null);

        Assert.Equal("twitch-id", config.GetProviderConfig("twitch").ClientId);
        Assert.Null(config.GetProviderConfig("twitch").ClientSecret);
        Assert.Equal("", config.GetProviderConfig("youtube").ClientId);
    }

    [Fact]
    public void Blank_environment_values_do_not_erase_the_file()
    {
        var config = Load(_ => "  ");

        Assert.Equal("twitch-id", config.GetProviderConfig("twitch").ClientId);
    }

    [Fact]
    public void Credentials_file_gives_the_client_and_leaves_the_other_providers()
    {
        WriteCredentials();

        var config = Load(_ => null);

        var youtube = config.GetProviderConfig("youtube");
        Assert.Equal("file-id", youtube.ClientId);
        Assert.Equal("file-secret", youtube.ClientSecret);
        Assert.Equal(["https://www.googleapis.com/auth/youtube"], youtube.Scopes);
        Assert.Equal("twitch-id", config.GetProviderConfig("twitch").ClientId);
    }

    [Fact]
    public void Environment_wins_over_the_credentials_file()
    {
        WriteCredentials();

        var config = Load(name => name == "CASTOR_YOUTUBE_CLIENT_ID" ? "env-id" : null);

        var youtube = config.GetProviderConfig("youtube");
        Assert.Equal("env-id", youtube.ClientId);
        Assert.Equal("file-secret", youtube.ClientSecret);
    }

    [Fact]
    public void The_credentials_file_is_read_beside_the_config_file()
    {
        WriteCredentials();

        var config = new JsonConfigService(_path);

        // Unless the machine sets CASTOR_YOUTUBE_CLIENT_ID itself.
        if (Environment.GetEnvironmentVariable("CASTOR_YOUTUBE_CLIENT_ID") is { Length: > 0 }) return;
        Assert.Equal("file-id", config.GetProviderConfig("youtube").ClientId);
    }
}
