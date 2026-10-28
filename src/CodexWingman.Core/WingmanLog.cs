using System.Globalization;
using System.Text;

namespace CodexWingman.Core;

/// <summary>
/// Small rolling event log (<c>logs\wingman.log</c> beside settings.json, rolled once to <c>wingman.1.log</c>).
/// Lines hold an event name and short app-level values (app, action, process ID, port, version), never
/// paths, window titles, page addresses, chat text or error messages. Logging never throws.
/// </summary>
public static class WingmanLog
{
    public const long MaxBytes = 256 * 1024;
    public const string FileName = "wingman.log";
    public const string RolledFileName = "wingman.1.log";
    private const int MaxValueLength = 64;
    private static readonly object Sync = new();
    private static string? directory;

    /// <summary>Folder the log is written to, or null while logging is off (the default, e.g. in tests).</summary>
    public static string? Directory => directory;

    public static void Configure(string? logDirectory)
    {
        lock (Sync) directory = string.IsNullOrWhiteSpace(logDirectory) ? null : logDirectory;
    }

    public static string LogDirectory(string settingsDirectory) => Path.Combine(settingsDirectory, "logs");

    public static void Write(string eventName, params (string Name, object? Value)[] fields)
    {
        var line = Format(DateTimeOffset.Now, eventName, fields);
        lock (Sync)
        {
            if (directory is null) return;
            try
            {
                System.IO.Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, FileName);
                var info = new FileInfo(path);
                if (info.Exists && info.Length + line.Length > MaxBytes)
                    File.Move(path, Path.Combine(directory, RolledFileName), overwrite: true);
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static string Format(DateTimeOffset time, string eventName, params (string Name, object? Value)[] fields)
    {
        var builder = new StringBuilder(time.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture))
            .Append(' ').Append(Clean(eventName));
        foreach (var (name, value) in fields)
            builder.Append(' ').Append(Clean(name)).Append('=').Append(Clean(value switch
            {
                null => "-",
                bool flag => flag ? "true" : "false",
                DateTime moment => moment.ToUniversalTime().ToString("HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? "-",
            }));
        return builder.Append('\n').ToString();
    }

    private static string Clean(string value)
    {
        var builder = new StringBuilder(Math.Min(value.Length, MaxValueLength));
        foreach (var character in value)
        {
            if (builder.Length == MaxValueLength) break;
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':' or '+' ? character : '_');
        }
        return builder.Length == 0 ? "-" : builder.ToString();
    }
}
