# Grok Bot after an update

Grok Bot updates itself, often while the PC is idle. Its installer then opens Grok Bot again, but without the debugging port Wingman connects through. Grok Bot works normally; only Wingman's Helpers (such as the usage dial) are missing until Wingman connects again.

Wingman never closes or reopens Grok Bot on its own to fix this.

## The reconnect prompt

When Grok Bot comes back without Wingman's port, Wingman shows a small prompt window in the corner by the tray icons, on the screen with the main taskbar. It stays on top but never takes focus, so typing elsewhere carries on. Its title says:

- **Grok Bot updated** when an update is proven: the new Grok Bot was started with `--updated` (its installer adds this), or the Grok Bot.exe version changed since Wingman last had it connected.
- **Grok Bot reopened without Wingman** in every other case, for example when you closed Grok Bot and opened it again yourself.

It says "Helpers need Grok Bot reopened by Wingman." and has two buttons, **Reconnect** and **Not now**.

The prompt appears:

- only if Wingman had Grok Bot connected earlier in the same session;
- a few seconds after the unconnected Grok Bot starts, even before its window shows;
- only while someone is at the PC (keyboard or mouse input in the last minute);
- never over a full-screen app, game, Store app or presentation; it waits until that's over;
- once per Grok Bot run.

It closes when that Grok Bot exits or connects, or after **Not now**. To reconnect later, choose `Open window` in Wingman's Grok Bot menu.

## What Reconnect does

1. If that Grok Bot already has a port Wingman can use (for example from the Start menu option below), Wingman connects to it and restarts nothing.
2. Otherwise Wingman brings Grok Bot's window forward and the prompt says "Close Grok Bot's window. Wingman reopens it." On Windows Grok Bot has no tray icon, and closing its last window quits it. Grok Bot offers no outside way to quit it gracefully, so you close it yourself.
3. Once Grok Bot has quit, Wingman opens it again with the port. Grok Bot keeps its sign-in and data.

While Wingman waits, the prompt shows **Cancel**, which stops the wait and starts nothing. After 10 minutes without a quit, the prompt goes back to **Reconnect** and **Not now**.

If an update is waiting, quitting Grok Bot starts it, and its installer doesn't reopen Grok Bot. You already chose Reconnect, so Wingman finishes the job:

1. The prompt says "Grok Bot is updating…" and shows **Cancel**.
2. Wingman waits, for up to 5 minutes, until no Grok Bot installer runs and Grok Bot.exe has stayed unchanged for 5 seconds.
3. Then it opens Grok Bot with the port, with the same checks as any start. If an installer starts again in the meantime, Wingman waits for that too.

If Grok Bot opens again by itself during the update, or you open it yourself, Wingman starts nothing. If that Grok Bot has a port Wingman can use, Wingman connects to it. Otherwise the prompt moves to it. After an update the prompt goes straight on to "Close Grok Bot's window. Wingman reopens it." with no second **Reconnect**; after your own reopen it offers **Reconnect** again.

Once you have quit Grok Bot, the prompt closes only when Grok Bot is connected again or you press **Cancel** or **Not now**. In every other case Wingman starts nothing, and the prompt says why with an **Open Grok Bot** button:

- the update is still running after 5 minutes;
- a Grok Bot process is still running 20 seconds after the quit, or Wingman stays busy for 60 seconds;
- Grok Bot ended with an error (a crash, or ended from Task Manager) instead of your quit;
- Windows began to shut down or sign out after your quit, and another app cancelled it.

Before your quit, Wingman starts nothing and closes the prompt when a Grok Bot update or uninstall runs; Grok Bot is still open then, and the updated Grok Bot gets its own prompt. If Windows begins to shut down or sign out before your quit and another app cancels that, the prompt offers **Reconnect** again.

## Connecting to a Grok Bot that already has a port

Always on. When Grok Bot runs unconnected with a debugging port on `127.0.0.1`, Wingman records the port in `grok-runtime.json` and connects to it, with no prompt and no restart. It checks at startup and every 2 seconds while Grok Bot is unconnected.

- The port must be listening on `127.0.0.1` and belong to that Grok Bot process. Grok Bot counts as connected on the same terms.
- Any other address (`0.0.0.0`, `localhost`, `::1`) and the ports Wingman keeps for Codex and T3 Code are never used.

