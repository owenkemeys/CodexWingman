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
internal sealed class GrokBotLauncher(string wingmanDataRoot, Func<IEnumerable<int>> reservedPorts) : IGrokBotFacts
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
        try { return await new GrokBotTargetSource(Http, () => port, CdpConnectionPool.Shared).ListAsync(cancellationToken); }
        catch (HttpRequestException) { return []; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return []; }
    }

    /// <summary>The Grok Bot process that owns a window (null when none): one Grok Bot run, for the reconnect prompt.</summary>
    public int? WindowProcessId()
    {
        var processes = Running();
        try { return processes.Where(process => SafeMainWindow(process) != IntPtr.Zero).Select(process => (int?)process.Id).Min(); }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    /// <summary>How long since the last keyboard or mouse input on this session (zero when Windows can't say).</summary>
    public static TimeSpan UserIdle()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    /// <summary>Grok Bot.exe's file version (null when unreadable); a change proves Grok Bot updated.</summary>
    public string? ExecutableVersion()
    {
        try { return File.Exists(Executable) ? FileVersionInfo.GetVersionInfo(Executable).FileVersion : null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// A process's command line (null when Windows won't say), read with limited query rights only; used to see whether
    /// Grok Bot's installer started it with --updated. Reads; never writes to or signals the process.
    /// </summary>
    public string? CommandLine(int processId)
    {
        const uint QueryLimitedInformation = 0x1000;
        const int ProcessCommandLineInformation = 60;
        const int InfoLengthMismatch = unchecked((int)0xC0000004), BufferTooSmall = unchecked((int)0xC0000023);
        var handle = OpenProcess(QueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var size = 2048;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    var status = NtQueryInformationProcess(handle, ProcessCommandLineInformation, buffer, size, out var needed);
                    if (status == 0)
                    {
                        // UNICODE_STRING: ushort Length (bytes), ushort MaximumLength, then the pointer at pointer alignment.
                        var length = (ushort)Marshal.ReadInt16(buffer);
                        var text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                        return text == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(text, length / 2);
                    }
                    if (status is not (InfoLengthMismatch or BufferTooSmall)) return null;
                    size = Math.Max(needed, size * 2);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            return null;
        }
        finally { CloseHandle(handle); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, IntPtr information, int length, out int returnLength);

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
            try { foreach (var process in hooked) if (SafeMainWindow(process) is var window && window != IntPtr.Zero) Core.Helpers.WindowsForegroundActivator.ActivateWindow(window); }
            finally { foreach (var process in hooked) process.Dispose(); }
            return true;
        }
        var running = Running();
        try
        {
            if (running.Length > 0)
            {
                if (!confirmRelaunch()) return false;
                WingmanLog.Write("grok-bot-close", ("processes", running.Length), ("forced", false));
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
                throw new TimeoutException("Grok Bot did not close, so Wingman left it running. Close it yourself, then choose Open window in the Grok Bot menu again.");
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
        WingmanLog.Write("grok-bot-start", ("port", port));
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
        // Session and image path are asked per process id: cheaper than Process.GetCurrentProcess().SessionId
        // (a machine-wide snapshot) and MainModule (a module-list walk) on this 2-second status path.
        var currentSession = WindowProcessInfo.CurrentSession();
        var executable = Path.GetFullPath(Executable);
        var result = new List<Process>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Executable)))
        {
            try
            {
                if (process.SessionId == currentSession
                    && WindowProcessInfo.ImagePathOf((uint)process.Id) is { Length: > 0 } image
                    && string.Equals(Path.GetFullPath(image), executable, StringComparison.OrdinalIgnoreCase))
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
}
