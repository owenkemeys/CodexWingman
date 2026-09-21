using System.Reflection;
using System.Text.Json;

namespace CodexWingman.Core;

public sealed record InjectionStatus(int Discovered, int Succeeded, int Failed)
{
    public static InjectionStatus None { get; } = new(0, 0, 0);
}

public sealed class InjectionScriptBuilder
{
    private const string ResourceName = "CodexWingman.Core.Injection.usage-dials.mjs";
    private const string Placeholder = "__CODEX_HELPER_SNAPSHOT__";
    private readonly string source;

    public InjectionScriptBuilder()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {ResourceName}");
        using var reader = new StreamReader(stream);
        source = reader.ReadToEnd();
    }

    public string Build(QuotaSnapshot snapshot, DateTimeOffset now) => source.Replace(
        Placeholder,
        JsonSerializer.Serialize(new
        {
            primary = ToWire(snapshot.Primary, now),
            secondary = ToWire(snapshot.Secondary, now),
        }),
        StringComparison.Ordinal);

    private static object? ToWire(QuotaWindow? window, DateTimeOffset now)
    {
        if (window is null) return null;
        return new
        {
            usedPercent = window.UsedPercent,
            windowMinutes = window.WindowMinutes,
            resetsAtUnixSeconds = window.ResetsAtUnixSeconds,
            state = QuotaPolicy.Evaluate(window, now).ToString().ToLowerInvariant(),
            resetLabel = window.ResetsAtUnixSeconds is { } seconds
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("ddd h:mm tt")
                : null,
        };
    }
}

public interface ICodexInjectionController
{
    Task<InjectionStatus> InjectAllAsync(QuotaSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<InjectionStatus> RemoveAllAsync(CancellationToken cancellationToken = default);
}

public sealed class CodexInjectionController(
    ICodexTargetSource targetSource,
    ICdpEvaluator evaluator,
    InjectionScriptBuilder scriptBuilder) : ICodexInjectionController
{
    public async Task<InjectionStatus> InjectAllAsync(QuotaSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var targets = await targetSource.ListAsync(cancellationToken);
        var expression = scriptBuilder.Build(snapshot, DateTimeOffset.Now);
        return await BroadcastAsync(targets, expression, cancellationToken);
    }

    public async Task<InjectionStatus> RemoveAllAsync(CancellationToken cancellationToken = default)
    {
        var targets = await targetSource.ListAsync(cancellationToken);
        return await BroadcastAsync(targets, "window.__codexHelperUsageDials?.cleanup?.() ?? false", cancellationToken);
    }

    private async Task<InjectionStatus> BroadcastAsync(
        IReadOnlyList<CodexTarget> targets,
        string expression,
        CancellationToken cancellationToken)
    {
        var succeeded = 0;
        var failed = 0;
        foreach (var target in targets)
        {
            try
            {
                await evaluator.EvaluateAsync(target, expression, cancellationToken);
                succeeded++;
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
            }
        }
        return new(targets.Count, succeeded, failed);
    }
}
