using System.Collections.ObjectModel;
using CastorApplication.Models.Studio;
using CastorApplication.Services;
using CastorApplication.Services.Studio;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels.Scenes;

public abstract record AddSourceResult
{
    public sealed record Video(CaptureSourceOption Option) : AddSourceResult;
    public sealed record Audio(AudioSourceOption Option) : AddSourceResult;
    public sealed record Media : AddSourceResult;

    public sealed record Network(string Name, string Url) : AddSourceResult
    {
        // L'URL porte souvent la clé du flux : elle ne doit jamais finir dans un journal.
        protected override bool PrintMembers(System.Text.StringBuilder builder)
        {
            builder.Append($"Name = {Name}, Url = <masquée>");
            return true;
        }
    }
}

public enum AddSourceCategoryKind
{
    Monitors,
    Windows,
    Cameras,
    NetworkCameras,
    SystemAudio,
    Microphones,
    Files
}

public partial class AddSourceCategoryItem : ViewModelBase
{
    public AddSourceCategoryKind Kind { get; }
    public string Name { get; }
    public string IconPath { get; }
    public bool HasCount { get; }

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _isSelected;

    public AddSourceCategoryItem(AddSourceCategoryKind kind, string name, string iconPath, bool hasCount = true)
    {
        Kind = kind;
        Name = name;
        IconPath = iconPath;
        HasCount = hasCount;
    }
}

public sealed class AddSourceItem
{
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string FullLabel { get; init; } = "";
    public string IconPath { get; init; } = "";
    public bool AlreadyInScene { get; init; }
    public CaptureSourceOption? VideoOption { get; init; }
    public AudioSourceOption? AudioOption { get; init; }
    public AddSourceResult? FixedResult { get; init; }
    public bool HasDetail => Detail.Length > 0;
}

public partial class AddSourceDialogViewModel : ViewModelBase
{
    private const string IconMonitor = SourceIcons.Monitor;
    private const string IconWindow = SourceIcons.Window;
    private const string IconCamera = SourceIcons.Camera;
    private const string IconVolume = SourceIcons.Volume;
    private const string IconMic = SourceIcons.Mic;
    private const string IconFolder = SourceIcons.Folder;
    private const string IconMovie = SourceIcons.Movie;
    private const string IconNetwork = SourceIcons.Network;
    private const string DefaultNetworkName = "Caméra RTMP";

    private readonly ISourceRuntime _runtime;
    private readonly SceneItemViewModel? _scene;
    private readonly IRtmpDiscoveryService? _discovery;
    private List<AddSourceItem> _monitors = [];
    private List<AddSourceItem> _windows = [];
    private List<AddSourceItem> _cameras = [];
    private List<AddSourceItem> _systemAudio = [];
    private List<AddSourceItem> _microphones = [];
    private readonly List<AddSourceItem> _fileEntries;

    public ObservableCollection<AddSourceCategoryItem> Categories { get; }
    public ObservableCollection<AddSourceItem> VisibleItems { get; } = [];
    public ObservableCollection<DiscoveredRtmpServer> DiscoveredServers { get; } = [];

    [ObservableProperty] private AddSourceCategoryItem _selectedCategory;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private AddSourceItem? _selectedItem;
    [ObservableProperty] private string _networkName = "";
    [ObservableProperty] private string _networkUrl = "";
    [ObservableProperty] private string _networkError = "";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _scanMessage = "";
    [ObservableProperty] private DiscoveredRtmpServer? _selectedServer;
    // What the last refresh could not list. It stays until dismissed or the next refresh.
    public StatusMessage CatalogMessage { get; }

    public bool IsNetworkCategory => SelectedCategory.Kind == AddSourceCategoryKind.NetworkCameras;
    public bool IsListCategory => !IsNetworkCategory;
    public string NetworkNamePlaceholder => DefaultNetworkName;
    public bool CanScan => _discovery != null;
    public bool CanConfirm => _runtime.IsAvailable &&
        (IsNetworkCategory ? NetworkUrl.Trim().Length > 0 : SelectedItem != null);

