using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Castor.Engine.Models;
using Castor.Engine.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels;

/// <summary>Résultat renvoyé par le dialogue « Ajouter une source ».
/// Null = annulé. Les cas fichier délèguent au sélecteur de fichier
/// existant côté ScenesViewModel.</summary>
public abstract record AddSourceResult
{
    public sealed record Video(CaptureSourceOption Option) : AddSourceResult;
    public sealed record Audio(AudioSourceOption Option) : AddSourceResult;
    public sealed record PickFileVideo : AddSourceResult;
    public sealed record PickFileAudio : AddSourceResult;
    public sealed record PickFileMedia : AddSourceResult;
    public sealed record Network(string Label, string Url) : AddSourceResult;
}

public enum AddSourceCategoryKind
{
    Monitors,
    Windows,
    Cameras,
    SystemAudio,
    Microphones,
    Files,
    Network,
}

/// <summary>Entrée de la colonne catégories du dialogue.</summary>
public partial class AddSourceCategoryItem : ObservableObject
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

/// <summary>Ligne de la liste de sources du dialogue.</summary>
public sealed class AddSourceItem
{
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    /// <summary>Label natif complet — tooltip et détection « déjà dans la scène ».</summary>
    public string FullLabel { get; init; } = "";
    public string IconPath { get; init; } = "";
    public bool AlreadyInScene { get; init; }

    public CaptureSourceOption? VideoOption { get; init; }
    public AudioSourceOption? AudioOption { get; init; }
    /// <summary>Résultat fixe (entrées de la catégorie Fichiers).</summary>
    public AddSourceResult? FixedResult { get; init; }

    public bool HasDetail => Detail.Length > 0;
}

public partial class AddSourceDialogViewModel : ViewModelBase
{
    // Tracés Tabler (outline 24×24), même convention que la toolbar des scènes.
    private const string IconMonitor = "M3 5a1 1 0 0 1 1 -1h16a1 1 0 0 1 1 1v10a1 1 0 0 1 -1 1h-16a1 1 0 0 1 -1 -1z M7 20h10 M9 16v4 M15 16v4";
    private const string IconWindow  = "M3 7a2 2 0 0 1 2 -2h14a2 2 0 0 1 2 2v10a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2z M3 9h18 M6 7v.01";
    private const string IconCamera  = "M5 7h1a2 2 0 0 0 2 -2a1 1 0 0 1 1 -1h6a1 1 0 0 1 1 1a2 2 0 0 0 2 2h1a2 2 0 0 1 2 2v9a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-9a2 2 0 0 1 2 -2 M9 13a3 3 0 1 0 6 0a3 3 0 0 0 -6 0";
    private const string IconVolume  = "M15 8a5 5 0 0 1 0 8 M17.7 5a9 9 0 0 1 0 14 M6 15h-2a1 1 0 0 1 -1 -1v-4a1 1 0 0 1 1 -1h2l3.5 -4.5a.8 .8 0 0 1 1.5 .5v14a.8 .8 0 0 1 -1.5 .5z";
    private const string IconMic     = "M9 5a3 3 0 0 1 6 0v5a3 3 0 0 1 -6 0z M5 10a7 7 0 0 0 14 0 M8 21h8 M12 17v4";
    private const string IconFolder  = "M5 4h4l3 3h7a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-11a2 2 0 0 1 2 -2";
    private const string IconNetwork = "M18.36 19.36a9 9 0 1 0 -12.72 0 M15.54 16.54a5 5 0 1 0 -7.08 0 M12 13a1 1 0 1 0 0 -2a1 1 0 0 0 0 2";
    private const string IconMusic   = "M6 17a3 3 0 1 0 6 0a3 3 0 0 0 -6 0 M9 17v-13h10v13 M9 8h10 M13 17a3 3 0 1 0 6 0a3 3 0 0 0 -6 0";
    private const string IconMovie   = "M4 4m0 2a2 2 0 0 1 2 -2h12a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2z M8 4v16 M16 4v16 M4 8h4 M16 8h4 M4 16h4 M16 16h4 M4 12h16";

    private readonly INativeCaptureService _nativeCaptureService;
    private readonly INetworkCameraDiscoveryService _networkCameraDiscoveryService;
    private readonly SceneItem? _scene;

    private List<AddSourceItem> _monitors    = new();
    private List<AddSourceItem> _windows     = new();
    private List<AddSourceItem> _cameras     = new();
    private List<AddSourceItem> _systemAudio = new();
    private List<AddSourceItem> _microphones = new();
    private readonly List<AddSourceItem> _fileEntries;

    public ObservableCollection<AddSourceCategoryItem> Categories { get; }
    public ObservableCollection<AddSourceItem> VisibleItems { get; } = new();

    [ObservableProperty]
    private AddSourceCategoryItem _selectedCategory;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private AddSourceItem? _selectedItem;

    [ObservableProperty]
    private string _networkUrl = "";

    [ObservableProperty]
    private string _networkError = "";

    [ObservableProperty]
    private bool _isScanning;

