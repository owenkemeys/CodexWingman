using System.Net;
using CodexWingman.Core;

/// <summary>
/// The opt-in Start menu shortcut change: Grok Bot.exe stays the target; only Wingman's loopback debugging pair is added
/// to the arguments and recorded, and only that exact pair is ever removed. A shortcut with any other debugging switch is
/// refused. Name, icon and AppUserModelID kept, backed up, re-applied after a reinstall.
/// </summary>
internal static class GrokBotStartLinkTests
{
    private const string Grok = "/apps/Grok Bot/Grok Bot.exe";
    private const string Link = "/start/Programs/Grok Bot.lnk";
    private const string Backups = "/data/backups/start-menu";
    private const string Aumid = "com.example.grokbot";
    private const string Flags9523 = "--remote-debugging-address=127.0.0.1 --remote-debugging-port=9523";
    private static readonly ShortcutLink Original = new(Grok, "", "/apps/Grok Bot", Grok + ",0", Aumid, "Grok Bot");

    public static void Run()
    {
        OffByDefault();
        ApplyAddsOnlyTheFlags();
        ApplyRefusesWhatItShouldNotTouch();
        AnyOtherChangeIsUndone();
        RestoreRemovesOnlyTheFlags();
        NeverFlagsWithoutARecord();
        RestoreSaysWhatHappened();
        FailedRestoreKeepsTheRecord();
        AdoptionSkipsOtherAppsPorts();
        ReinstallIsDetectedAndReapplied();
        ReportIsReadOnly();
        OnlyTheArgumentsAreWritten();
        PortFromCommandLine();
        OnlyWhatWingmanConnectsToCounts();
        RestoreAndReconcileNeverInterleave();
        CorruptSettingSaysSoAndNothingWritesNeedlessly();
        UnsafeArgumentsAreRefusedAndReconcileIsIdempotent();
        CommandsParse();
        StandaloneOutputNeverOpensAConsole();
        Console.WriteLine("Grok Bot Start menu shortcut tests passed");
    }

    private sealed class MemoryShortcuts : IShortcutFiles
    {
        public readonly Dictionary<string, ShortcutLink> Links = new(StringComparer.Ordinal);
        public Func<ShortcutLink, ShortcutLink>? Distort;
        public int Writes, Copies;
        public ShortcutLink? Read(string path) => Links.TryGetValue(path, out var link) ? link : null;
        public Action? BeforeWrite;
        public bool FailUndo;
        public void WriteArguments(string path, string arguments)
        {
            if (!Links.TryGetValue(path, out var link)) throw new FileNotFoundException(path);
            BeforeWrite?.Invoke();
            Writes++;
            var edited = link with { Arguments = arguments };
            Links[path] = Distort?.Invoke(edited) ?? edited;
        }
        public void Copy(string source, string destination)
        {
            if (FailUndo && destination == Link) throw new IOException("undo failed");
            Copies++;
            Links[destination] = Links[source];
        }
        public IEnumerable<string> Files(string folder) => Links.Keys.Where(path => Path.GetDirectoryName(path) == folder).ToArray();
    }

    private static (GrokBotStartLink Change, MemoryShortcuts Files) Setup(ShortcutLink? existing = null, int minute = 0)
    {
        var files = new MemoryShortcuts();
        if (existing is not null) files.Links[Link] = existing;
        return (Make(files, minute), files);
    }

    private static GrokBotStartLink Make(MemoryShortcuts files, int minute = 0) =>
        new(Link, Backups, Grok, files, () => new DateTime(2026, 1, 1, 9, minute, 0));

