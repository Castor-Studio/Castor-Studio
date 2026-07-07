using CastorApplication.Models.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CastorApplication.ViewModels.Settings.Sections;

public partial class AudioSettingsViewModel : SettingsSectionViewModel
{
    [ObservableProperty]
    private int _selectedSampleRateIndex;

    [ObservableProperty]
    private int _selectedChannelsIndex;

    [ObservableProperty]
    private int _selectedAudioBitrateIndex = 1;

    // ── Lecture (preview player) ──────────────────────────────────────────────

    /// <summary>Volume du player de prévisualisation (0–100).</summary>
    [ObservableProperty]
    private double _playerVolume = 80;

    /// <summary>Coupe le son du player dès qu'un enregistrement ou un live démarre.</summary>
    [ObservableProperty]
    private bool _mutePlayersOnRecord = true;

    public string PlayerVolumeLabel => $"{(int)PlayerVolume}%";

    partial void OnPlayerVolumeChanged(double value) => OnPropertyChanged(nameof(PlayerVolumeLabel));

    protected override void LoadCore(ApplicationSettings settings)
    {
        SelectedSampleRateIndex = settings.SelectedSampleRateIndex;
        // Migration : « 5.1 Surround » (ex-index 2) a été retiré — le moteur
        // capture en stéréo maximum.
        SelectedChannelsIndex = settings.SelectedChannelsIndex is < 0 or > 1
            ? 0
            : settings.SelectedChannelsIndex;
        SelectedAudioBitrateIndex = settings.SelectedAudioBitrateIndex;
        PlayerVolume = settings.PlayerVolume;
        MutePlayersOnRecord = settings.MutePlayersOnRecord;
    }

    protected override void SaveCore(ApplicationSettings settings)
    {
        settings.SelectedSampleRateIndex = SelectedSampleRateIndex;
        settings.SelectedChannelsIndex = SelectedChannelsIndex;
        settings.SelectedAudioBitrateIndex = SelectedAudioBitrateIndex;
        settings.PlayerVolume = PlayerVolume;
        settings.MutePlayersOnRecord = MutePlayersOnRecord;
    }
}
