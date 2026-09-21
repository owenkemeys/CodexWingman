using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
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
    private const string AppUserModelId = "OpenAI.Codex_2p2nqsd0c76g0!App";
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
        var processIds = GetCodexProcesses().Select(process =>
        {
            try { return process.Id; }
            finally { process.Dispose(); }
        }).ToHashSet();
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
        var processes = GetCodexProcesses();
        var closeRequestedProcessIds = new List<int>();
        try
        {
            foreach (var process in processes.Where(HasMainWindow))
            {
                try
                {
                    if (process.CloseMainWindow())
                        closeRequestedProcessIds.Add(process.Id);
                }
                catch { }
            }

            if (closeRequestedProcessIds.Count == 0)
                throw new InvalidOperationException(
                    "Codex is running, but no closable GUI process was found. Background Codex/ChatGPT children remain untouched; Wingman did not terminate anything.");

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = GetCodexProcesses();
                try
                {
                    if (remaining.Length == 0) return;
                }
                finally
                {
                    foreach (var process in remaining) process.Dispose();
                }
                await Task.Delay(250, cancellationToken);
            }

            var remainingIds = GetCodexProcesses();
            var remainingDescription = string.Join(", ", remainingIds.Select(process => $"{process.ProcessName} ({process.Id})"));
            foreach (var process in remainingIds) process.Dispose();
            throw new TimeoutException(
                $"Codex process(es) {remainingDescription} remained after the graceful close request. Close the remaining Codex taskbar/background process, then retry; Wingman did not terminate anything.");
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static bool HasMainWindow(Process process)
    {
        try { return process.MainWindowHandle != IntPtr.Zero; }
        catch { return false; }
    }

    private static bool IsCodexRunning()
    {
        foreach (var processName in CodexLaunchPolicy.ProcessNames)
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                if (processes.Length > 0) return true;
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }
        return false;
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
