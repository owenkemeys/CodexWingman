using System.Text;
using System.Text.Json;

namespace CodexWingman.Core;

/// <summary>What a Windows shortcut says: target, arguments, folder, icon ("path,index"), description and AppUserModelID.</summary>
public sealed record ShortcutLink(string Target, string Arguments, string WorkingDirectory, string IconLocation,
    string? AppUserModelId, string Description = "");

/// <summary>Shortcut file access. Windows reads and writes .lnk files through the shell; tests use memory.</summary>
public interface IShortcutFiles
{
    /// <summary>The shortcut at <paramref name="path"/>, or null when there is none or it can't be read.</summary>
    ShortcutLink? Read(string path);
    /// <summary>Sets only the arguments of the existing shortcut; every other property is left as it is in the file.</summary>
    void WriteArguments(string path, string arguments);
    /// <summary>Copies a whole shortcut file, overwriting the destination and creating its folder.</summary>
    void Copy(string source, string destination);
    IEnumerable<string> Files(string folder);
}

public enum GrokBotStartLinkState
{
    Missing,
    /// <summary>Points at Grok Bot.exe with no remote debugging switch: Grok Bot's own shortcut as its installer wrote it.</summary>
    Plain,
    /// <summary>Points at Grok Bot.exe and its only debugging switches are exactly the pair Wingman recorded adding.</summary>
    Ours,
    /// <summary>Points at Grok Bot.exe with a debugging switch Wingman didn't add (or didn't record); left alone.</summary>
    Foreign,
    /// <summary>Points at another program; left alone.</summary>
    Other,
    /// <summary>
    /// Points at Grok Bot.exe with no debugging switch, but flags appended to its arguments would not reach Grok Bot: an
    /// unbalanced double quote (they would land inside a quoted argument) or a bare "--" (after which Grok Bot reads no
    /// switches). Refused, never appended to; left alone.
    /// </summary>
    Unsafe,
}

public enum GrokBotStartLinkOutcome { Unchanged, Reapplied, Missing, Disabled, Foreign, Confirmed, Unsafe }

/// <summary>Read-only report for <c>--grok-bot-start-link-status</c>.</summary>
public sealed record GrokBotStartLinkReport(
    string LinkPath, GrokBotStartLinkState State, string? Target, string? Arguments, string? IconLocation,
    string? AppUserModelId, bool OptedIn, int? AddedPort, bool Pending, int? RecordedPort, string? KeepShortcuts, string? KeepShortcutsKey,
    string? LatestBackup);

/// <summary>
/// Opt-in, off by default, and never enabled by Wingman itself. Grok Bot's installer reopens Grok Bot after an update
/// through its own Start menu shortcut ("Grok Bot.lnk") when that shortcut exists. This adds Wingman's two loopback
/// debugging flags (<c>--remote-debugging-address=127.0.0.1 --remote-debugging-port=&lt;port&gt;</c>) to that shortcut's
/// arguments, so Grok Bot comes back with the port and Wingman connects without restarting it. Only the arguments are
/// written: target (Grok Bot.exe), folder, icon, description and AppUserModelID stay as they are, and whatever the
/// installer passes (such as --updated) still reaches Grok Bot. The original is backed up first, and the port added is
/// recorded in the setting. <see cref="Restore"/> removes exactly that recorded pair and nothing else. A shortcut that
/// already has any debugging switch Wingman didn't add is never changed. Grok Bot's program files are never touched.
/// </summary>
public sealed class GrokBotStartLink(string linkPath, string backupFolder, string grokExecutable, IShortcutFiles files, Func<DateTime> now)
{
    public const string FileName = "Grok Bot.lnk";

    public static string DefaultLinkPath(string roamingAppData) =>
        Path.Combine(roamingAppData, "Microsoft", "Windows", "Start Menu", "Programs", FileName);

    public static string BackupFolder(string wingmanDataRoot) => Path.Combine(wingmanDataRoot, "backups", "start-menu");

    public string LinkPath => linkPath;

