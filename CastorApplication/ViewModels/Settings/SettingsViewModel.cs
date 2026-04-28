using CastorApplication.Models;
using CastorApplication.Services;
using CastorApplication.ViewModels.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace CastorApplication.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly SettingsSectionViewModel[] _sectionViewModels;

    public IReadOnlyList<ISettingsSection> Sections { get; }

    public GeneralSettingsSectionViewModel General { get; }
    public VideoSettingsSectionViewModel Video { get; }
    public AudioSettingsSectionViewModel Audio { get; }
    public StreamingSettingsSectionViewModel Streaming { get; }
    public OutputSettingsSectionViewModel Output { get; }
    public AccountsSettingsSectionViewModel Accounts { get; }

    [ObservableProperty]
    private ISettingsSection? _currentSection;

    public bool HasUnsavedChanges => Sections.Any(section => section.IsDirty);

    public bool IsGeneralActive => ReferenceEquals(CurrentSection, General);
    public bool IsVideoActive => ReferenceEquals(CurrentSection, Video);
    public bool IsAudioActive => ReferenceEquals(CurrentSection, Audio);
    public bool IsStreamingActive => ReferenceEquals(CurrentSection, Streaming);
    public bool IsOutputActive => ReferenceEquals(CurrentSection, Output);
    public bool IsAccountsActive => ReferenceEquals(CurrentSection, Accounts);

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;

        General = new GeneralSettingsSectionViewModel();
        Video = new VideoSettingsSectionViewModel();
        Audio = new AudioSettingsSectionViewModel();
        Streaming = new StreamingSettingsSectionViewModel();
        Output = new OutputSettingsSectionViewModel();
        Accounts = new AccountsSettingsSectionViewModel();

        _sectionViewModels = [General, Video, Audio, Streaming, Output, Accounts];
        Sections = _sectionViewModels;

        foreach (var section in _sectionViewModels)
        {
            section.PropertyChanged += OnSectionPropertyChanged;
        }

        CurrentSection = General;
        Load();
    }

    partial void OnCurrentSectionChanged(ISettingsSection? value)
    {
        OnPropertyChanged(nameof(IsGeneralActive));
        OnPropertyChanged(nameof(IsVideoActive));
        OnPropertyChanged(nameof(IsAudioActive));
        OnPropertyChanged(nameof(IsStreamingActive));
        OnPropertyChanged(nameof(IsOutputActive));
        OnPropertyChanged(nameof(IsAccountsActive));
    }

    [RelayCommand]
    private void ShowGeneral()
    {
        CurrentSection = General;
    }

    [RelayCommand]
    private void ShowVideo()
    {
        CurrentSection = Video;
    }

    [RelayCommand]
    private void ShowAudio()
    {
        CurrentSection = Audio;
    }

    [RelayCommand]
    private void ShowStreaming()
    {
        CurrentSection = Streaming;
    }

    [RelayCommand]
    private void ShowOutput()
    {
        CurrentSection = Output;
    }

    [RelayCommand]
    private void ShowAccounts()
    {
        CurrentSection = Accounts;
    }

    private bool CanSaveSettings()
    {
        return HasUnsavedChanges;
    }

    [RelayCommand(CanExecute = nameof(CanSaveSettings))]
    private void SaveSettings()
    {
        try
        {
            var settings = new ApplicationSettings();

            foreach (var section in _sectionViewModels)
            {
                section.Save(settings);
            }

            _settingsService.Save(settings);

            foreach (var section in _sectionViewModels)
            {
                section.MarkClean();
            }

            OnPropertyChanged(nameof(HasUnsavedChanges));
            SaveSettingsCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsViewModel] Failed to save settings. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Load()
    {
        var settings = _settingsService.Load();

        foreach (var section in _sectionViewModels)
        {
            section.Load(settings);
            section.MarkClean();
        }

        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveSettingsCommand.NotifyCanExecuteChanged();
    }

    private void OnSectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ISettingsSection.IsDirty))
        {
            return;
        }

        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveSettingsCommand.NotifyCanExecuteChanged();
    }
}
