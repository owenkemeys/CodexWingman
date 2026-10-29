using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CodexWingman.Core.Helpers;

public static class WindowsForegroundActivator
{
    private const int RestoreWindow = 9;
    private const int ProcessWindowAttempts = 20;
    private static readonly TimeSpan ProcessWindowDelay = TimeSpan.FromMilliseconds(100);

    public static Task<bool> ActivateProcessAsync(Process process, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        return WaitAndActivateAsync(
            () =>
            {
                try
                {
                    process.Refresh();
                    return process.HasExited ? IntPtr.Zero : process.MainWindowHandle;
                }
                catch (InvalidOperationException)
                {
                    return IntPtr.Zero;
                }
            },
            ActivateWindow,
            (delay, token) => Task.Delay(delay, token),
            ProcessWindowAttempts,
            cancellationToken);
    }

    public static async Task<bool> WaitAndActivateAsync(
        Func<IntPtr> getWindowHandle,
        Func<IntPtr, bool> activateWindow,
        Func<TimeSpan, CancellationToken, Task> delay,
        int attempts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(getWindowHandle);
        ArgumentNullException.ThrowIfNull(activateWindow);
        ArgumentNullException.ThrowIfNull(delay);
        if (attempts < 1) throw new ArgumentOutOfRangeException(nameof(attempts));

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handle = getWindowHandle();
            if (handle != IntPtr.Zero && activateWindow(handle)) return true;
            if (attempt + 1 < attempts) await delay(ProcessWindowDelay, cancellationToken);
        }
        return false;
    }

    /// <summary>
    /// The show command before activating: SW_RESTORE always, as before (it also un-maximizes a maximized window), unless
    /// <paramref name="keepSize"/>, which restores only a minimized window.
    /// </summary>
    public static int? ShowCommandBeforeActivating(bool minimized, bool keepSize) => !keepSize || minimized ? RestoreWindow : null;

    /// <summary>Restores the window (SW_RESTORE) and brings it to the front. Codex, Open window and Host Action opens use this.</summary>
    public static bool ActivateWindow(IntPtr handle) => Activate(handle, keepSize: false);

    /// <summary>
    /// The Grok Bot reconnect only: brings the window forward keeping its size (a minimized window is restored, a maximized
    /// one stays maximized), and when Windows refuses the foreground the taskbar button flashes until the user looks.
    /// </summary>
    public static bool BringForwardKeepingSize(IntPtr handle) => Activate(handle, keepSize: true);

    private static bool Activate(IntPtr handle, bool keepSize)
    {
        if (ShowCommandBeforeActivating(keepSize && IsIconic(handle), keepSize) is { } command) _ = ShowWindowAsync(handle, command);
        var currentThread = GetCurrentThreadId();
        var foregroundWindow = GetForegroundWindow();
        var foregroundThread = foregroundWindow == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foregroundWindow, out _);
        var targetThread = GetWindowThreadProcessId(handle, out _);
        var foregroundAttached = false;
        var targetAttached = false;

        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
                foregroundAttached = AttachThreadInput(currentThread, foregroundThread, true);
            if (targetThread != 0 && targetThread != currentThread && targetThread != foregroundThread)
                targetAttached = AttachThreadInput(currentThread, targetThread, true);

            _ = BringWindowToTop(handle);
            _ = SetForegroundWindow(handle);
            _ = SetFocus(handle);
        }
        finally
        {
            if (targetAttached) _ = AttachThreadInput(currentThread, targetThread, false);
            if (foregroundAttached) _ = AttachThreadInput(currentThread, foregroundThread, false);
        }
        if (GetForegroundWindow() == handle) return true;
        if (keepSize) Flash(handle);
        return false;
    }

    private static void Flash(IntPtr handle)
    {
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = handle,
            Flags = FlashTray | FlashUntilForeground,
        };
        _ = FlashWindowEx(ref info);
    }

    private const uint FlashTray = 0x00000002;
    private const uint FlashUntilForeground = 0x0000000C;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attachingThread, uint attachedThread, bool attach);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr handle, int command);
}
