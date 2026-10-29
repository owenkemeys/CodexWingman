using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using CodexWingman.Core;

namespace CodexWingman;

/// <summary>
/// Relaunches Grok Bot once with a 127.0.0.1-only debugging port. Grok Bot keeps its own user data, so its sign-in is
/// untouched. Wingman never changes Grok Bot's program or data files, never kills it, and never calls its servers. It closes
/// Grok Bot only after the user says yes to Open window or Repair; for the reconnect prompt the user quits Grok Bot
/// themselves. (Its taskbar pin and Start menu shortcut are changed only by the user's own opt-in steps.)
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

    /// <summary>
    /// The port the opted-in Start menu shortcut asks Grok Bot for: the recorded Grok Bot port while it is still one Wingman
    /// may use for Grok Bot (it may be listening right now: that is Grok Bot), else a free one, recorded in grok-runtime.json
    /// so Wingman finds it after an update without having launched Grok Bot.
    /// </summary>
    public int ReservePortForShortcut()
    {
        lock (runtimeFile)
        {
            var remembered = Port();
            var port = remembered is { } value && GrokBotLaunchPolicy.CandidatePorts(value, reservedPorts().Where(item => item != value)).FirstOrDefault() == value
                ? value
                : SelectPort(null);
            if (port != remembered) AppRuntimePortFile.Write(RuntimePath, port);
            return port;
        }
    }

    /// <summary>
    /// Guards grok-runtime.json: the shortcut thread (<see cref="ReservePortForShortcut"/>) and the UI thread (adoption,
    /// <see cref="RecordPort"/>, and the start with the port) each read and write it under this lock.
    /// </summary>
    private readonly object runtimeFile = new();

    /// <summary>Records <paramref name="port"/> as Grok Bot's port (adoption). True when the file changed.</summary>
    public bool RecordPort(int port)
    {
        lock (runtimeFile)
        {
            if (port == Port()) return false;
            AppRuntimePortFile.Write(RuntimePath, port);
            return true;
        }
    }

    public async Task<IReadOnlyList<CodexTarget>> PagesAsync(CancellationToken cancellationToken = default)
    {
        if (Port() is not { } port) return [];
        try { return await new GrokBotTargetSource(Http, () => port, CdpConnectionPool.Shared).ListAsync(cancellationToken); }
        catch (HttpRequestException) { return []; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return []; }
    }

    /// <summary>How long since the last keyboard or mouse input on this session (zero when Windows can't say).</summary>
    public static TimeSpan UserIdle()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
    }

    /// <summary>A full-screen app, game or presentation is in front (Windows' notification state); the prompt waits.</summary>
    public static bool FullScreenInFront() =>
        SHQueryUserNotificationState(out var state) == 0 && GrokBotReconnectPolicy.FullScreen(state);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

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

    /// <summary>
    /// The id of the process that started <paramref name="processId"/> (PROCESS_BASIC_INFORMATION's
    /// InheritedFromUniqueProcessId), read with limited query rights; null when Windows won't say. Reads only.
    /// </summary>
    private static int? ParentProcessId(int processId)
    {
        const uint QueryLimitedInformation = 0x1000;
        const int ProcessBasicInformation = 0;
        var handle = OpenProcess(QueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return null;
        var size = 6 * IntPtr.Size;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (NtQueryInformationProcess(handle, ProcessBasicInformation, buffer, size, out _) != 0) return null;
            // ExitStatus, PebBaseAddress, AffinityMask, BasePriority, UniqueProcessId, InheritedFromUniqueProcessId: each pointer-sized.
            var parent = Marshal.ReadIntPtr(buffer, 5 * IntPtr.Size).ToInt64();
            return parent is > 0 and <= int.MaxValue ? (int)parent : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>
    /// Connected, on the same terms as adoption (GrokBotLaunchPolicy.Connected): the recorded <paramref name="port"/> listens
    /// where Wingman connects (127.0.0.1) and the socket belongs to the Grok Bot process <paramref name="grokProcess"/>.
    /// </summary>
    public static bool ConnectedTo(int? port, int? grokProcess) =>
        GrokBotLaunchPolicy.Connected(port, port is { } value && grokProcess is not null ? ListenerProcess(value) : null, grokProcess);

    /// <summary>
    /// The process listening on TCP <paramref name="port"/> where Wingman connects (IPv4 127.0.0.1 only, as for adoption);
    /// null when nothing does or Windows can't tell. All-address and IPv6 listeners don't count.
    /// </summary>
    public static int? ListenerProcess(int port)
    {
        var size = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidListener, 0);
        // The table can grow between the size query and the read; Windows then reports the new size, so try again.
        for (var attempt = 0; attempt < 4 && size > 0; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var read = GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidListener, 0);
                if (read == ErrorInsufficientBuffer) continue;
                if (read != 0) return null;
                return ListenerIn(buffer, port);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return null;
    }

    private const uint ErrorInsufficientBuffer = 122;

    private static int? ListenerIn(IntPtr buffer, int port)
    {
        var count = Marshal.ReadInt32(buffer);
        // MIB_TCPROW_OWNER_PID: state, local addr, local port, remote addr, remote port, pid (6 x 4 bytes).
        for (var index = 0; index < count; index++)
        {
            var row = buffer + 4 + index * 24;
            var address = new IPAddress(BitConverter.GetBytes(Marshal.ReadInt32(row + 4)));
            var networkPort = (ushort)Marshal.ReadInt32(row + 8);
            var local = new IPEndPoint(address, (ushort)IPAddress.NetworkToHostOrder((short)networkPort));
            if (GrokBotLaunchPolicy.ReachableByWingman(local, port)) return Marshal.ReadInt32(row + 20);
        }
        return null;
    }

    private const int AfInet = 2;
    private const int TcpTableOwnerPidListener = 3;

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sorted, int family, int tableClass, uint reserved);

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
        var stamp = ExecutableStamp();
        if ((await PagesAsync(cancellationToken)).Count > 0 && Running() is { Length: > 0 } hooked)
        {
            try { foreach (var process in hooked) if (SafeMainWindow(process) is var window && window != IntPtr.Zero) Core.Helpers.WindowsForegroundActivator.ActivateWindow(window); }
            finally { foreach (var process in hooked) process.Dispose(); }
            return true;
        }
        await ThrowIfUpdateUnderWayAsync(stamp);
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
        while (StillRunning())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTime.UtcNow > closeDeadline)
                throw new TimeoutException("Grok Bot did not close, so Wingman left it running. Close it yourself, then choose Open window in the Grok Bot menu again.");
            await Task.Delay(250, cancellationToken);
        }
        // Closing Grok Bot may start its staged update: the reconnect's installer guard, with nothing awaited after it.
        await ThrowIfUpdateUnderWayAsync(stamp);
        StartWithPort();
        await WaitForWindowAsync(cancellationToken);
        return true;
    }

    private async Task ThrowIfUpdateUnderWayAsync(GrokBotExecutableStamp? since)
    {
        if (GrokBotRelaunch.UpdateUnderWay(await Task.Run(InstallerRunning), ExecutableStamp(), since))
        {
            WingmanLog.Write("grok-bot-open-update-running");
            throw new InvalidOperationException(GrokBotRelaunch.UpdateRunningText);
        }
    }

    /// <summary>
    /// The reconnect prompt's relaunch. Wingman never closes Grok Bot: it brings exactly <paramref name="target"/>'s window
    /// forward (id and start time, from one snapshot) and waits up to 10 minutes for the user to quit that process. Grok Bot
    /// is started only once it has exited, no Grok Bot is left running (up to 20 s) and <paramref name="mayStart"/> holds
    /// (up to 60 s), and never while a Grok Bot installer runs or Grok Bot.exe changed (GrokBotRelaunch). Relaunched:
    /// started; the caller waits with <see cref="WaitForWindowAsync"/>.
    /// </summary>
    public async Task<GrokBotRelaunchResult> ReconnectAsync(GrokBotProcess target, Func<bool> mayStart, Func<bool> sessionEnding, CancellationToken cancellationToken)
    {
        if (!IsInstalled()) throw new FileNotFoundException("Grok Bot is not installed in its usual folder.");
        var result = await GrokBotRelaunch.RunAsync(target, new ProcessControl(this, mayStart, sessionEnding), GrokBotRelaunchLimits.Default,
            () => DateTime.UtcNow, (wait, token) => Task.Delay(wait, token), cancellationToken);
        WingmanLog.Write("grok-bot-reconnect-relaunch", ("process", target.Id), ("result", result.Kind));
        return result;
    }

    /// <summary>The Open Grok Bot button after Wingman couldn't reopen it: the same bounded, installer-safe start.</summary>
    public async Task<GrokBotRelaunchResult> StartAfterQuitAsync(GrokBotRelaunch.ClosedProcess quit, Func<bool> mayStart, Func<bool> sessionEnding, CancellationToken cancellationToken)
    {
        if (!IsInstalled()) throw new FileNotFoundException("Grok Bot is not installed in its usual folder.");
        var control = new ProcessControl(this, mayStart, sessionEnding);
        var result = await GrokBotRelaunch.StartWhenClearAsync(quit, control.ExecutableStamp(), control, GrokBotRelaunchLimits.Default,
            () => DateTime.UtcNow, (wait, token) => Task.Delay(wait, token), cancellationToken);
        WingmanLog.Write("grok-bot-reconnect-open", ("process", quit.Process.Id), ("result", result.Kind));
        return result;
    }

    /// <summary>
    /// The user's quit started a Grok Bot update: waits for it to finish, then opens Grok Bot with the port (see
    /// <see cref="GrokBotRelaunch.AfterUpdateAsync"/>). Grok Bot.exe may be missing while the installer replaces it, so
    /// this doesn't require it to be installed up front; the start's own guards do.
    /// </summary>
    public async Task<GrokBotRelaunchResult> AfterUpdateAsync(GrokBotRelaunch.ClosedProcess quit, Func<bool> mayStart, Func<bool> sessionEnding, CancellationToken cancellationToken)
    {
        var control = new ProcessControl(this, mayStart, sessionEnding);
        var result = await GrokBotRelaunch.AfterUpdateAsync(quit, control, GrokBotRelaunchLimits.Default,
            () => DateTime.UtcNow, (wait, token) => Task.Delay(wait, token), cancellationToken);
        WingmanLog.Write("grok-bot-reconnect-after-update", ("process", quit.Process.Id), ("result", result.Kind));
        return result;
    }

    private sealed class ProcessControl(GrokBotLauncher launcher, Func<bool> mayStart, Func<bool> sessionEnding) : IGrokBotProcessControl
    {
        public IReadOnlyList<IGrokBotProcessHandle> Snapshot()
        {
            var running = launcher.Running();
            try { return running.Select(process => ProcessHandle.Open(process.Id)).OfType<IGrokBotProcessHandle>().ToArray(); }
            finally { foreach (var process in running) process.Dispose(); }
        }
        public void BringForward(IGrokBotProcessHandle process) => ((ProcessHandle)process).BringForward();
        public bool AnyRunning() => launcher.StillRunning();
        public GrokBotProcess? MainProcess() => launcher.MainProcess();
        public bool MayStart() => mayStart();
        public GrokBotExecutableStamp? ExecutableStamp() => launcher.ExecutableStamp();
        // Lists every process once a second during the wait: on the thread pool, never on the UI thread.
        public Task<bool> InstallerRunningAsync() => Task.Run(launcher.InstallerRunning);
        public bool SessionEnding() => sessionEnding();
        public void Start(GrokBotRelaunch.ClosedProcess proof) => launcher.StartWithPort();
    }

    /// <summary>Grok Bot.exe's write time and size; null when missing or unreadable (an installer may be replacing it).</summary>
    public GrokBotExecutableStamp? ExecutableStamp()
    {
        try
        {
            var file = new FileInfo(Executable);
            return file.Exists ? new GrokBotExecutableStamp(file.LastWriteTimeUtc, file.Length) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Whether a Grok Bot installer, update or uninstaller runs (GrokBotInstallers). Only processes whose name could be one
    /// are opened, to read their program path.
    /// </summary>
    public bool InstallerRunning()
    {
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                string name;
                try { name = process.ProcessName; }
                catch (InvalidOperationException) { continue; }
                if (!MayBeInstaller(name)) continue;
                var id = process.Id;
                if (GrokBotInstallers.IsInstaller(WindowProcessInfo.ImagePathOf((uint)id), Executable, () => CommandLine(id),
                    () => ParentProcessId(id) is { } parent ? WindowProcessInfo.ImagePathOf((uint)parent) : null)) return true;
            }
            return false;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static bool MayBeInstaller(string name) =>
        name.StartsWith("sand-update-", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Un_A", StringComparison.OrdinalIgnoreCase) || name.Equals("Au_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A Grok Bot process held by its own process handle (SYNCHRONIZE | QUERY_LIMITED_INFORMATION) from the snapshot until
    /// it exits: Windows doesn't reuse the id while this handle is open, and the exit check waits on the handle itself.
    /// </summary>
    private sealed class ProcessHandle : IGrokBotProcessHandle
    {
        private readonly SafeProcessHandle handle;

        private ProcessHandle(SafeProcessHandle handle, GrokBotProcess identity) { this.handle = handle; Identity = identity; }

        /// <summary>Opens the process; null when it already exited or can't be opened. Its start time comes from this handle.</summary>
        public static ProcessHandle? Open(int processId)
        {
            var handle = OpenProcessHandle(Synchronize | ProcessQueryLimitedInformation, false, processId);
            if (handle.IsInvalid) { handle.Dispose(); return null; }
            return new ProcessHandle(handle, new GrokBotProcess(processId, StartedUtc(handle)));
        }

        public GrokBotProcess Identity { get; }

        /// <summary>Signalled when this exact process exits. A failed wait counts as still running, so nothing starts.</summary>
        public bool HasExited => WaitForSingleObject(handle, 0) == WaitObject0;

        /// <summary>The exit code once exited (QUERY_LIMITED_INFORMATION is enough); null while running or unreadable.</summary>
        public int? ExitCode => HasExited && GetExitCodeProcess(handle, out var code) ? unchecked((int)code) : null;

        /// <summary>Activates its visible main window, if any; with none shown yet there is nothing to bring forward.</summary>
        public void BringForward()
        {
            if (HasExited) return;
            try
            {
                // The open handle keeps this id on this process, so looking it up by id finds the same process.
                using var process = Process.GetProcessById(Identity.Id);
                if (SafeMainWindow(process) is var window && window != IntPtr.Zero) Core.Helpers.WindowsForegroundActivator.BringForwardKeepingSize(window);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException) { }
        }

        public void Dispose() => handle.Dispose();
    }

    private const uint Synchronize = 0x00100000;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint WaitObject0 = 0;

    [DllImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcessHandle(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle handle, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(SafeProcessHandle handle, out long creation, out long exit, out long kernel, out long user);

    /// <summary>
    /// The process creation time in UTC, straight from GetProcessTimes (no local-time round trip, so a DST change can't
    /// shift it). The prompt and the quit wait compare these, so both read it here.
    /// </summary>
    private static DateTime? StartedUtc(SafeProcessHandle handle) =>
        GetProcessTimes(handle, out var created, out _, out _, out _) ? DateTime.FromFileTimeUtc(created) : null;

    private static DateTime? StartedUtc(Process process)
    {
        using var handle = OpenProcessHandle(ProcessQueryLimitedInformation, false, process.Id);
        return handle.IsInvalid ? null : StartedUtc(handle);
    }

    /// <summary>
    /// Grok Bot's main (browser) process, with or without a visible window: a Grok Bot.exe whose command line has no
    /// <c>--type=</c> and runs no script, preferring the one that owns Grok Bot's single-instance window
    /// (GrokBotLaunchPolicy.ChooseMainProcess). Falls back to the main window's process if no command line can be read.
    /// </summary>
    public GrokBotProcess? MainProcess()
    {
        var processes = Running();
        try
        {
            var candidates = processes
                .Select(process => new GrokBotProcess(process.Id, StartedUtc(process)))
                .Where(IsMain)
                .ToArray();
            var main = GrokBotLaunchPolicy.ChooseMainProcess(candidates, candidates.Length > 1 ? SingleInstanceWindowOwners() : new HashSet<int>());
            if (main is not null) return main;
            var window = processes.Where(process => SafeMainWindow(process) != IntPtr.Zero).OrderBy(process => process.Id).FirstOrDefault();
            return window is null ? null : new GrokBotProcess(window.Id, StartedUtc(window));
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    /// <summary>
    /// The processes owning a Chromium single-instance window: a message-only window of class <c>Chrome_MessageWindow</c>
    /// named after the profile folder, created by the instance that holds Electron's single-instance lock.
    /// </summary>
    private static HashSet<int> SingleInstanceWindowOwners()
    {
        var owners = new HashSet<int>();
        var window = IntPtr.Zero;
        for (var seen = 0; seen < 256; seen++)
        {
            window = FindWindowEx(MessageOnlyParent, window, "Chrome_MessageWindow", null);
            if (window == IntPtr.Zero) break;
            if (GetWindowThreadProcessId(window, out var process) != 0) owners.Add((int)process);
        }
        return owners;
    }

    private static readonly IntPtr MessageOnlyParent = new(-3); // HWND_MESSAGE

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    private readonly Dictionary<GrokBotProcess, bool> mainProcessCache = [];

    private bool IsMain(GrokBotProcess identity)
    {
        if (mainProcessCache.TryGetValue(identity, out var known)) return known;
        if (mainProcessCache.Count > 64) mainProcessCache.Clear();
        var commandLine = CommandLine(identity.Id);
        var main = GrokBotLaunchPolicy.IsMainProcess(commandLine);
        if (commandLine is not null) mainProcessCache[identity] = main; // unreadable: ask again next time
        return main;
    }

    private void StartWithPort()
    {
        lock (runtimeFile) StartWithPortLocked();
    }

    private void StartWithPortLocked()
    {
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
    }

    public async Task WaitForWindowAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + EndpointTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((await PagesAsync(cancellationToken)).Count > 0) return;
            await Task.Delay(500, cancellationToken);
        }
        throw new TimeoutException("Grok Bot started but did not expose its window to Wingman on the local port.");
    }

    /// <summary>
    /// Whether Grok Bot is still running: Grok Bot itself or its own children (GrokBotLaunchPolicy.KeepsGrokBotRunning). A
    /// leftover Grok Bot.exe running only a script, such as a detached local-exec daemon, doesn't count unless it owns the
    /// single-instance window.
    /// </summary>
    private bool StillRunning()
    {
        var processes = Running();
        try
        {
            HashSet<int>? owners = null;
            foreach (var process in processes)
            {
                var commandLine = CommandLine(process.Id);
                if (commandLine is null || !GrokBotLaunchPolicy.RunsScriptOnly(commandLine)) return true;
                owners ??= SingleInstanceWindowOwners();
                if (GrokBotLaunchPolicy.KeepsGrokBotRunning(commandLine, owners.Contains(process.Id))) return true;
            }
            return false;
        }
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
