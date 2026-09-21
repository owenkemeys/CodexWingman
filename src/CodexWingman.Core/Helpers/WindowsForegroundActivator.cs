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

    private static bool ActivateWindow(IntPtr handle)
    {
        _ = ShowWindowAsync(handle, RestoreWindow);
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
            return GetForegroundWindow() == handle;
        }
        finally
        {
            if (targetAttached) _ = AttachThreadInput(currentThread, targetThread, false);
            if (foregroundAttached) _ = AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

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
