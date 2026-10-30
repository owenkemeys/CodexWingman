namespace CodexWingman.Core;

/// <summary>
/// Windows shutting down or signing out, for the Grok Bot runs under way when it said so. Windows tells Wingman when a
/// shutdown begins (SessionEnding) but not when another app cancels it, so the news belongs to the runs it stopped: a run
/// begun later (a new Reconnect, Open Grok Bot, Open window or Repair) starts unmarked and never inherits a shutdown that
/// didn't happen.
/// </summary>
public sealed class GrokBotSessionWatch
{
    private readonly object sync = new();
    private readonly List<Run> active = [];

    /// <summary>One run's view: <see cref="Run.Ending"/> once Windows began to shut down or sign out during it.</summary>
    public sealed class Run : IDisposable
    {
        private readonly GrokBotSessionWatch owner;
        private volatile bool ending;
        internal Run(GrokBotSessionWatch owner) => this.owner = owner;
        public bool Ending => ending;
        internal void MarkEnding() => ending = true;
        public void Dispose() => owner.End(this);
    }

    public Run Begin()
    {
        var run = new Run(this);
        lock (sync) active.Add(run);
        return run;
    }

    /// <summary>Windows began to shut down or sign out: marks the runs under way now, and only those.</summary>
    public void SessionEnding()
    {
        lock (sync) foreach (var run in active) run.MarkEnding();
    }

    private void End(Run run)
    {
        lock (sync) active.Remove(run);
    }
}
