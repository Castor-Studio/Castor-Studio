using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Threading;
using CastorApplication.Models.Settings;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.ViewModels.Studio;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Settings.Sections;

public sealed record StreamingDestinationOption(string ProviderId, string Label);

public sealed record YouTubePrivacyOption(YouTubePrivacy Privacy, string Label)
{
    public static IReadOnlyList<YouTubePrivacyOption> All { get; } =
    [
        new(YouTubePrivacy.Public, "Public"),
        new(YouTubePrivacy.Unlisted, "Non répertorié"),
        new(YouTubePrivacy.Private, "Privé")
    ];

    public static YouTubePrivacyOption For(YouTubePrivacy privacy) =>
        All.FirstOrDefault(option => option.Privacy == privacy) ?? All[0];
}

public partial class StreamingSettingsViewModel : SettingsSectionViewModel
{
    private readonly IProviderStore? _providerStore;

    // Kept as loaded while its account is not connected, so a reconnection finds the choice back.
    private string _storedProviderId = "";

    [ObservableProperty]
    private double _streamingBitrate = 4000;

    public string StreamingBitrateDisplay => $"{(int)StreamingBitrate}";

    partial void OnStreamingBitrateChanged(double value)
        => OnPropertyChanged(nameof(StreamingBitrateDisplay));

    // Accounts that can go live. The choice only matters with two of them: one alone is used
    // whatever is stored.
    public ObservableCollection<StreamingDestinationOption> Destinations { get; } = [];

    [ObservableProperty]
    private StreamingDestinationOption? _selectedDestination;

    public bool CanChooseDestination => Destinations.Count > 1;

    public string DestinationSummary => Destinations.Count switch
    {
        0 => "Aucun compte connecté. Connectez Twitch ou YouTube dans Paramètres → Comptes.",
        1 => Destinations[0].Label,
        _ => ""
    };

    // Each YouTube live is a broadcast of its own, created with this title and visibility.
    public bool IsYouTubeConnected =>
        Destinations.Any(option => option.ProviderId == StreamingProviders.YouTube);

    public IReadOnlyList<YouTubePrivacyOption> PrivacyOptions => YouTubePrivacyOption.All;

    public int YouTubeBroadcastTitleMaxLength => ApplicationSettings.YouTubeBroadcastTitleMaxLength;

    [ObservableProperty]
    private string _youTubeBroadcastTitle = ApplicationSettings.DefaultYouTubeBroadcastTitle;

    [ObservableProperty]
    private YouTubePrivacyOption? _selectedPrivacy = YouTubePrivacyOption.For(YouTubePrivacy.Public);

    public StreamingSettingsViewModel(IProviderStore? providerStore = null)
    {
        _providerStore = providerStore;
        if (_providerStore != null)
            _providerStore.Changed += OnProviderStoreChanged;
        RefreshDestinations();
    }

    // A refresh that fails to renew a token disconnects its account from a background thread.
    private void OnProviderStoreChanged(object? sender, EventArgs e)
    {
        if (Application.Current == null || Dispatcher.UIThread.CheckAccess()) RefreshDestinations();
        else Dispatcher.UIThread.Post(RefreshDestinations);
    }

    private void RefreshDestinations()
    {
        var wasDirty = IsDirty;
        var selected = SelectedDestination?.ProviderId ?? _storedProviderId;

        Destinations.Clear();
        foreach (var providerId in StreamingProviders.All)
        {
            var account = _providerStore?.Get(providerId);
            if (!StudioViewModel.IsReadyToStream(account)) continue;
            Destinations.Add(new StreamingDestinationOption(
                providerId,
                $"{StreamingProviders.DisplayName(providerId)} · {account!.UserName}"));
        }

        SelectedDestination = Destinations.FirstOrDefault(option => option.ProviderId == selected)
            ?? Destinations.FirstOrDefault();
        // An account connected elsewhere is not an edit of this page.
        IsDirty = wasDirty;

        OnPropertyChanged(nameof(CanChooseDestination));
        OnPropertyChanged(nameof(DestinationSummary));
        OnPropertyChanged(nameof(IsYouTubeConnected));
    }

    protected override void LoadCore(ApplicationSettings settings)
    {
        StreamingBitrate = settings.StreamingBitrate;
        _storedProviderId = settings.StreamingProviderId;
        SelectedDestination = Destinations.FirstOrDefault(option => option.ProviderId == _storedProviderId)
            ?? Destinations.FirstOrDefault();
        YouTubeBroadcastTitle = settings.YouTubeBroadcastTitle;
        SelectedPrivacy = YouTubePrivacyOption.For(settings.YouTubePrivacy);
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
        settings.StreamingBitrate = StreamingBitrate;
        // Saving with a single account connected must not erase the choice made for two.
        if (CanChooseDestination && SelectedDestination != null)
        {
            settings.StreamingProviderId = SelectedDestination.ProviderId;
            _storedProviderId = SelectedDestination.ProviderId;
        }

        // An empty title would make YouTube refuse the broadcast at the next live.
        var title = YouTubeBroadcastTitle.Trim();
        settings.YouTubeBroadcastTitle = title.Length == 0
            ? ApplicationSettings.DefaultYouTubeBroadcastTitle
            : title[..Math.Min(title.Length, ApplicationSettings.YouTubeBroadcastTitleMaxLength)];
        settings.YouTubePrivacy = SelectedPrivacy?.Privacy ?? YouTubePrivacy.Public;
    }
}
