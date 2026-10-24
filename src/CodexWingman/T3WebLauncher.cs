using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using CodexWingman.Core;

namespace CodexWingman;

/// <summary>
/// Opens T3's web client in Wingman's own Chrome app window: its own Chrome data folder, a
/// localhost-only debugging port, and Wingman's T3 web taskbar identity. the user's normal Chrome
/// profile and its cookies are never opened, read, or copied.
/// </summary>
internal sealed class T3WebLauncher(string wingmanDataRoot, Func<HelperSettings> settings, Func<IEnumerable<int>> reservedPorts)
{
    private static readonly TimeSpan EndpointTimeout = TimeSpan.FromSeconds(30);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };
    public string RuntimePath { get; } = T3WebLaunchPolicy.RuntimePath(wingmanDataRoot);
    public string DataDirectory { get; } = T3WebLaunchPolicy.DataDirectory(wingmanDataRoot);
    public string PairingStatePath { get; } = T3WebLaunchPolicy.PairingStatePath(wingmanDataRoot);
    public string AppIconPath { get; } = T3WebAppIcon.IconPath(wingmanDataRoot);
    private static readonly HttpClient IconHttp = new() { Timeout = TimeSpan.FromSeconds(10) };
    private DateTime nextIconAttemptUtc = DateTime.MinValue;

    /// <summary>
    /// T3's own icon, cached from the site's public web app manifest (refreshed weekly; a failed fetch is
    /// retried after 10 minutes). Returns the .ico path or null when none is cached yet, and whether it changed.
    /// </summary>
    public async Task<(string? Path, bool Changed)> EnsureAppIconAsync(CancellationToken cancellationToken = default)
    {
        var cached = File.Exists(AppIconPath) ? AppIconPath : null;
        var written = cached is null ? (DateTime?)null : File.GetLastWriteTimeUtc(AppIconPath);
        if (!T3WebAppIcon.NeedsRefresh(written, DateTime.UtcNow) || DateTime.UtcNow < nextIconAttemptUtc || Origin() is not { } origin)
            return (cached, false);
        nextIconAttemptUtc = DateTime.UtcNow + TimeSpan.FromMinutes(10);
        try
        {
            string? html = null;
            try { html = await IconHttp.GetStringAsync(origin, cancellationToken); }
            catch (HttpRequestException) { }
            var manifestUrl = T3WebAppIcon.ManifestUrl(html, origin);
            var manifest = await IconHttp.GetStringAsync(manifestUrl, cancellationToken);
            var pngs = new List<byte[]>();
            foreach (var url in T3WebAppIcon.PngIcons(manifest, manifestUrl).Take(4))
            {
                var bytes = await IconHttp.GetByteArrayAsync(url, cancellationToken);
                if (T3WebAppIcon.PngSize(bytes) is not null) pngs.Add(bytes);
            }
            var ico = T3WebAppIcon.BuildIco(pngs);
            if (cached is not null && File.ReadAllBytes(cached).AsSpan().SequenceEqual(ico))
            {
                File.SetLastWriteTimeUtc(AppIconPath, DateTime.UtcNow);
                return (cached, false);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(AppIconPath)!);
            var temporary = AppIconPath + ".tmp";
            File.WriteAllBytes(temporary, ico);
            File.Move(temporary, AppIconPath, overwrite: true);
            return (AppIconPath, true);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException
            or InvalidDataException or System.Text.Json.JsonException
            || (error is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Debug.WriteLine($"[T3 web icon] {error.Message}");
            return (cached, false);
        }
    }

    /// <summary>The T3 web origin: local setting first, then the origin remembered from the last pairing.</summary>
    public Uri? Origin()
    {
        if (T3WebUrlPolicy.TryNormalizeOrigin(settings().T3Web?.Url, out var configured)) return configured;
        var state = T3PairingStateStore.Load(PairingStatePath);
        return state is not null && T3WebUrlPolicy.TryNormalizeOrigin(state.Origin, out var remembered) ? remembered : discoveredOrigin;
    }

    // Origin learned from the first pairing link before any pairing state exists.
    private Uri? discoveredOrigin;

    public string? PairingFolder() => SharedFolderPairingProvider.ResolveFolder(
        settings().T3Web?.Pairing, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>Shown in menus and gets a Start shortcut when a T3 address or a pairing folder is known.</summary>
    public bool IsAvailable() => FindChrome() is not null && (Origin() is not null || PairingFolder() is not null);

    public int? Port() => AppRuntimePortFile.Read(RuntimePath);

    public IT3PairingLinkProvider? PairingProvider()
    {
        var folder = PairingFolder();
        return folder is null ? null : new SharedFolderPairingProvider(folder, Environment.MachineName);
    }

    public string PairingLabel() => settings().T3Web?.Pairing?.Label is { Length: > 0 } label
        ? label : T3PairingCoordinator.DefaultLabel(Environment.MachineName);

    public string? FindChrome()
    {
        var configured = settings().T3Web?.ChromePath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        return T3WebLaunchPolicy.CandidateChromeExecutables(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)).FirstOrDefault(File.Exists);
    }

    public async Task<IReadOnlyList<CodexTarget>> PagesAsync(CancellationToken cancellationToken = default)
    {
        var port = Port();
        var origin = Origin();
        if (port is null || origin is null) return [];
        try { return await new T3WebTargetSource(Http, () => port, () => origin, CdpConnectionPool.Shared).ListAsync(cancellationToken); }
        catch (HttpRequestException) { return []; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return []; }
    }

    /// <summary>
    /// Opens a T3 web window. When the origin is not known yet, the pairing provider's first link
    /// supplies it (zero setup); that link is then used for the first sign-in.
    /// </summary>
    public async Task<(int Port, T3PairingLink? Prefetched)> OpenAsync(CancellationToken cancellationToken)
    {
        var chrome = FindChrome() ?? throw new FileNotFoundException("Google Chrome is needed for T3 Code (web) and was not found.");
        T3PairingLink? prefetched = null;
        var origin = Origin();
        if (origin is null)
        {
            var provider = PairingProvider()
                ?? throw new InvalidOperationException("Wingman does not know the T3 Code (web) address yet and no pairing folder is reachable.");
            prefetched = await provider.CreateLinkAsync(new(null, PairingLabel(), "first-run"), cancellationToken);
            if (!T3WebUrlPolicy.TryParsePairingLink(prefetched.Url, null, out var linkOrigin, out _))
                throw new InvalidDataException("The pairing link was not a T3 Code (web) pairing link.");
            origin = discoveredOrigin = linkOrigin;
        }

        var remembered = Port();
        if (remembered is { } existing && await EndpointAliveAsync(existing, cancellationToken))
        {
            var pages = await PagesAsync(cancellationToken);
            if (T3WebLaunchPolicy.OpenAction(true, pages.Count) == T3WebOpenAction.ActivateExisting
                && await ActivateAsync(existing, pages[0], cancellationToken))
                return (existing, prefetched);
            // No T3 web window (or it could not be raised): Chrome hands the request to the running
            // Wingman profile, which opens one app window.
            Start(chrome, existing, origin);
            return (existing, prefetched);
        }
        var port = SelectPort(remembered);
        Directory.CreateDirectory(DataDirectory);
        Start(chrome, port, origin);
        var deadline = DateTime.UtcNow + EndpointTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await EndpointAliveAsync(port, cancellationToken))
            {
                AppRuntimePortFile.Write(RuntimePath, port);
                return (port, prefetched);
            }
            await Task.Delay(250, cancellationToken);
        }
        throw new TimeoutException("Chrome did not open Wingman's T3 Code (web) window with a local Helper endpoint.");
    }

    /// <summary>Re-applies Wingman's T3 web taskbar identity to the Chrome app windows of Wingman's own profile.</summary>
    public async Task<int> ApplyWindowIdentityAsync(string wingmanExecutable, string? profileOverride, CancellationToken cancellationToken = default)
    {
        if (Port() is not { } port) return 0;
        System.Text.Json.JsonElement? result;
        try { result = await CdpBrowserCommand.SendAsync(Http, port, "SystemInfo.getProcessInfo", null, cancellationToken); }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException or TimeoutException
            or System.Net.WebSockets.WebSocketException or System.Text.Json.JsonException
            || (error is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        { return 0; }
        if (CdpBrowserCommand.BrowserProcessId(result) is not { } pid) return 0;
        // T3's own icon (not Wingman's) for the Start shortcut the taskbar matches by AppUserModelID, and for the
        // window's relaunch icon, so the running button and a pin made from it both show T3.
        var (icon, changed) = await EnsureAppIconAsync(cancellationToken);
        if (changed && icon is not null)
        {
            try { LauncherShortcuts.EnsureT3Web(wingmanExecutable, profileOverride, icon); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or NotSupportedException)
            { Debug.WriteLine($"[T3 web shortcut icon] {error.Message}"); }
        }
        return WindowAppIdentity.Apply(pid,
            WingmanLaunchOptions.AppUserModelIdFor(WingmanLaunchTarget.T3Web),
            "\"" + wingmanExecutable + "\" " + WingmanLaunchOptions.ShortcutArguments(WingmanLaunchTarget.T3Web, profileOverride),
            LauncherShortcuts.T3WebShortcutName,
            T3WebIconResource(wingmanExecutable, icon));
    }

    /// <summary>The icon resource for T3 Code (web): T3's cached icon, or Wingman's until one is cached.</summary>
    public static string T3WebIconResource(string wingmanExecutable, string? appIcon) =>
        (appIcon is not null && File.Exists(appIcon) ? appIcon : wingmanExecutable) + ",0";

    /// <summary>Brings the existing T3 web window to the front (restoring it if minimized) through its own DevTools.</summary>
    private static async Task<bool> ActivateAsync(int port, CodexTarget page, CancellationToken cancellationToken)
    {
        try
        {
            // Let Wingman's Chrome take the foreground: the shortcut click gave it to Wingman.
            var info = await CdpBrowserCommand.SendAsync(Http, port, "SystemInfo.getProcessInfo", null, cancellationToken);
            if (CdpBrowserCommand.BrowserProcessId(info) is { } pid) AllowSetForegroundWindow(pid);
            var window = await CdpBrowserCommand.SendAsync(Http, port, "Browser.getWindowForTarget", new { targetId = page.Id }, cancellationToken);
            if (window is { ValueKind: System.Text.Json.JsonValueKind.Object } found
                && found.TryGetProperty("windowId", out var id) && id.TryGetInt32(out var windowId)
                && found.TryGetProperty("bounds", out var bounds)
                && bounds.TryGetProperty("windowState", out var state) && state.GetString() == "minimized")
                await CdpBrowserCommand.SendAsync(Http, port, "Browser.setWindowBounds",
                    new { windowId, bounds = new { windowState = "normal" } }, cancellationToken);
            await CdpBrowserCommand.SendAsync(Http, port, "Target.activateTarget", new { targetId = page.Id }, cancellationToken);
            return true;
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException or TimeoutException
            or System.Net.WebSockets.WebSocketException or System.Text.Json.JsonException
            || (error is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Debug.WriteLine($"[T3 web activate] {error.Message}");
            return false;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);

    private int SelectPort(int? remembered)
    {
        foreach (var port in T3WebLaunchPolicy.CandidatePorts(remembered, reservedPorts().Where(port => port != remembered)))
            if (IsFree(port)) return port;
        throw new IOException("No free local port was available for T3 Code (web).");
    }

    private static bool IsFree(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException) { return false; }
    }

    private static async Task<bool> EndpointAliveAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.GetAsync($"http://127.0.0.1:{port}/json/version", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }

    private void Start(string chrome, int port, Uri origin)
    {
        var start = new ProcessStartInfo(chrome) { UseShellExecute = false };
        foreach (var argument in T3WebLaunchPolicy.ChromeArguments(DataDirectory, port, origin)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Chrome did not start.");
    }
}
