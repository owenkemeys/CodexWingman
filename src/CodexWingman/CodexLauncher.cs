using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CodexWingman.Core;

namespace CodexWingman;

internal enum HookReadyAcquisitionMode
{
    ExistingEndpoint,
    Launched,
    RestartRequired,
}

internal sealed record HookReadyAcquisitionResult(int? Port, HookReadyAcquisitionMode Mode);

internal static class CodexLauncher
{
    private const string AppUserModelId = CodexLaunchPolicy.PackageFamilyName + "!App";
    private static readonly TimeSpan EndpointTimeout = TimeSpan.FromSeconds(30);

    public static async Task<int> EnsureRunningWithDebuggingAsync(
        string runtimeStatePath,
        int? preferredPort = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var port in CandidatePorts(runtimeStatePath, preferredPort))
        {
            if (await IsEndpointReadyAsync(port, cancellationToken))
            {
                CodexRuntimeStateStore.SavePort(runtimeStatePath, port);
                return port;
            }
        }

        if (IsCodexRunning())
            throw new InvalidOperationException(
                "Codex is already running without a verified localhost helper endpoint. This may be a background-only process tree, so Wingman will not treat it as a recoverable GUI; use 'Restart Codex into hook-ready mode' to request a graceful restart.");

        return await LaunchWithDebuggingAsync(runtimeStatePath, preferredPort, cancellationToken);
    }

    public static async Task<int> RestartWithDebuggingAsync(
        string runtimeStatePath,
        int? preferredPort = null,
        CancellationToken cancellationToken = default)
    {
        await RequestGracefulCloseAsync(cancellationToken);
        return await LaunchWithDebuggingAsync(runtimeStatePath, preferredPort, cancellationToken);
    }

    public static async Task<HookReadyAcquisitionResult> OpenHookReadyWindowAsync(
        string runtimeStatePath,
        int? preferredPort = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var port in CandidatePorts(runtimeStatePath, preferredPort))
        {
            if (await IsEndpointReadyAsync(port, cancellationToken))
            {
                CodexRuntimeStateStore.SavePort(runtimeStatePath, port);
                return new(port, HookReadyAcquisitionMode.ExistingEndpoint);
            }
        }

        var plan = HookReadyWindowPolicy.Decide(endpointReady: false, codexRunning: IsCodexRunning());
        if (plan == HookReadyWindowPlan.ConfirmRestart)
            return new(null, HookReadyAcquisitionMode.RestartRequired);

        var selectedPort = await LaunchWithDebuggingAsync(runtimeStatePath, preferredPort, cancellationToken);
        return new(selectedPort, HookReadyAcquisitionMode.Launched);
    }

    public static async Task<CodexHookState> GetHookStatusAsync(
        string runtimeStatePath,
        int? preferredPort = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var port in CandidatePorts(runtimeStatePath, preferredPort))
        {
            if (await IsEndpointReadyAsync(port, cancellationToken))
                return CodexHookState.Hooked;
        }

        return IsCodexRunning()
            ? CodexHookState.OpenButCannotHook
            : CodexHookState.NotOpen;
    }

    public static int GetOpenWindowCount()
    {
        using var wingmanProcess = Process.GetCurrentProcess();
        var currentSessionId = wingmanProcess.SessionId;
        var processes = GetCodexProcesses();
        var processIds = new HashSet<int>();
        try
        {
            foreach (var process in processes)
                if (CodexShutdownPolicy.IsAppRunning([Snapshot(process)], currentSessionId))
                    processIds.Add(process.Id);
        }
        finally { foreach (var process in processes) process.Dispose(); }
        if (processIds.Count == 0) return 0;

        var count = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var processId);
            if (processIds.Contains((int)processId)
                && IsWindowVisible(window)
                && GetWindow(window, GetWindowCommand.Owner) == IntPtr.Zero
                && GetWindowTextLength(window) > 0)
            {
                count++;
            }

            return true;
        }, IntPtr.Zero);
        return count;
    }

    private static IEnumerable<int> CandidatePorts(string runtimeStatePath, int? preferredPort)
    {
        if (preferredPort is { } requestedPort) yield return requestedPort;
        var remembered = CodexRuntimeStateStore.LoadPort(runtimeStatePath);
        if (remembered is { } rememberedPort && rememberedPort != preferredPort) yield return rememberedPort;
        if (preferredPort != CodexLaunchPolicy.PreferredPort && remembered != CodexLaunchPolicy.PreferredPort)
            yield return CodexLaunchPolicy.PreferredPort;
    }

    private static async Task<int> LaunchWithDebuggingAsync(
        string runtimeStatePath,
        int? preferredPort,
        CancellationToken cancellationToken)
    {
        var port = CodexLaunchPolicy.FindAvailablePort(preferredPort ?? CodexLaunchPolicy.PreferredPort);
        var settings = HelperSettingsStore.Load();
        var manager = (IApplicationActivationManager)new ApplicationActivationManager();
        var result = manager.ActivateApplication(
            AppUserModelId,
            CodexLaunchPolicy.DebugArguments(port, settings.ForceHighPerformanceGpu),
            ActivateOptions.None,
            out _);
        Marshal.ThrowExceptionForHR(result);

        if (!await WaitForEndpointAsync(port, cancellationToken))
            throw new TimeoutException($"Codex did not expose localhost CDP on port {port}");

        CodexRuntimeStateStore.SavePort(runtimeStatePath, port);
        return port;
    }

    private static async Task<bool> WaitForEndpointAsync(int port, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + EndpointTimeout;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        while (DateTime.UtcNow < deadline)
        {
            if (await IsEndpointReadyAsync(port, cancellationToken, http)) return true;
            await Task.Delay(250, cancellationToken);
        }
        return false;
    }

    private static async Task<bool> IsEndpointReadyAsync(
        int port,
        CancellationToken cancellationToken,
        HttpClient? http = null)
    {
        try
        {
            using var ownedHttp = http is null ? new HttpClient { Timeout = TimeSpan.FromSeconds(1) } : null;
            var client = http ?? ownedHttp!;
            var targets = await new HttpCodexTargetSource(client, port).ListAsync(cancellationToken);
            return targets.Count > 0;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }

    private static async Task RequestGracefulCloseAsync(CancellationToken cancellationToken)
    {
        using var wingmanProcess = Process.GetCurrentProcess();
        var currentSessionId = wingmanProcess.SessionId;
        var processes = GetCodexProcesses();
        string executablePath;
        try
        {
            var snapshots = processes.Select(Snapshot).ToArray();
            executablePath = CodexShutdownPolicy.SelectExecutablePath(snapshots, currentSessionId);
            var targetIds = CodexShutdownPolicy.SelectProcessIds(snapshots, currentSessionId);
            foreach (var process in processes.Where(process => targetIds.Contains(process.Id) && HasMainWindow(process)))
            {
                try { process.CloseMainWindow(); }
                catch { }
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }

        if (await WaitForAppExitAsync(currentSessionId, TimeSpan.FromSeconds(5), cancellationToken))
            return;

        var remaining = GetAppProcesses(currentSessionId);
        try
        {
            foreach (var process in remaining)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { process.Kill(); }
                catch (InvalidOperationException) { } // The process exited after the snapshot.
            }
        }
        finally
        {
            foreach (var process in remaining) process.Dispose();
        }

        if (await WaitForAppExitAsync(currentSessionId, TimeSpan.FromSeconds(10), cancellationToken))
            return;

        remaining = GetAppProcesses(currentSessionId);
        try
        {
            throw new TimeoutException(
                $"Codex package processes remained after the restart request for {executablePath}: "
                + string.Join(", ", remaining.Select(process => process.Id)));
        }
        finally
        {
            foreach (var process in remaining) process.Dispose();
        }
    }

    private static CodexProcessSnapshot Snapshot(Process process)
    {
        int sessionId;
        string? executablePath;
        try { sessionId = process.SessionId; }
        catch { sessionId = -1; }
        try { executablePath = process.MainModule?.FileName; }
        catch { executablePath = null; }
        return new(process.Id, process.ProcessName, sessionId, executablePath,
            TryGetPackageFamilyName(process.Id), HasMainWindow(process));
    }

    private static Process[] GetAppProcesses(int currentSessionId)
    {
        var processes = GetCodexProcesses();
        var snapshots = processes.Select(Snapshot).ToArray();
        if (snapshots.Any(process => process.SessionId == currentSessionId
            && string.Equals(process.PackageFamilyName, CodexLaunchPolicy.PackageFamilyName,
                StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(process.ExecutablePath)))
        {
            foreach (var process in processes) process.Dispose();
            throw new InvalidOperationException(
                "A Codex process remains but Wingman cannot verify its executable. Restart stopped before another app could be affected.");
        }
        var targetIds = CodexShutdownPolicy.SelectProcessIds(snapshots, currentSessionId);
        foreach (var process in processes.Where(process => !targetIds.Contains(process.Id))) process.Dispose();
        return processes.Where(process => targetIds.Contains(process.Id)).ToArray();
    }

    private static async Task<bool> WaitForAppExitAsync(
        int currentSessionId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = GetAppProcesses(currentSessionId);
            try { if (remaining.Length == 0) return true; }
            finally { foreach (var process in remaining) process.Dispose(); }
            await Task.Delay(250, cancellationToken);
        } while (DateTime.UtcNow < deadline);
        return false;
    }

    private static string? TryGetPackageFamilyName(int processId)
    {
        const uint processQueryLimitedInformation = 0x1000;
        const int errorInsufficientBuffer = 122;
        var handle = OpenProcess(processQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            uint length = 0;
            if (GetPackageFamilyName(handle, ref length, null) != errorInsufficientBuffer || length == 0)
                return null;
            var name = new StringBuilder((int)length);
            return GetPackageFamilyName(handle, ref length, name) == 0 ? name.ToString() : null;
        }
        finally { CloseHandle(handle); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(IntPtr process, ref uint length, StringBuilder? familyName);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    private static bool HasMainWindow(Process process)
    {
        try { if (process.MainWindowHandle != IntPtr.Zero) return true; }
        catch { }
        var found = false;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var processId);
            if (processId == process.Id
                && IsWindowVisible(window)
                && GetWindow(window, GetWindowCommand.Owner) == IntPtr.Zero
                && GetWindowTextLength(window) > 0)
            {
                found = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static bool IsCodexRunning()
    {
        using var wingmanProcess = Process.GetCurrentProcess();
        var processes = GetCodexProcesses();
        try
        {
            return CodexShutdownPolicy.IsAppRunning(
                processes.Select(Snapshot), wingmanProcess.SessionId);
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static Process[] GetCodexProcesses() => CodexLaunchPolicy.ProcessNames
        .SelectMany(Process.GetProcessesByName)
        .GroupBy(process => process.Id)
        .Select(group => group.First())
        .ToArray();

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    private enum GetWindowCommand : uint { Owner = 4 }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, GetWindowCommand command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [Flags]
    private enum ActivateOptions : uint { None = 0 }

    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments,
            ActivateOptions options,
            out uint processId);
    }

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private class ApplicationActivationManager { }
}
