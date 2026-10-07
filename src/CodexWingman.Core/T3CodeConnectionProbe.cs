namespace CodexWingman.Core;

public sealed record T3CodeConnectionReport(int Discovered, int Connected, int Failed);

public sealed class T3CodeConnectionProbe(ICodexTargetSource targetSource, ICdpEvaluator evaluator)
{
    public async Task<T3CodeConnectionReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var targets = T3CodeTargetCatalog.SelectPages(await targetSource.ListAsync(cancellationToken));
        var connected = 0;
        var failed = 0;
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await evaluator.EvaluateAsync(target, "void 0", cancellationToken);
                connected++;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
            }
        }
        return new(targets.Count, connected, failed);
    }
}
