using System.Collections.ObjectModel;
using CastorApplication.Models.Studio;
using CastorApplication.Services.Studio;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels.Scenes;

public abstract record AddSourceResult
{
    public sealed record Video(CaptureSourceOption Option) : AddSourceResult;
    public sealed record Audio(AudioSourceOption Option) : AddSourceResult;
    public sealed record Media : AddSourceResult;
}

public enum AddSourceCategoryKind
{
    Monitors,
    Windows,
    Cameras,
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
    private const string IconMonitor = "M3 5a1 1 0 0 1 1 -1h16a1 1 0 0 1 1 1v10a1 1 0 0 1 -1 1h-16a1 1 0 0 1 -1 -1z M7 20h10 M9 16v4 M15 16v4";
    private const string IconWindow = "M3 7a2 2 0 0 1 2 -2h14a2 2 0 0 1 2 2v10a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2z M3 9h18 M6 7v.01";
    private const string IconCamera = "M5 7h1a2 2 0 0 0 2 -2a1 1 0 0 1 1 -1h6a1 1 0 0 1 1 1a2 2 0 0 0 2 2h1a2 2 0 0 1 2 2v9a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-9a2 2 0 0 1 2 -2 M9 13a3 3 0 1 0 6 0a3 3 0 0 0 -6 0";
    private const string IconVolume = "M15 8a5 5 0 0 1 0 8 M17.7 5a9 9 0 0 1 0 14 M6 15h-2a1 1 0 0 1 -1 -1v-4a1 1 0 0 1 1 -1h2l3.5 -4.5a.8 .8 0 0 1 1.5 .5v14a.8 .8 0 0 1 -1.5 .5z";
    private const string IconMic = "M9 5a3 3 0 0 1 6 0v5a3 3 0 0 1 -6 0z M5 10a7 7 0 0 0 14 0 M8 21h8 M12 17v4";
    private const string IconFolder = "M5 4h4l3 3h7a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-11a2 2 0 0 1 2 -2";
    private const string IconMovie = "M4 4m0 2a2 2 0 0 1 2 -2h12a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2z M8 4v16 M16 4v16 M4 8h4 M16 8h4 M4 16h4 M16 16h4 M4 12h16";

    private readonly ISourceRuntime _runtime;
    private readonly SceneItemViewModel? _scene;
    private List<AddSourceItem> _monitors = [];
    private List<AddSourceItem> _windows = [];
    private List<AddSourceItem> _cameras = [];
    private List<AddSourceItem> _systemAudio = [];
    private List<AddSourceItem> _microphones = [];
    private readonly List<AddSourceItem> _fileEntries;

    public ObservableCollection<AddSourceCategoryItem> Categories { get; }
    public ObservableCollection<AddSourceItem> VisibleItems { get; } = [];

    [ObservableProperty] private AddSourceCategoryItem _selectedCategory;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private AddSourceItem? _selectedItem;
    [ObservableProperty] private string _catalogMessage = "";

    public bool IsListCategory => true;
    public bool CanConfirm => _runtime.IsAvailable && SelectedItem != null;

    public event Action<AddSourceResult?>? CloseRequested;

    internal AddSourceDialogViewModel(ISourceRuntime runtime, SceneItemViewModel? scene)
    {
        _runtime = runtime;
        _scene = scene;
        Categories =
        [
            new(AddSourceCategoryKind.Monitors, "Écrans", IconMonitor),
            new(AddSourceCategoryKind.Windows, "Fenêtres", IconWindow),
            new(AddSourceCategoryKind.Cameras, "Caméras", IconCamera),
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
        RebuildVisibleItems();
    }

    partial void OnSearchTextChanged(string value) => RebuildVisibleItems();
    partial void OnSelectedItemChanged(AddSourceItem? value) => OnPropertyChanged(nameof(CanConfirm));

    [RelayCommand]
    public async Task Refresh(CancellationToken cancellationToken)
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        CatalogMessage = "";
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

            CatalogMessage = catalog.Message;
            RebuildVisibleItems();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            CatalogMessage = $"Actualisation impossible : {exception.Message}";
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
        if (SelectedItem != null) ConfirmItem(SelectedItem);
    }

    public void ConfirmItem(AddSourceItem item)
    {
        var result = item.FixedResult ?? (AddSourceResult?)
            (item.VideoOption != null ? new AddSourceResult.Video(item.VideoOption) :
             item.AudioOption != null ? new AddSourceResult.Audio(item.AudioOption) : null);
        if (result != null) CloseRequested?.Invoke(result);
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
