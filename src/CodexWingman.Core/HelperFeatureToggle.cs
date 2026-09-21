namespace CodexWingman.Core;

public sealed class HelperFeatureToggle(
    string name,
    bool enabled,
    Func<CancellationToken, Task> apply,
    Func<CancellationToken, Task> remove,
    Action<bool> persist)
{
    public string Name { get; } = name;
    public bool Enabled { get; private set; } = enabled;

    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (Enabled == enabled) return;
        if (enabled) await apply(cancellationToken);
        else await remove(cancellationToken);
        Enabled = enabled;
        persist(enabled);
    }
}
