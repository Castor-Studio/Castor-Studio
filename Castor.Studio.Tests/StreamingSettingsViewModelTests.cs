using CastorApplication.Models.Settings;
using CastorApplication.Models.Settings.Providers;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.ViewModels.Settings.Sections;

namespace Castor.Studio.Tests;

public sealed class StreamingSettingsViewModelTests
{
    [Fact]
    public void Live_title_description_and_visibility_are_loaded_and_saved()
    {
        var page = new StreamingSettingsViewModel(new FakeAccounts(YouTube()));
        page.Load(new ApplicationSettings
        {
            LiveTitle = "Match du samedi",
            LiveDescription = "Demi-finale",
            YouTubePrivacy = YouTubePrivacy.Unlisted
        });

        Assert.True(page.IsYouTubeConnected);
        Assert.Equal("Match du samedi", page.LiveTitle);
        Assert.Equal("Demi-finale", page.LiveDescription);
        Assert.Equal(YouTubePrivacy.Unlisted, page.SelectedPrivacy?.Privacy);

        page.LiveTitle = "  Finale  ";
        page.LiveDescription = "  Finale régionale, <en direct>.  ";
        page.SelectedPrivacy = YouTubePrivacyOption.For(YouTubePrivacy.Private);
        var saved = new ApplicationSettings();
        page.Save(saved);

        Assert.Equal("Finale", saved.LiveTitle);
        Assert.Equal("Finale régionale, en direct.", saved.LiveDescription);
        Assert.Equal(YouTubePrivacy.Private, saved.YouTubePrivacy);
    }

    [Fact]
    public void Defaults_are_a_public_live_with_the_castor_title()
    {
        var settings = new ApplicationSettings();

        Assert.Equal(YouTubePrivacy.Public, settings.YouTubePrivacy);
        Assert.Equal("Live Castor Studio", settings.LiveTitle);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_title_falls_back_to_the_default_one(string title)
    {
        var page = new StreamingSettingsViewModel(new FakeAccounts(YouTube()));
        page.Load(new ApplicationSettings());
        page.LiveTitle = title;

        var saved = new ApplicationSettings();
        page.Save(saved);

        Assert.Equal("Live Castor Studio", saved.LiveTitle);
    }

    [Fact]
    public void A_title_longer_than_youtube_allows_is_cut()
    {
        var page = new StreamingSettingsViewModel(new FakeAccounts(YouTube()));
        page.Load(new ApplicationSettings());
        page.LiveTitle = new string('a', 150);

        var saved = new ApplicationSettings();
        page.Save(saved);

        Assert.Equal(100, saved.LiveTitle.Length);
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

    private static List<string> Ticked(StreamingSettingsViewModel page) =>
        [.. page.Destinations.Where(option => option.IsSelected).Select(option => option.ProviderId)];

    private static StreamingDestinationOption Option(StreamingSettingsViewModel page, string providerId) =>
        page.Destinations.Single(option => option.ProviderId == providerId);

    [Fact]
    public void Both_platforms_can_be_ticked_and_saved()
    {
        var page = new StreamingSettingsViewModel(new FakeAccounts(Twitch(), YouTube()));
        page.Load(new ApplicationSettings { StreamingProviderIds = ["twitch"] });
        Assert.Equal(["twitch"], Ticked(page));
        Assert.False(page.IsDirty);

        Option(page, "youtube").IsSelected = true;

        Assert.True(page.IsDirty);
        Assert.Equal("2 lives en même temps : prévoyez environ 8000 kbps d'envoi.", page.DestinationHint);
        var saved = new ApplicationSettings();
        page.Save(saved);
        Assert.Equal(["twitch", "youtube"], saved.StreamingProviderIds);
    }

    [Fact]
    public void The_last_ticked_platform_cannot_be_unticked()
    {
        var page = new StreamingSettingsViewModel(new FakeAccounts(Twitch(), YouTube()));
        page.Load(new ApplicationSettings { StreamingProviderIds = ["twitch", "youtube"] });
        Assert.All(page.Destinations, option => Assert.True(option.CanToggle));

        Option(page, "twitch").IsSelected = false;

        Assert.False(Option(page, "youtube").CanToggle);
        Assert.True(Option(page, "twitch").CanToggle);
        Assert.Equal("Cochez plusieurs plateformes pour y diffuser en même temps.", page.DestinationHint);
    }

    [Fact]
    public void Without_a_stored_choice_the_first_account_is_ticked_like_the_studio_does()
    {
        var page = new StreamingSettingsViewModel(new FakeAccounts(Twitch(), YouTube()));
        page.Load(new ApplicationSettings());

        Assert.Equal(["twitch"], Ticked(page));
    }

    [Fact]
    public void Ticks_survive_an_account_connected_while_the_page_is_open()
    {
        var accounts = new FakeAccounts(Twitch(), YouTube());
        var page = new StreamingSettingsViewModel(accounts);
        page.Load(new ApplicationSettings { StreamingProviderIds = ["twitch"] });
        Option(page, "youtube").IsSelected = true;

        accounts.Save(Twitch());

        Assert.Equal(["twitch", "youtube"], Ticked(page));
        Assert.True(page.IsDirty);
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
