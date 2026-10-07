"""Prove another .NET app can consume the packed release kit and updater."""

import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class ReleaseKitConsumerTests(unittest.TestCase):
    @unittest.skipUnless(shutil.which("dotnet"), "dotnet SDK is unavailable")
    def test_packed_kit_is_usable_by_second_app(self):
        with tempfile.TemporaryDirectory(prefix="releasekit-consumer-") as temporary:
            work = Path(temporary)
            feed = work / "feed"
            feed.mkdir()
            isolated_env = os.environ.copy()
            isolated_env["NUGET_PACKAGES"] = str(work / "nuget-cache")
            subprocess.run(
                ["dotnet", "pack", str(ROOT / "src/CodexApp.ReleaseKit/CodexApp.ReleaseKit.csproj"),
                 "-c", "Release", "-o", str(feed), "-p:NuGetAudit=false"],
                check=True, cwd=work, env=isolated_env, capture_output=True, text=True, timeout=180,
            )
            app = work / "OtherCodexApp"
            app.mkdir()
            (app / "OtherCodexApp.csproj").write_text(
                '<Project Sdk="Microsoft.NET.Sdk">'
                '<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework>'
                '<ImplicitUsings>enable</ImplicitUsings></PropertyGroup>'
                '<ItemGroup><PackageReference Include="CodexApp.ReleaseKit" '
                'Version="0.1.0-preview.3" /></ItemGroup></Project>', encoding="utf-8")
            (app / "Program.cs").write_text(
                'using CodexApp.ReleaseKit; '
                'Console.WriteLine(AppReleaseClient.Summarize("- Shared updater works"));',
                encoding="utf-8")
            (work / "NuGet.Config").write_text(
                '<?xml version="1.0" encoding="utf-8"?>'
                '<configuration><packageSources><clear />'
                f'<add key="local" value="{feed.as_posix()}" />'
                '</packageSources></configuration>', encoding="utf-8")
            output = subprocess.run(
                ["dotnet", "run", "--project", str(app / "OtherCodexApp.csproj"),
                 "-c", "Release", "-p:NuGetAudit=false"],
                check=True, cwd=work, env=isolated_env, capture_output=True, text=True, timeout=180,
            )
            self.assertIn("Shared updater works", output.stdout)
            self.assertTrue((app / "bin/Release/net9.0/Apply-CodexAppUpdate.ps1").is_file())


if __name__ == "__main__":
    unittest.main()