## Optional: keep Grok Bot's Start menu shortcut ready for Wingman

Off by default; only development builds can turn it on (`--grok-bot-start-link`), and Wingman never turns it on by itself.

Grok Bot's installer reopens Grok Bot through its Start menu shortcut (`Grok Bot.lnk`) after an update. With this option on, that shortcut also carries `--remote-debugging-address=127.0.0.1 --remote-debugging-port=<port>`, so Grok Bot comes back with the port and Wingman connects without a prompt.

- The port listens on this PC only, whenever Grok Bot is opened from that shortcut. Any program on this PC can then control the signed-in Grok Bot through it, as with Wingman's own launch.
- The shortcut still opens Grok Bot.exe directly, with or without Wingman. Only its arguments change; its name, icon and grouping don't. Anything the installer adds, such as `--updated`, still reaches Grok Bot.
- Wingman keeps a backup under `%LOCALAPPDATA%\CodexWingman\backups\start-menu` and records exactly what it added in `grok-bot-start-link.json`. Turning the option off removes exactly that and nothing else.
- Wingman refuses a shortcut that already has a debugging setting it didn't add, a bare `--`, or an unbalanced double quote; the option then stays off with a message saying why.
- If a Grok Bot reinstall writes a fresh shortcut, Wingman adds the flags again. It never creates a missing shortcut or changes one that points at another program.

Whether Grok Bot's installer keeps the shortcut over updates depends on its `KeepShortcuts` setting; the status command shows it.

## Commands

Run these from the Wingman folder. To make the terminal wait for the output, pipe it, for example `CodexWingman.exe --grok-bot-start-link-status | more`.

- `CodexWingman.exe --grok-bot-start-link-status` shows the shortcut, whether the option is on, what Wingman recorded adding, Wingman's Grok Bot port, the latest backup and `KeepShortcuts`. It changes nothing.
- `CodexWingman.exe --restore-grok-bot-start-link` removes exactly what Wingman added, leaves any other setting alone, turns the option off, and reports what it did. With nothing to undo, it writes nothing. It works in every build, so a release build can undo an option turned on in a development build.
- `CodexWingman.exe --grok-bot-start-link` (development builds only) turns the option on or off. It asks first.

## Implementation notes

- Grok Bot's main process is a Grok Bot.exe with no `--type=` switch and no script argument; with several, the one owning Chromium's single-instance window (`Chrome_MessageWindow`) wins.
- "Still running" (before Reconnect, `Open window` or `Repair` starts Grok Bot) means that main process or its `--type=` children. A Grok Bot.exe running only a script, such as a detached `local-exec-daemon\main.cjs`, doesn't count unless it owns the single-instance window. (0.78.1 runs the daemon as a `--type=utility` child, which ends with Grok Bot; it takes the single-instance lock only in its main process.)
- `Open window` and `Repair` use the same installer guard: they start nothing while a Grok Bot installer or uninstaller runs or Grok Bot.exe changed, checked before asking and again right before the start.
- The prompt is bound to one Grok Bot main process by id and start time. During the quit wait Wingman holds that process by its own process handle, so Windows can't give the id to another process, and reads its exit code there: only exit code 0 (the user's quit) is followed by a start, and an installer, uninstaller or Grok Bot.exe change is checked first, so an installer ending Grok Bot counts as an update. The once-a-second installer check lists processes on the thread pool.
- Installers are recognised by their program file: the staged `%TEMP%\sand-update-…\sand-update-<version>-….exe`, any other program in Grok Bot's install folder (such as its uninstaller), or Grok Bot.exe changing or disappearing. Every NSIS app's uninstaller runs from a `%TEMP%\~nsu….tmp` copy (`Un_A.exe` or `Au_.exe`); such a copy counts only when its `_?=` argument names Grok Bot's install folder or its parent runs from there, so another app's uninstall doesn't hold up Grok Bot.
- The update wait after the quit is `GrokBotRelaunch.AfterUpdateAsync`; it ends in the same guarded start as every other reopen. An installer seen just as Grok Bot exits counts as after the quit.
- The shortcut record is written as pending before the shortcut is saved and confirmed after, so the shortcut never carries Wingman's flags without a record. Wingman and the restore command take turns through a named lock.
- Outcomes are logged as `grok-bot-reconnect-outcome`.