    public event Action<AddSourceResult?>? CloseRequested;

    internal AddSourceDialogViewModel(
        ISourceRuntime runtime,
        SceneItemViewModel? scene,
        StatusMessageService? messages = null,
        IRtmpDiscoveryService? discovery = null)
    {
        CatalogMessage = (messages ?? new StatusMessageService()).Create();
        _runtime = runtime;
        _scene = scene;
        _discovery = discovery;
        Categories =
        [
            new(AddSourceCategoryKind.Monitors, "Écrans", IconMonitor),
            new(AddSourceCategoryKind.Windows, "Fenêtres", IconWindow),
            new(AddSourceCategoryKind.Cameras, "Caméras", IconCamera),
            new(AddSourceCategoryKind.NetworkCameras, "Caméras RTMP", IconNetwork, false),
            new(AddSourceCategoryKind.SystemAudio, "Audio système", IconVolume),
            new(AddSourceCategoryKind.Microphones, "Micros", IconMic),
            new(AddSourceCategoryKind.Files, "Fichiers", IconFolder, false)
        ];
        _fileEntries =
        [
            new()
            {
                Title = "Fichier média",
                Detail = "Vidéo ou audio — mp4, mkv, mov, webm, mp3, wav, flac…",
                IconPath = IconMovie,
                FixedResult = new AddSourceResult.Media()
            }
        ];
        _selectedCategory = Categories[1];
        _selectedCategory.IsSelected = true;
    }

    [RelayCommand]
    private void SelectCategory(AddSourceCategoryItem category) => SelectedCategory = category;

    partial void OnSelectedCategoryChanged(AddSourceCategoryItem? oldValue, AddSourceCategoryItem newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        newValue.IsSelected = true;
        SelectedItem = null;
        NetworkError = "";
        OnPropertyChanged(nameof(IsNetworkCategory));
        OnPropertyChanged(nameof(IsListCategory));
        OnPropertyChanged(nameof(CanConfirm));
        RebuildVisibleItems();
    }

    partial void OnSearchTextChanged(string value) => RebuildVisibleItems();
    partial void OnSelectedItemChanged(AddSourceItem? value) => OnPropertyChanged(nameof(CanConfirm));

    partial void OnNetworkUrlChanged(string value)
    {
        NetworkError = "";
        OnPropertyChanged(nameof(CanConfirm));
    }