    private static void OffByDefault()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wingman-start-link-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = GrokBotStartLinkOptIn.PathIn(folder);
            Equal(GrokBotStartLinkSetting.Off, GrokBotStartLinkOptIn.Load(path), "no setting file: off");
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, "{ not json");
            var unreadable = GrokBotStartLinkSetting.Off with { Unreadable = true };
            Equal(unreadable, GrokBotStartLinkOptIn.Load(path), "unreadable setting: off, and known to be unreadable");
            File.WriteAllText(path, "{\"enabled\":true}");
            Equal(unreadable, GrokBotStartLinkOptIn.Load(path), "a setting without Wingman's schema: off, unreadable");
            File.WriteAllText(path, "{\"schema\":\"wingman.grok-bot-start-link.v1\",\"enabled\":true}");
            Equal(unreadable, GrokBotStartLinkOptIn.Load(path), "the earlier draft's schema: off, unreadable");
            GrokBotStartLinkOptIn.Save(path, new GrokBotStartLinkSetting(true, 9523));
            Equal(new GrokBotStartLinkSetting(true, 9523), GrokBotStartLinkOptIn.Load(path), "switched on, with the pair recorded");
            Equal(true, File.ReadAllText(path).Contains("\"added\":{\"address\":\"127.0.0.1\",\"port\":9523}", StringComparison.Ordinal),
                "the file names exactly what was added");
            File.WriteAllText(path, "{\"schema\":\"wingman.grok-bot-start-link.v2\",\"enabled\":true,\"added\":{\"address\":\"0.0.0.0\",\"port\":9523}}");
            Equal(new GrokBotStartLinkSetting(true, null), GrokBotStartLinkOptIn.Load(path), "a record that isn't Wingman's pair is not trusted");
            GrokBotStartLinkOptIn.Save(path, GrokBotStartLinkSetting.Off);
            Equal(GrokBotStartLinkSetting.Off, GrokBotStartLinkOptIn.Load(path), "switched off, nothing recorded");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

        var (change, files) = Setup(Original);
        Equal(GrokBotStartLinkOutcome.Disabled, change.Reconcile(GrokBotStartLinkSetting.Off, () => 9523, Ignore), "not opted in: nothing happens");
        Equal(0, files.Writes + files.Copies, "not opted in: the shortcut is not touched");
    }

    private static void ApplyAddsOnlyTheFlags()
    {
        var (change, files) = Setup(Original);
        var backup = change.Apply(9523, GrokBotStartLinkSetting.Off, Ignore);
        var edited = files.Links[Link];
        Equal(Original with { Arguments = Flags9523 }, edited, "only the arguments change: target, folder, icon, AppUserModelID, description kept");
        Equal(Backups + "/20260101-090000 Grok Bot.lnk", backup, "backup named by time");
        Equal(Original, files.Links[backup!], "backup is the original");
        Equal(GrokBotStartLinkState.Ours, change.State(9523), "with the record: Wingman's pair");
        Equal(GrokBotStartLinkState.Foreign, change.State(null), "without the record it isn't claimed");
        Equal(GrokBotStartLinkState.Foreign, change.State(9524), "a different recorded port isn't claimed");
        Equal(null, change.Apply(9524, new GrokBotStartLinkSetting(true, 9523), Ignore), "applying again changes nothing");
        Equal(Flags9523, files.Links[Link].Arguments, "the first port stays");

        var (withArgs, argFiles) = Setup(Original with { Arguments = "--some-flag \"a b\"" });
        withArgs.Apply(9530, GrokBotStartLinkSetting.Off, Ignore);
        Equal("--some-flag \"a b\" --remote-debugging-address=127.0.0.1 --remote-debugging-port=9530", argFiles.Links[Link].Arguments,
            "existing arguments kept as written, flags appended");
    }

    private static void ApplyRefusesWhatItShouldNotTouch()
    {
        var (missing, missingFiles) = Setup();
        Throws<FileNotFoundException>(() => missing.Apply(9523, GrokBotStartLinkSetting.Off, Ignore), "no shortcut: Wingman never creates one");
        Equal(0, missingFiles.Links.Count, "nothing created");
        var other = Original with { Target = "/apps/Other/Other.exe" };
        var (elsewhere, elsewhereFiles) = Setup(other);
        Throws<InvalidOperationException>(() => elsewhere.Apply(9523, GrokBotStartLinkSetting.Off, Ignore), "a shortcut to something else is left alone");
        Equal(other, elsewhereFiles.Links[Link], "left as it is");

        // Any debugging switch Wingman didn't add, in any of Chromium's spellings, means hands off.
        foreach (var arguments in new[]
        {
            "--user-flag --remote-debugging-port=9333",
            "--remote-debugging-address=0.0.0.0",
            "-remote-debugging-address=0.0.0.0",
            "/remote-debugging-port=9333",
            "--Remote-Debugging-Port=9333",
            "--remote-debugging-pipe",
            "\"--remote-debugging-port=9333\"",
            "--remote-debugging-address=127.0.0.1",
        })
        {
            var existing = Original with { Arguments = arguments };
            var (foreign, foreignFiles) = Setup(existing);
            Equal(GrokBotStartLinkState.Foreign, foreign.State(null), arguments + ": not Wingman's");
            var refused = Catch(() => foreign.Apply(9523, GrokBotStartLinkSetting.Off, Ignore));
            Equal(true, refused?.Message.Contains("didn't add", StringComparison.Ordinal) == true
                && refused.Message.Contains("stays off", StringComparison.Ordinal), arguments + ": refused with a clear reason");
            Equal(GrokBotStartLinkOutcome.Foreign, foreign.Reconcile(new GrokBotStartLinkSetting(true, null), () => 9523, Ignore),
                arguments + ": not re-applied either");
            Equal(false, foreign.Restore(Recorded(9333), Ignore).Removed, arguments + ": nothing of Wingman's to remove");
            Equal(existing, foreignFiles.Links[Link], arguments + ": left exactly as it is");
            Equal(0, foreignFiles.Writes + foreignFiles.Copies, arguments + ": no writes, no backup");
        }

        var (badPort, badPortFiles) = Setup(Original);
        Throws<ArgumentOutOfRangeException>(() => badPort.Apply(80, GrokBotStartLinkSetting.Off, Ignore), "a port outside 1024-65535 is refused");
        Equal(0, badPortFiles.Writes + badPortFiles.Copies, "nothing written for a bad port");
    }

    private static void AnyOtherChangeIsUndone()
    {
        foreach (var (name, distort) in new (string, Func<ShortcutLink, ShortcutLink>)[]
        {
            ("AppUserModelID lost", link => link with { AppUserModelId = null }),
            ("icon changed", link => link with { IconLocation = "/other.ico,0" }),
            ("target changed", link => link with { Target = "/apps/Other/Other.exe" }),
            ("folder changed", link => link with { WorkingDirectory = "/elsewhere" }),
            ("description changed", link => link with { Description = "" }),
        })
        {
            var (change, files) = Setup(Original);
            files.Distort = distort;
            Throws<InvalidOperationException>(() => change.Apply(9523, GrokBotStartLinkSetting.Off, Ignore), name + ": the save fails");
            Equal(Original, files.Links[Link], name + ": the original is put back from the backup");
        }
    }

    private static void RestoreRemovesOnlyTheFlags()
    {
        var (change, files) = Setup(Original);
        change.Apply(9523, GrokBotStartLinkSetting.Off, Ignore);
        Equal(false, change.Restore(Recorded(null), Ignore).Removed, "no record: nothing removed");
        Equal(false, change.Restore(Recorded(9600), Ignore).Removed, "a different record: nothing removed");
        Equal(Flags9523, files.Links[Link].Arguments, "still there");
        Equal(true, change.Restore(Recorded(9523), Ignore).Removed, "restored");
        Equal(Original, files.Links[Link], "exactly the original arguments again");
        Equal(false, change.Restore(Recorded(9523), Ignore).Removed, "nothing to restore twice");

        // Exact repro: a debugging port the user set survives Apply (refused) and Restore unchanged.
        var user = Original with { Arguments = "--user-flag --remote-debugging-port=9333" };
        var (userLink, userFiles) = Setup(user);
        Catch(() => userLink.Apply(9523, GrokBotStartLinkSetting.Off, Ignore));
        _ = userLink.Restore(Recorded(9523), Ignore);
        _ = userLink.Restore(Recorded(9333), Ignore);
        Equal(user, userFiles.Links[Link], "'--user-flag --remote-debugging-port=9333' survives Apply and Restore");

        // Added by the user after Wingman's pair: only Wingman's exact pair goes, the user's switch stays.
        var mixed = Original with { Arguments = "--a " + Flags9523 + " --remote-debugging-port=9333 --b" };
        var (later, laterFiles) = Setup(mixed);
        Equal(GrokBotStartLinkState.Foreign, later.State(9523), "a user's extra port makes it not purely Wingman's");
        Equal(true, later.Restore(Recorded(9523), Ignore).Removed, "Wingman's recorded pair still comes out");
        Equal("--a --remote-debugging-port=9333 --b", laterFiles.Links[Link].Arguments, "only that pair removed");
        Equal(Aumid, laterFiles.Links[Link].AppUserModelId, "AppUserModelID kept");

        Equal("--x  \"y\"", GrokBotStartLink.WithoutPair("--x  \"y\" " + Flags9523, ["--remote-debugging-address=127.0.0.1", "--remote-debugging-port=9523"]),
            "appended pair removed: what came before stays exactly as written");
        Equal(null, GrokBotStartLink.WithoutPair("--remote-debugging-port=9523", ["--remote-debugging-address=127.0.0.1", "--remote-debugging-port=9523"]),
            "half the pair: nothing removed");
        Equal(null, GrokBotStartLink.WithoutPair("--remote-debugging-address=127.0.0.1 --remote-debugging-port=95230",
            ["--remote-debugging-address=127.0.0.1", "--remote-debugging-port=9523"]), "a longer port isn't Wingman's");

        var (plain, plainFiles) = Setup(Original);
        Equal(false, plain.Restore(Recorded(9523), Ignore).Removed, "a shortcut without the pair is not touched");
        Equal(0, plainFiles.Writes + plainFiles.Copies, "no writes");
    }

    private static readonly Action<GrokBotStartLinkSetting> Ignore = _ => { };
    private static GrokBotStartLinkSetting Recorded(int? port) => new(true, port);

    private static void NeverFlagsWithoutARecord()
    {
        var pending = new GrokBotStartLinkSetting(true, 9523, Pending: true);
        var confirmed = new GrokBotStartLinkSetting(true, 9523);

        // Success: the pending record is written before the shortcut is saved, then confirmed.
        var (change, files) = Setup(Original);
        var saved = new List<GrokBotStartLinkSetting>();
        GrokBotStartLinkSetting? atWrite = null;
        files.BeforeWrite = () => atWrite = saved.LastOrDefault();
        change.Apply(9523, GrokBotStartLinkSetting.Off, saved.Add);
        Equal(pending, atWrite, "the exact pair is recorded (pending) before the shortcut is saved");
        Equal(confirmed, saved[^1], "then confirmed");

        // Save undone (something else changed): the previous record comes back, the shortcut is the original.
        var (undone, undoneFiles) = Setup(Original);
        undoneFiles.Distort = link => link with { IconLocation = "/other.ico,0" };
        saved.Clear();
        Throws<InvalidOperationException>(() => undone.Apply(9523, GrokBotStartLinkSetting.Off, saved.Add), "undone save fails");
        Equal(Original, undoneFiles.Links[Link], "original back");
        Equal(GrokBotStartLinkSetting.Off, saved[^1], "and the record is put back too");

        // The undo itself fails, so the pair stays on the shortcut: the pending record stays, so it is still Wingman's.
        var (stuck, stuckFiles) = Setup(Original);
        stuckFiles.Distort = link => link with { IconLocation = "/other.ico,0" };
        stuckFiles.FailUndo = true;
        saved.Clear();
        Catch(() => stuck.Apply(9523, GrokBotStartLinkSetting.Off, saved.Add));
        Equal(pending, saved[^1], "flags left on the shortcut: the pending record stays");
        Equal(true, stuck.Restore(Recorded(saved[^1].AddedPort), Ignore).Removed, "and Restore can still take out exactly that pair");

        // Wingman stops after the shortcut save but before the confirm: reconcile finds the pair and confirms it.
        var (crashed, crashedFiles) = Setup(Original);
        GrokBotStartLinkSetting? store = null;
        Catch(() => crashed.Apply(9523, GrokBotStartLinkSetting.Off,
            setting => { if (!setting.Pending) throw new IOException("confirm not saved"); store = setting; }));
        Equal(pending, store, "the pending record is what was saved");
        Equal(GrokBotStartLinkState.Ours, crashed.State(store!.AddedPort), "a pending record's exact pair counts as Wingman's");
        saved.Clear();
        Equal(GrokBotStartLinkOutcome.Confirmed, crashed.Reconcile(store, () => 9600, saved.Add), "reconcile confirms it");
        Equal(confirmed, saved[^1], "confirmed record saved");
        Equal(Original with { Arguments = Flags9523 }, crashedFiles.Links[Link], "the shortcut is left as it is");

        // The pending record can't be saved: the shortcut is never touched.
        var (noRecord, noRecordFiles) = Setup(Original);
        Catch(() => noRecord.Apply(9523, GrokBotStartLinkSetting.Off, _ => throw new IOException("setting not writable")));
        Equal(Original, noRecordFiles.Links[Link], "no record, no flags");
        Equal(0, noRecordFiles.Writes, "the shortcut was never written");

        // A pending record survives the setting file.
        var folder = Path.Combine(Path.GetTempPath(), "wingman-start-link-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = GrokBotStartLinkOptIn.PathIn(folder);
            GrokBotStartLinkOptIn.Save(path, pending);
            Equal(pending, GrokBotStartLinkOptIn.Load(path), "pending is saved and read back");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    private static void RestoreSaysWhatHappened()
    {
        const string Off = "The Start menu option is now off.";
        var (ours, _) = Setup(Original with { Arguments = Flags9523 });
        var removed = ours.Restore(Recorded(9523), Ignore);
        Equal(GrokBotStartLinkRestoreKind.Removed, removed.Kind, "removed");
        Equal("Removed Wingman's debugging port (127.0.0.1, port 9523) from Grok Bot's Start menu shortcut. " + Off, removed.Message, "removed: says so");

        var (noRecord, _) = Setup(Original with { Arguments = Flags9523 });
        var none = noRecord.Restore(Recorded(null), Ignore);
        Equal(GrokBotStartLinkRestoreKind.NoRecord, none.Kind, "no record");
        Equal(true, none.Message.StartsWith("Wingman has no record of adding a debugging port", StringComparison.Ordinal)
            && none.Message.Contains("nothing was removed", StringComparison.Ordinal) && none.Message.EndsWith(Off, StringComparison.Ordinal),
            "no record: says nothing was removed, option off");

        var (foreign, _) = Setup(Original with { Arguments = "--user-flag --remote-debugging-port=9333" });
        var left = foreign.Restore(Recorded(9523), Ignore);
        Equal(GrokBotStartLinkRestoreKind.NotOnShortcut, left.Kind, "recorded pair not there");
        Equal(true, left.Message.Contains("isn't on Grok Bot's Start menu shortcut, so nothing was removed", StringComparison.Ordinal)
            && left.Message.Contains("A debugging setting Wingman didn't add was left alone: --remote-debugging-port=9333.", StringComparison.Ordinal)
            && left.Message.EndsWith(Off, StringComparison.Ordinal), "foreign flag: named as left alone");

        var (mixed, _) = Setup(Original with { Arguments = Flags9523 + " -remote-debugging-port=9333" });
        var both = mixed.Restore(Recorded(9523), Ignore);
        Equal(true, both.Removed && both.Message.Contains("left alone: -remote-debugging-port=9333.", StringComparison.Ordinal),
            "removed ours and named the one left alone");

        Equal(GrokBotStartLinkRestoreKind.NoShortcut, Setup().Change.Restore(Recorded(9523), Ignore).Kind, "no shortcut");
        Equal(true, Setup().Change.Restore(Recorded(9523), Ignore).Message.EndsWith(Off, StringComparison.Ordinal), "no shortcut: option off too");

        var question = GrokBotStartLinkRestore.Question(9523);
        Equal(true, question.Contains("exactly the debugging port it added (127.0.0.1, port 9523)", StringComparison.Ordinal)
            && question.Contains("didn't add, is left alone", StringComparison.Ordinal)
            && question.Contains("still opens Grok Bot", StringComparison.Ordinal), "turning off explains itself like turning on");
        Equal(true, GrokBotStartLinkRestore.Question(null).Contains("no record", StringComparison.Ordinal), "no record: says nothing is removed");
        // Turning the option off from the development toggle shows that same message, like turning it on confirms.
        var tray = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        var toggle = tray[tray.IndexOf("private async Task ToggleGrokBotStartLinkAsync()", StringComparison.Ordinal)..];
        toggle = toggle[..toggle.IndexOf("\n    }\n", StringComparison.Ordinal)];
        Equal(true, toggle.Contains("MessageBox.Show(restored.Message", StringComparison.Ordinal), "turning off confirms what happened");
        Equal(true, toggle.Contains("GrokBotStartLinkRestore.Question(setting.AddedPort), Title,\n                        MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2)", StringComparison.Ordinal),
            "turning off asks first, default No");
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CodexWingman", "Program.cs"));
        Equal(true, program.Contains("var restored = GrokBotStartLinkWindows.RestoreForUninstall(grok, dataRoot);\n                text = restored.Message;", StringComparison.Ordinal), "the command prints the same message");
    }

    private static void FailedRestoreKeepsTheRecord()
    {
        var (dashes, dashFiles) = Setup(Original with { Arguments = "-- file.txt" });
        var dashSaved = new List<GrokBotStartLinkSetting>();
        Throws<InvalidOperationException>(() => dashes.Apply(9523, GrokBotStartLinkSetting.Off, dashSaved.Add), "a bare -- would make the port inert: refused");
        Equal(0, dashFiles.Writes + dashSaved.Count, "nothing written or recorded");

        var setting = new GrokBotStartLinkSetting(true, 9523);
        // Whatever Restore recorded along the way, a failure leaves the setting as it was.
        static bool Kept(List<GrokBotStartLinkSetting> saved, GrokBotStartLinkSetting setting) => saved.Count == 0 || saved[^1] == setting;
        // The save throws: the pair stays, so the record and the option stay.
        var (throwing, throwingFiles) = Setup(Original with { Arguments = Flags9523 });
        throwingFiles.BeforeWrite = () => throw new IOException("the shortcut is locked");
        var saved = new List<GrokBotStartLinkSetting>();
        var failed = throwing.Restore(setting, saved.Add);
        Equal(GrokBotStartLinkRestoreKind.Failed, failed.Kind, "a failed save is reported");
        Equal(true, Kept(saved, setting), "the record and the option are kept");
        Equal(GrokBotStartLinkState.Ours, throwing.State(9523), "the pair is still there, and still recorded");
        Equal(true, failed.Message.StartsWith("Restore failed: the shortcut is locked", StringComparison.Ordinal)
            && failed.Message.Contains("still on", StringComparison.Ordinal) && !failed.Message.Contains("now off", StringComparison.Ordinal),
            "says restore failed and the option is still on");

        // The save "succeeds" but the pair is read back: also failed, record kept.
        var (stuck, stuckFiles) = Setup(Original with { Arguments = Flags9523 });
        stuckFiles.Distort = link => link with { Arguments = Flags9523 };
        saved.Clear();
        Equal(GrokBotStartLinkRestoreKind.Failed, stuck.Restore(setting, saved.Add).Kind, "pair read back: failed");
        Equal(true, Kept(saved, setting), "record kept");

        // Saved as asked, yet the pair is still there (it was on the shortcut twice): not gone, so record kept.
        var (twice, _) = Setup(Original with { Arguments = Flags9523 + " " + Flags9523 });
        saved.Clear();
        Equal(GrokBotStartLinkRestoreKind.Failed, twice.Restore(setting, saved.Add).Kind, "pair still read back: failed");
        Equal(true, Kept(saved, setting), "record kept while the pair is on the shortcut");

        // Verified gone: then, and only then, off with the record cleared. While removing, the option is already off.
        var (ok, _) = Setup(Original with { Arguments = Flags9523 });
        saved.Clear();
        Equal(true, ok.Restore(setting, saved.Add).Removed, "removed");
        Equal(true, saved.Count == 2 && saved[0] == new GrokBotStartLinkSetting(false, 9523, Pending: true) && saved[1] == GrokBotStartLinkSetting.Off,
            "off while removing (record kept), then the record cleared once the pair is verified gone");

        // Pair verified gone, but saving also changed the icon: still a restore (off, record cleared); the change is reported.
        var (icon, iconFiles) = Setup(Original with { Arguments = Flags9523 });
        iconFiles.Distort = link => link with { IconLocation = "elsewhere.ico" };
        saved.Clear();
        var drifted = icon.Restore(setting, saved.Add);
        Equal(true, drifted.Removed && saved[^1] == GrokBotStartLinkSetting.Off, "pair gone: off and cleared despite another change");
        Equal(true, drifted.ChangedProperties is ["icon"]
            && drifted.Message.Contains("Saving the shortcut also changed its icon", StringComparison.Ordinal)
            && drifted.Message.EndsWith("The Start menu option is now off.", StringComparison.Ordinal), "the other change is reported separately");

        // The pair is gone but saving Off fails: what's stored is off-with-record, which Reconcile never re-adds.
        var (lost, lostFiles) = Setup(Original with { Arguments = Flags9523 });
        GrokBotStartLinkSetting stored = setting;
        Throws<IOException>(() => lost.Restore(setting, next =>
        {
            if (next == GrokBotStartLinkSetting.Off) throw new IOException("settings are read-only");
            stored = next;
        }), "a failed final save is reported");
        Equal(GrokBotStartLinkState.Plain, lost.State(9523), "the pair is gone");
        var writes = lostFiles.Writes;
        Equal(GrokBotStartLinkOutcome.Disabled, lost.Reconcile(stored, () => 9600, _ => throw new InvalidOperationException("no record change")),
            "reconcile after that restore never re-adds");
        Equal(writes, lostFiles.Writes, "and writes nothing");

        // Callers save only through Restore's callback.
        var root = FindRepositoryRoot();
        foreach (var file in new[] { "GrokBotStartLinkWindows.cs", "TrayApplicationContext.cs" })
        {
            var source = File.ReadAllText(Path.Combine(root, "src", "CodexWingman", file));
            Equal(false, source.Contains("finally { GrokBotStartLinkOptIn.Save(path, GrokBotStartLinkSetting.Off); }", StringComparison.Ordinal),
                file + ": never turns off unconditionally");
        }
    }

    private static void AdoptionSkipsOtherAppsPorts()
    {
        Equal(true, GrokBotLaunchPolicy.Adoptable(9600, [9222], listenerProcess: 200, grokProcess: 200), "Grok Bot's own listener");
        Equal(false, GrokBotLaunchPolicy.Adoptable(9600, [], listenerProcess: null, grokProcess: 200), "not listening yet, or owner unknown: not adopted");
        Equal(false, GrokBotLaunchPolicy.Adoptable(9600, [], listenerProcess: 300, grokProcess: 200), "another process listens there");
        Equal(false, GrokBotLaunchPolicy.Adoptable(9600, [9600], 200, 200), "a port Wingman uses for Codex or T3 now");
        foreach (var reserved in new[] { CodexLaunchPolicy.PreferredPort, T3CodeLaunchPolicy.PreferredPort, T3WebLaunchPolicy.PreferredPort })
            Equal(false, GrokBotLaunchPolicy.Adoptable(reserved, [], 200, 200), reserved + ": kept for Codex or T3");
        Equal(false, GrokBotLaunchPolicy.Adoptable(null, [], 200, 200), "no port");
        Equal(true, GrokBotLaunchPolicy.IsMainProcess("\"C:\\Grok Bot\\Grok Bot.exe\" --updated"), "the main process");
        Equal(false, GrokBotLaunchPolicy.IsMainProcess("\"C:\\Grok Bot\\Grok Bot.exe\" --type=renderer --lang=en"), "a renderer");
        Equal(false, GrokBotLaunchPolicy.IsMainProcess("\"C:\\Grok Bot\\Grok Bot.exe\" -type=gpu-process"), "any spelling of --type");
        Equal(true, GrokBotLaunchPolicy.IsMainProcess("\"C:\\Grok Bot\\Grok Bot.exe\" -- --type=renderer"), "after -- it's a plain argument");
        Equal(false, GrokBotLaunchPolicy.IsMainProcess(null), "unreadable: not known");
        // The tray checks it before adopting.
        var tray = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        Equal(true, tray.Contains("whenever Grok Bot is opened from this shortcut, and any program on this PC can then control the signed-in Grok Bot through it", StringComparison.Ordinal),
            "the opt-in question says plainly what the open port allows");
        var adopt = tray[tray.IndexOf("private bool AdoptGrokBotPort(", StringComparison.Ordinal)..];
        adopt = adopt[..adopt.IndexOf("\n    }\n", StringComparison.Ordinal)];
        Equal(true, adopt.IndexOf("GrokBotLaunchPolicy.Adoptable(", StringComparison.Ordinal) is var check and >= 0
            && check < adopt.IndexOf("grokLauncher.RecordPort(port)", StringComparison.Ordinal), "checked before the port is recorded");
    }

    private static void ReinstallIsDetectedAndReapplied()
    {
        var files = new MemoryShortcuts();
        files.Links[Link] = Original;
        Make(files).Apply(9523, GrokBotStartLinkSetting.Off, Ignore);
        var on = new GrokBotStartLinkSetting(true, 9523);
        var saved = new List<GrokBotStartLinkSetting>();
        Equal(GrokBotStartLinkOutcome.Unchanged, Make(files, minute: 1).Reconcile(on, () => 9523, saved.Add), "still has the pair: nothing to do");
        Equal(0, saved.Count, "nothing recorded");
        files.Links[Link] = Original with { AppUserModelId = Aumid + ".v2" };   // a reinstall writes a fresh shortcut
        var asked = 0;
        Equal(GrokBotStartLinkOutcome.Reapplied, Make(files, minute: 2).Reconcile(on, () => { asked++; return 9600; }, saved.Add),
            "fresh plain shortcut: flags added again");
        Equal(new GrokBotStartLinkSetting(true, 9600), saved[^1], "new port recorded");
        Equal(1, asked, "the port is reserved only when needed");
        Equal("--remote-debugging-address=127.0.0.1 --remote-debugging-port=9600", files.Links[Link].Arguments, "port flags back");
        Equal(Aumid + ".v2", files.Links[Link].AppUserModelId, "keeps the reinstalled shortcut's own AppUserModelID");
        Equal(2, files.Files(Backups).Count(), "the fresh shortcut is backed up too");
        files.Links.Remove(Link);                                                  // uninstalled Grok Bot
        Equal(GrokBotStartLinkOutcome.Missing, Make(files, minute: 3).Reconcile(on, () => 9523, Ignore), "missing: reported, never created");
        Equal(false, files.Links.ContainsKey(Link), "not created");
    }

    private static void ReportIsReadOnly()
    {
        var (change, files) = Setup(Original with { Arguments = Flags9523 });
        var report = change.Report(new GrokBotStartLinkSetting(true, 9523), recordedPort: 9523, keepShortcuts: "true", keepShortcutsKey: "HKCU\\Software\\example");
        Equal(GrokBotStartLinkState.Ours, report.State, "state");
        Equal(Grok, report.Target, "target");
        Equal(9523, report.AddedPort, "the pair Wingman recorded adding");
        Equal(9523, report.RecordedPort, "Wingman's recorded Grok Bot port");
        Equal(Aumid, report.AppUserModelId, "AppUserModelID");
        Equal("true", report.KeepShortcuts, "KeepShortcuts");
        Equal(0, files.Writes + files.Copies, "the report writes nothing");
        var none = Setup().Change.Report(GrokBotStartLinkSetting.Off, null, null, null);
        Equal(GrokBotStartLinkState.Missing, none.State, "missing shortcut reported");
    }

    private static void OnlyTheArgumentsAreWritten()
    {
        // The Windows writer loads the shortcut and sets its arguments only; it never sets path, folder, icon,
        // description or properties, so whatever the shell would otherwise normalise stays byte-for-byte the installer's.
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CodexWingman", "ShellLinkFiles.cs"));
        var writer = source[source.IndexOf("public void WriteArguments", StringComparison.Ordinal)..];
        writer = writer[..writer.IndexOf("\n    }\n", StringComparison.Ordinal)];
        Equal(true, writer.Contains(".SetArguments(arguments)", StringComparison.Ordinal), "sets the arguments");
        foreach (var call in new[] { "SetPath", "SetWorkingDirectory", "SetIconLocation", "SetDescription", "SetValue", "Commit", "SetRelativePath", "SetIDList" })
            Equal(false, writer.Contains(call, StringComparison.Ordinal), "the writer doesn't call " + call);
    }

    private static void PortFromCommandLine()
    {
        Equal(9523, GrokBotLaunchPolicy.DebugPortFrom("\"C:\\Grok Bot\\Grok Bot.exe\" " + Flags9523 + " --updated"), "loopback port found");
        Equal(9600, GrokBotLaunchPolicy.DebugPortFrom("\"C:\\Grok Bot\\Grok Bot.exe\" --remote-debugging-port=9600"), "no address: loopback default");
        Equal(null, GrokBotLaunchPolicy.DebugPortFrom("\"C:\\Grok Bot\\Grok Bot.exe\" --remote-debugging-address=0.0.0.0 --remote-debugging-port=9600"),
            "an open address is never adopted");
        foreach (var open in new[] { "-remote-debugging-address=0.0.0.0", "/remote-debugging-address=0.0.0.0", "--REMOTE-DEBUGGING-ADDRESS=0.0.0.0", "--remote-debugging-address" })
            Equal(null, GrokBotLaunchPolicy.DebugPortFrom("\"C:\\Grok Bot\\Grok Bot.exe\" " + open + " --remote-debugging-port=9600"),
                open + ": single dash, slash or any case is the same switch, never adopted");
        Equal(9601, GrokBotLaunchPolicy.DebugPortFrom("\"C:\\Grok Bot\\Grok Bot.exe\" -remote-debugging-address=127.0.0.1 -remote-debugging-port=9601"),
            "single-dash loopback port found");
        Equal(null, GrokBotLaunchPolicy.DebugPortFrom("\"C:\\Grok Bot\\Grok Bot.exe\" --remote-debugging-port=0"), "port 0 (random) is not adoptable");
        Equal(null, GrokBotLaunchPolicy.DebugPortFrom("\"C:\\Grok Bot\\Grok Bot.exe\" --updated"), "no port");
        Equal(null, GrokBotLaunchPolicy.DebugPortFrom("\"C:\\Grok Bot\\Grok Bot.exe\" -- --remote-debugging-port=9600"),
            "after a bare -- Chromium reads no switches: no port");
        Equal(9600, GrokBotLaunchPolicy.DebugPortFrom("\"C:\\Grok Bot\\Grok Bot.exe\" --remote-debugging-port=9600 -- --remote-debugging-port=9700"),
            "only the port before -- counts");
        Equal(9600, GrokBotLaunchPolicy.DebugPortFrom("\"C:\\Grok Bot\\Grok Bot.exe\" --remote-debugging-port=9600 -- --remote-debugging-address=0.0.0.0"),
            "an address after -- is a plain argument");
        // Wingman connects through 127.0.0.1 only, so a port on another local address is not one it can use.
        foreach (var local in new[] { "::1", "[::1]", "localhost", "" })
            Equal(null, GrokBotLaunchPolicy.DebugPortFrom("\"C:\\Grok Bot\\Grok Bot.exe\" --remote-debugging-address=" + local + " --remote-debugging-port=9600"),
                "address '" + local + "': Wingman's 127.0.0.1 connection may not reach it, never adopted");
        Equal(null, GrokBotLaunchPolicy.DebugPortFrom(null), "unreadable command line");
        Equal(null, GrokBotLaunchPolicy.DebugPortFrom("\"C:\\x --remote-debugging-port=9523\\Grok Bot.exe\""), "inside the program path");
    }

    /// <summary>
    /// The tray's reconcile and the restore command (possibly another process) each re-read the setting and act while
    /// holding the setting's lock, so a restore that just turned the option off is never undone by a reconcile.
    /// </summary>
    private static void RestoreAndReconcileNeverInterleave()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wingman-lock-" + Guid.NewGuid().ToString("N"));
        var path = GrokBotStartLinkOptIn.PathIn(folder);
        try
        {
            using var held = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var holder = Task.Run(() => GrokBotStartLinkOptIn.Locked(path, () => { held.Set(); release.Wait(); return 1; }));
            held.Wait();
            Throws<TimeoutException>(() => GrokBotStartLinkOptIn.Locked(path, () => 2, TimeSpan.FromMilliseconds(200)), "a second holder waits, then gives up");
            var waiter = Task.Run(() => GrokBotStartLinkOptIn.Locked(path, () => 3, TimeSpan.FromSeconds(10)));
            Thread.Sleep(150);
            Equal(false, waiter.IsCompleted, "never runs while the other holds it");
            release.Set();
            Equal((1, 3), (holder.Result, waiter.Result), "runs once the other is done");
            Equal(false, Directory.Exists(folder), "a named mutex: no lock file left behind");
            Equal(GrokBotStartLinkOptIn.MutexName(path), GrokBotStartLinkOptIn.MutexName(path.ToUpperInvariant()), "one mutex per file, whatever the case");
            Equal(false, GrokBotStartLinkOptIn.MutexName(path) == GrokBotStartLinkOptIn.MutexName(path + "x"), "another file: another mutex");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

        var tray = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        var reconcile = tray[tray.IndexOf("private async Task ReconcileGrokBotStartLinkAsync(", StringComparison.Ordinal)..];
        reconcile = reconcile[..reconcile.IndexOf("\n    }\n", StringComparison.Ordinal)];
        Equal(true, reconcile.Contains("GrokBotStartLinkOptIn.Locked(path, () =>\n            {\n                var setting = GrokBotStartLinkOptIn.Load(path);", StringComparison.Ordinal)
            && reconcile.Contains("return setting.Enabled", StringComparison.Ordinal), "the reconcile re-reads the setting under the lock");
        Equal(true, tray.Contains("GrokBotStartLinkOptIn.Locked(path, () => link.Restore(GrokBotStartLinkOptIn.Load(path),", StringComparison.Ordinal)
            && tray.Contains("GrokBotStartLinkOptIn.Locked(path, () => link.Apply(port, GrokBotStartLinkOptIn.Load(path),", StringComparison.Ordinal),
            "the tray's restore and apply take the lock");
        // Never on the UI thread: every tray lock runs on its own thread with a short wait; a busy lock retries next tick.
        Equal((3, 3, 3), (tray.Split("GrokBotStartLinkOptIn.Locked(").Length - 1, tray.Split("OnStaThread(() => GrokBotStartLinkOptIn.Locked(").Length - 1,
            tray.Split(", StartLinkLockWait))").Length - 1), "the tray waits for the lock only off its UI thread, briefly");
        Equal(true, reconcile.Contains("catch (TimeoutException)\n        {\n            startLinkRetry ??= reason;", StringComparison.Ordinal)
            && tray.Contains("if (startLinkRetry is { } retry) ReconcileGrokBotStartLink(retry);", StringComparison.Ordinal), "a busy lock: tried again on a later tick");
        Equal(false, GrokBotStartLinkRestore.Question(9600).Contains("one-click", StringComparison.OrdinalIgnoreCase), "no stale one-click wording");
        var policy = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CodexWingman.Core", "Apps", "GrokBot.cs"));
        Equal(false, policy.Contains("/// </summary>\n    /// <summary>", StringComparison.Ordinal), "every doc block sits on its own member");
        var windows = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CodexWingman", "GrokBotStartLinkWindows.cs"));
        Equal(true, windows.Contains("var result = GrokBotStartLinkOptIn.Locked(path, () =>\n            For(grokExecutable, wingmanDataRoot).Restore(GrokBotStartLinkOptIn.Load(path),", StringComparison.Ordinal),
            "the restore command takes the lock");
    }

    /// <summary>
    /// A setting file Wingman can't read is named as such and changes nothing; a restore with nothing to undo writes
    /// nothing at all.
    /// </summary>
    private static void CorruptSettingSaysSoAndNothingWritesNeedlessly()
    {
        var corrupt = GrokBotStartLinkSetting.Off with { Unreadable = true };
        var (change, files) = Setup(Original with { Arguments = Flags9523 });
        var saved = new List<GrokBotStartLinkSetting>();
        Throws<InvalidOperationException>(() => change.Apply(9523, corrupt, saved.Add), "Apply refuses an unreadable record");
        try { change.Apply(9523, corrupt, saved.Add); }
        catch (InvalidOperationException error)
        {
            Equal(GrokBotStartLinkSetting.UnreadableMessage, error.Message, "and says the record can't be read");
            Equal(false, error.Message.Contains("didn't add", StringComparison.Ordinal), "not a debugging setting Wingman didn't add");
        }
        var restored = change.Restore(corrupt, saved.Add);
        Equal((GrokBotStartLinkRestoreKind.Failed, GrokBotStartLinkSetting.UnreadableMessage), (restored.Kind, restored.Message), "Restore says the record can't be read");
        Equal(0, files.Writes + files.Copies + saved.Count, "and nothing is changed or written");
        Equal(true, GrokBotStartLinkSetting.UnreadableMessage.Contains("can't be read", StringComparison.Ordinal), "the wording");

        var (plain, plainFiles) = Setup(Original);
        var noRecord = plain.Restore(GrokBotStartLinkSetting.Off, saved.Add);
        Equal(GrokBotStartLinkRestoreKind.NoRecord, noRecord.Kind, "nothing to undo");
        Equal(0, plainFiles.Writes + plainFiles.Copies + saved.Count, "nothing to undo: nothing written, not even the setting");
        plain.Restore(new GrokBotStartLinkSetting(true, null), saved.Add);
        Equal(true, saved is [var off] && off == GrokBotStartLinkSetting.Off, "on with no record: switched off once");
    }

    /// <summary>Only listeners Wingman's http://127.0.0.1 connection reaches count as connected or adoptable.</summary>
    private static void OnlyWhatWingmanConnectsToCounts()
    {
        static bool Reaches(string address, int port) => GrokBotLaunchPolicy.ReachableByWingman(new IPEndPoint(IPAddress.Parse(address), port), 9600);
        Equal(true, Reaches("127.0.0.1", 9600), "127.0.0.1");
        Equal(false, Reaches("0.0.0.0", 9600), "all addresses: not loopback-only, so neither connected nor adopted (adoption takes only 127.0.0.1)");
        Equal(false, Reaches("127.0.0.2", 9600), "another loopback address: Wingman connects to 127.0.0.1 only");
        Equal(false, Reaches("::1", 9600), "IPv6 loopback only: not reachable through 127.0.0.1");
        Equal(false, Reaches("::", 9600), "IPv6 any: not counted");
        Equal(false, Reaches("192.168.1.5", 9600), "another address");
        Equal(false, Reaches("127.0.0.1", 9601), "another port");
        var launcher = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CodexWingman", "GrokBotLauncher.cs"));
        Equal(true, launcher.Contains("var read = GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidListener, 0);\n                if (read == ErrorInsufficientBuffer) continue;", StringComparison.Ordinal)
            && launcher.Contains("for (var attempt = 0; attempt < 4 && size > 0; attempt++)", StringComparison.Ordinal)
            && launcher.Contains("private const uint ErrorInsufficientBuffer = 122;", StringComparison.Ordinal),
            "a TCP table that grew between the two calls is read again, with the new size");
        Equal(true, launcher.Contains("GetExtendedTcpTable(buffer, ref size, false, AfInet,", StringComparison.Ordinal)
            && !launcher.Contains("23 }", StringComparison.Ordinal) && launcher.Split("GrokBotLaunchPolicy.ReachableByWingman(").Length - 1 == 1
            && launcher.Contains("GrokBotLaunchPolicy.Connected(port, port is { } value && grokProcess is not null ? ListenerProcess(value) : null, grokProcess)", StringComparison.Ordinal),
            "connected uses the listener owner, the same 127.0.0.1-only rule as adoption");
        Equal(true, GrokBotLaunchPolicy.Connected(9600, 4242, 4242), "listening on 127.0.0.1 and owned by that Grok Bot: connected");
        Equal(false, GrokBotLaunchPolicy.Connected(9600, 777, 4242), "another program on the recorded port: not connected");
        Equal(false, GrokBotLaunchPolicy.Connected(9600, null, 4242), "nothing listening on 127.0.0.1 there: not connected");
        Equal(false, GrokBotLaunchPolicy.Connected(9600, 4242, null), "no Grok Bot process: not connected");
        Equal(false, GrokBotLaunchPolicy.Connected(null, 4242, 4242), "no recorded port: not connected");
        var tray = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CodexWingman", "TrayApplicationContext.cs"));
        Equal(false, tray.Contains("AppPresence.IsListening(grokLauncher", StringComparison.Ordinal), "Grok Bot's connected check is the 127.0.0.1 one");
        Equal(true, tray.Contains("GrokBotLauncher.ConnectedTo(grokLauncher.Port(), main?.Id)", StringComparison.Ordinal)
            && tray.Contains("GrokBotLauncher.ConnectedTo(grokLauncher.Port(), grokLauncher.MainProcess()?.Id)", StringComparison.Ordinal)
            && !tray.Contains("ListeningForWingman", StringComparison.Ordinal), "the tray's connected checks require Grok Bot's own listener");
    }

    /// <summary>
    /// An unbalanced quote (appended flags would land inside a quoted argument) or a bare -- is refused, never appended to,
    /// and reconciling again and again changes nothing more.
    /// </summary>
    private static void UnsafeArgumentsAreRefusedAndReconcileIsIdempotent()
    {
        foreach (var arguments in new[] { "\"--profile=C:\\My Folder", "--a \"b\" \"c", "-- file.txt" })
        {
            var (change, files) = Setup(Original with { Arguments = arguments });
            var saved = new List<GrokBotStartLinkSetting>();
            Equal(GrokBotStartLinkState.Unsafe, change.State(null), arguments + ": unsafe");
            Throws<InvalidOperationException>(() => change.Apply(9523, GrokBotStartLinkSetting.Off, saved.Add), arguments + ": refused");
            var on = new GrokBotStartLinkSetting(true, null);
            for (var round = 0; round < 3; round++)
                Equal(GrokBotStartLinkOutcome.Unsafe, change.Reconcile(on, () => 9523, saved.Add), arguments + ": reconcile refuses too");
            Equal(0, files.Writes + files.Copies + saved.Count, arguments + ": nothing written, backed up or recorded");
            Equal(arguments, files.Links[Link].Arguments, arguments + ": left as it was");
        }

        var (plain, plainFiles) = Setup(Original with { Arguments = "--a \"b c\"" });
        var record = new GrokBotStartLinkSetting(true, null);
        var asked = 0;
        Equal(GrokBotStartLinkOutcome.Reapplied, plain.Reconcile(record, () => { asked++; return 9600; }, updated => record = updated), "balanced quotes: added once");
        var after = plainFiles.Links[Link].Arguments;
        for (var round = 0; round < 3; round++)
            Equal(GrokBotStartLinkOutcome.Unchanged, plain.Reconcile(record, () => { asked++; return 9700; }, updated => record = updated), "again: nothing to do");
        Equal((1, 1, after), (plainFiles.Writes, asked, plainFiles.Links[Link].Arguments), "one write, one port, no flag appended twice");
        Equal("--a \"b c\" " + string.Join(' ', GrokBotLaunchPolicy.Arguments(9600)), after, "appended after the closed quote");
    }

    private static void CommandsParse()
    {
        Equal(WingmanCommand.GrokBotStartLink, WingmanLaunchOptions.Parse(["--grok-bot-start-link"]).Command, "toggle parses");
        Equal(true, WingmanLaunchOptions.Commands.Contains(WingmanCommand.GrokBotStartLink), "the toggle signals a running development Wingman");
        Equal(false, WingmanLaunchOptions.RunsOnItsOwn(WingmanCommand.GrokBotStartLink), "the toggle needs the running Wingman (it asks first)");
        foreach (var (argument, command) in new[]
        {
            ("--grok-bot-start-link-status", WingmanCommand.GrokBotStartLinkStatus),
            ("--restore-grok-bot-start-link", WingmanCommand.RestoreGrokBotStartLink),
        })
        {
            Equal(command, WingmanLaunchOptions.Parse([argument]).Command, argument + " parses");
            Equal(true, WingmanLaunchOptions.RunsOnItsOwn(command), argument + " runs without a running Wingman");
            Equal(false, WingmanLaunchOptions.Commands.Contains(command), argument + " is not a signal");
        }
    }

    private static void StandaloneOutputNeverOpensAConsole()
    {
        var attached = 0;
        Equal(StandaloneOutputRoute.Redirected, StandaloneOutput.Choose(true, () => { attached++; return true; }), "redirected output is used as is");
        Equal(0, attached, "no console attach when output is redirected");
        Equal(StandaloneOutputRoute.ParentConsole, StandaloneOutput.Choose(false, () => true), "started from a console: that console");
        Equal(StandaloneOutputRoute.MessageBox, StandaloneOutput.Choose(false, () => false), "no console: a message box");
        // A windowed program never gets a console of its own, so nothing flashes; the code only ever attaches.
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "src", "CodexWingman", "CodexWingman.csproj"));
        Equal(true, project.Contains("<OutputType>WinExe</OutputType>", StringComparison.Ordinal), "Wingman is a windowed program");
        var console = File.ReadAllText(Path.Combine(root, "src", "CodexWingman", "StandaloneConsole.cs"));
        Equal(false, console.Contains("AllocConsole", StringComparison.Ordinal), "never allocates a console");
        Equal(true, console.Contains("AttachConsole(AttachParentProcess)", StringComparison.Ordinal), "attaches to the parent console");
    }

    private static string FindRepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "src", "CodexWingman", "CodexWingman.csproj"))) return folder.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
    }

    private static Exception? Catch(Action action)
    {
        try { action(); return null; }
        catch (Exception error) { return error; }
    }

    private static void Throws<T>(Action action, string name) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"{name}: expected {typeof(T).Name}");
    }
}
