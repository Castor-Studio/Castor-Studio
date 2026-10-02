using CastorApplication.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels;

public enum StatusMessageKind
{
    None,
    Info,
    Error
}

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
    [NotifyPropertyChangedFor(nameof(IsError))]
    private StatusMessageKind _kind;

    public bool HasText => Text.Length > 0;
    public bool IsError => Kind == StatusMessageKind.Error;

    internal StatusMessage(IStatusMessageTimer timer, TimeSpan infoLifetime)
    {
        _timer = timer;
        _infoLifetime = infoLifetime;
    }

    public void ShowInfo(string text)
    {
        Show(text, StatusMessageKind.Info);
        if (HasText) _expiry = _timer.Schedule(_infoLifetime, Clear);
    }

    public void ShowError(string text) => Show(text, StatusMessageKind.Error);

    public void Clear() => Show("", StatusMessageKind.None);

    [RelayCommand]
    private void Dismiss() => Clear();

    private void Show(string text, StatusMessageKind kind)
    {
        _expiry?.Dispose();
        _expiry = null;
        Text = text ?? "";
        Kind = Text.Length == 0 ? StatusMessageKind.None : kind;
    }
}
