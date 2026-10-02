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
        if (GoLiveButton.Flyout is { } goLiveFlyout)
            goLiveFlyout.Closed += (_, _) => _viewModel?.StreamError.Clear();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel != null) _viewModel.RecordError.PropertyChanged -= OnRecordErrorChanged;
        _viewModel = DataContext as StudioViewModel;
        if (_viewModel != null) _viewModel.RecordError.PropertyChanged += OnRecordErrorChanged;
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
