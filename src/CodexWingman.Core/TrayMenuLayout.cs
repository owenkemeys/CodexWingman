namespace CodexWingman.Core;

/// <summary>One entry in an app line's flyout. Every app gets the same entries in the same order.</summary>
public enum AppFlyoutItem { OpenWindow, Repair, StatusDetails, Separator, OpenOnLaunch }

/// <summary>
/// The tray's app lines (Codex, T3 Code, T3 Code (web), Grok Bot) share one short flyout with plain, identical
/// wording; the flyout's parent line already names the app. Status lives in the flyout's status item
/// only: app lines carry no tooltip and show the app and its state, nothing else. Automatic work (T3 Code (web) sign-in renewal) and setup-only actions
/// (the Grok Bot taskbar pin) are not menu items.
/// </summary>
public static class TrayMenuLayout
{
    public static IReadOnlyList<AppFlyoutItem> AppFlyout { get; } =
    [
        AppFlyoutItem.OpenWindow,
        AppFlyoutItem.Repair,
        AppFlyoutItem.StatusDetails,
        AppFlyoutItem.Separator,
        AppFlyoutItem.OpenOnLaunch,
    ];

    public const string SeparatorText = "-";

    public static string Label(AppFlyoutItem item) => item switch
    {
        AppFlyoutItem.OpenWindow => "Open window",
        AppFlyoutItem.Repair => "Repair",
        AppFlyoutItem.StatusDetails => "Status details...",
        AppFlyoutItem.Separator => SeparatorText,
        AppFlyoutItem.OpenOnLaunch => "Open when Wingman starts",
        _ => throw new ArgumentOutOfRangeException(nameof(item)),
    };

    /// <summary>
    /// The label for one app's flyout item. Identical for every app except one: Codex's first item is
    /// "New window", because it opens an additional native Codex window (Codex's own new-window action);
    /// the other apps' "Open window" brings their window forward or opens one if none.
    /// </summary>
    public static string Label(WingmanLaunchTarget app, AppFlyoutItem item) =>
        app == WingmanLaunchTarget.Codex && item == AppFlyoutItem.OpenWindow ? "New window" : Label(item);

    public static IReadOnlyList<string> FlyoutLabels(WingmanLaunchTarget app) =>
        AppFlyout.Select(item => Label(app, item)).ToArray();

    /// <summary>The app lines, top to bottom.</summary>
    public static IReadOnlyList<WingmanLaunchTarget> Apps { get; } =
        [WingmanLaunchTarget.Codex, WingmanLaunchTarget.T3Code, WingmanLaunchTarget.T3Web, WingmanLaunchTarget.GrokBot];

    /// <summary>The one user-facing name of each app, used on its line and in its dialogs and balloons.</summary>
    public static string AppName(WingmanLaunchTarget app) => app switch
    {
        WingmanLaunchTarget.Codex => "Codex",
        WingmanLaunchTarget.T3Code => "T3 Code",
        WingmanLaunchTarget.T3Web => "T3 Code (web)",
        WingmanLaunchTarget.GrokBot => "Grok Bot",
        _ => throw new ArgumentOutOfRangeException(nameof(app)),
    };

    /// <summary>The only states an app line shows. Counts and other detail live in Status details....</summary>
    public static IReadOnlyList<string> LineStates { get; } =
        ["Running", "Closed", "Needs repair", "Paused", "No Helpers", "Checking", "Not ready", "Status unavailable"];

    /// <summary>The app line: the app and its state only, e.g. "Codex: Closed" or "Grok Bot: Running".</summary>
    public static string LineText(WingmanLaunchTarget app, string state) =>
        LineStates.Contains(state, StringComparer.Ordinal)
            ? AppName(app) + ": " + state
            : throw new ArgumentOutOfRangeException(nameof(state), state, "Not an app line state.");

    /// <summary>An app line shows only when the app is on this PC (Codex is Wingman's base app and always shows).</summary>
    public static bool ShowAppLine(WingmanLaunchTarget app, bool installed) => app == WingmanLaunchTarget.Codex || installed;
}

/// <summary>A plain copy of a tray menu entry, used to dump and check the live menu.</summary>
public sealed record TrayMenuNode(
    string Text,
    string? ToolTip = null,
    bool Visible = true,
    bool Enabled = true,
    bool Checked = false,
    IReadOnlyList<TrayMenuNode>? Children = null)
{
    public bool IsSeparator => Text == TrayMenuLayout.SeparatorText;
}

public static class TrayMenuCheck
{
    private static readonly string[] AppLinePrefixes = TrayMenuLayout.Apps.Select(app => TrayMenuLayout.AppName(app) + ":").ToArray();

    /// <summary>App lines: root entries named for an app, or whose flyout has a shared app item.</summary>
    public static IReadOnlyList<TrayMenuNode> AppLines(IReadOnlyList<TrayMenuNode> root) =>
        root.Where(node => AppLinePrefixes.Any(prefix => node.Text.StartsWith(prefix, StringComparison.Ordinal))
            || node.Children?.Any(child => child.Text == TrayMenuLayout.Label(AppFlyoutItem.OpenWindow)
                || child.Text == TrayMenuLayout.Label(WingmanLaunchTarget.Codex, AppFlyoutItem.OpenWindow)
                || child.Text == TrayMenuLayout.Label(AppFlyoutItem.OpenOnLaunch)) == true).ToList();

    /// <summary>The app a line belongs to, from its "&lt;app name&gt;:" prefix (longest name first).</summary>
    public static WingmanLaunchTarget? AppOf(string text) => TrayMenuLayout.Apps
        .OrderByDescending(app => TrayMenuLayout.AppName(app).Length)
        .Select(app => (WingmanLaunchTarget?)app)
        .FirstOrDefault(app => text.StartsWith(TrayMenuLayout.AppName(app!.Value) + ":", StringComparison.Ordinal));

    /// <summary>True for "&lt;app name&gt;: &lt;state&gt;" with one of the known app names and line states.</summary>
    public static bool IsLineText(string text) => TrayMenuLayout.Apps.Any(app =>
        TrayMenuLayout.LineStates.Any(state => text == TrayMenuLayout.AppName(app) + ": " + state));

    /// <summary>Everything wrong with a menu tree against the tray contract; empty when it holds.</summary>
    public static IReadOnlyList<string> Problems(IReadOnlyList<TrayMenuNode> root)
    {
        var problems = new List<string>();
        foreach (var node in root.Where(node => !string.IsNullOrEmpty(node.ToolTip)))
            problems.Add($"First-layer item \"{node.Text}\" has a tooltip.");
        var lines = AppLines(root);
        if (lines.Count == 0) problems.Add("No app line found.");
        foreach (var line in lines)
        {
            if (!IsLineText(line.Text))
                problems.Add($"\"{line.Text}\" is not an app name and a plain state (e.g. \"Codex: Closed\").");
            // Same flyout for every app; only Codex's first item reads "New window".
            var expected = AppOf(line.Text) is { } app ? TrayMenuLayout.FlyoutLabels(app)
                : TrayMenuLayout.AppFlyout.Select(TrayMenuLayout.Label).ToArray();
            var actual = (line.Children ?? []).Select(child => child.Text).ToArray();
            if (!actual.SequenceEqual(expected))
                problems.Add($"\"{line.Text}\" flyout is [{string.Join(" | ", actual)}], expected [{string.Join(" | ", expected)}].");
            foreach (var child in (line.Children ?? []).Where(child => !child.Visible && !child.IsSeparator))
                problems.Add($"\"{line.Text}\" hides \"{child.Text}\".");
        }
        return problems;
    }
}