    // RTMP cannot say which streams a device serves: picking one only fills in its server,
    // the operator completes the path.
    partial void OnSelectedServerChanged(DiscoveredRtmpServer? value)
    {
        if (value != null) NetworkUrl = value.Server + "/";
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanNetwork(CancellationToken cancellationToken)
    {
        IsScanning = true;
        SelectedServer = null;
        DiscoveredServers.Clear();
        ScanMessage = "";
        try
        {
            var servers = await _discovery!.ScanAsync(cancellationToken);
            foreach (var server in servers) DiscoveredServers.Add(server);
            ScanMessage = servers.Count == 0
                ? $"Aucun appareil n'accepte de connexion RTMP (port {RtmpStreamUrl.DefaultPort}) sur le réseau local."
                : $"{servers.Count} appareil(s) trouvé(s) : choisissez-en un, puis complétez le chemin du flux.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ScanMessage = $"Scan impossible : {exception.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    public async Task Refresh(CancellationToken cancellationToken)
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        CatalogMessage.Clear();
        try
        {
            var catalog = await _runtime.EnumerateSourcesAsync(cancellationToken);
            _monitors = catalog.VideoSources.Where(source => source.Type == VideoCaptureKind.Monitor).Select(BuildVideoItem).ToList();
            _windows = catalog.VideoSources.Where(source => source.Type == VideoCaptureKind.Window).Select(BuildVideoItem).ToList();
            _cameras = catalog.VideoSources.Where(source => source.Type == VideoCaptureKind.Camera).Select(BuildVideoItem).ToList();
            _systemAudio = catalog.AudioSources.Where(source => source.Type is AudioCaptureKind.LoopbackGlobal or AudioCaptureKind.LoopbackWindow).Select(BuildAudioItem).ToList();
            _microphones = catalog.AudioSources.Where(source => source.Type is AudioCaptureKind.Microphone or AudioCaptureKind.CameraMic).Select(BuildAudioItem).ToList();

            foreach (var category in Categories.Where(category => category.HasCount))
                category.Count = ItemsFor(category.Kind).Count;

            CatalogMessage.ShowError(catalog.Message);
            RebuildVisibleItems();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            CatalogMessage.ShowError($"Actualisation impossible : {exception.Message}");
        }
        finally
        {
            IsRefreshing = false;
            OnPropertyChanged(nameof(CanConfirm));
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);

    [RelayCommand]
    private void Confirm()
    {
        if (IsNetworkCategory) ConfirmNetwork();
        else if (SelectedItem != null) ConfirmItem(SelectedItem);
    }

    public void ConfirmItem(AddSourceItem item)
    {
        var result = item.FixedResult ?? (AddSourceResult?)
            (item.VideoOption != null ? new AddSourceResult.Video(item.VideoOption) :
             item.AudioOption != null ? new AddSourceResult.Audio(item.AudioOption) : null);
        if (result != null) CloseRequested?.Invoke(result);
    }

    // The message never repeats the address: its path usually carries the stream key.
    private void ConfirmNetwork()
    {
        if (!_runtime.IsAvailable) return;
        var url = NetworkUrl.Trim();
        if (url.Length == 0)
        {
            NetworkError = "Saisissez l'adresse du flux.";
            return;
        }

        if (!RtmpStreamUrl.IsValid(url))
        {
            NetworkError = "Adresse invalide : elle doit commencer par rtmp:// ou rtmps://, puis indiquer le serveur et le chemin du flux.";
            return;
        }

        var name = NetworkName.Trim();
        CloseRequested?.Invoke(new AddSourceResult.Network(name.Length > 0 ? name : DefaultNetworkName, url));
    }

    private List<AddSourceItem> ItemsFor(AddSourceCategoryKind kind) => kind switch
    {
        AddSourceCategoryKind.Monitors => _monitors,
        AddSourceCategoryKind.Windows => _windows,
        AddSourceCategoryKind.Cameras => _cameras,
        AddSourceCategoryKind.SystemAudio => _systemAudio,
        AddSourceCategoryKind.Microphones => _microphones,
        AddSourceCategoryKind.Files => _fileEntries,
        _ => []
    };

    private void RebuildVisibleItems()
    {
        VisibleItems.Clear();
        var query = SearchText.Trim();
        foreach (var item in ItemsFor(SelectedCategory.Kind).Where(item =>
                     query.Length == 0 || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Detail.Contains(query, StringComparison.OrdinalIgnoreCase)))
            VisibleItems.Add(item);
    }

    private AddSourceItem BuildVideoItem(CaptureSourceOption option) => new()
    {
        Title = option.Label,
        Detail = option.Type switch { VideoCaptureKind.Monitor => "Écran", VideoCaptureKind.Camera => "Webcam", _ => "Fenêtre" },
        FullLabel = option.Label,
        IconPath = option.Type switch { VideoCaptureKind.Monitor => IconMonitor, VideoCaptureKind.Camera => IconCamera, _ => IconWindow },
        AlreadyInScene = _scene?.Sources.Any(source =>
            source.Origin == SourceOrigin.HardwareVideo && source.OriginPath == option.Id) == true,
        VideoOption = option
    };

    private AddSourceItem BuildAudioItem(AudioSourceOption option) => new()
    {
        Title = option.Label,
        Detail = option.Type is AudioCaptureKind.Microphone or AudioCaptureKind.CameraMic ? "Microphone" : "Audio système",
        FullLabel = option.Label,
        IconPath = option.Type is AudioCaptureKind.Microphone or AudioCaptureKind.CameraMic ? IconMic : IconVolume,
        AlreadyInScene = _scene?.Sources.Any(source =>
            source.Origin == SourceOrigin.HardwareAudio && source.OriginPath == option.Id) == true,
        AudioOption = option
    };
}
