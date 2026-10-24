namespace CodexWingman.Core;

/// <summary>One entry in an app line's flyout. Every app gets the same entries in the same order.</summary>
public enum AppFlyoutItem { OpenWindow, Repair, StatusDetails, Separator, OpenOnLaunch }

/// <summary>
/// The tray's app lines (Codex, T3 Code, T3 web, Grok Bot) share one short flyout with plain, identical
/// wording; the flyout's parent line already names the app. Status lives in the flyout's status item
/// only: app lines carry no tooltip. Automatic work (T3 web sign-in renewal) and setup-only actions
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
        AppFlyoutItem.OpenOnLaunch => "Open on launch",
        _ => throw new ArgumentOutOfRangeException(nameof(item)),
    };

    /// <summary>The app lines, top to bottom.</summary>
    public static IReadOnlyList<WingmanLaunchTarget> Apps { get; } =
        [WingmanLaunchTarget.Codex, WingmanLaunchTarget.T3Code, WingmanLaunchTarget.T3Web, WingmanLaunchTarget.GrokBot];

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
    private static readonly string[] AppLinePrefixes = ["Codex:", "T3 Code:", "T3 web:", "Grok Bot:"];

    /// <summary>App lines: root entries named for an app, or whose flyout has a shared app item.</summary>
    public static IReadOnlyList<TrayMenuNode> AppLines(IReadOnlyList<TrayMenuNode> root) =>
        root.Where(node => AppLinePrefixes.Any(prefix => node.Text.StartsWith(prefix, StringComparison.Ordinal))
            || node.Children?.Any(child => child.Text == TrayMenuLayout.Label(AppFlyoutItem.OpenWindow)
                || child.Text == TrayMenuLayout.Label(AppFlyoutItem.OpenOnLaunch)) == true).ToList();

    /// <summary>Everything wrong with a menu tree against the tray contract; empty when it holds.</summary>
    public static IReadOnlyList<string> Problems(IReadOnlyList<TrayMenuNode> root)
    {
        var problems = new List<string>();
        foreach (var node in root.Where(node => !string.IsNullOrEmpty(node.ToolTip)))
            problems.Add($"First-layer item \"{node.Text}\" has a tooltip.");
        var expected = TrayMenuLayout.AppFlyout.Select(TrayMenuLayout.Label).ToArray();
        var lines = AppLines(root);
        if (lines.Count == 0) problems.Add("No app line found.");
        foreach (var line in lines)
        {
            var actual = (line.Children ?? []).Select(child => child.Text).ToArray();
            if (!actual.SequenceEqual(expected))
                problems.Add($"\"{line.Text}\" flyout is [{string.Join(" | ", actual)}], expected [{string.Join(" | ", expected)}].");
            foreach (var child in (line.Children ?? []).Where(child => !child.Visible && !child.IsSeparator))
                problems.Add($"\"{line.Text}\" hides \"{child.Text}\".");
        }
        return problems;
    }
}
