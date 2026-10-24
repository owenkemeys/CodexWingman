using System.Text;
using System.Text.Json;
using CodexWingman.Core;
using CodexWingman.Core.Helpers;

/// <summary>
/// T3 web, T3 auto-pairing and Grok Bot targets. Hosts are synthetic (t3.example-tailnet.ts.net);
/// Grok Bot cache fixtures use the real file format with a fake account id and made-up numbers.
/// </summary>
internal static class AppTargetTests
{
    private const string Origin = "https://t3.example-tailnet.ts.net:8443";
    private const string FakeAccount = "auth0|000000000000000000000000";

    public static async Task RunAsync()
    {
        UrlPolicyAndPageMatch();
        LaunchPolicies();
        LaunchOptionsAndIdentities();
        SettingsRoundTrip();
        BrowserProcessAndSessionParsing();
        GrokCatalog();
        GrokUsageCache();
        await BundledHelpersForNewTargetsAsync();
        MenuPolicy();
        TrayMenuTree();
        AppNames();
        T3WebIcon();
        AppLinesDropToClosed();
        RepairAndStartupAreQuiet();
        OpenOnLaunchChoices();
        await PairingAsync();
        Console.WriteLine("App target tests passed");
    }

    private static void UrlPolicyAndPageMatch()
    {
        True(T3WebUrlPolicy.TryNormalizeOrigin(Origin + "/some/path?x=1", out var origin), "https origin normalizes");
        Equal(Origin + "/", origin!.AbsoluteUri, "origin keeps host and port only");
        False(T3WebUrlPolicy.TryNormalizeOrigin("http://t3.example-tailnet.ts.net:8443", out _), "plain http refused");
        False(T3WebUrlPolicy.TryNormalizeOrigin("https://user:pw@t3.example-tailnet.ts.net:8443", out _), "user info refused");
        False(T3WebUrlPolicy.TryNormalizeOrigin("t3code://app", out _), "desktop scheme is not a web origin");

        True(T3WebUrlPolicy.TryParsePairingLink(Origin + "/pair#token=synthetic-credential-0001", origin, out var linkOrigin, out var credential), "pairing link parses");
        Equal(origin.AbsoluteUri, linkOrigin!.AbsoluteUri, "pairing link origin");
        Equal("synthetic-credential-0001", credential, "pairing credential from fragment");
        True(T3WebUrlPolicy.TryParsePairingLink(Origin + "/pair#token=synthetic-credential-0001", null, out _, out _), "first-run link accepted without a known origin");
        False(T3WebUrlPolicy.TryParsePairingLink("https://other.example-tailnet.ts.net:8443/pair#token=synthetic-credential-0001", origin, out _, out _), "other host refused");
        False(T3WebUrlPolicy.TryParsePairingLink("https://t3.example-tailnet.ts.net:9443/pair#token=synthetic-credential-0001", origin, out _, out _), "other port refused");
        False(T3WebUrlPolicy.TryParsePairingLink(Origin + "/login#token=synthetic-credential-0001", origin, out _, out _), "non-pair path refused");
        False(T3WebUrlPolicy.TryParsePairingLink(Origin + "/pair?token=synthetic-credential-0001", origin, out _, out _), "query token refused");
        False(T3WebUrlPolicy.TryParsePairingLink(Origin + "/pair#token=short", origin, out _, out _), "short token refused");
        False(T3WebUrlPolicy.TryParsePairingLink("http://t3.example-tailnet.ts.net:8443/pair#token=synthetic-credential-0001", null, out _, out _), "http link refused");

        // Real T3 0.1.2 URL shapes: /<environment uuid>/<thread uuid>, /draft/<uuid>, /pair#token=..., and the root.
        var env = "2f8a6c3e-6b1d-4b8e-9a51-0c3d2e1f4a77";
        var thread = "9b0e1d2c-3a4f-4e5d-8c7b-6a5f4e3d2c1b";
        var targets = new[]
        {
            Target("a", "page", $"{Origin}/{env}/{thread}"),
            Target("b", "page", $"{Origin}/draft/{thread}"),
            Target("c", "page", $"{Origin}/pair#token=synthetic-credential-0001"),
            Target("d", "page", Origin + "/"),
            Target("a", "page", $"{Origin}/{env}/{thread}"),
            Target("e", "service_worker", $"{Origin}/sw.js"),
            Target("f", "page", $"https://t3.example-tailnet.ts.net:9443/{env}/{thread}"),
            Target("g", "page", "https://example.com/"),
            Target("h", "page", "t3code://app/index.html"),
            Target("i", "page", "chrome://newtab/"),
            new CodexTarget("j", "page", Origin + "/", ""),
        };
        var pages = T3WebTargetCatalog.SelectPages(targets, origin);
        Equal("a,b,c,d", string.Join(",", pages.Select(page => page.Id)), "only T3 web pages on the exact origin");
    }

    private static void LaunchPolicies()
    {
        var root = Path.Combine(Path.GetTempPath(), "wingman-root");
        var data = T3WebLaunchPolicy.DataDirectory(root);
        Equal(Path.Combine(root, "t3-web", "chrome-data"), data, "own Chrome data folder under Wingman's data");
        var args = T3WebLaunchPolicy.ChromeArguments(data, 9423, new Uri(Origin + "/"));
        Equal("--user-data-dir=" + data, args[0], "own data folder argument");
        Equal("--remote-debugging-address=127.0.0.1", args[1], "debugging bound to localhost");
        Equal("--remote-debugging-port=9423", args[2], "debugging port");
        Equal("--app=" + Origin + "/", args[^1], "app window on the T3 origin");
        False(args.Any(arg => arg.Contains("Default", StringComparison.Ordinal) || arg.Contains("profile-directory", StringComparison.Ordinal)), "never the user's normal Chrome profile");
        Throws<ArgumentException>(() => T3WebLaunchPolicy.ChromeArguments(data, 9423, new Uri("http://t3.example-tailnet.ts.net/")), "http start url refused");
        Throws<ArgumentException>(() => T3WebLaunchPolicy.ChromeArguments("relative", 9423, new Uri(Origin)), "relative data folder refused");
        True(T3WebLaunchPolicy.IsWingmanChromeCommandLine("chrome.exe --user-data-dir=" + data + " --app=x", data), "own Chrome recognized");
        False(T3WebLaunchPolicy.IsWingmanChromeCommandLine("chrome.exe --profile-directory=Default", data), "normal Chrome not recognized");

        Equal(T3WebOpenAction.StartNew, T3WebLaunchPolicy.OpenAction(false, 0), "nothing running: start Wingman's Chrome");
        Equal(T3WebOpenAction.OpenWindowInRunningProfile, T3WebLaunchPolicy.OpenAction(true, 0), "Chrome runs without a T3 web window: open one");
        Equal(T3WebOpenAction.ActivateExisting, T3WebLaunchPolicy.OpenAction(true, 1), "a T3 web window exists: bring it forward, no second window");
        Equal(T3WebOpenAction.ActivateExisting, T3WebLaunchPolicy.OpenAction(true, 3), "several windows: still none added");

        Equal("4100", string.Join(",", WingmanForegroundPolicy.Recipients([(4100, 1), (4200, 1)], 4200, 1)), "second launch lets only the running Wingman come forward, not itself");
        Equal("", string.Join(",", WingmanForegroundPolicy.Recipients([(4100, 0), (4300, null), (4200, 1)], 4200, 1)), "other sessions and unreadable processes get no foreground right");
        Equal("4100", string.Join(",", WingmanForegroundPolicy.Recipients([(4100, 1), (4100, 1), (0, 1)], 4200, 1)), "each process once, never pid 0");

        var webPorts = T3WebLaunchPolicy.CandidatePorts(null, [9423]).Take(3).ToArray();
        False(webPorts.Contains(9423), "reserved port skipped");
        False(webPorts.Contains(CodexLaunchPolicy.PreferredPort) || webPorts.Contains(T3CodeLaunchPolicy.PreferredPort), "Codex and T3 ports never reused");
        Equal(9500, T3WebLaunchPolicy.CandidatePorts(9500, []).First(), "remembered port first");

        var grokArgs = GrokBotLaunchPolicy.Arguments(9523);
        Equal("--remote-debugging-address=127.0.0.1|--remote-debugging-port=9523", string.Join("|", grokArgs), "Grok Bot gets only localhost debugging flags");
        False(grokArgs.Any(arg => arg.Contains("user-data", StringComparison.Ordinal)), "Grok Bot keeps its own user data (login kept)");
        Equal(Path.Combine("C:\\Users\\x\\AppData\\Local", "Programs", "Grok Bot", "Grok Bot.exe"),
            GrokBotLaunchPolicy.ExecutablePath("C:\\Users\\x\\AppData\\Local"), "Grok Bot install path");
        True(GrokBotLaunchPolicy.IsGrokBotExecutable("C:\\Users\\x\\AppData\\Local\\Programs\\Grok Bot\\Grok Bot.exe"), "Grok Bot exe recognized");
        False(GrokBotLaunchPolicy.IsGrokBotExecutable("C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe"), "other exe not Grok Bot");
        var grokPorts = GrokBotLaunchPolicy.CandidatePorts(null, []).Take(5).ToArray();
        False(grokPorts.Contains(T3WebLaunchPolicy.PreferredPort) || grokPorts.Contains(CodexLaunchPolicy.PreferredPort), "Grok Bot port distinct");

        var portFile = Path.Combine(Path.GetTempPath(), "wingman-port-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Equal<int?>(null, AppRuntimePortFile.Read(portFile), "missing port file");
            AppRuntimePortFile.Write(portFile, 9424);
            Equal<int?>(9424, AppRuntimePortFile.Read(portFile), "port file round trip");
            File.WriteAllText(portFile, "{\"port\":80}");
            Equal<int?>(null, AppRuntimePortFile.Read(portFile), "privileged port refused");
        }
        finally { if (File.Exists(portFile)) File.Delete(portFile); }
    }

