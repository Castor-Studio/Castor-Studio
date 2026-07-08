using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using Castor.Engine.Models;
using Castor.Engine.Services;
using Castor.Native;
using CastorApplication.Services;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels;

public partial class StudioViewModel : ViewModelBase
{
    // ── Scene selection ──

    private readonly IStudioController _studioController;
    private readonly IFilePickerService _filePickerService;

    public ObservableCollection<SceneItem> Scenes => _studioController.Scenes;

    public SceneItem? ActiveScene
    {
        get => _studioController.ActiveScene;
        set
        {
            if (value == null) return;
            _studioController.SelectScene(value);
            OnPropertyChanged();
            RefreshPreviewPlaceholder();
        }
    }

    // ── Placeholder du preview ───────────────────────────────────────────────
    // Sans lui, l'utilisateur regarde un rectangle noir sans savoir si c'est
    // un bug ou juste une scène vide.

    public string PreviewPlaceholderText
    {
        get
        {
            var scene = _studioController.ActiveScene;
            if (scene == null)
                return "Aucune scène active — créez-en une dans l'onglet Scènes.";
            if (!_studioController.HasVideoSource(scene))
                return "Cette scène n'a pas de source vidéo.";
            return "";
        }
    }

    public bool ShowPreviewPlaceholder => PreviewPlaceholderText.Length > 0;

    private void RefreshPreviewPlaceholder()
    {
        OnPropertyChanged(nameof(PreviewPlaceholderText));
        OnPropertyChanged(nameof(ShowPreviewPlaceholder));
    }

    [ObservableProperty]
    private int _selectedSceneIndex;

    // ── Sources ──

    public ObservableCollection<SourceItem> Sources { get; } = new();

    // ── Audio Mixer ──

    [ObservableProperty]
    private double _desktopVolume = 80;

    [ObservableProperty]
    private double _micVolume = 65;

    [ObservableProperty]
    private double _musicVolume = 30;

    public string DesktopVolumeDisplay => $"{(int)DesktopVolume}%";
    public string MicVolumeDisplay => $"{(int)MicVolume}%";
    public string MusicVolumeDisplay => $"{(int)MusicVolume}%";

    partial void OnDesktopVolumeChanged(double value) => OnPropertyChanged(nameof(DesktopVolumeDisplay));
    partial void OnMicVolumeChanged(double value) => OnPropertyChanged(nameof(MicVolumeDisplay));
    partial void OnMusicVolumeChanged(double value) => OnPropertyChanged(nameof(MusicVolumeDisplay));

    // ── Streaming state (F1) ──

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private int _streamSceneIndex;

    [ObservableProperty]
    private int _streamPlatformIndex;

    [ObservableProperty]
    private string _streamRtmpKey = "";

    public string StreamStatusText => IsStreaming ? "EN DIRECT" : "OFFLINE";
    public IBrush StreamStatusBrush => IsStreaming
        ? SolidColorBrush.Parse("#f87171")
        : SolidColorBrush.Parse("#3c3c4e");
    public IBrush StreamTimerBrush => IsStreaming || IsRecording
        ? SolidColorBrush.Parse("#f87171")
        : SolidColorBrush.Parse("#3c3c4e");

    // ── Barre de scène : état réel (remplace le « Prêt » codé en dur) ──

    public string SceneBarStatusText => IsStreaming ? "EN DIRECT"
                                      : IsRecording ? "REC"
                                      : "Prêt";
    public IBrush SceneBarStatusBrush => IsStreaming || IsRecording
        ? SolidColorBrush.Parse("#f87171")
        : SolidColorBrush.Parse("#34d399");

    private void NotifySessionStateChanged()
    {
        OnPropertyChanged(nameof(StreamStatusText));
        OnPropertyChanged(nameof(StreamStatusBrush));
        OnPropertyChanged(nameof(StreamTimerBrush));
        OnPropertyChanged(nameof(SceneBarStatusText));
        OnPropertyChanged(nameof(SceneBarStatusBrush));
    }

    partial void OnIsStreamingChanged(bool value)
    {
        NotifySessionStateChanged();
        if (value) StartSessionTimerIfNeeded();
    }

    // ── Timer stream/record ──

    [ObservableProperty]
    private string _streamTimerText = "00:00:00";

    private readonly DispatcherTimer _sessionTimer;
    private DateTime? _sessionStartUtc;

