using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using CodexWingman.Core;

internal static class NativeUpdaterTests
{
    public static bool TryChild(string[] args)
    {
        if (args.FirstOrDefault() != "--updater-child") return false;
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "fail-start.marker")))
        {
            Environment.ExitCode = 9;
            return true;
        }
        var profile = WingmanLaunchOptions.Parse(args.Skip(2)).LocalAppData;
        File.WriteAllText(args[1], JsonSerializer.Serialize(new
        {
            profile, pid = Environment.ProcessId, path = Environment.ProcessPath,
            marker = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "package.marker")),
        }));
        Thread.Sleep(8_000);
        return true;
    }

    public static void Run(string root)
    {
        var work = Path.Combine(root, "Windows PowerShell update contract");
        var installed = Path.Combine(work, "App-stable");
        var incoming = Path.Combine(work, "Incoming");
        var receipt = Path.Combine(work, "child.json");
        var profile = Path.Combine(work, "Retained profile with spaces");
        Directory.CreateDirectory(profile);
        var settings = Path.Combine(profile, "settings.json");
        File.WriteAllText(settings, "preserve external settings");
        var settingsHash = Hash(settings);
        var exe = Path.GetFileName(Environment.ProcessPath!);
        var script = Path.Combine(AppContext.BaseDirectory, "Apply-CodexAppUpdate.ps1");
        var relaunch = "--updater-child \"" + receipt + "\" " + WingmanLaunchOptions.RelaunchArguments(profile);

        void Package(string marker, bool fail = false)
        {
            if (Directory.Exists(incoming)) Directory.Delete(incoming, true);
            Directory.CreateDirectory(incoming);
            foreach (var file in Directory.GetFiles(AppContext.BaseDirectory))
                File.Copy(file, Path.Combine(incoming, Path.GetFileName(file)));
            File.WriteAllText(Path.Combine(incoming, "package.marker"), marker);
            Directory.CreateDirectory(Path.Combine(incoming, "Helpers", "Bundled"));
            File.WriteAllText(Path.Combine(incoming, "Helpers", "Bundled", "data"), marker);
            if (fail) File.WriteAllText(Path.Combine(incoming, "fail-start.marker"), "fail only the new package");
            var files = Directory.GetFiles(incoming, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(incoming, path).Replace('\\', '/'), Hash);
            File.WriteAllText(Path.Combine(incoming, "release.json"), JsonSerializer.Serialize(new
            {
                schema = "codexwingman.release.v1", repository = "https://github.com/owenkemeys/CodexWingman",
                version = "1.0.0", files,
            }));
        }

        int Update(bool preflight = false, bool corrupt = false)
        {
            if (File.Exists(receipt)) File.Delete(receipt);
            var info = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var arg in new[]
            {
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
                "-Package", incoming, "-Installed", installed, "-WaitingPid", int.MaxValue.ToString(),
                "-ExpectedVersion", "1.0.0", "-ExpectedRepository", "https://github.com/owenkemeys/CodexWingman",
                "-ExpectedSchema", "codexwingman.release.v1", "-ExecutableName", exe,
                "-StableDirectoryName", "App-stable", "-ExtensionsDirectoryName", "Helpers",
                "-RelaunchArguments", relaunch,
            }) info.ArgumentList.Add(arg);
            if (preflight) info.ArgumentList.Add("-PreflightOnly");
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(25_000)) throw new Exception("Disposable updater timed out");
            if (!corrupt && process.ExitCode != 0) throw new Exception(error.Result + output.Result);
            return process.ExitCode;
        }

        void Child(string marker)
        {
            // Rollback starts the restored child asynchronously before reporting the
            // original failure. Wait for its own receipt, rather than racing startup.
            JsonDocument? received = null;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (received is null && DateTime.UtcNow < deadline)
            {
                try
                {
                    if (File.Exists(receipt)) received = JsonDocument.Parse(File.ReadAllText(receipt));
                }
                catch (IOException) { }
                catch (JsonException) { }
                if (received is null) Thread.Sleep(50);
            }
            using var json = received ?? throw new Exception("Updated app did not report its relaunch");
            var value = json.RootElement;
            Equal(profile, value.GetProperty("profile").GetString(), "update and rollback retain the profile");
            Equal(Path.Combine(installed, exe), value.GetProperty("path").GetString(), "update relaunch uses the stable executable path");
            Equal(marker, value.GetProperty("marker").GetString(), "expected package is running");
            Equal(settingsHash, Hash(settings), "external settings are unchanged");
            Equal("custom", File.ReadAllText(Path.Combine(installed, "Helpers", "Custom", "data")), "custom helpers survive");
            try
            {
                using var child = Process.GetProcessById(value.GetProperty("pid").GetInt32());
                if (!child.WaitForExit(12_000)) throw new Exception("Disposable child did not finish");
            }
            catch (ArgumentException) { /* Child already completed. */ }
        }

        Package("first");
        Directory.CreateDirectory(installed);
        File.Copy(Path.Combine(incoming, exe), Path.Combine(installed, exe));
        Directory.CreateDirectory(Path.Combine(installed, "Helpers", "Custom"));
        File.WriteAllText(Path.Combine(installed, "Helpers", "Custom", "data"), "custom");
        Equal(0, Update(preflight: true), "PowerShell 5.1 disposable preflight");
        Equal(0, Update(), "first managed installation");
        Child("first");
        Package("second");
        Equal(0, Update(), "repeated managed update");
        Child("second");
        Package("failed", fail: true);
        if (Update(corrupt: true) == 0) throw new Exception("Failed launch did not trigger rollback");
        Child("second");
        Package("tampered");
        File.AppendAllText(Path.Combine(incoming, "package.marker"), "tamper");
        if (Update(preflight: true, corrupt: true) == 0) throw new Exception("Tampered package passed preflight");
        Equal("second", File.ReadAllText(Path.Combine(installed, "package.marker")), "tamper rejection preserves the installed package");
        Console.WriteLine("Windows PowerShell update: first install, repeated update, profile retention, helper carry-forward, rollback and tamper rejection passed");
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception(message);
    }
}
