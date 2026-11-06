using CastorApplication.Models.Settings;
using CastorApplication.Models.Settings.Providers;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.ViewModels.Settings.Sections;

namespace Castor.Studio.Tests;

public sealed class StreamingSettingsViewModelTests
{
    [Fact]
    public void Youtube_title_and_visibility_are_loaded_and_saved()
    {
        var page = new StreamingSettingsViewModel(new FakeAccounts(YouTube()));
        page.Load(new ApplicationSettings
        {
            YouTubeBroadcastTitle = "Match du samedi",
            YouTubePrivacy = YouTubePrivacy.Unlisted
        });

        Assert.True(page.IsYouTubeConnected);
        Assert.Equal("Match du samedi", page.YouTubeBroadcastTitle);
        Assert.Equal(YouTubePrivacy.Unlisted, page.SelectedPrivacy?.Privacy);

        page.YouTubeBroadcastTitle = "  Finale  ";
        page.SelectedPrivacy = YouTubePrivacyOption.For(YouTubePrivacy.Private);
        var saved = new ApplicationSettings();
        page.Save(saved);

        Assert.Equal("Finale", saved.YouTubeBroadcastTitle);
        Assert.Equal(YouTubePrivacy.Private, saved.YouTubePrivacy);
    }

    [Fact]
    public void Defaults_are_a_public_live_with_the_castor_title()
    {
        var settings = new ApplicationSettings();

        Assert.Equal(YouTubePrivacy.Public, settings.YouTubePrivacy);
        Assert.Equal("Live Castor Studio", settings.YouTubeBroadcastTitle);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_title_falls_back_to_the_default_one(string title)
    {
        var page = new StreamingSettingsViewModel(new FakeAccounts(YouTube()));
        page.Load(new ApplicationSettings());
        page.YouTubeBroadcastTitle = title;

        var saved = new ApplicationSettings();
        page.Save(saved);

        Assert.Equal("Live Castor Studio", saved.YouTubeBroadcastTitle);
    }

    [Fact]
    public void A_title_longer_than_youtube_allows_is_cut()
    {
        var page = new StreamingSettingsViewModel(new FakeAccounts(YouTube()));
        page.Load(new ApplicationSettings());
        page.YouTubeBroadcastTitle = new string('a', 150);

        var saved = new ApplicationSettings();
        page.Save(saved);

        Assert.Equal(100, saved.YouTubeBroadcastTitle.Length);
    }

    [Fact]
    public void Youtube_section_follows_the_youtube_account()
    {
        var accounts = new FakeAccounts(Twitch());
        var page = new StreamingSettingsViewModel(accounts);
        Assert.False(page.IsYouTubeConnected);
        Assert.False(page.CanChooseDestination);

        accounts.Save(YouTube());

        Assert.True(page.IsYouTubeConnected);
        Assert.True(page.CanChooseDestination);
    }

    [Fact]
    public void The_destination_choice_survives_a_save_with_a_single_account()
    {
        var page = new StreamingSettingsViewModel(new FakeAccounts(Twitch()));
        page.Load(new ApplicationSettings { StreamingProviderIds = ["youtube"] });

        var saved = new ApplicationSettings { StreamingProviderIds = ["youtube"] };
        page.Save(saved);

        Assert.Equal(["youtube"], saved.StreamingProviderIds);
    }

    private static ProviderSettings Twitch() => new()
    {
        ProviderId = "twitch",
        IsConnected = true,
        UserName = "Castor",
        StreamKey = "live-key"
    };

    private static ProviderSettings YouTube() => new()
    {
        ProviderId = "youtube",
        IsConnected = true,
        UserName = "Club TV",
        StreamKey = "yt-key",
        IngestUrl = "rtmps://a.rtmps.youtube.com:443/live2",
        StreamId = "stream-1"
    };

    private sealed class FakeAccounts(params ProviderSettings[] accounts) : IProviderStore
    {
        private readonly List<ProviderSettings> _accounts = [.. accounts];
        public event EventHandler? Changed;
        public IReadOnlyCollection<ProviderSettings> GetAll() => _accounts;
        public ProviderSettings? Get(string providerId) => _accounts.FirstOrDefault(a => a.ProviderId == providerId);

        public void Save(ProviderSettings provider)
        {
            _accounts.RemoveAll(a => a.ProviderId == provider.ProviderId);
            _accounts.Add(provider);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Delete(string providerId)
        {
            _accounts.RemoveAll(a => a.ProviderId == providerId);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
