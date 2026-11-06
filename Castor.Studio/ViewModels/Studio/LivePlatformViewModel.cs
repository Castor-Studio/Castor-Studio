using CastorApplication.Models.Studio;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Studio;

// One platform of the running live, as listed under the live controls: its account, its public
// page when the platform gives one (YouTube), and a stop of its own.
public sealed partial class LivePlatformViewModel(string providerId, string accountName) : ViewModelBase
{
    public string ProviderId { get; } = providerId;
    public string Name => StreamingProviders.DisplayName(ProviderId);
    public string AccountName { get; } = accountName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWatchUrl))]
    private string _watchUrl = "";

    public bool HasWatchUrl => WatchUrl.Length > 0;
}
