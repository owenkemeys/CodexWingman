using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using CodexWingman.Core;

namespace CodexWingman;

internal static class LauncherShortcuts
{
    public const string T3WebShortcutName = "Wingman T3 Code (web)";

    /// <summary>
    /// Creates or refreshes Wingman's Start shortcuts. The T3 web shortcut is added only when T3 web can
    /// work on this PC. Shortcuts are never pinned; pinning stays the user's choice.
    /// </summary>
    public static string[] Ensure(string executable, string? localAppData = null, string? programsFolder = null, bool includeT3Web = false)
    {
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable) || !Directory.Exists(Path.Combine(Path.GetDirectoryName(executable)!, "Helpers")))
            throw new FileNotFoundException("Wingman's complete package is required to create its launchers.");
        programsFolder ??= Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (string.IsNullOrWhiteSpace(programsFolder))
            throw new IOException("Windows did not provide your Start menu folder.");
        Directory.CreateDirectory(programsFolder);
        var shortcuts = new[]
        {
            (Name: "Wingman Codex", Target: WingmanLaunchTarget.Codex, Id: WingmanIdentity.AppUserModelId + ".Codex"),
            (Name: "Wingman T3 Code", Target: WingmanLaunchTarget.T3Code, Id: WingmanIdentity.AppUserModelId + ".T3Code"),
            (Name: "Wingman", Target: WingmanLaunchTarget.Preferences, Id: WingmanIdentity.AppUserModelId),
        }.ToList();
        if (includeT3Web)
            shortcuts.Add((Name: T3WebShortcutName, Target: WingmanLaunchTarget.T3Web,
                Id: WingmanLaunchOptions.AppUserModelIdFor(WingmanLaunchTarget.T3Web)));
        return shortcuts.Select(item => Save(Path.Combine(programsFolder, item.Name + ".lnk"),
            executable, item.Target == WingmanLaunchTarget.Preferences
                ? WingmanLaunchOptions.RelaunchArguments(localAppData)
                : WingmanLaunchOptions.ShortcutArguments(item.Target, localAppData), item.Name, item.Id)).ToArray();
    }

    /// <summary>True when this profile has a T3 web address or a reachable pairing folder (set up or found via the sync share).</summary>
    public static bool T3WebAvailable(string? localAppData)
    {
        try
        {
            var settings = HelperSettingsStore.Load(HelperSettingsStore.DefaultPath(localAppData));
            return settings.T3Web?.IsConfigured == true
                // The the sync share receipt belongs to the Windows user, not to a Wingman profile override.
                || SharedFolderPairingProvider.ResolveFolder(settings.T3Web?.Pairing,
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) is { } folder
                    && Directory.Exists(folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            return false;
        }
    }

    public static string? CurrentProfileOverride(WingmanLaunchOptions options)
    {
        var profile = options.LocalAppData ?? Environment.GetEnvironmentVariable("LOCALAPPDATA");
        var normal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return !string.IsNullOrWhiteSpace(profile)
            && !Path.GetFullPath(profile).TrimEnd('\\', '/').Equals(normal.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(profile) : null;
    }

    private static string Save(string path, string executable, string arguments, string name, string appId)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, ".wingman-" + Guid.NewGuid().ToString("N") + ".lnk");
        object? shell = null;
        object? shortcut = null;
        object? link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new NotSupportedException("Windows shortcut services are unavailable."))!;
            shortcut = ((dynamic)shell).CreateShortcut(temporary);
            dynamic writable = shortcut;
            writable.TargetPath = executable;
            writable.Arguments = arguments;
            writable.WorkingDirectory = Path.GetDirectoryName(executable);
            writable.IconLocation = executable + ",0";
            writable.Description = name + " with Wingman Helpers";
            writable.Save();

            link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!)!;
            var persisted = (IPersistFile)link;
            persisted.Load(temporary, 2);
            var store = (IPropertyStore)link;
            var key = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
            using var value = new PropertyVariant(appId);
            store.SetValue(in key, in value);
            store.Commit();
            persisted.Save(temporary, true);
            Marshal.FinalReleaseComObject(link);
            link = null;
            Marshal.FinalReleaseComObject(shortcut);
            shortcut = null;
            File.Move(temporary, path, overwrite: true);
            return path;
        }
        finally
        {
            if (link is not null) Marshal.FinalReleaseComObject(link);
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey(Guid format, uint id)
    {
        public readonly Guid Format = format;
        public readonly uint Id = id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyVariant : IDisposable
    {
        private readonly ushort type;
        private readonly ushort reserved1, reserved2, reserved3;
        private readonly VariantValue value;
        public PropertyVariant(string text)
        {
            type = 31; // VT_LPWSTR; IPropertyStore copies the string.
            reserved1 = reserved2 = reserved3 = 0;
            value = new VariantValue { Pointer = Marshal.StringToCoTaskMemUni(text) };
        }
        public void Dispose() => Marshal.FreeCoTaskMem(value.Pointer);
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct VariantValue
    {
        [FieldOffset(0)] public IntPtr Pointer;
        // The counted-array arm supplies the native union's x86/x64 size and alignment.
        [FieldOffset(0)] public CountedArray Array;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CountedArray { public uint Count; public IntPtr Pointer; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(in PropertyKey key, out PropertyVariant value);
        void SetValue(in PropertyKey key, in PropertyVariant value);
        void Commit();
    }
}
