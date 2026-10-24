namespace CodexWingman.Core;

public enum WingmanLaunchTarget { Preferences, Codex, T3Code, T3Web, GrokBot }

/// <summary>Development-channel commands for an already running Wingman. A command never starts a new tray.</summary>
public enum WingmanCommand { None, T3WebRenewNow, Close, DumpMenu, T3WebCheckSignIn, GrokBotPin }

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
            else if (CommandFor(argument) is { } command)
            {
                if (options.Command != WingmanCommand.None) throw new ArgumentException("Give Wingman one command at a time.");
                options = options with { Command = command };
            }
        }
        return options;
    }

    public bool OpenOnStartup(WingmanLaunchTarget app, HelperSettings settings) =>
        // The tray checks that the app is on this PC (T3 web: Chrome plus an address or pairing folder).
        Target == WingmanLaunchTarget.Preferences ? settings.OpensOnLaunch(app) : Target == app;

    private static WingmanCommand? CommandFor(string argument) => argument switch
    {
        "--t3-web-renew-now" => WingmanCommand.T3WebRenewNow,
        "--close" => WingmanCommand.Close,
        "--dump-menu" => WingmanCommand.DumpMenu,
        "--t3-web-check-sign-in" => WingmanCommand.T3WebCheckSignIn,
        "--grok-bot-pin" => WingmanCommand.GrokBotPin,
        _ => null,
    };

    /// <summary>Every command a running development build listens for.</summary>
    public static IReadOnlyList<WingmanCommand> Commands { get; } =
        [WingmanCommand.T3WebRenewNow, WingmanCommand.Close, WingmanCommand.DumpMenu, WingmanCommand.T3WebCheckSignIn, WingmanCommand.GrokBotPin];

    /// <summary>Event a running development build listens on for this command.</summary>
    public static string CommandEventName(WingmanCommand command) => command == WingmanCommand.None
        ? throw new ArgumentException("No command selected.", nameof(command))
        : WingmanIdentity.ActivationEventName + ".Command." + command;

    /// <summary>File a running development build writes the live tray menu to on --dump-menu.</summary>
    public static string MenuDumpPath(string settingsDirectory) => Path.Combine(settingsDirectory, "menu-dump.json");

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