    /// <param name="addedPort">The port Wingman recorded adding, or null when it recorded none.</param>
    public GrokBotStartLinkState Classify(ShortcutLink? link, int? addedPort)
    {
        if (link is null) return GrokBotStartLinkState.Missing;
        if (!SamePath(link.Target, grokExecutable)) return GrokBotStartLinkState.Other;
        var debugging = RawTokens(link.Arguments).Select(Unquote).Where(ChromiumSwitch.IsRemoteDebugging).ToArray();
        if (debugging.Length == 0)
            return link.Arguments.Count(character => character == '"') % 2 != 0 || RawTokens(link.Arguments).Any(token => Unquote(token) == "--")
                ? GrokBotStartLinkState.Unsafe : GrokBotStartLinkState.Plain;
        return addedPort is { } port && debugging.SequenceEqual(GrokBotLaunchPolicy.Arguments(port), StringComparer.Ordinal)
            ? GrokBotStartLinkState.Ours : GrokBotStartLinkState.Foreign;
    }

    public GrokBotStartLinkState State(int? addedPort) => Classify(files.Read(linkPath), addedPort);

    /// <summary>
    /// Adds the flags for <paramref name="port"/> to a plain shortcut and returns the backup path; null when the shortcut
    /// already has exactly Wingman's recorded pair. Throws, changing nothing, for any other shortcut.
    /// The pair is recorded (as pending, through <paramref name="record"/>) before the shortcut is saved, so Wingman's
    /// flags can never be on the shortcut without a record. Saved and verified: the record is confirmed. Not saved or
    /// undone: the previous record is put back. If the shortcut does hold the pair after a failure, the pending record
    /// stays and <see cref="Reconcile"/> confirms it.
    /// </summary>
    public string? Apply(int port, GrokBotStartLinkSetting setting, Action<GrokBotStartLinkSetting> record)
    {
        if (setting.Unreadable) throw new InvalidOperationException(GrokBotStartLinkSetting.UnreadableMessage);
        var flags = GrokBotLaunchPolicy.Arguments(port); // validates the port
        var link = files.Read(linkPath);
        switch (Classify(link, setting.AddedPort))
        {
            case GrokBotStartLinkState.Missing:
                throw new FileNotFoundException("Grok Bot has no Start menu shortcut here. Wingman never creates one.", linkPath);
            case GrokBotStartLinkState.Ours:
                return null;
            case GrokBotStartLinkState.Other:
                throw new InvalidOperationException("Grok Bot's Start menu shortcut points somewhere else (" + link!.Target + "); Wingman leaves it as it is.");
            case GrokBotStartLinkState.Foreign:
                throw new InvalidOperationException("Grok Bot's Start menu shortcut already has a debugging setting Wingman didn't add ("
                    + string.Join(' ', RawTokens(link!.Arguments).Where(token => ChromiumSwitch.IsRemoteDebugging(Unquote(token))))
                    + "). Wingman leaves the shortcut as it is, and the setting stays off.");
            case GrokBotStartLinkState.Unsafe:
                throw new InvalidOperationException("Grok Bot's Start menu shortcut has an unbalanced quote or a bare \"--\" in its arguments, so Wingman's port would not reach Grok Bot. Wingman leaves the shortcut as it is, and the setting stays off.");
        }
        var backup = Path.Combine(backupFolder, now().ToString("yyyyMMdd-HHmmss") + " " + Path.GetFileName(linkPath));
        files.Copy(linkPath, backup);
        var added = string.Join(' ', flags);
        record(new GrokBotStartLinkSetting(true, port, Pending: true));
        try
        {
            WriteVerified(link!, link!.Arguments.Trim().Length == 0 ? added : link.Arguments.TrimEnd() + " " + added, backup);
            record(new GrokBotStartLinkSetting(true, port));
        }
        catch
        {
            // The pending record stays while the pair is on the shortcut (an undo that failed, or a failed confirm).
            if (State(port) != GrokBotStartLinkState.Ours) record(setting);
            throw;
        }
        return backup;
    }

