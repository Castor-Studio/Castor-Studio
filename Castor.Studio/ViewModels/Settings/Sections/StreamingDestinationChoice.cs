using System.Collections.ObjectModel;
using System.ComponentModel;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.ViewModels.Studio;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Settings.Sections;

// A platform the live can go to; every ticked one goes live at once.
public sealed partial class StreamingDestinationOption(string providerId, string accountName) : ViewModelBase
{
    public string ProviderId { get; } = providerId;
    public string PlatformName => StreamingProviders.DisplayName(ProviderId);
    public string AccountName { get; } = accountName;
    public string Label => $"{PlatformName} · {AccountName}";

    [ObservableProperty]
    private bool _isSelected;

    // False for the last ticked platform: a live needs somewhere to go.
    [ObservableProperty]
    private bool _canToggle = true;
}

// The platforms ticked for the live among the accounts that can go live, as Settings › Streaming
// and the go-live panel both show them. With nothing chosen, the first account is ticked, as the
// studio would use it.
public sealed class StreamingDestinationChoice
{
    private bool _applying;

    public ObservableCollection<StreamingDestinationOption> Options { get; } = [];

    // A tick changed, by the user or through Refresh / Apply.
    public event EventHandler? SelectionChanged;

    // One account alone is used whatever is stored: there is nothing to choose.
    public bool CanChoose => Options.Count > 1;

    public int SelectedCount => Options.Count(option => option.IsSelected);

    public IReadOnlyList<string> SelectedProviderIds =>
        [.. Options.Where(option => option.IsSelected).Select(option => option.ProviderId)];

    public bool IsSelected(string providerId) =>
        Options.Any(option => option.ProviderId == providerId && option.IsSelected);

    public bool Contains(string providerId) => Options.Any(option => option.ProviderId == providerId);

    // Lists again the accounts ready to stream, in StreamingProviders.All order.
    public void Refresh(IProviderStore? providerStore, IReadOnlyCollection<string> chosenProviderIds)
    {
        foreach (var option in Options) option.PropertyChanged -= OnOptionChanged;
        Options.Clear();
        foreach (var providerId in StreamingProviders.All)
        {
            var account = providerStore?.Get(providerId);
            if (!StudioViewModel.IsReadyToStream(account)) continue;
            var option = new StreamingDestinationOption(providerId, account!.UserName ?? "");
            option.PropertyChanged += OnOptionChanged;
            Options.Add(option);
        }

        Apply(chosenProviderIds);
    }

    public void Apply(IReadOnlyCollection<string> providerIds)
    {
        _applying = true;
        try
        {
            foreach (var option in Options) option.IsSelected = providerIds.Contains(option.ProviderId);
            if (Options.Count > 0 && SelectedCount == 0) Options[0].IsSelected = true;
        }
        finally
        {
            _applying = false;
        }

        RefreshToggles();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_applying || e.PropertyName != nameof(StreamingDestinationOption.IsSelected)) return;
        RefreshToggles();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshToggles()
    {
        var lastOne = SelectedCount == 1;
        foreach (var option in Options) option.CanToggle = !(lastOne && option.IsSelected);
    }
}