    /// <summary>Démarre le timer si aucune session (stream/recording) n'est déjà en cours.</summary>
    private void StartSessionTimerIfNeeded()
    {
        if (_sessionStartUtc != null) return; // déjà en cours (l'autre était déjà actif)
        _sessionStartUtc = DateTime.UtcNow;
        StreamTimerText  = "00:00:00";
        _sessionTimer.Start();
    }

    /// <summary>Arrête et remet le timer à zéro. Appelé explicitement à l'arrêt du
    /// stream ou de l'enregistrement (pas seulement à la fermeture de l'app).</summary>
    private void ResetSessionTimer()
    {
        _sessionTimer.Stop();
        _sessionStartUtc = null;
        StreamTimerText  = "00:00:00";
    }

    private void OnSessionTimerTick(object? sender, EventArgs e)
    {
        if (_sessionStartUtc == null) return;
        var elapsed = DateTime.UtcNow - _sessionStartUtc.Value;
        StreamTimerText = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }

    // ── Provider helpers ──

    /* Index du ComboBox plateforme : 0=Twitch, 1=YouTube Live, 2=RTMP Manuel */

    private static string? GetProviderId(int platformIndex) => platformIndex switch
    {
        0 => "twitch",
        1 => "youtube",
        _ => null   // RTMP Manuel
    };

    private static string GetPlatformName(int platformIndex) => platformIndex switch
    {
        0 => "Twitch",
        1 => "YouTube Live",
        _ => "RTMP"
    };

    [ObservableProperty]
    private bool _isManualKeyRequired = true;

    [ObservableProperty]
    private string _connectedAccountLabel = "";

    private void RefreshProviderState(int platformIndex)
    {
        var providerId = GetProviderId(platformIndex);
        if (providerId == null)
        {
            IsManualKeyRequired = true;
            ConnectedAccountLabel = "";
            return;
        }

        var provider = _providerStore.Get(providerId);
        IsManualKeyRequired = provider == null;
        ConnectedAccountLabel = provider != null ? $"Connecté en tant que {provider.UserName}" : "";
    }

    partial void OnStreamPlatformIndexChanged(int value)
    {
        RefreshProviderState(value);

        if (value == 2 && string.IsNullOrWhiteSpace(StreamRtmpKey))
            StreamRtmpKey = AppSettings.CustomRtmpUrl;
    }

    // ── Recording state (F2) ──

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private int _recordSceneIndex;

    [ObservableProperty]
    private string _recordError = "";