    /// <summary>
    /// Removes exactly the pair Wingman recorded adding (address 127.0.0.1 and the port in <paramref name="setting"/>), once
    /// each, and nothing else. No write when there is no record or the shortcut doesn't hold both. Before writing, the option
    /// is recorded off with the record kept, so <see cref="Reconcile"/> never adds the pair back even if a later save fails.
    /// Once the shortcut is read back without the pair, the record is cleared, even if saving changed another property
    /// (reported in <see cref="GrokBotStartLinkRestore.ChangedProperties"/>, not undone). If removing fails, the record and
    /// the option are put back as they were, so the pair is never on the shortcut without a record. The result says what
    /// happened, including any debugging switch Wingman didn't add, which is always left alone.
    /// </summary>
    public GrokBotStartLinkRestore Restore(GrokBotStartLinkSetting setting, Action<GrokBotStartLinkSetting> record)
    {
        if (setting.Unreadable) return new(GrokBotStartLinkRestoreKind.Failed, null, [], GrokBotStartLinkSetting.UnreadableMessage);
        var addedPort = setting.AddedPort;
        GrokBotStartLinkRestore result;
        var removing = false;
        try
        {
            var link = files.Read(linkPath);
            if (link is null) result = new(GrokBotStartLinkRestoreKind.NoShortcut, addedPort, []);
            else if (!SamePath(link.Target, grokExecutable)) result = new(GrokBotStartLinkRestoreKind.OtherProgram, addedPort, []);
            else
            {
                var arguments = addedPort is { } port ? WithoutPair(link.Arguments, GrokBotLaunchPolicy.Arguments(port)) : null;
                IReadOnlyList<string> changed = [];
                if (arguments is not null)
                {
                    // Removing: the option is off from here on (Reconcile never adds the pair back), the record is kept.
                    record(setting with { Enabled = false, Pending = true });
                    removing = true;
                    files.WriteArguments(linkPath, arguments);
                    // The read-back decides: the pair gone is a restore, even if saving changed another property too.
                    // That change is reported, not undone: restore has no backup of its own to put back.
                    changed = files.Read(linkPath) is { } after ? ChangedProperties(link with { Arguments = arguments }, after) : [];
                }
                var leftAlone = RawTokens(arguments ?? link.Arguments).Where(token => ChromiumSwitch.IsRemoteDebugging(Unquote(token))).ToArray();
                var kind = arguments is not null ? GrokBotStartLinkRestoreKind.Removed
                    : addedPort is null ? GrokBotStartLinkRestoreKind.NoRecord
                    : GrokBotStartLinkRestoreKind.NotOnShortcut;
                result = new(kind, addedPort, leftAlone, ChangedProperties: changed);
            }
            if (PairOnShortcut(addedPort)) throw new InvalidOperationException("Wingman's debugging port is still on the shortcut after saving.");
        }
        catch (Exception error)
        {
            // Not verified gone: the option and the record stay as they were.
            if (removing) TryRecord(record, setting);
            return new(GrokBotStartLinkRestoreKind.Failed, addedPort, [], error.Message);
        }
        // Nothing to undo and already off: writes nothing.
        if (setting != GrokBotStartLinkSetting.Off) record(GrokBotStartLinkSetting.Off);
        return result;
    }

    private static void TryRecord(Action<GrokBotStartLinkSetting> record, GrokBotStartLinkSetting setting)
    {
        try { record(setting); } catch (Exception) { }
    }

    /// <summary>Which shortcut properties other than the arguments differ from what Wingman expected to read back.</summary>
    private static IReadOnlyList<string> ChangedProperties(ShortcutLink expected, ShortcutLink after)
    {
        var changed = new List<string>();
        if (!string.Equals(expected.Target, after.Target, StringComparison.Ordinal)) changed.Add("target");
        if (!string.Equals(expected.WorkingDirectory, after.WorkingDirectory, StringComparison.Ordinal)) changed.Add("start folder");
        if (!string.Equals(expected.IconLocation, after.IconLocation, StringComparison.Ordinal)) changed.Add("icon");
        if (!string.Equals(expected.AppUserModelId, after.AppUserModelId, StringComparison.Ordinal)) changed.Add("taskbar grouping");
        if (!string.Equals(expected.Description, after.Description, StringComparison.Ordinal)) changed.Add("description");
        if (!string.Equals(expected.Arguments, after.Arguments, StringComparison.Ordinal)) changed.Add("other arguments");
        return changed;
    }

