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
                T3Web = new T3WebSettings(Origin, new T3PairingSettings(Folder: "R:\\ops\\jarvis-sync\\requests\\t3-pairing"), OpenOnLaunch: true),
                OpenGrokBotOnLaunch = true,
            };
            HelperSettingsStore.Save(path, settings);
            var loaded = HelperSettingsStore.Load(path);
            Equal(Origin, loaded.T3Web?.Url, "T3 web url round trip");
            Equal("R:\\ops\\jarvis-sync\\requests\\t3-pairing", loaded.T3Web?.Pairing?.Folder, "pairing folder round trip");
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
        var web = AppTargetMenuPolicy.Evaluate("T3 web", 1, true, null, new T3CodeConnectionReport(1, 1, 0), true);
        True(web.Label.StartsWith("T3 web", StringComparison.Ordinal), "T3 web label: " + web.Label);
    }

    private static async Task PairingAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "wingman-pairing-" + Guid.NewGuid().ToString("N"));
        var share = Path.Combine(root, "share");
        var folder = Path.Combine(share, "ops", "jarvis-sync", "requests", "t3-pairing");
        Directory.CreateDirectory(folder);
        var statePath = Path.Combine(root, "wingman", "t3-web", "pairing.json");
        var origin = new Uri(Origin + "/");
        var label = T3PairingCoordinator.DefaultLabel("DESKTOP-TEST");
        Equal("Wingman T3 web (DESKTOP-TEST)", label, "session label names the PC only");
        Equal("Wingman T3 web (abc)", T3PairingCoordinator.DefaultLabel("a b/c"), "label is sanitized");
        try
        {
            // Zero touch: the folder is found from the Jarvis Sync receipt on this PC.
            var localAppData = Path.Combine(root, "local");
            Equal<string?>(null, SharedFolderPairingProvider.ResolveFolder(null, localAppData), "no receipt, no folder");
            Directory.CreateDirectory(Path.Combine(localAppData, "Jarvis", "Sync", "receipts"));
            File.WriteAllText(Path.Combine(localAppData, "Jarvis", "Sync", "receipts", "latest.json"),
                JsonSerializer.Serialize(new Dictionary<string, object> { ["share_root"] = share, ["status"] = "ok" }));
            Equal(folder, SharedFolderPairingProvider.ResolveFolder(null, localAppData), "folder from the Jarvis Sync receipt");
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

    /// <summary>Stands in for the Jarvis-side bridge: answers request files in the same JSON shape.</summary>
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
