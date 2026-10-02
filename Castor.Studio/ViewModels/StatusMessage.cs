using CastorApplication.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels;

// One message line with its lifetime (see StatusMessageService): ShowInfo expires on its own,
// ShowError stays until Dismiss, Clear, or a newer message replaces it.
public sealed partial class StatusMessage : ObservableObject
{
    private readonly IStatusMessageTimer _timer;
    private readonly TimeSpan _infoLifetime;
    private IDisposable? _expiry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasText))]
    private string _text = "";

    [ObservableProperty]
    private bool _isError;

    public bool HasText => Text.Length > 0;

    internal StatusMessage(IStatusMessageTimer timer, TimeSpan infoLifetime)
    {
        _timer = timer;
        _infoLifetime = infoLifetime;
    }

    public void ShowInfo(string text)
    {
        Show(text, isError: false);
        if (HasText) _expiry = _timer.Schedule(_infoLifetime, Clear);
    }

    public void ShowError(string text) => Show(text, isError: true);

    public void Clear() => Show("", isError: false);

    [RelayCommand]
    private void Dismiss() => Clear();

    private void Show(string text, bool isError)
    {
        _expiry?.Dispose();
        _expiry = null;
        Text = text;
        IsError = isError && HasText;
    }
}