    /// <summary>Whether the shortcut (read again) is Grok Bot's and still holds the pair for <paramref name="port"/>.</summary>
    private bool PairOnShortcut(int? port) =>
        port is { } value && files.Read(linkPath) is { } link && SamePath(link.Target, grokExecutable)
        && WithoutPair(link.Arguments, GrokBotLaunchPolicy.Arguments(value)) is not null;

    /// <summary>
    /// Keeps the opt-in true over Grok Bot reinstalls: opted in and the shortcut is plain again (an installer wrote a fresh
    /// one), the flags are added again and the new port recorded. Never creates a missing shortcut and never changes
    /// one with a debugging setting Wingman didn't add; does nothing when not opted in. A pending record whose exact pair
    /// is on the shortcut is Wingman's and is confirmed. Every new setting goes through <paramref name="record"/>.
    /// </summary>
    public GrokBotStartLinkOutcome Reconcile(GrokBotStartLinkSetting setting, Func<int> port, Action<GrokBotStartLinkSetting> record)
    {
        if (!setting.Enabled) return GrokBotStartLinkOutcome.Disabled;
        switch (State(setting.AddedPort))
        {
            case GrokBotStartLinkState.Plain:
                return Apply(port(), setting, record) is null ? GrokBotStartLinkOutcome.Unchanged : GrokBotStartLinkOutcome.Reapplied;
            case GrokBotStartLinkState.Ours when setting.Pending:
                record(setting with { Pending = false });
                return GrokBotStartLinkOutcome.Confirmed;
            case GrokBotStartLinkState.Missing: return GrokBotStartLinkOutcome.Missing;
            case GrokBotStartLinkState.Foreign: return GrokBotStartLinkOutcome.Foreign;
            case GrokBotStartLinkState.Unsafe: return GrokBotStartLinkOutcome.Unsafe;
            default: return GrokBotStartLinkOutcome.Unchanged;
        }
    }

    public GrokBotStartLinkReport Report(GrokBotStartLinkSetting setting, int? recordedPort, string? keepShortcuts, string? keepShortcutsKey)
    {
        var link = files.Read(linkPath);
        return new GrokBotStartLinkReport(linkPath, Classify(link, setting.AddedPort), link?.Target, link?.Arguments,
            link?.IconLocation, link?.AppUserModelId, setting.Enabled, setting.AddedPort, setting.Pending, recordedPort, keepShortcuts,
            keepShortcutsKey, LatestBackup());
    }

