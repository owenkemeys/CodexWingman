using System.Diagnostics;
using CodexWingman.Core;

namespace CodexWingman;

internal static class T3CodeLauncher
{
    private static readonly TimeSpan EndpointTimeout = TimeSpan.FromSeconds(30);

    public static bool IsInstalled() => FindExecutable() is not null;

    public static async Task<CodexHookState> GetHookStatusAsync(
        string runtimeStatePath, int? codexPort = null, CancellationToken cancellationToken = default)
    {
        var executable = FindExecutable();
        if (executable is null) return CodexHookState.NotOpen;
        if (!IsRunning(executable)) return CodexHookState.NotOpen;
        foreach (var port in T3CodeLaunchPolicy.CandidatePorts(CodexRuntimeStateStore.LoadPort(runtimeStatePath), codexPort))
        {
            if (await HasT3PagesAsync(port, cancellationToken))
                return CodexHookState.Hooked;
        }
        return IsRunning(executable) ? CodexHookState.OpenButCannotHook : CodexHookState.NotOpen;
    }

    public static async Task<HookReadyAcquisitionResult> OpenHookReadyWindowAsync(
        string runtimeStatePath, int? codexPort = null, CancellationToken cancellationToken = default)
    {
        var executable = FindExecutable()
            ?? throw new FileNotFoundException("T3 Code is not installed in a recognized location.");
        var remembered = CodexRuntimeStateStore.LoadPort(runtimeStatePath);
        foreach (var port in IsRunning(executable)
            ? T3CodeLaunchPolicy.CandidatePorts(remembered, codexPort) : [])
        {
            if (!await HasT3PagesAsync(port, cancellationToken)) continue;
            var previousCount = await CountT3PagesAsync(port, cancellationToken);
            Start(executable, port, newWindow: true);
            if (!await WaitForPagesAsync(port, previousCount + 1, cancellationToken))
                throw new TimeoutException("T3 Code did not expose a new hooked window.");
            CodexRuntimeStateStore.SavePort(runtimeStatePath, port);
            return new(port, HookReadyAcquisitionMode.ExistingEndpoint);
        }
        if (IsRunning(executable))
            return new(null, HookReadyAcquisitionMode.RestartRequired);
        var selectedPort = await LaunchAsync(executable, runtimeStatePath, cancellationToken);
        return new(selectedPort, HookReadyAcquisitionMode.Launched);
    }

    public static async Task<int> EnsureRunningWithDebuggingAsync(
        string runtimeStatePath, int? codexPort = null, CancellationToken cancellationToken = default)
    {
        var executable = FindExecutable()
            ?? throw new FileNotFoundException("T3 Code is not installed in a recognized location.");
        foreach (var port in IsRunning(executable)
            ? T3CodeLaunchPolicy.CandidatePorts(CodexRuntimeStateStore.LoadPort(runtimeStatePath), codexPort) : [])
        {
            if (!await HasT3PagesAsync(port, cancellationToken)) continue;
            CodexRuntimeStateStore.SavePort(runtimeStatePath, port);
            return port;
        }
        if (IsRunning(executable))
            throw new InvalidOperationException("T3 Code is already running without a verified Helper endpoint. Use Repair T3 Code after saving work.");
        return await LaunchAsync(executable, runtimeStatePath, cancellationToken);
    }

    public static async Task<int> RestartWithDebuggingAsync(
        string runtimeStatePath, CancellationToken cancellationToken = default)
    {
        var executable = FindExecutable()
            ?? throw new FileNotFoundException("T3 Code is not installed in a recognized location.");
        using var current = Process.GetCurrentProcess();
        var running = RunningProcesses(executable, current.SessionId);
        try
        {
            foreach (var process in running)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { if (process.MainWindowHandle != IntPtr.Zero) process.CloseMainWindow(); }
                catch (InvalidOperationException) { }
            }
        }
        finally { foreach (var process in running) process.Dispose(); }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsRunning(executable)) return await LaunchAsync(executable, runtimeStatePath, cancellationToken);
            await Task.Delay(250, cancellationToken);
        }
        throw new TimeoutException("T3 Code did not close. Wingman left its remaining processes running.");
    }

    private static string? FindExecutable()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var paths = T3CodeLaunchPolicy.CandidateExecutables(local, programs);
        using var current = Process.GetCurrentProcess();
        var running = new List<string>();
        foreach (var path in paths)
        {
            var processes = RunningProcesses(path, current.SessionId);
            if (processes.Length > 0) running.Add(path);
            foreach (var process in processes) process.Dispose();
        }
        return T3CodeLaunchPolicy.SelectExecutable(paths, running, File.Exists);
    }

    private static bool IsRunning(string executable)
    {
        using var current = Process.GetCurrentProcess();
        var matches = RunningProcesses(executable, current.SessionId);
        try { return matches.Length > 0; }
        finally { foreach (var process in matches) process.Dispose(); }
    }

    private static Process[] RunningProcesses(string executable, int sessionId)
    {
        var name = Path.GetFileNameWithoutExtension(executable);
        var result = new List<Process>();
        foreach (var process in Process.GetProcessesByName(name))
        {
            try
            {
                if (T3CodeLaunchPolicy.MatchesRunningProcess(
                    executable, sessionId, process.MainModule?.FileName, process.SessionId))
                {
                    result.Add(process);
                    continue;
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            process.Dispose();
        }
        return [.. result];
    }

    private static async Task<int> LaunchAsync(string executable, string runtimeStatePath, CancellationToken cancellationToken)
    {
        var port = CodexLaunchPolicy.FindAvailablePort(T3CodeLaunchPolicy.PreferredPort);
        Start(executable, port, newWindow: false);
        if (!await WaitForPagesAsync(port, 1, cancellationToken))
            throw new TimeoutException("T3 Code did not expose a localhost Helper endpoint.");
        CodexRuntimeStateStore.SavePort(runtimeStatePath, port);
        return port;
    }

    private static void Start(string executable, int port, bool newWindow)
    {
        var process = Process.Start(new ProcessStartInfo(executable, T3CodeLaunchPolicy.DebugArguments(port, newWindow))
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        }) ?? throw new InvalidOperationException("T3 Code did not start.");
        process.Dispose();
    }

    private static async Task<bool> WaitForPagesAsync(int port, int minimumCount, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + EndpointTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await CountT3PagesAsync(port, cancellationToken) >= minimumCount) return true;
            await Task.Delay(250, cancellationToken);
        }
        return false;
    }

    private static async Task<bool> HasT3PagesAsync(int port, CancellationToken cancellationToken) =>
        await CountT3PagesAsync(port, cancellationToken) > 0;

    private static async Task<int> CountT3PagesAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            return (await new T3CodeTargetSource(http, () => port).ListAsync(cancellationToken)).Count;
        }
        catch (HttpRequestException) { return 0; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return 0; }
    }
}
