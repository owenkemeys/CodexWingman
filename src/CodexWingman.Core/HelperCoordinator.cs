using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace CodexWingman.Core;

public static class CodexLaunchPolicy
{
    public const int PreferredPort = 9223;
    public static IReadOnlyList<string> ProcessNames { get; } = ["codex", "ChatGPT"];

    public static bool ShouldLaunchAtStartup(bool isCodexRunning) => !isCodexRunning;

    public static string DebugArguments(int port, bool forceHighPerformanceGpu = false)
    {
        var arguments = $"--remote-debugging-address=127.0.0.1 --remote-debugging-port={port}";
        return forceHighPerformanceGpu
            ? $"{arguments} --force_high_performance_gpu"
            : arguments;
    }

    public static int? TryGetPortOverride(IEnumerable<string> arguments)
    {
        var argument = arguments.FirstOrDefault(value => value.StartsWith("--cdp-port=", StringComparison.Ordinal));
        if (argument is null) return null;
        var text = argument["--cdp-port=".Length..];
        if (!int.TryParse(text, out var port) || port is < 1 or > 65535)
            throw new ArgumentException($"Invalid CDP port override '{text}'", nameof(arguments));
        return port;
    }

    public static int FindAvailablePort(int preferredPort = PreferredPort, int maxAttempts = 100)
    {
        if (preferredPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(preferredPort));
        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));

        for (var offset = 0; offset < maxAttempts; offset++)
        {
            var port = preferredPort + offset;
            if (port > 65535) break;
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return port;
            }
            catch (SocketException)
            {
                // A stale or unrelated listener is not a reason to fail the launch.
            }
        }

        throw new InvalidOperationException($"No available localhost CDP port found in {preferredPort}-{preferredPort + maxAttempts - 1}");
    }
}

public static class CodexRuntimeStateStore
{
    public static int? LoadPort(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("port", out var portValue)
                || !portValue.TryGetInt32(out var port)
                || port is < 1 or > 65535)
                return null;
            return port;
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    public static void SavePort(string path, int port)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new { port }));
        File.Move(temporaryPath, path, overwrite: true);
    }
}

public sealed class HelperCoordinator(IQuotaSource quotaSource, ICodexInjectionController injectionController)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public InjectionStatus LastStatus { get; private set; } = InjectionStatus.None;

    public async Task<InjectionStatus> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await quotaSource.ReadAsync(cancellationToken);
            LastStatus = await injectionController.InjectAllAsync(snapshot, cancellationToken);
            return LastStatus;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<InjectionStatus> RemoveAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            LastStatus = await injectionController.RemoveAllAsync(cancellationToken);
            return LastStatus;
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<InjectionStatus> ShutdownAsync(CancellationToken cancellationToken = default) => RemoveAsync(cancellationToken);
}
