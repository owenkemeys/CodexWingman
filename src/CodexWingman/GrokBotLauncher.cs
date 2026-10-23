using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using CodexWingman.Core;

namespace CodexWingman;

/// <summary>
/// Relaunches Grok Bot once with a localhost-only debugging port. Grok Bot keeps its own user data,
/// so its sign-in is untouched. Wingman never edits Grok Bot files, never kills it, and never calls
/// its servers; a relaunch is always asked first.
/// </summary>
internal sealed class GrokBotLauncher(string wingmanDataRoot, Func<IEnumerable<int>> reservedPorts)
{
    private static readonly TimeSpan EndpointTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(20);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };
    public string RuntimePath { get; } = GrokBotLaunchPolicy.RuntimePath(wingmanDataRoot);
    public string Executable { get; } = GrokBotLaunchPolicy.ExecutablePath(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    public string UserData { get; } = GrokBotLaunchPolicy.UserDataDirectory(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    public bool IsInstalled() => File.Exists(Executable);

    public int? Port() => AppRuntimePortFile.Read(RuntimePath);

    public async Task<IReadOnlyList<CodexTarget>> PagesAsync(CancellationToken cancellationToken = default)
    {
        if (Port() is not { } port) return [];
        try { return await new GrokBotTargetSource(Http, () => port).ListAsync(cancellationToken); }
        catch (HttpRequestException) { return []; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return []; }
    }

    public int OpenWindowCount()
    {
        var processes = Running();
        try { return processes.Count(process => SafeMainWindow(process) != IntPtr.Zero); }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    /// <summary>
    /// Already hooked: bring Grok Bot forward. Not running: start it with the debugging port.
    /// Running without the port: ask, close it gracefully, and start it again with the same user data.
    /// </summary>
    public async Task<bool> OpenAsync(Func<bool> confirmRelaunch, CancellationToken cancellationToken)
    {
        if (!IsInstalled()) throw new FileNotFoundException("Grok Bot is not installed in its usual folder.");
        if ((await PagesAsync(cancellationToken)).Count > 0 && Running() is { Length: > 0 } hooked)
        {
            try { foreach (var process in hooked) if (SafeMainWindow(process) is var window && window != IntPtr.Zero) SetForegroundWindow(window); }
            finally { foreach (var process in hooked) process.Dispose(); }
            return true;
        }
        var running = Running();
        try
        {
            if (running.Length > 0)
            {
                if (!confirmRelaunch()) return false;
                foreach (var process in running)
                {
                    try { if (SafeMainWindow(process) != IntPtr.Zero) process.CloseMainWindow(); }
                    catch (InvalidOperationException) { }
                }
            }
        }
        finally { foreach (var process in running) process.Dispose(); }

        var closeDeadline = DateTime.UtcNow + CloseTimeout;
        while (IsRunning())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTime.UtcNow > closeDeadline)
                throw new TimeoutException("Grok Bot did not close, so Wingman left it running. Close it yourself, then use Open Grok Bot with Wingman again.");
            await Task.Delay(250, cancellationToken);
        }

        var port = SelectPort(Port());
        var start = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Executable)!,
        };
        foreach (var argument in GrokBotLaunchPolicy.Arguments(port)) start.ArgumentList.Add(argument);
        using (Process.Start(start) ?? throw new InvalidOperationException("Grok Bot did not start.")) { }
        AppRuntimePortFile.Write(RuntimePath, port);
        var deadline = DateTime.UtcNow + EndpointTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((await PagesAsync(cancellationToken)).Count > 0) return true;
            await Task.Delay(500, cancellationToken);
        }
        throw new TimeoutException("Grok Bot started but did not expose its window to Wingman on the local port.");
    }

    private bool IsRunning()
    {
        var processes = Running();
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private Process[] Running()
    {
        using var current = Process.GetCurrentProcess();
        var result = new List<Process>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Executable)))
        {
            try
            {
                if (process.SessionId == current.SessionId
                    && string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? string.Empty), Path.GetFullPath(Executable), StringComparison.OrdinalIgnoreCase))
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

    private static IntPtr SafeMainWindow(Process process)
    {
        try { return process.MainWindowHandle; }
        catch (InvalidOperationException) { return IntPtr.Zero; }
    }

    private int SelectPort(int? remembered)
    {
        foreach (var port in GrokBotLaunchPolicy.CandidatePorts(remembered, reservedPorts().Where(port => port != remembered)))
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return port;
            }
            catch (SocketException) { }
        }
        throw new IOException("No free local port was available for Grok Bot.");
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
