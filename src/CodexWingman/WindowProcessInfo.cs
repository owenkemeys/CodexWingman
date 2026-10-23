using System.Runtime.InteropServices;
using System.Text;

namespace CodexWingman;

/// <summary>
/// Cheap per-process facts for window counting. <see cref="System.Diagnostics.Process.SessionId"/> and
/// <c>MainModule</c> each take a snapshot of every process on the machine (or walk the module list),
/// which made the 2-second tray status check cost about a fifth of a core when it ran once per visible
/// window. These calls ask Windows about one process id directly and return the same values.
/// </summary>
internal static class WindowProcessInfo
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>The Windows session of a process id, or null when the process is gone or inaccessible.</summary>
    public static int? SessionOf(uint processId) =>
        ProcessIdToSessionId(processId, out var session) ? (int)session : null;

    public static int CurrentSession() => SessionOf((uint)Environment.ProcessId) ?? -1;

    /// <summary>The full Win32 path of the process image, or null when the process is gone or inaccessible.</summary>
    public static string? ImagePathOf(uint processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var capacity = 1024;
            var buffer = new StringBuilder(capacity);
            return QueryFullProcessImageName(handle, 0, buffer, ref capacity) ? buffer.ToString() : null;
        }
        finally { CloseHandle(handle); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