    private static void LaunchOptionsAndIdentities()
    {
        Equal(WingmanLaunchTarget.T3Web, WingmanLaunchOptions.Parse(["--launch=t3-web"]).Target, "t3-web launch argument");
        Equal(WingmanLaunchTarget.GrokBot, WingmanLaunchOptions.Parse(["--launch=grok-bot"]).Target, "grok-bot launch argument");
        Equal("CodexWingman.Desktop.v1", WingmanIdentity.AppUserModelId, "base AppUserModelID unchanged");
        Equal("CodexWingman.Desktop.v1.Codex", WingmanLaunchOptions.AppUserModelIdFor(WingmanLaunchTarget.Codex), "Codex AppUserModelID unchanged");
        Equal("CodexWingman.Desktop.v1.T3Code", WingmanLaunchOptions.AppUserModelIdFor(WingmanLaunchTarget.T3Code), "T3 desktop AppUserModelID unchanged");
        Equal("CodexWingman.Desktop.v1.T3Web", WingmanLaunchOptions.AppUserModelIdFor(WingmanLaunchTarget.T3Web), "T3 web AppUserModelID");
        Equal("CodexWingman.Desktop.v1.GrokBot", WingmanLaunchOptions.AppUserModelIdFor(WingmanLaunchTarget.GrokBot), "Grok Bot AppUserModelID");
        Equal("--launch=t3-web", WingmanLaunchOptions.ShortcutArguments(WingmanLaunchTarget.T3Web), "T3 web shortcut argument");
        Equal("--launch=grok-bot", WingmanLaunchOptions.ShortcutArguments(WingmanLaunchTarget.GrokBot), "Grok Bot pin argument");
        var preferences = WingmanLaunchOptions.Parse([]);
        False(preferences.OpenOnStartup(WingmanLaunchTarget.T3Web, HelperSettings.Default), "T3 web does not open on launch by default");
        False(preferences.OpenOnStartup(WingmanLaunchTarget.GrokBot, HelperSettings.Default), "Grok Bot does not open on launch by default");
        True(WingmanLaunchOptions.Parse(["--launch=grok-bot"]).OpenOnStartup(WingmanLaunchTarget.GrokBot, HelperSettings.Default), "Grok Bot shortcut opens Grok Bot");
    }

