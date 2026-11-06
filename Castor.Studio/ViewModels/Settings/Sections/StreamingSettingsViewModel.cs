using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Threading;
using CastorApplication.Models.Settings;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.ViewModels.Studio;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Settings.Sections;

// A platform the live can go to; every ticked one goes live at once.
public sealed partial class StreamingDestinationOption(string providerId, string label) : ViewModelBase
{
    public string ProviderId { get; } = providerId;
    public string Label { get; } = label;

    [ObservableProperty]
    private bool _isSelected;

    // False for the last ticked platform: a live needs somewhere to go.
    [ObservableProperty]
    private bool _canToggle = true;
}

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
        OnPropertyChanged(nameof(MultistreamHint));
    }

    // Accounts that can go live. The choice only matters with two of them: one alone is used
    // whatever is stored.
    public ObservableCollection<StreamingDestinationOption> Destinations { get; } = [];

    public bool CanChooseDestination => Destinations.Count > 1;

    public int SelectedDestinationCount => Destinations.Count(option => option.IsSelected);

    // Each platform receives the whole stream: the upload adds up.
    public string MultistreamHint => SelectedDestinationCount > 1
        ? $"{SelectedDestinationCount} lives en même temps : prévoyez environ " +
          $"{SelectedDestinationCount * (int)StreamingBitrate} kbps d'envoi."
        : "";

    public bool HasMultistreamHint => MultistreamHint.Length > 0;

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
        // Ticks made on the page survive an account connected meanwhile.
        var chosen = ChosenProviderIds();

        foreach (var option in Destinations) option.PropertyChanged -= OnDestinationOptionChanged;
        Destinations.Clear();
        foreach (var providerId in StreamingProviders.All)
        {
            var account = _providerStore?.Get(providerId);
            if (!StudioViewModel.IsReadyToStream(account)) continue;
            var option = new StreamingDestinationOption(
                providerId,
                $"{StreamingProviders.DisplayName(providerId)} · {account!.UserName}");
            option.PropertyChanged += OnDestinationOptionChanged;
            Destinations.Add(option);
        }

        ApplyChoice(chosen);
        // An account connected elsewhere is not an edit of this page.
        IsDirty = wasDirty;

        OnPropertyChanged(nameof(CanChooseDestination));
        OnPropertyChanged(nameof(DestinationSummary));
        OnPropertyChanged(nameof(IsYouTubeConnected));
    }

    // Ticked platforms, plus the stored ones whose account cannot go live right now.
    private List<string> ChosenProviderIds() =>
    [
        .. Destinations.Where(option => option.IsSelected).Select(option => option.ProviderId)
            .Concat(_storedProviderIds.Where(id => Destinations.All(option => option.ProviderId != id)))
            .Distinct()
    ];

    // Nothing chosen can go live: the first account is ticked, as the studio would use it.
    private void ApplyChoice(IReadOnlyCollection<string> providerIds)
    {
        foreach (var option in Destinations) option.IsSelected = providerIds.Contains(option.ProviderId);
        if (Destinations.Count > 0 && !Destinations.Any(option => option.IsSelected))
            Destinations[0].IsSelected = true;
        RefreshToggles();
    }

    private void OnDestinationOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StreamingDestinationOption.IsSelected)) return;
        RefreshToggles();
        // Also what marks the page as edited.
        OnPropertyChanged(nameof(SelectedDestinationCount));
        OnPropertyChanged(nameof(MultistreamHint));
        OnPropertyChanged(nameof(HasMultistreamHint));
    }

    private void RefreshToggles()
    {
        var lastOne = SelectedDestinationCount == 1;
        foreach (var option in Destinations) option.CanToggle = !(lastOne && option.IsSelected);
    }

    protected override void LoadCore(ApplicationSettings settings)
    {
        StreamingBitrate = settings.StreamingBitrate;
        _storedProviderIds = [.. settings.StreamingProviderIds];
        ApplyChoice(_storedProviderIds);
        YouTubeBroadcastTitle = settings.YouTubeBroadcastTitle;
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

        // An empty title would make YouTube refuse the broadcast at the next live.
        var title = YouTubeBroadcastTitle.Trim();
        settings.YouTubeBroadcastTitle = title.Length == 0
            ? ApplicationSettings.DefaultYouTubeBroadcastTitle
            : title[..Math.Min(title.Length, ApplicationSettings.YouTubeBroadcastTitleMaxLength)];
        settings.YouTubePrivacy = SelectedPrivacy?.Privacy ?? YouTubePrivacy.Public;
    }
}
