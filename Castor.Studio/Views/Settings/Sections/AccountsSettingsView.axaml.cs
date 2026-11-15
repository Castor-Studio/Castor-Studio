using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using CastorApplication.ViewModels.Settings.Sections;

namespace CastorApplication.Views.Settings.Sections;

public partial class AccountsSettingsView : UserControl
{
    public AccountsSettingsView()
    {
        InitializeComponent();
    }

    // The clipboard belongs to the window, which the view model does not know.
    private async void OnCopyYouTubeCodeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AccountsSettingsViewModel { HasYouTubeUserCode: true } viewModel) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null) await clipboard.SetTextAsync(viewModel.YouTubeUserCode);
    }
}