    public ObservableCollection<DiscoveredCamera> DiscoveredCameras { get; } = new();

    public bool IsNetworkCategory => SelectedCategory.Kind == AddSourceCategoryKind.Network;
    public bool IsListCategory => !IsNetworkCategory;
    public bool CanConfirm => IsNetworkCategory || SelectedItem != null;

    /// <summary>Levé quand le dialogue doit se fermer (null = annulation).</summary>
    public event Action<AddSourceResult?>? CloseRequested;

    public AddSourceDialogViewModel(
        INativeCaptureService nativeCaptureService,
        INetworkCameraDiscoveryService networkCameraDiscoveryService,
        SceneItem? scene)
    {
        _nativeCaptureService = nativeCaptureService;
        _networkCameraDiscoveryService = networkCameraDiscoveryService;
        _scene = scene;

        Categories = new ObservableCollection<AddSourceCategoryItem>
        {
            new(AddSourceCategoryKind.Monitors,    "Écrans",        IconMonitor),
            new(AddSourceCategoryKind.Windows,     "Fenêtres",      IconWindow),
            new(AddSourceCategoryKind.Cameras,     "Caméras",       IconCamera),
            new(AddSourceCategoryKind.SystemAudio, "Audio système", IconVolume),
            new(AddSourceCategoryKind.Microphones, "Micros",        IconMic),
            new(AddSourceCategoryKind.Files,       "Fichiers",      IconFolder, hasCount: false),
            new(AddSourceCategoryKind.Network,     "Flux réseau",   IconNetwork, hasCount: false),
        };

        _fileEntries = new List<AddSourceItem>
        {
            new()
            {
                Title = "Vidéo seulement", Detail = "mp4, mkv, mov, avi, webm",
                IconPath = IconMovie, FixedResult = new AddSourceResult.PickFileVideo(),
            },
            new()
            {
                Title = "Audio seulement", Detail = "mp3, wav, aac, ogg, flac, m4a…",
                IconPath = IconMusic, FixedResult = new AddSourceResult.PickFileAudio(),
            },
            new()
            {
                Title = "Vidéo + audio", Detail = "Ajoute les deux pistes du fichier",
                IconPath = IconMovie, FixedResult = new AddSourceResult.PickFileMedia(),
            },
        };

        _selectedCategory = Categories[1]; // Fenêtres : le cas le plus fréquent
        _selectedCategory.IsSelected = true;
    }

    // ── Sélection / filtrage ─────────────────────────────────────────────────

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

    private List<AddSourceItem> ItemsFor(AddSourceCategoryKind kind) => kind switch
    {
        AddSourceCategoryKind.Monitors    => _monitors,
        AddSourceCategoryKind.Windows     => _windows,
        AddSourceCategoryKind.Cameras     => _cameras,
        AddSourceCategoryKind.SystemAudio => _systemAudio,
        AddSourceCategoryKind.Microphones => _microphones,
        AddSourceCategoryKind.Files       => _fileEntries,
        _                                 => new List<AddSourceItem>(),
    };

