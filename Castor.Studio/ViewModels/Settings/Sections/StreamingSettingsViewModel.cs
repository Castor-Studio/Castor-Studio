using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Threading;
using CastorApplication.Models.Settings;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Auth.Storage;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Settings.Sections;

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

    // Kept as loaded for accounts not ready to stream, so a reconnection finds the choice back.
    private List<string> _storedProviderIds = [];

    [ObservableProperty]
    private double _streamingBitrate = 4000;

    public string StreamingBitrateDisplay => $"{(int)StreamingBitrate}";

    partial void OnStreamingBitrateChanged(double value)
    {
        OnPropertyChanged(nameof(StreamingBitrateDisplay));
        OnPropertyChanged(nameof(DestinationHint));
    }

    private readonly StreamingDestinationChoice _destinations = new();

    // Accounts that can go live. The choice only matters with two of them: one alone is used
    // whatever is stored.
    public ObservableCollection<StreamingDestinationOption> Destinations => _destinations.Options;

    public bool CanChooseDestination => _destinations.CanChoose;

    public int SelectedDestinationCount => _destinations.SelectedCount;

    // One line under the ticks: how to go live on both, or, once both are ticked, the upload it
    // takes, since each platform receives the whole stream.
    public string DestinationHint => SelectedDestinationCount > 1
        ? $"{SelectedDestinationCount} lives en même temps : prévoyez environ " +
          $"{SelectedDestinationCount * (int)StreamingBitrate} kbps d'envoi."
        : "Cochez plusieurs plateformes pour y diffuser en même temps.";

    public string DestinationSummary => Destinations.Count switch
    {
        0 => "Aucun compte connecté. Connectez Twitch ou YouTube dans Paramètres → Comptes.",
        1 => Destinations[0].Label,
        _ => ""
    };

    // Each YouTube live is a broadcast of its own, created with this description and visibility.
    public bool IsYouTubeConnected =>
        Destinations.Any(option => option.ProviderId == StreamingProviders.YouTube);

    public IReadOnlyList<YouTubePrivacyOption> PrivacyOptions => YouTubePrivacyOption.All;

    public int LiveTitleMaxLength => ApplicationSettings.LiveTitleMaxLength;

    public int LiveDescriptionMaxLength => ApplicationSettings.LiveDescriptionMaxLength;

    // Given to every platform the live goes to.
    [ObservableProperty]
    private string _liveTitle = ApplicationSettings.DefaultLiveTitle;

    [ObservableProperty]
    private string _liveDescription = "";

    [ObservableProperty]
    private YouTubePrivacyOption? _selectedPrivacy = YouTubePrivacyOption.For(YouTubePrivacy.Public);

    public StreamingSettingsViewModel(IProviderStore? providerStore = null)
    {
        _providerStore = providerStore;
        _destinations.SelectionChanged += OnDestinationSelectionChanged;
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
        // Ticks made on the page survive an account connected meanwhile.
        _destinations.Refresh(_providerStore, ChosenProviderIds());
        // An account connected elsewhere is not an edit of this page.
        IsDirty = wasDirty;

        OnPropertyChanged(nameof(CanChooseDestination));
        OnPropertyChanged(nameof(DestinationSummary));
        OnPropertyChanged(nameof(IsYouTubeConnected));
    }

    // Ticked platforms, plus the stored ones whose account cannot go live right now.
    private List<string> ChosenProviderIds() =>
    [
        .. _destinations.SelectedProviderIds
            .Concat(_storedProviderIds.Where(id => !_destinations.Contains(id)))
            .Distinct()
    ];

    private void OnDestinationSelectionChanged(object? sender, EventArgs e)
    {
        // Also what marks the page as edited.
        OnPropertyChanged(nameof(SelectedDestinationCount));
        OnPropertyChanged(nameof(DestinationHint));
    }

    protected override void LoadCore(ApplicationSettings settings)
    {
        StreamingBitrate = settings.StreamingBitrate;
        _storedProviderIds = [.. settings.StreamingProviderIds];
        _destinations.Apply(_storedProviderIds);
        LiveTitle = settings.LiveTitle;
        LiveDescription = settings.LiveDescription;
        SelectedPrivacy = YouTubePrivacyOption.For(settings.YouTubePrivacy);
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
        settings.StreamingBitrate = StreamingBitrate;
        // Saving with a single account connected must not erase the choice made for two.
        if (CanChooseDestination)
        {
            _storedProviderIds = ChosenProviderIds();
            settings.StreamingProviderIds = [.. _storedProviderIds];
        }

        settings.LiveTitle = ApplicationSettings.NormalizeLiveTitle(LiveTitle);
        settings.LiveDescription = ApplicationSettings.NormalizeLiveDescription(LiveDescription);
        settings.YouTubePrivacy = SelectedPrivacy?.Privacy ?? YouTubePrivacy.Public;
    }
}
