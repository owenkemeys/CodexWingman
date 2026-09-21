# Agent installation and customization

An agent receiving this repository should finish installation on the user's own Windows machine and report any unresolved choices. Setup is entirely command-line driven, noninteractive, and returns structured JSON; there is no setup wizard or GUI dependency. No shared server, network drive, owner credentials or remote automation is required.

## Discover first

1. Confirm Windows, the official Codex desktop app, Git, Python 3.11+, Node 24+ and .NET 9 SDK. Building needs the SDK; running the framework-dependent build needs the .NET 9 Desktop Runtime. Check their installed commands before installing anything. Obtain the user's normal approval for missing software or a Codex restart. Do not copy authentication files between users.
2. Inspect an existing Wingman installation and `%LOCALAPPDATA%\CodexWingman\settings.json`; preserve them. Do not start a second Wingman instance. Keep Codex running during a Wingman update. For first-time Codex debugging activation, use Wingman's normal repair/restart action when the user is ready.
3. Run `python tools/configure_local.py` for a read-only JSON report. It reads only Wingman settings and Obsidian's local vault registry, not note contents. Check every `notices` entry and tell the user about unresolved customization. Never interpret a report as applied configuration.

## Apply local configuration

Run `python tools/configure_local.py --apply` once the detected mappings are appropriate. The tool preserves existing preferences and custom helper configuration, backs up an existing settings file, and writes atomically. It does not launch apps, alter vaults or install plugins. If Wingman is running, the agent can stop its identified process, wait for exit, and restart the same executable after writing settings; Codex stays open. The tray's Reload helpers is an optional alternative, not a required setup step. Avoid changing settings concurrently in the tray.

- Local Codex sessions work automatically. Set `additionalSessionRoots` only for a confirmed nonstandard or accessible remote session store.
- Obsidian vault directories are discovered from `%APPDATA%\obsidian\obsidian.json`. Only existing directories containing `.obsidian` are considered. Duplicate vault names are reported rather than guessed. Standard `obsidian://open` works without a community plugin.
- For an undiscovered vault, use `--vault "My Notes=C:\Notes\My Notes"` with `--apply`. Repeat for each desired vault. Explicit vault arguments replace the Obsidian mapping list, so include all desired mappings. Use the vault name registered in Obsidian. An existing disabled helper stays disabled; enable it deliberately after reviewing configuration.
- If there is no vault, Obsidian links stay disabled and the report explains why. The other helpers remain usable. Do not invent a vault or network share.
- Remote file translation is optional. The historical helper ID `jarvis-file-links` also handles local Windows file links; it does not require Jarvis. Its public default has no remote mappings. For remote translation configure `sourcePrefix` (an absolute slash-terminated remote directory) and `windowsPrefix` (an existing mapped drive such as `R:/`). This does not create or authenticate a network drive. Add remote vault prefixes to this helper's `excludePrefixes` so Obsidian owns those links.
- A synchronized-note setup may opt into `uriAction: "wait-for-note"` only if its matching plugin is already installed in every destination vault. The standard setup deliberately uses `"open"`.
- Usage windows and available resets come from the signed-in account, not a manually selected subscription tier. An absent five-hour window is normal. No credentials or reset balances need configuration.
- Debug Canaries and Hook Trace are optional diagnostics; setup leaves them off unless already explicitly enabled. GPU routing remains off unless already configured.

Personalization belongs in `helperConfig` in the local settings file. Do not edit a sealed package's Helpers to change vault paths; doing so invalidates its integrity record. See [configuration](configuration.md).

## Build, install and update

When upgrading an installation that customized its helper manifests, first run `python tools/configure_local.py --import-helper-config "C:\Path\To\Installed\Helpers"` to review a report. Stop only the identified Wingman process, then repeat with `--apply` before replacing the package and starting the new build. The import copies configuration into local settings, preserves existing local overrides, and never copies helper code or changes the old package. Keep the settings backup together with the old package for rollback. A missing folder or duplicate helper ID stops import. Do not share the resulting personal report publicly.

From a clean checkout of the repository's current `main`:

```powershell
python tools/release_provenance.py verify-source
./scripts/Publish-CodexWingman.ps1
python tools/release_provenance.py verify-package --package dist/CodexWingman-verified
./scripts/Install-CodexWingmanStartMenu.ps1 -PackageDirectory ./dist/CodexWingman-verified
```

The publisher checks the exact commit against GitHub main, builds a whole package and preserves its predecessor. A public clone requires no author's credentials. If using a fork, set its HTTPS URL in `release-repository.json`, commit it to that fork's main, then verify and build. CI reads the same configuration. Do not weaken clean-source or integrity checks to make an arbitrary checkout pass. No GitHub authentication is required to fetch a public repository; pushing to a fork uses that user's own access.

The resulting folder is the installation; first launch `CodexWingman.exe` there or use the per-user Start Menu shortcut. No service, administrator access or Jarvis installer is required. Keep that folder at a stable location. On updates, stop only the identified Wingman process and wait for it to exit before publishing over its folder, then restart Wingman. If publishing fails, its prior folder is restored; relaunch it. Preserve local settings and user-added helper files separately before replacing a whole package, and report any custom code that needs reintegration instead of silently dropping it.

For an existing installation outside the checkout, the agent may use `Install-VerifiedWingman.ps1 -Package <verified-folder> -Destination <existing-installation>`. This is an update tool, not a first-install command. It requires an identifiable existing Wingman installation and retains rollback.

## Acceptance and handback

Inspect the tray's status details and the actual Codex window. Verify the requested helpers, usage popup, and a real note link in each configured vault. A green build is not proof that an installed Codex version supports every helper. If Codex needs a restart, report that honestly and arrange it with the user; do not force-kill it.

Give the user the installed location, what works, any disabled optional integrations, and any customization still needed. If a missing choice or permission blocks an optional helper, finish the rest and identify that exact item. Do not claim a colleague's live installation has been tested on the author's machine.
