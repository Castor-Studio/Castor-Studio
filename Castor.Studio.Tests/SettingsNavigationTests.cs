using CastorApplication.Models.Auth;
using CastorApplication.Models.Settings;
using CastorApplication.Models.Settings.Providers;
using CastorApplication.Services;
using CastorApplication.Services.Auth;
using CastorApplication.Services.Auth.Storage;
using CastorApplication.Services.Settings;
using CastorApplication.ViewModels.Settings;
using CastorApplication.ViewModels.Settings.Sections;

namespace Castor.Studio.Tests;

public sealed class SettingsNavigationTests
{
    [Fact]
    public void The_first_section_is_marked_when_settings_open()
    {
        using var settings = new SettingsFixture();

        Assert.Equal([true, false, false, false, false, false], settings.ViewModel.Sections.Select(s => s.IsSelected));
    }

    [Fact]
    public async Task Selecting_a_section_marks_it_and_only_it()
    {
        using var settings = new SettingsFixture();
        var video = settings.ViewModel.Sections[1];

        await settings.ViewModel.SelectSectionCommand.ExecuteAsync(video);

        Assert.Same(video.ViewModel, settings.ViewModel.CurrentSection);
        Assert.Equal([video], settings.ViewModel.Sections.Where(s => s.IsSelected));
    }

    [Fact]
    public async Task Selecting_the_section_already_open_keeps_it_marked()
    {
        using var settings = new SettingsFixture();
        var general = settings.ViewModel.Sections[0];

        await settings.ViewModel.SelectSectionCommand.ExecuteAsync(general);

        Assert.True(general.IsSelected);
    }

    [Fact]
    public void Opening_the_accounts_from_elsewhere_marks_them()
    {
        using var settings = new SettingsFixture();

        settings.ViewModel.ShowAccounts();

        var marked = Assert.Single(settings.ViewModel.Sections, s => s.IsSelected);
        Assert.IsType<AccountsSettingsViewModel>(marked.ViewModel);
    }

    private sealed class SettingsFixture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("castor-settings-nav-");

        public SettingsViewModel ViewModel { get; }

        public SettingsFixture()
        {
            var settingsService = new SettingsService(Path.Combine(_directory.FullName, "settings.json"));
            var store = new EmptyProviderStore();
            var auth = new UnusedAuthService();
            ViewModel = new SettingsViewModel(
                auth,
                store,
                settingsService,
                new GeneralSettingsViewModel(new FakeThemeService(), new FakeDockChromeService()),
                new VideoSettingsViewModel(new VideoCanvasResolutionResolver(
                    settingsService, new TestResolutionProvider(new VideoCanvasResolution(1920, 1080)))),
                new AudioSettingsViewModel(),
                new StreamingSettingsViewModel(),
                new OutputSettingsViewModel(new UnusedFilePickerService()),
                new AccountsSettingsViewModel(auth, store));
        }

        public void Dispose() => _directory.Delete(recursive: true);
    }

    private sealed class FakeThemeService : IThemeService
    {
        public bool IsLightTheme => false;
        public void ApplyTheme(int selectedThemeIndex) { }
    }

    private sealed class FakeDockChromeService : IDockChromeService
    {
        public void ApplyShowTitles(bool showTitles) { }
    }

    private sealed class EmptyProviderStore : IProviderStore
    {
        public event EventHandler? Changed { add { } remove { } }
        public IReadOnlyCollection<ProviderSettings> GetAll() => [];
        public ProviderSettings? Get(string providerId) => null;
        public void Save(ProviderSettings provider) { }
        public void Delete(string providerId) { }
    }

    private sealed class UnusedAuthService : IAuthService
    {
        public Task<DeviceCodeResult> BeginLoginAsync(string providerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthSession> CompleteLoginAsync(string providerId, DeviceCodeResult deviceCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthSession?> GetSessionAsync(string providerId, CancellationToken cancellationToken = default) => Task.FromResult<AuthSession?>(null);
        public Task<IReadOnlyCollection<AuthSession>> GetSessionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<AuthSession>>([]);
        public Task<string?> GetAccessTokenAsync(string providerId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task LogoutAsync(string providerId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public string GetClientId(string providerId) => "";
    }

    private sealed class UnusedFilePickerService : IFilePickerService
    {
        public Task<string?> PickRecordingOutputFolderAsync(string? initialPath = null) => Task.FromResult<string?>(null);
        public Task<string?> PickVideoFileAsync() => Task.FromResult<string?>(null);
        public Task<string?> PickAudioFileAsync() => Task.FromResult<string?>(null);
        public Task<string?> PickMediaFileAsync() => Task.FromResult<string?>(null);
        public Task<string?> PickSceneExportFileAsync() => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> PickSceneImportFilesAsync() => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
