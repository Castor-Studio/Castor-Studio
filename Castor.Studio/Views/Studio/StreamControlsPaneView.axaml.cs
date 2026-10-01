using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using CastorApplication.ViewModels.Studio;

namespace CastorApplication.Views.Studio;

public partial class StreamControlsPaneView : UserControl
{
    private StudioViewModel? _viewModel;

    public StreamControlsPaneView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel != null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as StudioViewModel;
        if (_viewModel != null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    // ENREGISTRER starts straight away, so a failure has no panel to land in: the pane's
    // attached flyout shows it above the buttons, and goes away once the error is cleared.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StudioViewModel.RecordError) || _viewModel == null) return;

        var flyout = FlyoutBase.GetAttachedFlyout(PaneRoot);
        if (flyout == null) return;

        if (string.IsNullOrEmpty(_viewModel.RecordError)) flyout.Hide();
        else if (PaneRoot.IsEffectivelyVisible && TopLevel.GetTopLevel(PaneRoot) != null)
            FlyoutBase.ShowAttachedFlyout(PaneRoot);
    }
}
