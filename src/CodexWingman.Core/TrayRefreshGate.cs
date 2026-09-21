namespace CodexWingman.Core;

public sealed class TrayRefreshGate
{
    private int active;

    public bool TryEnter(bool busy, bool exiting)
    {
        if (busy || exiting) return false;
        return Interlocked.CompareExchange(ref active, 1, 0) == 0;
    }

    public void Exit() => Volatile.Write(ref active, 0);
}