    private static void SettingsRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "wingman-settings-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            False(HelperSettings.Default.T3Web?.IsConfigured == true, "T3 web unconfigured by default");
            var settings = HelperSettings.Default with
            {
                T3Web = new T3WebSettings(Origin, new T3PairingSettings(Folder: "R:\\ops\\sync-share\\requests\\t3-pairing"), OpenOnLaunch: true),
                OpenGrokBotOnLaunch = true,
            };
            HelperSettingsStore.Save(path, settings);
            var loaded = HelperSettingsStore.Load(path);
            Equal(Origin, loaded.T3Web?.Url, "T3 web url round trip");
            Equal("R:\\ops\\sync-share\\requests\\t3-pairing", loaded.T3Web?.Pairing?.Folder, "pairing folder round trip");
            True(loaded.T3Web?.IsConfigured == true, "configured T3 web");
            True(loaded.OpenGrokBotOnLaunch, "Grok Bot open-on-launch round trip");
            True(new T3WebSettings(Pairing: new T3PairingSettings(Folder: "X:\\p")).IsConfigured, "pairing folder alone is enough (zero setup)");
        }
        finally
        {
            var directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void BrowserProcessAndSessionParsing()
    {
        using (var info = JsonDocument.Parse("""{"processInfo":[{"type":"renderer","id":200,"cpuTime":0},{"type":"browser","id":4120,"cpuTime":1.5},{"type":"GPU","id":300}]}"""))
            Equal<int?>(4120, CdpBrowserCommand.BrowserProcessId(info.RootElement), "browser pid from SystemInfo.getProcessInfo");
        using (var info = JsonDocument.Parse("""{"processInfo":[{"type":"renderer","id":200}]}"""))
            Equal<int?>(null, CdpBrowserCommand.BrowserProcessId(info.RootElement), "no browser entry");
        Equal<int?>(null, CdpBrowserCommand.BrowserProcessId(null), "no result");

        using (var session = JsonDocument.Parse("""{"authenticated":true,"expiresAt":"2026-11-21T01:00:00.000Z","origin":"https://t3.example-tailnet.ts.net:8443"}"""))
        {
            var status = CdpT3WebPage.Parse(session.RootElement);
            True(status.Authenticated, "authenticated session");
            Equal<DateTimeOffset?>(DateTimeOffset.Parse("2026-11-21T01:00:00Z"), status.ExpiresAtUtc, "session expiry");
        }
        using (var session = JsonDocument.Parse("""{"authenticated":false,"expiresAt":null}"""))
            False(CdpT3WebPage.Parse(session.RootElement).Authenticated, "pairing screen session");
        False(CdpT3WebPage.Parse(null).Authenticated, "no value means signed out");
    }

    private static void GrokCatalog()
    {
        const string renderer = "file:///C:/Users/x/AppData/Local/Programs/Grok%20Bot/resources/app.asar/dist/renderer/index.html";
        var pages = GrokBotTargetCatalog.SelectPages(
        [
            Target("main", "page", renderer),
            Target("main", "page", renderer),
            Target("hash", "page", renderer + "#/chat/1"),
            Target("web", "webview", "https://example.com/"),
            Target("http", "page", "https://example.com/resources/app.asar/dist/renderer/index.html"),
            Target("other", "page", "file:///C:/Users/x/AppData/Local/Programs/Grok%20Bot/resources/app.asar/dist/renderer/settings.html"),
            new CodexTarget("noSocket", "page", renderer, ""),
        ]);
        Equal("main,hash", string.Join(",", pages.Select(page => page.Id)), "Grok Bot main renderer pages only");
    }

    private static void GrokUsageCache()
    {
        Equal("mzxw6ytboi", GrokBotUsageCacheFile.EncodeName("foobar"), "RFC 4648 base32, lowercase, unpadded");
        Equal("foobar", GrokBotUsageCacheFile.DecodeName("mzxw6ytboi"), "base32 decode");
        var key = $"sand.client.slice.account.{FakeAccount}.weekly-usage.cache";
        True(GrokBotUsageCacheFile.IsUsageKey(key), "weekly usage key recognized");
        False(GrokBotUsageCacheFile.IsUsageKey("sand.client.slice.account.x.session"), "other slices ignored");
        Equal(key, GrokBotUsageCacheFile.DecodeName(GrokBotUsageCacheFile.EncodeName(key)), "key round trip");

        var now = DateTimeOffset.FromUnixTimeMilliseconds(1_792_800_000_000);
        var userData = Path.Combine(Path.GetTempPath(), "grok-fixture-" + Guid.NewGuid().ToString("N"));
        var persistence = GrokBotUsageCacheFile.PersistenceDirectory(userData);
        Directory.CreateDirectory(persistence);
        try
        {
            Equal<GrokBotUsageReading?>(null, GrokBotUsageCacheFile.Read(userData, now), "no cache file");
            // Sanitized real-format fixture: same structure as Grok Bot's file, fake account id, made-up numbers.
            File.WriteAllText(Path.Combine(persistence, GrokBotUsageCacheFile.EncodeName(key) + ".blob"),
                Fixture(22.58, now.AddMinutes(-3), now.AddMinutes(30)));
            File.WriteAllText(Path.Combine(persistence, GrokBotUsageCacheFile.EncodeName("sand.client.slice.other.cache") + ".blob"),
                Fixture(99, now, now.AddMinutes(30)));
            var reading = GrokBotUsageCacheFile.Read(userData, now)!;
            Equal(22.58, reading.PercentUsed, "dial percent equals cache percentUsed");
            Equal(now.AddMinutes(-3).ToUnixTimeMilliseconds(), reading.ReadAtMs, "read time from cache");
            using (var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(GrokBotUsageCacheFile.Snapshot(userData, now))))
            {
                Equal(22.58, snapshot.RootElement.GetProperty("percentUsed").GetDouble(), "snapshot percent");
                False(snapshot.RootElement.GetRawText().Contains("auth0", StringComparison.Ordinal), "account id never reaches the Helper");
            }

            // A long key stored as .kblob: first line is the JSON key; the newest reading wins.
            File.WriteAllText(Path.Combine(persistence, "0123456789abcdef.kblob"),
                JsonSerializer.Serialize(key) + "\n" + Fixture(61.2, now.AddMinutes(-1), now.AddMinutes(30)));
            Equal(61.2, GrokBotUsageCacheFile.Read(userData, now)!.PercentUsed, "kblob variant, newest wins");

            // Other slices stored as .kblob: only their key line is read, never the body.
            File.WriteAllText(Path.Combine(persistence, "fedcba9876543210.kblob"),
                JsonSerializer.Serialize("sand.client.slice.account.auth0|000000000000000000000000.private-notes") + "\n{\"secret\":\"x\"}");
            File.WriteAllText(Path.Combine(persistence, "aaaaaaaaaaaaaaaa.kblob"),
                JsonSerializer.Serialize("sand.client.slice.other") + "\n" + new string('x', 200_000));
            File.WriteAllText(Path.Combine(persistence, "bbbbbbbbbbbbbbbb.kblob"), new string('k', 5000) + "\n{}");
            var readsBefore = GrokBotUsageCacheFile.BodyReads;
            Equal(61.2, GrokBotUsageCacheFile.Read(userData, now)!.PercentUsed, "unrelated kblobs do not disturb the reading");
            Equal(readsBefore + 2, GrokBotUsageCacheFile.BodyReads, "bodies read only for the two weekly-usage files");
            Equal("sand.client.slice.other", GrokBotUsageCacheFile.ReadKblobKey(Path.Combine(persistence, "aaaaaaaaaaaaaaaa.kblob")), "key line read on its own");
            Equal<string?>(null, GrokBotUsageCacheFile.ReadKblobKey(Path.Combine(persistence, "bbbbbbbbbbbbbbbb.kblob")), "over-long key line rejected");
            Equal<string?>(null, GrokBotUsageCacheFile.ReadKblobUsageBody(Path.Combine(persistence, "fedcba9876543210.kblob")), "other key: body not returned");
            foreach (var other in new[] { "fedcba9876543210", "aaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbb" })
                File.Delete(Path.Combine(persistence, other + ".kblob"));

            Equal<GrokBotUsageReading?>(null, GrokBotUsageCacheFile.Read(userData, now.AddHours(25)), "stale cache (over 24 h) shows nothing");
            foreach (var file in Directory.EnumerateFiles(persistence)) File.Delete(file);
            File.WriteAllText(Path.Combine(persistence, GrokBotUsageCacheFile.EncodeName(key) + ".blob"),
                Fixture(40, now.AddMinutes(-10), now.AddMinutes(-1)));
            Equal<GrokBotUsageReading?>(null, GrokBotUsageCacheFile.Read(userData, now), "expired cache shows nothing");
            File.WriteAllText(Path.Combine(persistence, GrokBotUsageCacheFile.EncodeName(key) + ".blob"), "{\"schemaVersion\":3}");
            Equal<GrokBotUsageReading?>(null, GrokBotUsageCacheFile.Read(userData, now), "unknown schema fails closed");
            File.WriteAllText(Path.Combine(persistence, GrokBotUsageCacheFile.EncodeName(key) + ".blob"), new string(' ', GrokBotUsageCacheFile.MaxBytes + 1));
            Equal<GrokBotUsageReading?>(null, GrokBotUsageCacheFile.Read(userData, now), "oversized cache ignored");
            Equal<GrokBotUsageReading?>(null, GrokBotUsageCacheFile.Parse("""{"schemaVersion":2,"value":{"kind":"absent"}}"""), "absent value");
            Equal(100.0, GrokBotUsageCacheFile.Parse(Fixture(140, now, null))!.PercentUsed, "percent clamped");
        }
        finally { Directory.Delete(userData, true); }
    }

    private static async Task BundledHelpersForNewTargetsAsync()
    {
        var bundled = Path.Combine(RepositoryRoot(), "helpers");
        var userRoot = Path.Combine(Path.GetTempPath(), "wingman-user-helpers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(userRoot);
        var userData = Path.Combine(Path.GetTempPath(), "grok-fixture-" + Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = new HelperCatalog();
            var web = catalog.DiscoverForApp(HelperAppIds.T3Web, bundled, userRoot);
            var webIds = web.Packages.Select(package => package.Manifest.Id).ToHashSet();
            True(webIds.Contains("usage-dials") && webIds.Contains("obsidian-links") && webIds.Contains("clickable-file-links"),
                "T3 web gets usage dials, Obsidian links and clickable file links");
            var grok = catalog.DiscoverForApp(HelperAppIds.GrokBot, bundled, userRoot);
            Equal("usage-dials", string.Join(",", grok.Packages.Select(package => package.Manifest.Id)), "Grok Bot gets only the usage dial");
            var dials = grok.Packages.Single();
            True(dials.BackendSource?.Contains("grokBotUsage", StringComparison.Ordinal) == true, "Grok Bot target uses backend.grok.js");

            var now = DateTimeOffset.UtcNow;
            var persistence = GrokBotUsageCacheFile.PersistenceDirectory(userData);
            Directory.CreateDirectory(persistence);
            File.WriteAllText(Path.Combine(persistence,
                GrokBotUsageCacheFile.EncodeName($"sand.client.slice.account.{FakeAccount}.weekly-usage.cache") + ".blob"),
                Fixture(22.58, now.AddMinutes(-2), now.AddMinutes(30)));
            var backend = new HelperBackend([], () => now, grokBotUserData: userData);
            using (var state = await backend.RefreshAsync(dials))
            {
                var grokState = state.RootElement.GetProperty("grokBot");
                True(grokState.GetProperty("available").GetBoolean(), "bundled backend sees the cache");
                Equal(22.58, grokState.GetProperty("percentUsed").GetDouble(), "bundled Grok Bot backend: percent equals the cache");
            }
            using (var state = await new HelperBackend([], () => now).RefreshAsync(dials))
                False(state.RootElement.GetProperty("grokBot").GetProperty("available").GetBoolean(), "no Grok Bot data folder: unavailable");

            var webDials = web.Packages.Single(package => package.Manifest.Id == "usage-dials");
            False(webDials.BackendSource?.Contains("grokBotUsage", StringComparison.Ordinal) == true, "T3 web uses the provider usage backend");

            var apply = new HelperRenderer(HelperAppIds.T3Web).BuildApply(webDials, EmptyState(), "p");
            True(apply.Contains("app:\"t3-web\"", StringComparison.Ordinal), "renderer tells the Helper it runs on T3 web");
            var grokApply = new HelperRenderer(HelperAppIds.GrokBot).BuildApply(dials, EmptyState(), "g");
            True(grokApply.Contains("app:\"grok-bot\"", StringComparison.Ordinal), "renderer tells the Helper it runs in Grok Bot");
        }
        finally
        {
            Directory.Delete(userRoot, true);
            if (Directory.Exists(userData)) Directory.Delete(userData, true);
        }
    }

    private static void MenuPolicy()
    {
        var closed = AppTargetMenuPolicy.Evaluate("Grok Bot", 0, false, null, null, false);
        True(closed.Label.StartsWith("Grok Bot", StringComparison.Ordinal), "menu label names the app: " + closed.Label);
        False(closed.Label.Contains("T3 Code", StringComparison.Ordinal) || closed.Details.Contains("T3 Code", StringComparison.Ordinal), "no T3 Code wording for Grok Bot");
        var web = AppTargetMenuPolicy.Evaluate(TrayMenuLayout.AppName(WingmanLaunchTarget.T3Web), 1, true, null, new T3CodeConnectionReport(1, 1, 0), true);
        Equal("T3 Code (web): Needs repair", web.Label, "T3 Code (web) label");
        True(web.Details.Contains("of 1 visible T3 Code (web) window", StringComparison.Ordinal), "counts live in Status details: " + web.Details);
    }

    private static byte[] Png(int size)
    {
        var png = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(png, 0);
        png[18] = (byte)(size >> 8); png[19] = (byte)size; png[22] = (byte)(size >> 8); png[23] = (byte)size;
        return png;
    }

    /// <summary>Round 3d: T3 Code (web) shows T3's own icon, from the site's public manifest, cached as an .ico.</summary>
    private static void T3WebIcon()
    {
        var origin = new Uri(Origin);
        // The manifest T3 serves (shape as on the trial host, 2026-10-23).
        const string manifest = """
            { "name": "T3 Code", "icons": [
              { "src": "/favicon-32x32.png", "sizes": "32x32", "type": "image/png" },
              { "src": "/apple-touch-icon.png", "sizes": "180x180", "type": "image/png" },
              { "src": "/mask.png", "sizes": "512x512", "type": "image/png", "purpose": "maskable" },
              { "src": "https://other.example/evil.png", "sizes": "256x256", "type": "image/png" } ] }
            """;
        Equal(new Uri(origin, "/manifest.webmanifest"), T3WebAppIcon.ManifestUrl("<link rel=\"manifest\" href=\"/manifest.webmanifest\" />", origin), "manifest link");
        Equal(new Uri(origin, "/manifest.webmanifest"), T3WebAppIcon.ManifestUrl(null, origin), "default manifest path");
        Equal(new Uri(origin, "/manifest.webmanifest"), T3WebAppIcon.ManifestUrl("<link rel=\"manifest\" href=\"https://other.example/m.json\">", origin), "other-origin manifest ignored");
        var icons = T3WebAppIcon.PngIcons(manifest, new Uri(origin, "/manifest.webmanifest"));
        Equal("/apple-touch-icon.png|/favicon-32x32.png", string.Join("|", icons.Select(icon => icon.AbsolutePath)), "PNG app icons, largest first; maskable and other origins skipped");
        True(T3WebAppIcon.PngSize(Png(180)) == (180, 180), "PNG size from IHDR");
        True(T3WebAppIcon.PngSize([1, 2, 3]) is null, "not a PNG");
        var ico = T3WebAppIcon.BuildIco([Png(180), Png(32)]);
        True(T3WebAppIcon.IsIco(ico) && ico[4] == 2, "an .ico with two images");
        Equal(32, (int)ico[6], "smallest image first");
        Equal(180, (int)ico[22], "180 px entry");
        Equal(6 + 32, BitConverter.ToInt32(ico, 6 + 12), "first image right after the directory");
        Equal(0, (int)T3WebAppIcon.BuildIco([Png(256)])[6], "256 px is stored as 0");
        True(T3WebAppIcon.NeedsRefresh(null, DateTime.UtcNow) && T3WebAppIcon.NeedsRefresh(DateTime.UtcNow.AddDays(-8), DateTime.UtcNow)
            && !T3WebAppIcon.NeedsRefresh(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow), "weekly refresh");
        Equal(Path.Combine("root", "t3-web", "app-icon.ico"), T3WebAppIcon.IconPath("root"), "cache path");
        // Wiring: the Start shortcut (matched by AppUserModelID) and the window's relaunch icon both use T3's icon.
        var source = Path.Combine(RepositoryRoot(), "src", "CodexWingman");
        var shortcuts = File.ReadAllText(Path.Combine(source, "LauncherShortcuts.cs"));
        True(shortcuts.Contains("item.Target == WingmanLaunchTarget.T3Web ? T3WebLauncher.T3WebIconResource(executable, t3WebIcon)", StringComparison.Ordinal),
            "T3 Code (web) Start shortcut uses T3's icon");
        var launcher = File.ReadAllText(Path.Combine(source, "T3WebLauncher.cs"));
        True(launcher.Contains("T3WebIconResource(wingmanExecutable, icon));", StringComparison.Ordinal), "window relaunch icon is T3's");
        var identity = File.ReadAllText(Path.Combine(source, "WindowAppIdentity.cs"));
        True(identity.Contains("if (hasId) Clear(store, idKey);", StringComparison.Ordinal), "an open window picks up the new icon");
        Equal("CodexWingman.Desktop.v1.T3Web", WingmanLaunchOptions.AppUserModelIdFor(WingmanLaunchTarget.T3Web), "AppUserModelID unchanged, so grouping and pins keep working");
    }

    /// <summary>One name per app, the same in the menu line, dialogs and balloons.</summary>
    private static void AppNames()
    {
        Equal("Codex|T3 Code|T3 Code (web)|Grok Bot", string.Join("|", TrayMenuLayout.Apps.Select(TrayMenuLayout.AppName)), "app names");
        var source = Path.Combine(RepositoryRoot(), "src");
        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
            foreach (var literal in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), "\"(?:[^\"\\\n]|\\.)*\"").Select(match => match.Value))
                False(literal.Contains("T3 web", StringComparison.Ordinal) && !literal.StartsWith("\"[T3 web", StringComparison.Ordinal)
                    && !literal.StartsWith("\"Wingman T3 web (", StringComparison.Ordinal),
                    $"user-facing text says T3 Code (web), not T3 web: {Path.GetFileName(file)} {literal}");
    }

    /// <summary>Round 3b bug: the line read "T3 web: OK (1/1)" after the window had closed. Presence is live now.</summary>
    private static void AppLinesDropToClosed()
    {
        var name = TrayMenuLayout.AppName(WingmanLaunchTarget.T3Web);
        var lastCheck = new T3CodeConnectionReport(1, 1, 0);
        var ready = new HelperHostReport(1, 1, 1, 0, []);
        Equal(1, AppPresence.EndpointWindows(true, lastCheck), "endpoint up: the last check's window count stands");
        Equal("T3 Code (web): Running", AppTargetMenuPolicy.Evaluate(name, AppPresence.EndpointWindows(true, lastCheck), true, ready, lastCheck, false).Label, "open and connected");
        // The window closes: nothing listens on the port any more, but the last check still says 1 window.
        Equal(0, AppPresence.EndpointWindows(false, lastCheck), "endpoint gone: no window, whatever the last check said");
        Equal("T3 Code (web): Closed", AppTargetMenuPolicy.Evaluate(name, AppPresence.EndpointWindows(false, lastCheck), true, ready, lastCheck, false).Label,
            "closed window reads Closed at once");
        Equal("Grok Bot: Closed", AppTargetMenuPolicy.Evaluate("Grok Bot", 0, true, ready, lastCheck, false).Label, "same rule for Grok Bot");
        Equal("T3 Code: Closed", T3CodeMenuPolicy.Evaluate(0, true, ready, lastCheck, false).Label, "same rule for T3 Code");
        False(AppPresence.IsListening(null), "no port, nothing listening");
        using (var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0))
        {
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            True(AppPresence.IsListening(port), "a listening loopback port is seen");
            listener.Stop();
            False(AppPresence.IsListening(port), "a stopped port is seen as closed");
        }
        // Wiring: the T3 Code (web) line counts windows live and every tick updates the lines before the busy gate.
        var tray = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        True(tray.Contains("AppPresence.EndpointWindows(AppPresence.IsListening(t3WebLauncher.Port())", StringComparison.Ordinal), "T3 Code (web) presence is live");
        var tick = tray.IndexOf("private async Task RefreshTimerAsync()", StringComparison.Ordinal);
        True(tick >= 0 && tray.IndexOf("RefreshAppLinePresence();", tick, StringComparison.Ordinal)
            < tray.IndexOf("automaticRefreshGate.TryEnter", tick, StringComparison.Ordinal), "presence refresh runs even when the full refresh is skipped");
        True(tray.IndexOf("RewriteMenuDumpIfLinesChanged();", tick, StringComparison.Ordinal) >= 0
            && tray.IndexOf("RewriteMenuDumpIfLinesChanged();", tick, StringComparison.Ordinal) < tray.IndexOf("automaticRefreshGate.TryEnter", tick, StringComparison.Ordinal),
            "development builds rewrite menu-dump.json when a line changes, even while busy");
    }

    private static void RepairAndStartupAreQuiet()
    {
        Equal(T3CodeRepairStep.Reattached, T3CodeRepairPolicy.Decide(2, 2, false), "healthy T3 Code: Repair asks nothing");
        Equal(T3CodeRepairStep.RestartForUnreachedWindows, T3CodeRepairPolicy.Decide(2, 1, false), "an unreached window: restart needed, ask");
        Equal(T3CodeRepairStep.OpenRoute, T3CodeRepairPolicy.Decide(0, 0, false), "no window: open one");
        Equal(T3CodeRepairStep.OpenRoute, T3CodeRepairPolicy.Decide(2, 0, true), "check failed: Open route (asks only if a restart is needed)");
        var tray = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        True(tray.Contains("EnsureT3WebReadyAsync(atStartup: true)", StringComparison.Ordinal) && tray.Contains("EnsureGrokBotReadyAsync(atStartup: true)", StringComparison.Ordinal),
            "Open when Wingman starts uses the quiet start");
        True(tray.Contains("atStartup ? () => false : ConfirmGrokBotRelaunch", StringComparison.Ordinal), "no relaunch question at startup");
        True(tray.Contains("await CheckT3WebPairingAsync(interactive: false);\n    }\n\n    /// <summary>\n    /// Repair for Grok Bot.", StringComparison.Ordinal)
            || tray.Replace("\r\n", "\n").Contains("await CheckT3WebPairingAsync(interactive: false);\n    }\n\n    /// <summary>\n    /// Repair for Grok Bot.", StringComparison.Ordinal),
            "T3 Code (web) Repair shows a balloon only when the sign-in check failed");
    }

    private static TrayMenuNode Line(string text, IEnumerable<string>? items = null, string? tooltip = null) =>
        new(text, tooltip, Children: (items ?? (TrayMenuCheck.AppOf(text) is { } app ? TrayMenuLayout.FlyoutLabels(app)
                : TrayMenuLayout.AppFlyout.Select(TrayMenuLayout.Label)))
            .Select(item => new TrayMenuNode(item)).ToList());

    private static void TrayMenuTree()
    {
        var labels = TrayMenuLayout.AppFlyout.Select(TrayMenuLayout.Label).ToArray();
        Equal("Open window|Repair|Status details...|-|Open when Wingman starts", string.Join("|", labels), "one short flyout, plain wording, fixed order");
        False(labels.Any(label => label.Contains("Codex", StringComparison.Ordinal) || label.Contains("T3", StringComparison.Ordinal)
            || label.Contains("Grok", StringComparison.Ordinal)), "flyout wording names no app, so it is identical for every app");
        False(labels.Any(label => label.Contains("sign-in", StringComparison.OrdinalIgnoreCase) || label.Contains("pin", StringComparison.OrdinalIgnoreCase)),
            "automatic sign-in renewal and the taskbar pin are not menu items");
        Equal("Codex,T3Code,T3Web,GrokBot", string.Join(",", TrayMenuLayout.Apps), "app lines top to bottom");
        False(TrayMenuLayout.ShowAppLine(WingmanLaunchTarget.T3Code, installed: false), "T3 Code line hidden when T3 Code is not installed");
        True(TrayMenuLayout.ShowAppLine(WingmanLaunchTarget.T3Code, installed: true), "T3 Code line shown when installed");
        False(TrayMenuLayout.ShowAppLine(WingmanLaunchTarget.GrokBot, installed: false), "Grok Bot line hidden when not installed");
        False(TrayMenuLayout.ShowAppLine(WingmanLaunchTarget.T3Web, installed: false), "T3 web line hidden without Chrome and an address");
        True(TrayMenuLayout.ShowAppLine(WingmanLaunchTarget.Codex, installed: false), "Codex is Wingman's base app and always shows");

        // The live tray (AppFlyoutMenu) builds every line from the same layout; this is the tree it produces.
        var tree = new List<TrayMenuNode>
        {
            Line("Codex: Closed"), Line("T3 Code: Not ready"), Line("T3 Code (web): Running"), Line("Grok Bot: Running"),
            new("-"), new("Helpers enabled", Checked: true), new("Manage helpers", Children: [new("Usage Dials", "Usage Dials 1.0")]),
            new("-"), new("About"), new("Check for Updates"), new("Close"),
        };
        Equal(0, TrayMenuCheck.Problems(tree).Count, "contract holds: " + string.Join("; ", TrayMenuCheck.Problems(tree)));
        var lines = TrayMenuCheck.AppLines(tree);
        Equal(4, lines.Count, "four app lines");
        // Identical flyouts, except Codex's first item: "New window" (it opens an additional Codex window).
        True(lines.Skip(1).All(line => line.Children!.Select(child => child.Text).SequenceEqual(labels)),
            "T3 Code, T3 Code (web) and Grok Bot have the identical item list");
        Equal("New window|Repair|Status details...|-|Open when Wingman starts", string.Join("|", lines[0].Children!.Select(child => child.Text)),
            "Codex differs only in its first label");
        Equal(1, lines[0].Children!.Select(child => child.Text).Zip(labels).Count(pair => pair.First != pair.Second), "exactly one label differs");
        foreach (var app in TrayMenuLayout.Apps.Where(app => app != WingmanLaunchTarget.Codex))
            Equal("Open window", TrayMenuLayout.Label(app, AppFlyoutItem.OpenWindow), TrayMenuLayout.AppName(app) + " keeps Open window");
        var codexOpenWindow = tree.ToList();
        codexOpenWindow[0] = Line("Codex: Closed", labels);
        True(TrayMenuCheck.Problems(codexOpenWindow).Any(problem => problem.Contains("Codex", StringComparison.Ordinal)), "Codex with Open window is caught");
        var grokNewWindow = tree.ToList();
        grokNewWindow[3] = Line("Grok Bot: Running", TrayMenuLayout.FlyoutLabels(WingmanLaunchTarget.Codex));
        True(TrayMenuCheck.Problems(grokNewWindow).Any(problem => problem.Contains("Grok Bot", StringComparison.Ordinal)), "New window on another app is caught");
        True(lines.All(line => line.ToolTip is null), "no first-layer tooltips");

        var withTooltip = tree.ToList();
        withTooltip[3] = Line("Grok Bot: Running", tooltip: "Grok Bot: Running\n\nGrok Bot is open.");
        True(TrayMenuCheck.Problems(withTooltip).Any(problem => problem.Contains("tooltip", StringComparison.Ordinal)), "a first-layer tooltip is caught");
        var extraItem = tree.ToList();
        extraItem[2] = Line("T3 Code (web): Running", labels.Append("Check T3 sign-in now"));
        True(TrayMenuCheck.Problems(extraItem).Any(problem => problem.Contains("T3 Code (web)", StringComparison.Ordinal)), "an extra flyout item is caught");
        var counted = tree.ToList();
        counted[0] = Line("Codex: OK (2/2)");
        True(TrayMenuCheck.Problems(counted).Any(problem => problem.Contains("plain state", StringComparison.Ordinal)), "a count on the line is caught");
        Equal("Codex: Closed", TrayMenuLayout.LineText(WingmanLaunchTarget.Codex, "Closed"), "line text");
        True(TrayMenuCheck.IsLineText("T3 Code (web): Needs repair") && !TrayMenuCheck.IsLineText("T3 web: OK"), "line text check");
        True(TrayMenuCheck.IsLineText("Grok Bot: Running") && !TrayMenuCheck.IsLineText("Grok Bot: OK"), "an open app reads Running, not OK");
        True(TrayMenuLayout.LineStates.Contains("Running") && TrayMenuLayout.LineStates.Contains("Closed") && TrayMenuLayout.LineStates.Contains("Needs repair")
            && !TrayMenuLayout.LineStates.Contains("OK"), "line states: Running, Closed, Needs repair (no OK)");
        var reordered = tree.ToList();
        reordered[2] = Line("T3 Code (web): Running", [labels[1], labels[0], .. labels[2..]]);
        True(TrayMenuCheck.Problems(reordered).Count > 0, "a different order is caught");

        // Structural guard on the tray source: lines come only from AppFlyoutMenu and nothing sets a status tooltip.
        var source = Path.Combine(RepositoryRoot(), "src", "CodexWingman");
        var tray = File.ReadAllText(Path.Combine(source, "TrayApplicationContext.cs"));
        Equal(4, CountOf(tray, "new AppFlyoutMenu("), "the tray builds all four app lines with AppFlyoutMenu");
        False(tray.Contains("DropDownItems.AddRange([", StringComparison.Ordinal) && tray.Contains("Menu.DropDownItems", StringComparison.Ordinal),
            "no extra items are added to an app line");
        foreach (var file in Directory.GetFiles(source, "*.cs"))
        {
            var text = File.ReadAllText(file);
            var name = Path.GetFileName(file);
            var allowed = name switch { "AppFlyoutMenu.cs" => 1, "TrayApplicationContext.cs" => 2, _ => 0 };
            Equal(allowed, CountOf(text, "ToolTipText ="), name + ": only the helper items keep a tooltip (their own name or fault)");
        }
        Equal(2, CountOf(tray, "ToolTipText = helper.Diagnostic ??"), "helper item tooltips are the name/version or the fault");
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    private static void OpenOnLaunchChoices()
    {
        var settings = HelperSettings.Default with { OpenCodexOnLaunch = false };
        foreach (var app in TrayMenuLayout.Apps)
        {
            False(settings.OpensOnLaunch(app), app + " starts off");
            var on = settings.WithOpenOnLaunch(app, true);
            True(on.OpensOnLaunch(app), app + " Open on launch turns on");
            True(TrayMenuLayout.Apps.Where(other => other != app).All(other => !on.OpensOnLaunch(other)), app + " changes only its own choice");
            True(WingmanLaunchOptions.Parse([]).OpenOnStartup(app, on), app + " opens on a plain launch when chosen");
            False(WingmanLaunchOptions.Parse(["--launch=codex"]).OpenOnStartup(app == WingmanLaunchTarget.Codex ? WingmanLaunchTarget.GrokBot : app, on),
                app + ": a launcher for another app opens only that app");
            False(on.WithOpenOnLaunch(app, false).OpensOnLaunch(app), app + " turns off again");
        }
        True(HelperSettings.Default.T3Web is null && HelperSettings.Default.WithOpenOnLaunch(WingmanLaunchTarget.T3Web, true).T3Web?.OpenOnLaunch == true,
            "T3 web choice saves even before any T3 web settings exist (zero setup)");
        Throws<ArgumentOutOfRangeException>(() => HelperSettings.Default.WithOpenOnLaunch(WingmanLaunchTarget.Preferences, true), "only apps have the choice");

        var path = Path.Combine(Path.GetTempPath(), "wingman-launch-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var saved = HelperSettings.Default.WithOpenOnLaunch(WingmanLaunchTarget.T3Web, true).WithOpenOnLaunch(WingmanLaunchTarget.GrokBot, true);
            HelperSettingsStore.Save(path, saved);
            var loaded = HelperSettingsStore.Load(path);
            True(loaded.OpensOnLaunch(WingmanLaunchTarget.T3Web) && loaded.OpensOnLaunch(WingmanLaunchTarget.GrokBot), "T3 web and Grok Bot choices persist in settings");
        }
        finally
        {
            var directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        Equal(WingmanCommand.DumpMenu, WingmanLaunchOptions.Parse(["--dump-menu"]).Command, "menu dump command parses");
        Equal(WingmanCommand.T3WebCheckSignIn, WingmanLaunchOptions.Parse(["--t3-web-check-sign-in"]).Command, "sign-in check moved to a development command");
        Equal(WingmanCommand.GrokBotPin, WingmanLaunchOptions.Parse(["--grok-bot-pin"]).Command, "pin choice moved to a development command");
        Equal(WingmanLaunchOptions.Commands.Count, WingmanLaunchOptions.Commands.Select(WingmanLaunchOptions.CommandEventName).Distinct().Count(), "each command its own event");
        Equal(WingmanIdentity.ActivationEventName + ".Command.Close", WingmanLaunchOptions.CommandEventName(WingmanCommand.Close), "existing close event name unchanged (swap.ps1)");
        Equal(WingmanIdentity.ActivationEventName + ".Command.T3WebRenewNow", WingmanLaunchOptions.CommandEventName(WingmanCommand.T3WebRenewNow), "existing renew event name unchanged");
        Equal(Path.Combine("C:\\data", "menu-dump.json"), WingmanLaunchOptions.MenuDumpPath("C:\\data"), "menu dump beside settings");
    }

    private static async Task PairingAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "wingman-pairing-" + Guid.NewGuid().ToString("N"));
        var share = Path.Combine(root, "share");
        var folder = Path.Combine(share, "ops", "sync-share", "requests", "t3-pairing");
        Directory.CreateDirectory(folder);
        var statePath = Path.Combine(root, "wingman", "t3-web", "pairing.json");
        var origin = new Uri(Origin + "/");
        var label = T3PairingCoordinator.DefaultLabel("DESKTOP-TEST");
        Equal("Wingman T3 web (DESKTOP-TEST)", label, "session label names the PC only");
        Equal("Wingman T3 web (abc)", T3PairingCoordinator.DefaultLabel("a b/c"), "label is sanitized");
        try
        {
            // Zero touch: the folder is found from the the sync share receipt on this PC.
            var localAppData = Path.Combine(root, "local");
            Equal<string?>(null, SharedFolderPairingProvider.ResolveFolder(null, localAppData), "no receipt, no folder");
            Directory.CreateDirectory(Path.Combine(localAppData, "Library", "Sync", "receipts"));
            File.WriteAllText(Path.Combine(localAppData, "Library", "Sync", "receipts", "latest.json"),
                JsonSerializer.Serialize(new Dictionary<string, object> { ["share_root"] = share, ["status"] = "ok" }));
            Equal(folder, SharedFolderPairingProvider.ResolveFolder(null, localAppData), "folder from the the sync share receipt");
            Equal("X:\\explicit", SharedFolderPairingProvider.ResolveFolder(new T3PairingSettings(Folder: "X:\\explicit"), localAppData), "explicit folder wins");

            using var bridge = new FakeBridge(folder, Origin);
            var clock = DateTimeOffset.Parse("2026-10-22T20:00:00-05:00");
            var provider = new SharedFolderPairingProvider(folder, "DESKTOP-TEST", TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(20));
            var coordinator = new T3PairingCoordinator(provider, statePath, () => clock);

            // 1. Fresh Chrome data folder: the page shows the pairing screen; Wingman pairs with no prompt.
            var page = new FakePage(() => clock);
            var first = await coordinator.EnsurePairedAsync(page, null, label);
            Equal(T3PairingResult.Paired, first.Result, "fresh folder reaches a paired session: " + first.Message);
            True(page.Authenticated, "page signed in");
            Equal(1, page.Reloads, "pairing screen left after first pairing");
            Equal("pair", string.Join(",", bridge.Kinds), "one pair request, no revoke for a first pairing");
            Equal(label, bridge.Labels.Single(), "request carries the label");
            Equal<string?>(Origin, bridge.Origins.Single(), "first request names the page origin, so the host can check it");
            var state = T3PairingStateStore.Load(statePath)!;
            Equal(Origin, state.Origin, "origin remembered");
            Equal(clock.AddDays(30), state.ExpiresAtUtc, "fixed 30-day expiry remembered");
            False(File.ReadAllText(statePath).Contains("synthetic-credential", StringComparison.Ordinal), "credential never stored");
            Equal(0, Directory.EnumerateFiles(folder).Count(), "no request or answer left on the share");

            // 2. Not due: no provider call.
            clock = clock.AddDays(20);
            var quiet = await coordinator.EnsurePairedAsync(page, null, label);
            Equal(T3PairingResult.AlreadyPaired, quiet.Result, "not due yet");
            Equal(1, bridge.Kinds.Count, "no provider call when not due");

            // 3. Day 27.5 of 30: renew before expiry and revoke the previous session.
            clock = clock.AddDays(7.5);
            var renewed = await coordinator.EnsurePairedAsync(page, null, label);
            Equal(T3PairingResult.Renewed, renewed.Result, "renewed before expiry: " + renewed.Message);
            Equal("pair,pair,revoke-previous", string.Join(",", bridge.Kinds), "renewal: new link, then revoke the previous session");
            Equal(Origin, bridge.Origins[^1], "revoke names the origin");
            Equal(1, page.Reloads, "renewal does not navigate");
            Equal(clock.AddDays(30), T3PairingStateStore.Load(statePath)!.ExpiresAtUtc, "new expiry remembered");

            // 4. Simulated expiry (Chrome data kept, session gone): re-pair without a prompt, revoke the old one.
            clock = clock.AddDays(31);
            page.Expire();
            var repaired = await coordinator.EnsurePairedAsync(page, null, label);
            Equal(T3PairingResult.Paired, repaired.Result, "expired session re-pairs: " + repaired.Message);
            Equal("pair,pair,revoke-previous,pair,revoke-previous", string.Join(",", bridge.Kinds), "re-pair revokes the stale session");

            // 4b. Development test hook: a signed-in, not-due page renews now by the same path, no navigation.
            clock = clock.AddDays(1);
            var reloadsBefore = page.Reloads;
            var notDue = await coordinator.EnsurePairedAsync(page, null, label);
            Equal(T3PairingResult.AlreadyPaired, notDue.Result, "freshly paired session is not due");
            var forced = await coordinator.EnsurePairedAsync(page, null, label, forceRenew: true);
            Equal(T3PairingResult.Renewed, forced.Result, "forced renewal renews: " + forced.Message);
            Equal("pair,pair,revoke-previous,pair,revoke-previous,pair,revoke-previous", string.Join(",", bridge.Kinds),
                "forced renewal: new link, then revoke only the previous Wingman session");
            Equal(reloadsBefore, page.Reloads, "forced renewal does not navigate or prompt");

            // 5. A link for another host is never used.
            var wrongBridgeFolder = Path.Combine(root, "wrong");
            Directory.CreateDirectory(wrongBridgeFolder);
            using (new FakeBridge(wrongBridgeFolder, "https://evil.example-tailnet.ts.net:8443"))
            {
                var wrongPage = new FakePage(() => clock);
                var wrong = await new T3PairingCoordinator(
                    new SharedFolderPairingProvider(wrongBridgeFolder, "DESKTOP-TEST", TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(20)),
                    Path.Combine(root, "wrong-state.json"), () => clock).EnsurePairedAsync(wrongPage, origin, label);
                Equal(T3PairingResult.Failed, wrong.Result, "wrong origin refused");
                False(wrongPage.Authenticated || wrongPage.Exchanges > 0, "credential from a wrong origin never sent to the page");
            }

            // 6. The first-run link fetched by the launcher is used instead of a second request.
            var prefetchedPage = new FakePage(() => clock);
            var before = bridge.Kinds.Count;
            var prefetched = await new T3PairingCoordinator(provider, Path.Combine(root, "prefetched.json"), () => clock)
                .EnsurePairedAsync(prefetchedPage, null, label, default, new T3PairingLink(Origin + "/pair#token=" + FakePage.Credential));
            Equal(T3PairingResult.Paired, prefetched.Result, "prefetched link pairs");
            Equal(before, bridge.Kinds.Count, "no second pairing request");

            // 7. No answer from the host: a clear failure, nothing stuck on the share.
            var silent = Path.Combine(root, "silent");
            Directory.CreateDirectory(silent);
            var timedOut = await new T3PairingCoordinator(
                new SharedFolderPairingProvider(silent, "DESKTOP-TEST", TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(20)),
                Path.Combine(root, "silent.json"), () => clock).EnsurePairedAsync(new FakePage(() => clock), origin, label);
            Equal(T3PairingResult.Failed, timedOut.Result, "host silence fails clearly");
            Equal(0, Directory.EnumerateFiles(silent).Count(), "request removed after timeout");

            // 8. Host refusal is reported.
            bridge.Refuse = true;
            var refused = await new T3PairingCoordinator(provider, Path.Combine(root, "refused.json"), () => clock)
                .EnsurePairedAsync(new FakePage(() => clock), origin, label);
            Equal(T3PairingResult.Failed, refused.Result, "host refusal is a failure");
            True(refused.Message.Contains("refused for test", StringComparison.Ordinal), "host message passed through");
        }
        finally { Directory.Delete(root, true); }
    }

    private static string Fixture(double percent, DateTimeOffset readAt, DateTimeOffset? expires) => JsonSerializer.Serialize(new
    {
        schemaVersion = 2,
        value = new
        {
            kind = "present",
            reading = new
            {
                usage = new { percentUsed = percent, nextResetMs = readAt.AddDays(4).ToUnixTimeMilliseconds() },
                readAtMs = readAt.ToUnixTimeMilliseconds(),
            },
            expiresAtMs = expires?.ToUnixTimeMilliseconds(),
        },
    });

    private static JsonElement EmptyState()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static CodexTarget Target(string id, string type, string url) =>
        new(id, type, url, "ws://127.0.0.1:9423/devtools/page/" + id);

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "helpers", "usage-dials", "wingman.json"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root not found");
    }

    private sealed class FakePage(Func<DateTimeOffset> clock) : IT3WebPage
    {
        public const string Credential = "synthetic-credential-0001";
        private DateTimeOffset? expires;
        public bool Authenticated { get; private set; }
        public int Reloads { get; private set; }
        public int Exchanges { get; private set; }

        public void Expire()
        {
            Authenticated = false;
            expires = null;
        }

        public Task<T3SessionStatus> GetSessionAsync(CancellationToken cancellationToken)
        {
            if (Authenticated && expires is { } end && clock() >= end) Expire();
            return Task.FromResult(new T3SessionStatus(Authenticated, expires, Origin));
        }

        public Task<T3SessionStatus> ExchangePairingCredentialAsync(string credential, CancellationToken cancellationToken)
        {
            Exchanges++;
            if (credential.StartsWith("synthetic-credential-", StringComparison.Ordinal))
            {
                Authenticated = true;
                expires = clock().AddDays(30);
            }
            return Task.FromResult(new T3SessionStatus(Authenticated, expires, Origin));
        }

        public Task ReloadAsync(CancellationToken cancellationToken)
        {
            Reloads++;
            return Task.CompletedTask;
        }
    }

    /// <summary>Stands in for the Library-side bridge: answers request files in the same JSON shape.</summary>
    private sealed class FakeBridge : IDisposable
    {
        private readonly CancellationTokenSource stop = new();
        private readonly Task loop;
        private int counter;
        private readonly HashSet<string> answered = [];
        public List<string> Kinds { get; } = [];
        public List<string> Labels { get; } = [];
        public List<string?> Origins { get; } = [];
        public volatile bool Refuse;

        public FakeBridge(string folder, string linkOrigin)
        {
            loop = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    foreach (var request in Directory.EnumerateFiles(folder, "*.request.json").ToArray())
                    {
                        JsonDocument document;
                        try { document = JsonDocument.Parse(await File.ReadAllTextAsync(request)); }
                        catch (Exception error) when (error is IOException or JsonException) { continue; }
                        using (document)
                        {
                            var root = document.RootElement;
                            var id = root.GetProperty("id").GetString()!;
                            var response = Path.Combine(folder, id + ".response.json");
                            // Like the real bridge: each request id is answered once.
                            if (!answered.Add(id) || File.Exists(response)) continue;
                            if (root.GetProperty("schema_version").GetInt32() != 1) throw new InvalidDataException("schema");
                            _ = root.GetProperty("created_at_utc").GetString();
                            lock (Kinds)
                            {
                                Kinds.Add(root.GetProperty("kind").GetString()!);
                                Labels.Add(root.GetProperty("label").GetString()!);
                                Origins.Add(root.GetProperty("origin").ValueKind == JsonValueKind.Null ? null : root.GetProperty("origin").GetString());
                            }
                            object answer = Refuse
                                ? new Dictionary<string, object?> { ["id"] = id, ["status"] = "error", ["message"] = "refused for test" }
                                : root.GetProperty("kind").GetString() == "pair"
                                    ? new Dictionary<string, object?> { ["id"] = id, ["status"] = "ok", ["pairing_url"] = linkOrigin + "/pair#token=synthetic-credential-" + Interlocked.Increment(ref counter).ToString("0000") }
                                    : new Dictionary<string, object?> { ["id"] = id, ["status"] = "ok", ["revoked"] = 1 };
                            var temporary = response + ".tmp";
                            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(answer));
                            File.Move(temporary, response, true);
                        }
                    }
                    try { await Task.Delay(10, stop.Token); } catch (OperationCanceledException) { }
                }
            });
        }

        public void Dispose()
        {
            stop.Cancel();
            try { loop.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
            stop.Dispose();
        }
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
    }

    private static void False(bool value, string name) => True(!value, name);

    private static void Throws<TException>(Action action, string name) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException(name + ": expected " + typeof(TException).Name);
    }
}
