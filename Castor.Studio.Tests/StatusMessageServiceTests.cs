using CastorApplication.Services;

namespace Castor.Studio.Tests;

public sealed class StatusMessageServiceTests
{
    [Fact]
    public void Info_clears_itself_once_its_lifetime_is_over()
    {
        var timer = new ManualTimer();
        var message = new StatusMessageService(timer).Create();

        message.ShowInfo("3 scène(s) exportée(s).");

        Assert.Equal("3 scène(s) exportée(s).", message.Text);
        Assert.Equal(StatusMessageService.InfoLifetime, timer.LastDelay);
        timer.Elapse();
        Assert.False(message.HasText);
    }

    [Fact]
    public void Error_stays_until_dismissed()
    {
        var timer = new ManualTimer();
        var message = new StatusMessageService(timer).Create();

        message.ShowError("Export impossible : disque plein");

        Assert.True(message.IsError);
        Assert.Equal(0, timer.Pending);
        message.DismissCommand.Execute(null);
        Assert.Equal("", message.Text);
        Assert.False(message.IsError);
    }

    [Fact]
    public void A_newer_message_is_not_cleared_by_the_expiry_of_the_previous_one()
    {
        var timer = new ManualTimer();
        var message = new StatusMessageService(timer).Create();

        message.ShowInfo("1 scène(s) exportée(s).");
        message.ShowError("Import impossible : fichier illisible");
        timer.Elapse();

        Assert.Equal("Import impossible : fichier illisible", message.Text);
    }

    [Fact]
    public void Clear_all_empties_every_message_of_the_service()
    {
        var service = new StatusMessageService(new ManualTimer());
        var info = service.Create();
        var error = service.Create();
        info.ShowInfo("2 scène(s) importée(s).");
        error.ShowError("Connexion refusée");

        service.ClearAll();

        Assert.False(info.HasText);
        Assert.False(error.HasText);
    }

    private sealed class ManualTimer : IStatusMessageTimer
    {
        private readonly List<Entry> _scheduled = [];

        public TimeSpan LastDelay { get; private set; }
        public int Pending => _scheduled.Count(entry => !entry.Canceled);

        public IDisposable Schedule(TimeSpan delay, Action action)
        {
            LastDelay = delay;
            var entry = new Entry(action);
            _scheduled.Add(entry);
            return entry;
        }

        public void Elapse()
        {
            var due = _scheduled.Where(entry => !entry.Canceled).ToList();
            _scheduled.Clear();
            foreach (var entry in due) entry.Action();
        }

        private sealed class Entry(Action action) : IDisposable
        {
            public Action Action { get; } = action;
            public bool Canceled { get; private set; }
            public void Dispose() => Canceled = true;
        }
    }
}
