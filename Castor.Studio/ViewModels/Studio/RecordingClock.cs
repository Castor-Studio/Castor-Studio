namespace CastorApplication.ViewModels.Studio;

// Recorded time only: finished pauses add up, and the one in progress is taken off as it
// grows, so the displayed duration holds still while paused.
internal sealed class RecordingClock(TimeProvider timeProvider)
{
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _pausedSince;
    private TimeSpan _pausedTotal;

    public bool IsStarted => _startedAt != null;

    public void Start()
    {
        _startedAt ??= timeProvider.GetUtcNow();
    }

    public void SetPaused(bool paused)
    {
        var now = timeProvider.GetUtcNow();
        if (paused)
        {
            _pausedSince ??= now;
        }
        else if (_pausedSince is { } pausedSince)
        {
            _pausedTotal += now - pausedSince;
            _pausedSince = null;
        }
    }

    public void Reset()
    {
        _startedAt = null;
        _pausedSince = null;
        _pausedTotal = TimeSpan.Zero;
    }

    public string Text
    {
        get
        {
            if (_startedAt is not { } startedAt) return Format(TimeSpan.Zero);
            var now = timeProvider.GetUtcNow();
            var paused = _pausedTotal + (_pausedSince is { } pausedSince ? now - pausedSince : TimeSpan.Zero);
            return Format(now - startedAt - paused);
        }
    }

    private static string Format(TimeSpan elapsed) =>
        $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
}
