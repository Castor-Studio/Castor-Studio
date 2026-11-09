using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using CastorApplication.ViewModels;
using CastorApplication.ViewModels.Studio;

namespace CastorApplication.Views.Studio;

public partial class StreamControlsPaneView : UserControl
{
    private StudioViewModel? _viewModel;

    public StreamControlsPaneView()
    {
        InitializeComponent();

        // Closing a flyout that shows an error is dismissing it: the error must not be waiting
        // there the next time the flyout opens, long after the failure.
        if (FlyoutBase.GetAttachedFlyout(PaneRoot) is { } recordFlyout)
            recordFlyout.Closed += (_, _) => _viewModel?.RecordError.Clear();
        // Once on air, an error left in the go-live panel moves to the list of lives instead.
        if (GoLiveButton.Flyout is PopupFlyoutBase goLiveFlyout)
        {
            // Opens on the settings as they are now, whatever was typed and left unstarted before.
            goLiveFlyout.Opening += (_, _) => _viewModel?.ResetGoLivePanel();
            goLiveFlyout.Closed += (_, _) =>
            {
                if (_viewModel is { IsStreaming: false }) _viewModel.StreamError.Clear();
            };
        }
        if (LivePlatformsButton.Flyout is { } livesFlyout)
            livesFlyout.Closed += (_, _) => _viewModel?.StreamError.Clear();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel != null)
        {
            _viewModel.RecordError.PropertyChanged -= OnRecordErrorChanged;
            _viewModel.StreamError.PropertyChanged -= OnStreamErrorChanged;
        }
        _viewModel = DataContext as StudioViewModel;
        if (_viewModel != null)
        {
            _viewModel.RecordError.PropertyChanged += OnRecordErrorChanged;
            _viewModel.StreamError.PropertyChanged += OnStreamErrorChanged;
        }
    }

    // While on air the go-live panel is gone: a platform that fails or drops opens the list of
    // lives, which says what failed above the platforms still live.
    private void OnStreamErrorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StatusMessage.Text) || _viewModel is not { IsStreaming: true }) return;
        if (!_viewModel.StreamError.HasText) return;

        GoLiveButton.Flyout?.Hide();
        if (LivePlatformsButton.IsEffectivelyVisible && TopLevel.GetTopLevel(LivePlatformsButton) != null)
            LivePlatformsButton.Flyout?.ShowAt(LivePlatformsButton);
    }

    // ENREGISTRER starts straight away, so a failure has no panel to land in: the pane's
    // attached flyout shows it above the buttons, and goes away once the error is cleared.
    private void OnRecordErrorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StatusMessage.Text) || _viewModel == null) return;

        var flyout = FlyoutBase.GetAttachedFlyout(PaneRoot);
        if (flyout == null) return;

        if (!_viewModel.RecordError.HasText) flyout.Hide();
        else if (PaneRoot.IsEffectivelyVisible && TopLevel.GetTopLevel(PaneRoot) != null)
            FlyoutBase.ShowAttachedFlyout(PaneRoot);
    }
}
