namespace CodexWingman.Core;

public enum WingmanLaunchTarget { Preferences, Codex, T3Code, T3Web, GrokBot }

/// <summary>Development-channel commands for an already running Wingman. A command never starts a new tray.</summary>
public enum WingmanCommand { None, T3WebRenewNow, Close }

public sealed record WingmanLaunchOptions(
    WingmanLaunchTarget Target = WingmanLaunchTarget.Preferences,
    string? LocalAppData = null,
    bool RegisterOnly = false,
    WingmanCommand Command = WingmanCommand.None)
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
                    "t3-web" => WingmanLaunchTarget.T3Web,
                    "grok-bot" => WingmanLaunchTarget.GrokBot,
                    _ => throw new ArgumentException("Wingman launcher app must be codex, t3-code, t3-web or grok-bot."),
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
            else if (argument is "--t3-web-renew-now" or "--close")
            {
                if (options.Command != WingmanCommand.None) throw new ArgumentException("Give Wingman one command at a time.");
                options = options with { Command = argument == "--close" ? WingmanCommand.Close : WingmanCommand.T3WebRenewNow };
            }
        }
        return options;
    }

    public bool OpenOnStartup(WingmanLaunchTarget app, HelperSettings settings) =>
        Target == WingmanLaunchTarget.Preferences
            ? app switch
            {
                WingmanLaunchTarget.Codex => settings.OpenCodexOnLaunch,
                WingmanLaunchTarget.T3Code => settings.OpenT3CodeOnLaunch,
                WingmanLaunchTarget.T3Web => settings.T3Web?.OpenOnLaunch == true && settings.T3Web.IsConfigured,
                WingmanLaunchTarget.GrokBot => settings.OpenGrokBotOnLaunch,
                _ => false,
            }
            : Target == app;

    /// <summary>Event a running development build listens on for this command.</summary>
    public static string CommandEventName(WingmanCommand command) => command switch
    {
        WingmanCommand.T3WebRenewNow => WingmanIdentity.ActivationEventName + ".Command.T3WebRenewNow",
        WingmanCommand.Close => WingmanIdentity.ActivationEventName + ".Command.Close",
        _ => throw new ArgumentException("No command selected.", nameof(command)),
    };

    public string ActivationEventName => Target == WingmanLaunchTarget.Preferences
        ? WingmanIdentity.ActivationEventName
        : WingmanIdentity.ActivationEventName + IdentitySuffix(Target);

    /// <summary>Stable per-target suffix shared by the activation event and the AppUserModelID.</summary>
    public static string IdentitySuffix(WingmanLaunchTarget target) => target switch
    {
        WingmanLaunchTarget.Codex => ".Codex",
        WingmanLaunchTarget.T3Code => ".T3Code",
        WingmanLaunchTarget.T3Web => ".T3Web",
        WingmanLaunchTarget.GrokBot => ".GrokBot",
        _ => string.Empty,
    };

    public static string AppUserModelIdFor(WingmanLaunchTarget target) =>
        WingmanIdentity.AppUserModelId + IdentitySuffix(target);

    public static string ShortcutArguments(WingmanLaunchTarget target, string? localAppData = null)
    {
        var arguments = target switch
        {
            WingmanLaunchTarget.Codex => "--launch=codex",
            WingmanLaunchTarget.T3Code => "--launch=t3-code",
            WingmanLaunchTarget.T3Web => "--launch=t3-web",
            WingmanLaunchTarget.GrokBot => "--launch=grok-bot",
            _ => throw new ArgumentException("A shortcut must select an app.", nameof(target)),
        };
        var profile = RelaunchArguments(localAppData);
        return profile.Length == 0 ? arguments : arguments + " " + profile;
    }

    public static string RelaunchArguments(string? localAppData)
    {
        if (localAppData is null) return string.Empty;
        var validated = Parse(["--local-app-data=" + localAppData]).LocalAppData!;
        var profileArgument = "--local-app-data=" + validated;
        // Windows doubles trailing backslashes before the closing quote.
        var trailingSlashes = profileArgument.Length - profileArgument.TrimEnd('\\').Length;
        return "\"" + profileArgument + new string('\\', trailingSlashes) + "\"";
    }
}