    public string? LatestBackup() => files.Files(backupFolder)
        .Where(path => Path.GetFileName(path).EndsWith(" " + Path.GetFileName(linkPath), StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(path => path, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>
    /// The arguments with one exact copy of each of <paramref name="pair"/> removed; null unless both are present.
    /// When the pair is still where Wingman appended it, the arguments before it come back exactly as written.
    /// </summary>
    public static string? WithoutPair(string arguments, IReadOnlyList<string> pair)
    {
        var tokens = RawTokens(arguments).ToList();
        var trimmed = arguments.TrimEnd();
        var suffix = string.Join(' ', pair);
        if (tokens.Count >= pair.Count && tokens.Skip(tokens.Count - pair.Count).SequenceEqual(pair, StringComparer.Ordinal)
            && trimmed.EndsWith(suffix, StringComparison.Ordinal))
            return trimmed[..^suffix.Length].TrimEnd();
        foreach (var flag in pair)
        {
            var index = tokens.FindIndex(token => token == flag);
            if (index < 0) return null;
            tokens.RemoveAt(index);
        }
        return string.Join(' ', tokens);
    }

    private void WriteVerified(ShortcutLink link, string arguments, string? rollback)
    {
        files.WriteArguments(linkPath, arguments);
        if (files.Read(linkPath) == link with { Arguments = arguments }) return;
        if (rollback is not null) files.Copy(rollback, linkPath);
        throw new InvalidOperationException("Saving Grok Bot's Start menu shortcut changed more than its arguments"
            + (rollback is null ? "." : ", so Wingman put the original back."));
    }

    private static string Unquote(string token) => token.Replace("\"", string.Empty);

    /// <summary>Splits arguments at unquoted spaces, keeping each argument exactly as written (quotes included).</summary>
    private static IEnumerable<string> RawTokens(string arguments)
    {
        var current = new StringBuilder();
        var quoted = false;
        foreach (var character in arguments)
        {
            if (character == '"') quoted = !quoted;
            if (!quoted && char.IsWhiteSpace(character))
            {
                if (current.Length > 0) yield return current.ToString();
                current.Clear();
                continue;
            }
            current.Append(character);
        }
        if (current.Length > 0) yield return current.ToString();
    }

    private static bool SamePath(string? left, string right) =>
        !string.IsNullOrWhiteSpace(left)
        && string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(left)), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}

public enum GrokBotStartLinkRestoreKind { Removed, NoRecord, NotOnShortcut, NoShortcut, OtherProgram, Failed }

/// <summary>What a restore did, in words for <c>--restore-grok-bot-start-link</c> and the development toggle.</summary>
public sealed record GrokBotStartLinkRestore(GrokBotStartLinkRestoreKind Kind, int? AddedPort, IReadOnlyList<string> LeftAlone,
    string? Error = null, IReadOnlyList<string>? ChangedProperties = null)
{
    public bool Removed => Kind == GrokBotStartLinkRestoreKind.Removed;

    /// <summary>The question before turning the option off, matching the one before turning it on.</summary>
    public static string Question(int? addedPort) =>
        "Turn off Wingman's Start menu option for Grok Bot? "
        + (addedPort is { } port
            ? $"Wingman removes exactly the debugging port it added (127.0.0.1, port {port}) from Grok Bot's Start menu shortcut. "
            : "Wingman has no record of adding a debugging port, so it removes nothing from the shortcut. ")
        + "Any other setting on the shortcut, including a debugging port Wingman didn't add, is left alone. "
        + "The shortcut still opens Grok Bot, with its name, icon and grouping unchanged. "
        + "After Grok Bot's next self-update, Wingman shows its reconnect prompt instead of reconnecting on its own.";

    public string Message
    {
        get
        {
            if (Kind == GrokBotStartLinkRestoreKind.Failed && Error == GrokBotStartLinkSetting.UnreadableMessage) return Error;
            if (Kind == GrokBotStartLinkRestoreKind.Failed)
                return "Restore failed: " + Error + " Wingman kept its record of the debugging port it added (127.0.0.1, port "
                    + AddedPort + "), and the Start menu option is still on. Try again.";
            var what = Kind switch
            {
                GrokBotStartLinkRestoreKind.Removed =>
                    $"Removed Wingman's debugging port (127.0.0.1, port {AddedPort}) from Grok Bot's Start menu shortcut.",
                GrokBotStartLinkRestoreKind.NoRecord =>
                    "Wingman has no record of adding a debugging port to Grok Bot's Start menu shortcut, so nothing was removed.",
                GrokBotStartLinkRestoreKind.NotOnShortcut =>
                    $"Wingman's recorded debugging port (127.0.0.1, port {AddedPort}) isn't on Grok Bot's Start menu shortcut, so nothing was removed.",
                GrokBotStartLinkRestoreKind.NoShortcut => "Grok Bot has no Start menu shortcut here, so nothing was removed.",
                _ => "Grok Bot's Start menu shortcut points at another program, so nothing was changed.",
            };
            var left = LeftAlone.Count == 0 ? string.Empty
                : " A debugging setting Wingman didn't add was left alone: " + string.Join(' ', LeftAlone) + ".";
            var changed = ChangedProperties is not { Count: > 0 } properties ? string.Empty
                : " Saving the shortcut also changed its " + string.Join(", ", properties) + "; Wingman left that as Windows saved it.";
            return what + left + changed + " The Start menu option is now off.";
        }
    }
}

/// <summary>
/// The opt-in and exactly what Wingman added to the shortcut: the loopback address and <see cref="AddedPort"/>
/// (null when nothing is recorded, so nothing will ever be removed). <see cref="Pending"/>: recorded before the shortcut
/// was saved and not yet confirmed; the pair still counts as Wingman's wherever it is found on the shortcut.
/// </summary>
public sealed record GrokBotStartLinkSetting(bool Enabled, int? AddedPort, bool Pending = false, bool Unreadable = false)
{
    /// <summary>What Wingman says when its own setting file exists but can't be read; it then changes nothing.</summary>
    public const string UnreadableMessage = "Wingman's record of the Grok Bot shortcut (grok-bot-start-link.json) can't be read, so Wingman doesn't know what it added and changes nothing. Check the shortcut yourself, then delete that file to start over.";

    public static readonly GrokBotStartLinkSetting Off = new(false, null);
}

/// <summary>The setting, kept in Wingman's own data folder. Missing, unreadable or another schema means off.</summary>
public static class GrokBotStartLinkOptIn
{
    public const string FileName = "grok-bot-start-link.json";
    private const string Schema = "wingman.grok-bot-start-link.v2";

    public static string PathIn(string wingmanDataRoot) => Path.Combine(wingmanDataRoot, FileName);

    /// <summary>
    /// Runs <paramref name="action"/> holding a named mutex for this setting file (no lock file), so the tray's reconcile
    /// and the restore command, which may run in another process, never interleave a read and a write. Waits up to
    /// <paramref name="wait"/> (default 10 s), then throws <see cref="TimeoutException"/>. A mutex left by a process that
    /// died is taken over. The action runs on the calling thread, which owns the mutex.
    /// </summary>
    public static T Locked<T>(string path, Func<T> action, TimeSpan? wait = null)
    {
        using var mutex = new Mutex(false, MutexName(path));
        bool owned;
        try { owned = mutex.WaitOne(wait ?? TimeSpan.FromSeconds(10)); }
        catch (AbandonedMutexException) { owned = true; }
        if (!owned) throw new TimeoutException("Another Wingman is changing the Grok Bot shortcut setting. Try again.");
        try { return action(); }
        finally { mutex.ReleaseMutex(); }
    }

    /// <summary>One mutex per setting file (its full path, case-insensitive), in the user's session.</summary>
    public static string MutexName(string path)
    {
        var key = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()));
        return @"Local\CodexWingman.GrokBotStartLink." + Convert.ToHexString(key, 0, 12);
    }

