using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Linq;
using ReactiveUI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CastorApplication.ViewModels.Settings.Sections;
using CastorApplication.Services.Auth;
using CastorApplication.Services.Settings;
using CastorApplication.Services.Auth.Storage;

namespace CastorApplication.ViewModels.Settings;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly IProviderStore _providerStore;
    private readonly SettingsService _settingsService;

    [ObservableProperty]
    private ViewModelBase? _currentSection;

    public ObservableCollection<SettingsSectionItem> Sections { get; } = [];

    public IReadOnlyList<ISettingsSection> SectionViewModels => Sections
        .Select(s => s.ViewModel)
        .OfType<ISettingsSection>()
        .ToList();

    public bool HasUnsavedChanges => Sections.Any(
        section => section.ViewModel is ISettingsSection settingsSection && settingsSection.IsDirty);

    public SettingsViewModel(
        IAuthService authService,
        IProviderStore store,
        SettingsService settingsService,
        GeneralSettingsViewModel generalSettingsViewModel,
        VideoSettingsViewModel videoSettingsViewModel,
        AudioSettingsViewModel audioSettingsViewModel,
        StreamingSettingsViewModel streamingSettingsViewModel,
        OutputSettingsViewModel outputSettingsViewModel,
        AccountsSettingsViewModel accountsSettingsViewModel)
    {
        _authService = authService;
        _providerStore = store;
        _settingsService = settingsService;

        Sections.Add(new()
        {
            Title = "Général",
            ViewModel = generalSettingsViewModel,
            SelectCommand = SelectSectionCommand
        });

        Sections.Add(new()
        {
            Title = "Vidéo",
            ViewModel = videoSettingsViewModel,
            SelectCommand = SelectSectionCommand
        });

        Sections.Add(new()
        {
            Title = "Audio",
            ViewModel = audioSettingsViewModel,
            SelectCommand = SelectSectionCommand
        });

        Sections.Add(new()
        {
            Title = "Streaming",
            ViewModel = streamingSettingsViewModel,
            SelectCommand = SelectSectionCommand
        });

        Sections.Add(new()
        {
            Title = "Sortie",
            ViewModel = outputSettingsViewModel,
            SelectCommand = SelectSectionCommand
        });

        Sections.Add(new()
        {
            Title = "Comptes",
            ViewModel = accountsSettingsViewModel,
            SelectCommand = SelectSectionCommand
        });

        foreach (var section in SectionViewModels)
        {
            section.ObservableForProperty(s => s.IsDirty).Subscribe(_ =>
            {
                OnPropertyChanged(nameof(HasUnsavedChanges));
                SaveSettingsCommand.NotifyCanExecuteChanged();
            });
        }

        CurrentSection = Sections.FirstOrDefault()?.ViewModel;
        Load();
    }

    [RelayCommand]
    public async Task SelectSectionAsync(SettingsSectionItem item)
    {
        await Task.CompletedTask;
        CurrentSection = item.ViewModel;
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
            var settings = _settingsService.Load();

            foreach (var section in SectionViewModels)
            {
                section.Save(settings);
            }

            _settingsService.Save(settings);

            foreach (var section in SectionViewModels)
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

        foreach (var section in SectionViewModels)
        {
            section.Load(settings);
            section.MarkClean();
        }

        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveSettingsCommand.NotifyCanExecuteChanged();
    }
}
