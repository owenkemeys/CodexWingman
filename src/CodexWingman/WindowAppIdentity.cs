using System.Runtime.InteropServices;

namespace CodexWingman;

/// <summary>
/// Gives the top-level windows of one process Wingman's own taskbar identity, so they group under
/// a Wingman AppUserModelID instead of the browser's. Only window properties are set; no files change.
/// </summary>
internal static class WindowAppIdentity
{
    private static readonly Guid AppUserModelFormat = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private static readonly Guid PropertyStoreId = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    public static int Apply(int processId, string appId, string relaunchCommand, string displayName, string iconResource)
    {
        var updated = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != processId || !IsWindowVisible(window) || GetWindow(window, 4) != IntPtr.Zero) return true;
            try { if (SetIdentity(window, appId, relaunchCommand, displayName, iconResource)) updated++; }
            catch (COMException) { }
            return true;
        }, IntPtr.Zero);
        return updated;
    }

    /// <summary>The AppUserModelID set on one window, or null when the window has none of its own.</summary>
    public static string? Read(IntPtr window)
    {
        var iid = PropertyStoreId;
        if (SHGetPropertyStoreForWindow(window, ref iid, out var store) != 0 || store is null) return null;
        try { return CurrentString(store, new PropertyKey(AppUserModelFormat, 5)); }
        finally { Marshal.ReleaseComObject(store); }
    }

    private static bool SetIdentity(IntPtr window, string appId, string relaunchCommand, string displayName, string iconResource)
    {
        var iid = PropertyStoreId;
        if (SHGetPropertyStoreForWindow(window, ref iid, out var store) != 0 || store is null) return false;
        try
        {
            var idKey = new PropertyKey(AppUserModelFormat, 5);
            if (CurrentString(store, idKey) == appId) return false;
            // Relaunch properties first: Windows reads them when the ID is set.
            Set(store, new PropertyKey(AppUserModelFormat, 2), relaunchCommand);
            Set(store, new PropertyKey(AppUserModelFormat, 3), iconResource);
            Set(store, new PropertyKey(AppUserModelFormat, 4), displayName);
            Set(store, idKey, appId);
            return true;
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    private static string? CurrentString(IPropertyStore store, PropertyKey key)
    {
        var variant = default(PropVariant);
        try
        {
            store.GetValue(in key, out variant);
            return variant.Type == 31 && variant.Pointer != IntPtr.Zero ? Marshal.PtrToStringUni(variant.Pointer) : null;
        }
        finally { PropVariantClear(ref variant); }
    }

    private static void Set(IPropertyStore store, PropertyKey key, string value)
    {
        var variant = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(value) };
        try { store.SetValue(in key, in variant); }
        finally { PropVariantClear(ref variant); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey(Guid format, uint id)
    {
        public readonly Guid Format = format;
        public readonly uint Id = id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(in PropertyKey key, out PropVariant value);
        void SetValue(in PropertyKey key, in PropVariant value);
        void Commit();
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr window, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? store);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);
}
