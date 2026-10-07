namespace CodexWingman.Core;

public enum WingmanLaunchTarget { Preferences, Codex, T3Code }

public sealed record WingmanLaunchOptions(
    WingmanLaunchTarget Target = WingmanLaunchTarget.Preferences,
    string? LocalAppData = null,
    bool RegisterOnly = false)
{
    public static WingmanLaunchOptions Parse(IEnumerable<string> arguments)
    {
        var options = new WingmanLaunchOptions();
        var selected = false;
        foreach (var argument in arguments)
        {
            if (argument.StartsWith("--launch=", StringComparison.Ordinal))
            {
                if (selected) throw new ArgumentException("Select one Wingman launcher app.");
                selected = true;
                options = options with { Target = argument[9..] switch
                {
                    "codex" => WingmanLaunchTarget.Codex,
                    "t3-code" => WingmanLaunchTarget.T3Code,
                    _ => throw new ArgumentException("Wingman launcher app must be codex or t3-code."),
                }};
            }
            else if (argument.StartsWith("--local-app-data=", StringComparison.Ordinal))
            {
                var path = argument[17..];
                if (options.LocalAppData is not null || !Path.IsPathFullyQualified(path)
                    || path.IndexOfAny(['"', '\r', '\n']) >= 0)
                    throw new ArgumentException("The Wingman settings profile must be one absolute directory.");
                options = options with { LocalAppData = Path.GetFullPath(path) };
            }
            else if (argument == "--register-launchers") options = options with { RegisterOnly = true };
        }
        return options;
    }

    public bool OpenOnStartup(WingmanLaunchTarget app, HelperSettings settings) =>
        Target == WingmanLaunchTarget.Preferences
            ? app switch
            {
                WingmanLaunchTarget.Codex => settings.OpenCodexOnLaunch,
                WingmanLaunchTarget.T3Code => settings.OpenT3CodeOnLaunch,
                _ => false,
            }
            : Target == app;

    public string ActivationEventName => Target switch
    {
        WingmanLaunchTarget.T3Code => WingmanIdentity.ActivationEventName + ".T3Code",
        WingmanLaunchTarget.Codex => WingmanIdentity.ActivationEventName + ".Codex",
        _ => WingmanIdentity.ActivationEventName,
    };

    public static string ShortcutArguments(WingmanLaunchTarget target, string? localAppData = null)
    {
        var arguments = target switch
        {
            WingmanLaunchTarget.Codex => "--launch=codex",
            WingmanLaunchTarget.T3Code => "--launch=t3-code",
            _ => throw new ArgumentException("A shortcut must select an app.", nameof(target)),
        };
        if (localAppData is null) return arguments;
        var validated = Parse(["--local-app-data=" + localAppData]).LocalAppData!;
        var profileArgument = "--local-app-data=" + validated;
        // Windows doubles trailing backslashes before the closing quote.
        var trailingSlashes = profileArgument.Length - profileArgument.TrimEnd('\\').Length;
        return arguments + " \"" + profileArgument + new string('\\', trailingSlashes) + "\"";
    }
}