    public string RecordStatusText => IsRecording ? "REC" : "";

    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(RecordStatusText));
        NotifySessionStateChanged();
        if (value) StartSessionTimerIfNeeded();
    }

    // ── Résolution/fps de sortie (remplace le « 1920 × 1080 » codé en dur) ──

    [ObservableProperty]
    private string _outputInfoText = "";

    /// <summary>Relit la résolution et le fps de sortie depuis les paramètres.
    /// Appelé à chaque affichage de la page Studio, pour refléter un
    /// changement fait dans Paramètres → Vidéo.</summary>
    public void RefreshOutputInfo()
    {
        var settings = _settingsService.Load();
        var (w, h)   = OutputResolutionFromIndex(settings.SelectedOutputResolutionIndex);
        int fps      = FpsFromIndex(settings.SelectedFpsIndex);
        OutputInfoText = $"{w} × {h} @ {fps} fps";
    }

    // ── Constructor ──

    private readonly IProviderStore _providerStore;
    private readonly SettingsService _settingsService;

    public StudioViewModel(
        IProviderStore providerStore,
        SettingsService settingsService,
        IStudioController studioController,
        IFilePickerService filePickerService)
    {
        _providerStore   = providerStore;
        _settingsService = settingsService;
        _studioController  = studioController;
        _filePickerService = filePickerService;

        RefreshProviderState(_streamPlatformIndex);
        RefreshOutputInfo();

        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sessionTimer.Tick += OnSessionTimerTick;
    }

    private void OnActiveSceneChanged()
    {
        OnPropertyChanged(nameof(ActiveScene));
        OnPropertyChanged(nameof(CurrentPreviewPullUrl));
    }

    // ── Helpers settings → valeurs encoder ───────────────────────────────────

    private static int FpsFromIndex(int index) => index switch
    {
        0 => 60,
        2 => 25,
        _ => 30,
    };

    private static (int width, int height) OutputResolutionFromIndex(int index) => index switch
    {
        1 => (1280,  720),  // HD
        2 => ( 854,  480),  // SD
        _ => (1920, 1080),  // Full HD (défaut)
    };

    private static (string extension, CastorVideoCodec vcodec, CastorAudioCodec acodec)
        FormatFromIndex(int index) => index switch
    {
        1 => (".mkv",  CastorVideoCodec.H264, CastorAudioCodec.AAC),
        2 => (".webm", CastorVideoCodec.VP9,  CastorAudioCodec.Opus),
        _ => (".mp4",  CastorVideoCodec.H264, CastorAudioCodec.AAC),
    };

    /// <summary>
    /// Démarre le preview si une scène vidéo est active et que le preview ne tourne pas déjà.
    /// Appelé à chaque fois que la vue Studio est affichée.
    /// </summary>
    public void EnsurePreviewRunning()
    {
        RefreshOutputInfo();
        RefreshPreviewPlaceholder();

        var scene = _studioController.ActiveScene;
        if (scene == null)
        {
            System.Diagnostics.Debug.WriteLine("[Preview] EnsurePreviewRunning : aucune scène active.");
            return;
        }

        var hasVideo = _studioController.HasVideoSource(scene);
        System.Diagnostics.Debug.WriteLine($"[Preview] EnsurePreviewRunning : scène='{scene.Name}', sources={scene.Sources.Count}, hasVideo={hasVideo}, isPreviewActive={_studioController.IsPreviewActive(scene.Id)}");

        if (!hasVideo)
        {
            System.Diagnostics.Debug.WriteLine("[Preview] Aucune source vidéo dans la scène — preview ignoré.");
            return;
        }

        if (_studioController.IsPreviewActive(scene.Id))
        {
            System.Diagnostics.Debug.WriteLine($"[Preview] '{scene.Name}' déjà actif (Studio) — ignoré.");
            return;
        }

        _ = System.Threading.Tasks.Task.Run(() =>
        {
            int result = _studioController.EnsurePreview(scene);
            System.Diagnostics.Debug.WriteLine($"[Preview] StartPreview retourné : {result}");
        });
    }

    public string? CurrentPreviewPullUrl => ActiveScene == null ? null : _studioController.GetPreviewPullUrl(ActiveScene.Id);

    // ── Streaming error ──

    [ObservableProperty]
    private string _streamError = "";

    // ── Streaming commands ──

    [RelayCommand]
    private async Task StartStreaming()
    {
        StreamError = "";

        var scene = _studioController.ActiveScene;

        if (scene == null || !_studioController.HasVideoSource(scene))
        {
            StreamError = "Aucune source vidéo dans la scène active.";
            return;
        }

        // Mapping index UI flyout -> StreamingPlatform
        // 0=Twitch, 1=YouTube Live, 2=RTMP Manuel
        StreamingPlatform service = StreamPlatformIndex switch
        {
            0 => StreamingPlatform.Twitch,
            1 => StreamingPlatform.YouTube,
            _ => StreamingPlatform.Custom
        };

        string keyOrUrl;
        var providerId = GetProviderId(StreamPlatformIndex);
        if (providerId != null)
        {
            var provider = _providerStore.Get(providerId);
            if (provider != null)
            {
                if (string.IsNullOrWhiteSpace(provider.StreamKey))
                {
                    StreamError = $"Compte {GetPlatformName(StreamPlatformIndex)} déconnecté. Reconnectez-vous dans Paramètres → Comptes.";
                    return;
                }
                keyOrUrl = provider.StreamKey!;
            }
            else
            {
                keyOrUrl = StreamRtmpKey;
                if (string.IsNullOrWhiteSpace(keyOrUrl))
                {
                    StreamError = "Clé de stream ou URL RTMP manquante.";
                    return;
                }
            }
        }
        else
        {
            keyOrUrl = StreamRtmpKey;
            if (string.IsNullOrWhiteSpace(keyOrUrl))
            {
                StreamError = "URL RTMP manquante.";
                return;
            }
        }

        // Lance le preview local si pas encore actif
        if (!_studioController.IsPreviewActive(scene.Id))
            _ = Task.Run(() =>
            {
                int r = _studioController.EnsurePreview(scene);
                System.Diagnostics.Debug.WriteLine($"[Preview] StartPreview (StartStreaming) retourné : {r}");
            });

        var streamSettings = _settingsService.Load();
        int streamFps     = FpsFromIndex(streamSettings.SelectedFpsIndex);
        int streamBitrate = (int)streamSettings.StreamingBitrate;

        // streaming_service_get_url peut faire un appel HTTPS (Twitch) → thread BG
        int result = await Task.Run(() =>
            _studioController.StartStream(scene, service, keyOrUrl, streamFps, streamBitrate));

        if (result == 0)
        {
            IsStreaming = true;
        }
        else
            StreamError = result switch
            {
                -2  => "Aucune source vidéo dans la scène active.",
                -3  => "Impossible de créer le recorder natif.",
                -4  => "Impossible de construire l'URL RTMP (clé invalide ?).",
                -10 => "Impossible d'ouvrir la capture vidéo.",
                -11 => "Dimensions vidéo invalides (0×0).",
                -12 => "Impossible d'ouvrir la capture audio.",
                -20 => "Codec vidéo introuvable (H.264 absent de l'avcodec.dll).",
                -21 => "Impossible d'initialiser l'encodeur vidéo.",
                -22 => "Impossible d'initialiser le convertisseur de pixels.",
                -25 => "Codec audio introuvable (AAC absent de l'avcodec.dll).",
                -26 => "Impossible d'initialiser l'encodeur audio.",
                -27 => "Erreur d'allocation du buffer audio.",
                -30 => "Impossible d'ouvrir la connexion RTMP.",
                -31 => "Erreur d'initialisation du muxer RTMP.",
                _   => $"Erreur streaming (code {result})."
            };
    }

    [RelayCommand]
    private void StopStreaming()
    {
        _studioController.StopStream();
        IsStreaming = false;
        ResetSessionTimer();
    }

    // ── Recording commands ──

    [RelayCommand]
    private async Task StartRecording()
    {
        RecordError = "";

        var scene = _studioController.ActiveScene;
        if (scene == null || !_studioController.HasVideoSource(scene))
        {
            RecordError = "Aucune source vidéo dans la scène active.";
            return;
        }

        var settings                = _settingsService.Load();
        var (ext, vcodec, acodec)   = FormatFromIndex(settings.SelectedOutputFormatIndex);
        int fps                     = FpsFromIndex(settings.SelectedFpsIndex);
        int bitrate                 = (int)settings.VideoBitrate;
        var (outW, outH)            = OutputResolutionFromIndex(settings.SelectedOutputResolutionIndex);
        int quality                 = settings.RecordingQualityIndex;

        string formatLabel = (vcodec, acodec) switch
        {
            (CastorVideoCodec.VP9,  CastorAudioCodec.Opus) => "WebM (VP9 + Opus)",
            (CastorVideoCodec.H264, _) when ext == ".mkv"  => "MKV (H.264 + AAC)",
            _                                               => "MP4 (H.264 + AAC)",
        };

        var path = await _filePickerService.PickRecordingOutputFileAsync(ext, formatLabel);
        if (path == null) return; // annulé par l'utilisateur

        int result = _studioController.StartRecording(scene, path, fps, bitrate, vcodec, acodec,
                                                      outW, outH, quality);
        if (result == 0)
        {
            IsRecording = true;
        }
        else
        {
            RecordError = result switch
            {
                -2  => "Aucune source vidéo dans la scène.",
                -3  => "Impossible de créer le recorder natif.",
                -10 => "Impossible d'ouvrir la capture vidéo.",
                -11 => "Dimensions vidéo invalides (0×0).",
                -12 => "Impossible d'ouvrir la capture audio.",
                -20 => "Codec vidéo introuvable — recompilez avec support VP9/H.264.",
                -21 => "Impossible d'initialiser l'encodeur vidéo.",
                -22 => "Impossible d'initialiser le convertisseur de pixels.",
                -25 => "Codec audio introuvable — recompilez avec support Opus/AAC.",
                -26 => "Impossible d'initialiser l'encodeur audio.",
                -27 => "Erreur d'allocation du buffer audio.",
                -30 => "Impossible de créer le fichier de sortie (chemin invalide ou disque plein ?).",
                -31 => "Erreur d'initialisation du muxer (codec incompatible avec le conteneur ?).",
                _   => $"Erreur inconnue (code {result})."
            };
        }
    }

    [RelayCommand]
    private void StopRecording()
    {
        _studioController.StopRecording();
        IsRecording = false;
        ResetSessionTimer();
    }

    // ── Source management ──

    [RelayCommand]
    private void AddSource()
    {
        Sources.Add(new SourceItem("Nouvelle source", SourceKind.Video, "#5b8def"));
    }

    [RelayCommand]
    private void RemoveSource(SourceItem source)
    {
        Sources.Remove(source);
    }
}

public sealed class SourcesPanelContext(StudioViewModel studio)
{
    public StudioViewModel Studio => studio;
}

public sealed class AudioPanelContext(StudioViewModel studio)
{
    public StudioViewModel Studio => studio;
}

