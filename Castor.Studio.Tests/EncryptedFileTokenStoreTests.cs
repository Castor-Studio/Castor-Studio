using System.Runtime.Versioning;
using System.Text;
using CastorApplication.Models.Auth;
using CastorApplication.Services.Auth.Storage;

namespace Castor.Studio.Tests;

[SupportedOSPlatform("windows")]
public sealed class EncryptedFileTokenStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"castor-tokens-{Guid.NewGuid():N}");
    private string FilePath => Path.Combine(_directory, "tokens.dat");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task A_saved_session_is_read_back_after_a_restart()
    {
        await new EncryptedFileTokenStore(FilePath).SaveAsync(Session("youtube", "access-1"));

        // A new instance stands for the next app start.
        var session = await new EncryptedFileTokenStore(FilePath).GetAsync("youtube");

        Assert.NotNull(session);
        Assert.Equal("access-1", session.AccessToken);
        Assert.Equal("refresh-youtube", session.RefreshToken);
        Assert.Equal(["https://www.googleapis.com/auth/youtube"], session.Scopes);
        Assert.Equal("UC123", session.Profile?.Id);
        Assert.Equal("Club TV", session.Profile?.DisplayName);
    }

    [Fact]
    public async Task The_file_cannot_be_read_as_plain_text()
    {
        await new EncryptedFileTokenStore(FilePath).SaveAsync(Session("youtube", "access-very-secret"));

        var bytes = await File.ReadAllBytesAsync(FilePath);
        foreach (var encoding in new Encoding[] { Encoding.UTF8, Encoding.Unicode })
        {
            var text = encoding.GetString(bytes);
            Assert.DoesNotContain("access-very-secret", text);
            Assert.DoesNotContain("refresh-youtube", text);
            Assert.DoesNotContain("Club TV", text);
        }
    }

    [Fact]
    public async Task Each_provider_keeps_its_own_session_and_a_deletion_is_persisted()
    {
        var store = new EncryptedFileTokenStore(FilePath);
        await store.SaveAsync(Session("twitch", "twitch-access"));
        await store.SaveAsync(Session("youtube", "access-1"));
        await store.SaveAsync(Session("youtube", "access-2"));

        await store.DeleteAsync("twitch");

        var reloaded = new EncryptedFileTokenStore(FilePath);
        Assert.Null(await reloaded.GetAsync("twitch"));
        Assert.Equal("access-2", (await reloaded.GetAsync("youtube"))?.AccessToken);
        Assert.Single(await reloaded.GetAllAsync());
    }

    [Fact]
    public async Task A_corrupted_file_starts_empty_and_is_replaced_on_the_next_save()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, "not an encrypted file");

        var store = new EncryptedFileTokenStore(FilePath);
        Assert.Empty(await store.GetAllAsync());

        await store.SaveAsync(Session("youtube", "access-1"));
        Assert.Equal("access-1", (await new EncryptedFileTokenStore(FilePath).GetAsync("youtube"))?.AccessToken);
    }

    [Fact]
    public async Task Without_a_file_the_store_is_empty_and_deleting_writes_nothing()
    {
        var store = new EncryptedFileTokenStore(FilePath);

        Assert.Null(await store.GetAsync("youtube"));
        await store.DeleteAsync("youtube");

        Assert.False(File.Exists(FilePath));
    }

    private static AuthSession Session(string providerId, string accessToken) => new()
    {
        ProviderId = providerId,
        AccessToken = accessToken,
        RefreshToken = $"refresh-{providerId}",
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        Scopes = ["https://www.googleapis.com/auth/youtube"],
        Profile = new UserProfile { Id = "UC123", DisplayName = "Club TV" }
    };
}