    private void RebuildVisibleItems()
    {
        VisibleItems.Clear();

        var query = SearchText.Trim();
        foreach (var item in ItemsFor(SelectedCategory.Kind))
        {
            if (query.Length > 0 &&
                !item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !item.Detail.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;
            VisibleItems.Add(item);
        }
    }

    // ── Énumération des sources natives ──────────────────────────────────────

    /// <summary>Ré-énumère les sources. Appelé à l'ouverture du dialogue et
    /// par le bouton Actualiser ; l'énumération native tourne hors du thread UI.</summary>
    [RelayCommand]
    public async Task Refresh()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            var sceneLabels = _scene?.Sources.Select(s => s.Name).ToHashSet(StringComparer.Ordinal)
                              ?? new HashSet<string>(StringComparer.Ordinal);

            var (monitors, windows, cameras, systemAudio, microphones) = await Task.Run(() =>
            {
                var video = _nativeCaptureService.ListVideoSources();
                var audio = _nativeCaptureService.ListAudioSources();

                var mons = new List<AddSourceItem>();
                var wins = new List<AddSourceItem>();
                var cams = new List<AddSourceItem>();
                foreach (var source in video)
                {
                    var item = BuildVideoItem(source, sceneLabels);
                    switch (source.Type)
                    {
                        case VideoCaptureKind.Monitor: mons.Add(item); break;
                        case VideoCaptureKind.Window:  wins.Add(item); break;
                        case VideoCaptureKind.Camera:  cams.Add(item); break;
                    }
                }

                var loops = new List<AddSourceItem>();
                var mics  = new List<AddSourceItem>();
                foreach (var source in audio)
                {
                    var item = BuildAudioItem(source, sceneLabels);
                    if (source.Type is AudioCaptureKind.Microphone or AudioCaptureKind.CameraMic)
                        mics.Add(item);
                    else
                        loops.Add(item);
                }

                return (mons, wins, cams, loops, mics);
            });

            _monitors    = monitors;
            _windows     = windows;
            _cameras     = cameras;
            _systemAudio = systemAudio;
            _microphones = microphones;

            foreach (var category in Categories)
            {
                if (category.HasCount)
                    category.Count = ItemsFor(category.Kind).Count;
            }

            RebuildVisibleItems();
        }
        catch
        {
            // L'énumération native peut échouer en mode design.
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private AddSourceItem BuildVideoItem(CaptureSourceOption option, HashSet<string> sceneLabels)
    {
        var (iconPath, detail) = option.Type switch
        {
            VideoCaptureKind.Monitor => (IconMonitor, "Écran"),
            VideoCaptureKind.Camera  => (IconCamera,  "Webcam"),
            _                        => (IconWindow,  GetWindowProcessName(option.Hwnd)),
        };

        return new AddSourceItem
        {
            Title          = StripNativePrefix(option.Label),
            Detail         = detail,
            FullLabel      = option.Label,
            IconPath       = iconPath,
            AlreadyInScene = sceneLabels.Contains(option.Label),
            VideoOption    = option,
        };
    }

    private AddSourceItem BuildAudioItem(AudioSourceOption option, HashSet<string> sceneLabels)
    {
        var (iconPath, detail) = option.Type switch
        {
            AudioCaptureKind.LoopbackGlobal => (IconVolume, "Tout le son du système"),
            AudioCaptureKind.LoopbackWindow => (IconVolume, JoinDetail("Son de la fenêtre", GetWindowProcessName(option.Hwnd))),
            AudioCaptureKind.CameraMic      => (IconMic,    "Micro de caméra"),
            _                               => (IconMic,    "Microphone"),
        };

        return new AddSourceItem
        {
            Title          = StripNativePrefix(option.Label),
            Detail         = detail,
            FullLabel      = option.Label,
            IconPath       = iconPath,
            AlreadyInScene = sceneLabels.Contains(option.Label),
            AudioOption    = option,
        };
    }

    /// <summary>Retire le préfixe natif « [Fenetre] », « [Micro] », ... — redondant,
    /// le dialogue est déjà organisé par catégories.</summary>
    private static string StripNativePrefix(string label)
    {
        if (label.Length > 0 && label[0] == '[')
        {
            int end = label.IndexOf(']');
            if (end > 0 && end + 1 < label.Length)
                return label[(end + 1)..].TrimStart();
        }
        return label;
    }

    private static string JoinDetail(string left, string right)
        => right.Length > 0 ? $"{left} — {right}" : left;

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    private static string GetWindowProcessName(nint hwnd)
    {
        if (hwnd == 0) return "";
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return "";
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return ""; // fenêtre fermée entre-temps ou process inaccessible
        }
    }

    // ── Validation / fermeture ───────────────────────────────────────────────

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);

    /// <summary>Ajout via le bouton « Ajouter à la scène » ou la touche Entrée.</summary>
    [RelayCommand]
    private void Confirm()
    {
        if (IsNetworkCategory)
        {
            ConfirmNetwork();
            return;
        }

        if (SelectedItem != null)
            ConfirmItem(SelectedItem);
    }

    /// <summary>Ajout direct (double-clic sur une ligne).</summary>
    public void ConfirmItem(AddSourceItem item)
    {
        AddSourceResult? result = item switch
        {
            { FixedResult: not null } => item.FixedResult,
            { VideoOption: not null } => new AddSourceResult.Video(item.VideoOption),
            { AudioOption: not null } => new AddSourceResult.Audio(item.AudioOption),
            _                         => null,
        };
        if (result != null)
            CloseRequested?.Invoke(result);
    }

    private void ConfirmNetwork()
    {
        NetworkError = "";
        var url = NetworkUrl.Trim();

        if (string.IsNullOrEmpty(url))
        {
            NetworkError = "Entrez une URL valide.";
            return;
        }

        // Mêmes règles que l'ajout réseau historique de la page Scènes.
        if (!url.StartsWith("rtmp://",  StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("rtsp://",  StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("http://",  StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            NetworkError = "URL invalide (rtmp://, rtsp://, http://)";
            return;
        }

        var label = url.Length > 30 ? url[..30] + "…" : url;
        CloseRequested?.Invoke(new AddSourceResult.Network(label, url));
    }

    // ── Scan réseau ONVIF (repris du flyout historique) ──────────────────────

    [RelayCommand]
    private async Task ScanNetwork()
    {
        if (IsScanning) return;
        IsScanning = true;
        NetworkError = "";
        DiscoveredCameras.Clear();

        try
        {
            var found = await _networkCameraDiscoveryService.ScanAsync(TimeSpan.FromSeconds(3));
            foreach (var camera in found)
                DiscoveredCameras.Add(camera);

            if (DiscoveredCameras.Count == 0)
                NetworkError = "Aucune caméra ONVIF trouvée sur le réseau.";
        }
        catch (Exception ex)
        {
            NetworkError = $"Erreur scan : {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void ConfirmDiscoveredCamera(DiscoveredCamera camera)
        => CloseRequested?.Invoke(new AddSourceResult.Network(camera.Label, camera.SuggestedUrl));
}
