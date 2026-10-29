using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using CodexWingman.Core;

namespace CodexWingman;

/// <summary>
/// Reads and edits .lnk files through the Windows shell (IShellLink, plus IPropertyStore for the AppUserModelID).
/// An edit loads the existing file, sets only its arguments and saves it in place, so every other property is kept as it was.
/// </summary>
internal sealed class ShellLinkFiles : IShortcutFiles
{
    private const int StgmRead = 0, StgmReadWrite = 2;
    private static readonly PropertyKey AppUserModelIdKey = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    public ShortcutLink? Read(string path)
    {
        if (!File.Exists(path)) return null;
        object? link = null;
        try
        {
            link = NewLink();
            ((IPersistFile)link).Load(path, StgmRead);
            var shell = (IShellLinkW)link;
            var text = new StringBuilder(32_768);
            shell.GetPath(text, text.Capacity, IntPtr.Zero, 0);
            var target = text.ToString();
            text.Clear();
            shell.GetArguments(text, text.Capacity);
            var arguments = text.ToString();
            text.Clear();
            shell.GetWorkingDirectory(text, text.Capacity);
            var folder = text.ToString();
            text.Clear();
            shell.GetIconLocation(text, text.Capacity, out var iconIndex);
            var icon = text.Length == 0 ? string.Empty : text + "," + iconIndex;
            text.Clear();
            shell.GetDescription(text, text.Capacity);
            return new ShortcutLink(target, arguments, folder, icon, ReadAppId((IPropertyStore)link), text.ToString());
        }
        catch (COMException) { return null; }
        finally { if (link is not null) Marshal.FinalReleaseComObject(link); }
    }

    /// <summary>Loads the file, sets the arguments and saves it in place; no other property is set.</summary>
    public void WriteArguments(string path, string arguments)
    {
        object? link = null;
        try
        {
            link = NewLink();
            var persisted = (IPersistFile)link;
            persisted.Load(path, StgmReadWrite);
            ((IShellLinkW)link).SetArguments(arguments);
            persisted.Save(path, true);
        }
        finally { if (link is not null) Marshal.FinalReleaseComObject(link); }
    }

    public void Copy(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    public IEnumerable<string> Files(string folder) =>
        Directory.Exists(folder) ? Directory.GetFiles(folder, "*.lnk") : [];

    private static object NewLink() => Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))
        ?? throw new NotSupportedException("Windows shortcut services are unavailable."))!;

    private static string? ReadAppId(IPropertyStore store)
    {
        store.GetValue(in AppUserModelIdKey, out var value);
        try { return value.Text(); }
        finally { PropVariantClear(ref value); }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropertyVariant value);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey(Guid format, uint id)
    {
        public readonly Guid Format = format;
        public readonly uint Id = id;
    }

    /// <summary>Filled in by the shell (read only); freed with PropVariantClear.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyVariant
    {
#pragma warning disable CS0649, CS0169 // set by native code
        private const ushort LpWStr = 31;
        private ushort type;
        private ushort reserved1, reserved2, reserved3;
        private IntPtr pointer;
        private IntPtr second; // the union's second word (counted arrays): native size and alignment
#pragma warning restore CS0649, CS0169

        public readonly string? Text() => type == LpWStr && pointer != IntPtr.Zero ? Marshal.PtrToStringUni(pointer) : null;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(in PropertyKey key, out PropertyVariant value);
        void SetValue(in PropertyKey key, in PropertyVariant value);
        void Commit();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr findData, uint flags);
        void GetIDList(out IntPtr list);
        void SetIDList(IntPtr list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int size);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int size);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int size);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int size, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