    private static readonly GrokBotStartLinkSetting Unreadable = GrokBotStartLinkSetting.Off with { Unreadable = true };

    /// <summary>Missing: off. Present but unreadable, or another schema: off and <see cref="GrokBotStartLinkSetting.Unreadable"/>.</summary>
    public static GrokBotStartLinkSetting Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return GrokBotStartLinkSetting.Off;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.String || schema.GetString() != Schema)
                return Unreadable;
            var enabled = root.TryGetProperty("enabled", out var flag) && flag.ValueKind == JsonValueKind.True;
            int? port = null;
            if (root.TryGetProperty("added", out var added) && added.ValueKind == JsonValueKind.Object
                && added.TryGetProperty("address", out var address) && address.ValueKind == JsonValueKind.String
                && address.GetString() == "127.0.0.1"
                && added.TryGetProperty("port", out var number) && number.ValueKind == JsonValueKind.Number
                && number.TryGetInt32(out var value) && value is >= 1024 and <= 65535)
                port = value;
            var pending = port is not null && root.TryGetProperty("pending", out var waiting) && waiting.ValueKind == JsonValueKind.True;
            return new GrokBotStartLinkSetting(enabled, port, pending);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return Unreadable; }
    }

    public static void Save(string path, GrokBotStartLinkSetting setting)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new
        {
            schema = Schema,
            enabled = setting.Enabled,
            added = setting.AddedPort is { } port ? new { address = "127.0.0.1", port } : null,
            pending = setting.AddedPort is not null && setting.Pending,
        }));
        File.Move(temporary, path, overwrite: true);
    }
}

/// <summary>Where a standalone command's text goes: a redirected output, the console it was started from, or a message box.</summary>
public enum StandaloneOutputRoute { Redirected, ParentConsole, MessageBox }

public static class StandaloneOutput
{
    /// <summary>Never opens a console of its own (Wingman is a windowed program, so nothing flashes).</summary>
    public static StandaloneOutputRoute Choose(bool outputRedirected, Func<bool> attachToParentConsole) =>
        outputRedirected ? StandaloneOutputRoute.Redirected
        : attachToParentConsole() ? StandaloneOutputRoute.ParentConsole
        : StandaloneOutputRoute.MessageBox;
}
