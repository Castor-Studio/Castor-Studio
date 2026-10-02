using Avalonia.Threading;
using CastorApplication.ViewModels;

namespace CastorApplication.Services;

// Runs a message's expiry. The app goes through the UI dispatcher; tests advance time by hand.
public interface IStatusMessageTimer
{
    IDisposable Schedule(TimeSpan delay, Action action);
}

internal sealed class DispatcherStatusMessageTimer : IStatusMessageTimer
{
    public IDisposable Schedule(TimeSpan delay, Action action) => DispatcherTimer.RunOnce(action, delay);
}

// Lifetime of the status and error lines shown next to actions. Every line comes from here so
// they all follow the same rules: a confirmation goes away by itself after a few seconds, an
// error stays until it is dismissed, and changing page clears them all — an old message must
// never pass for a current one.
public sealed class StatusMessageService
{
    public static readonly TimeSpan InfoLifetime = TimeSpan.FromSeconds(5);

    private readonly IStatusMessageTimer _timer;
    // Weak: a dialog's messages die with the dialog, nothing has to unregister them.
    private readonly List<WeakReference<StatusMessage>> _messages = [];

    public StatusMessageService(IStatusMessageTimer? timer = null)
    {
        _timer = timer ?? new DispatcherStatusMessageTimer();
    }

    public StatusMessage Create()
    {
        _messages.RemoveAll(reference => !reference.TryGetTarget(out _));
        var message = new StatusMessage(_timer, InfoLifetime);
        _messages.Add(new WeakReference<StatusMessage>(message));
        return message;
    }

    public void ClearAll()
    {
        foreach (var reference in _messages)
            if (reference.TryGetTarget(out var message)) message.Clear();
    }
}
