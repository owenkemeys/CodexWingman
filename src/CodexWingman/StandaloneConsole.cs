using System.Runtime.InteropServices;
using CodexWingman.Core;
using Microsoft.Win32.SafeHandles;

namespace CodexWingman;

/// <summary>
/// Output for commands that run on their own. Wingman is a windowed program, so it has no console and none flashes:
/// redirected output (a pipe or file) is written as is; started from a console, it attaches to that console; otherwise
/// the text is shown in a message box.
/// </summary>
internal static class StandaloneConsole
{
    private const int AttachParentProcess = -1;
    private const int StdOutput = -11, StdError = -12;
    private const uint FileTypeDisk = 1, FileTypePipe = 3;

    public static void Show(string text, bool error, string title)
    {
        switch (StandaloneOutput.Choose(Redirected(error ? StdError : StdOutput), () => AttachConsole(AttachParentProcess)))
        {
            case StandaloneOutputRoute.Redirected:
                (error ? Console.Error : Console.Out).WriteLine(text);
                return;
            case StandaloneOutputRoute.ParentConsole:
                WriteToConsole(text);
                FreeConsole();
                return;
            default:
                MessageBox.Show(text, title, MessageBoxButtons.OK, error ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                return;
        }
    }

    private static bool Redirected(int standardHandle)
    {
        var handle = GetStdHandle(standardHandle);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return false;
        var type = GetFileType(handle);
        return type is FileTypeDisk or FileTypePipe;
    }

    private static void WriteToConsole(string text)
    {
        // The attached console's own output buffer; a fresh line so the text doesn't run into the prompt.
        using var handle = new SafeFileHandle(CreateFile("CONOUT$", 0x40000000 /* GENERIC_WRITE */, 2 /* FILE_SHARE_WRITE */,
            IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero), ownsHandle: true);
        if (handle.IsInvalid) return;
        using var stream = new FileStream(handle, FileAccess.Write);
        using var writer = new StreamWriter(stream) { AutoFlush = true };
        writer.Write(Environment.NewLine + text + Environment.NewLine);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
}
