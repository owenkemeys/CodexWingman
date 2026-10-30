# Grok Bot after an update

Grok Bot updates itself, often while the PC is idle. Its installer then opens Grok Bot again, but without the debugging port Wingman connects through. Grok Bot works normally; only Wingman's Helpers (such as the usage dial) are missing until Wingman connects again.

Wingman never closes or reopens Grok Bot on its own to fix this.

## The reconnect prompt

When Grok Bot comes back without Wingman's port, Wingman shows a small prompt window in the corner by the tray icons, on the screen with the main taskbar. It stays on top but never takes focus, so typing elsewhere carries on. Its title says:

- **Grok Bot updated** when an update is proven: the new Grok Bot was started with `--updated` (its installer adds this), or the Grok Bot.exe version changed since Wingman last had it connected.
- **Grok Bot reopened without Wingman** in every other case, for example when you closed Grok Bot and opened it again yourself.

The title never contradicts the message. Before you press anything, the title is one of the two above. The first time the message speaks for Wingman (it is opening Grok Bot, opened it, is connecting its Helpers, or couldn't connect them), the title becomes the neutral **Grok Bot** (as after Open window's Yes), or stays **Grok Bot updated** when the prompt was for a proven update. It then stays that way whatever the message says next, for example **Grok Bot is updating...** when Grok Bot's installer starts as you close it, or a run that ends without Wingman opening Grok Bot. Until the prompt follows another Grok Bot (below), the title changes at most once and never goes back. It changes when the message does, or when the Grok Bot Wingman has just started becomes the one the prompt is for.

When the prompt follows another Grok Bot that opened without Wingman, its title changes with it, by the same proof as above: **Grok Bot updated** when that Grok Bot was started with `--updated` or Grok Bot.exe's version changed since Wingman last had it connected, otherwise **Grok Bot reopened without Wingman**. This holds whether the prompt follows it at the end of a run or at a later check. The title can change here while the message stays the same. When it follows the Grok Bot Wingman opened, it says what it said before for that Grok Bot, with the neutral title (or **Grok Bot updated**).

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

While Wingman waits, the prompt shows **Cancel**, which stops the wait, starts nothing and closes the prompt.

If an update is waiting, quitting Grok Bot starts it, and its installer doesn't reopen Grok Bot. You already chose Reconnect, so Wingman finishes the job:

1. The prompt says "Grok Bot is updating..." and shows **Cancel**.
2. Wingman waits, for up to 5 minutes, until no Grok Bot installer runs and Grok Bot.exe has stayed unchanged for 5 seconds.
3. Then it opens Grok Bot with the port, with the same checks as any start. If an installer starts again in the meantime, Wingman waits for that too.

Once Grok Bot is open with the port, the prompt says "Wingman is connecting Grok Bot's Helpers..." until they are connected, and closes then.

If Grok Bot opens again by itself during the update, or you open it yourself, Wingman starts nothing. If that Grok Bot has a port Wingman can use, Wingman connects to it. Otherwise the same prompt window now follows that Grok Bot. After an update it goes straight on to "Close Grok Bot's window. Wingman reopens it." with no second **Reconnect**. After your own reopen it says "Grok Bot was opened again without Wingman." with **Try again**, which asks you to close that Grok Bot's window, as **Reconnect** does. If that Grok Bot closes too, the prompt says "Grok Bot closed before Wingman could reopen it." and **Open Grok Bot** starts Grok Bot. The prompt follows every Grok Bot opened like this, for as long as it is open. It follows by one rule, at the end of a run and at every check after it: when a run ends for any reason while another Grok Bot already runs, the prompt follows that one at once, so its words and button don't change a moment later.

## Every result after you act

Once you have pressed **Reconnect** or **Open Grok Bot**, or said Yes to `Open window` or `Repair` closing Grok Bot, the prompt closes only when Wingman has connected the Helpers, or when you press **Cancel** or **Not now**. Every other result stays in the same prompt window, with a plain reason and **Not now**. The words and the button come from Wingman's last look at the Grok Bot the prompt is for, the same way for every result. While that Grok Bot is still open, the button is **Try again**, which asks you to close it again, and the words say why without claiming a reopen was due, for example "Wingman was busy, so Grok Bot is still open."; when Grok Bot didn't close in time, they say Wingman left it running. Once that Grok Bot has closed, also if it closed just as Wingman stopped, the button is **Open Grok Bot**, which starts Grok Bot, and the words say Wingman didn't reopen it, for example "Wingman was busy, so it didn't reopen Grok Bot."; after Grok Bot didn't close in time, they say "Grok Bot closed before Wingman could reopen it.". When Wingman couldn't look, the button is **Open Grok Bot**, whose run looks first. It is never a message box, never a raw error text, and never **Reconnect** again. Wingman starts nothing in these cases:

- Grok Bot wasn't closed within 10 minutes of **Reconnect**; Wingman left it running;
- Grok Bot didn't close within 20 seconds of your Yes to `Open window` or `Repair`; Wingman left it running;
- the update is still running after 5 minutes;
- an update started but Grok Bot hadn't finished closing 30 seconds later;
- the Grok Bot you pressed **Reconnect** for had already closed, or was replaced by another one; the prompt then follows the new one and says "Another Grok Bot replaced the one you chose, so Grok Bot is still open." while it is open;
- a Grok Bot process is still running 20 seconds after the quit or the update, or Wingman stays busy for 60 seconds;
- Grok Bot ended with an error (a crash, or ended from Task Manager) instead of your quit;
- you pressed **Try again** while that Grok Bot was open, and it then closed before Wingman saw how; the button becomes **Open Grok Bot**;
- Windows began to shut down or sign out, and another app cancelled it;
- Wingman stopped first, for example because you chose Reload Helpers;
- something went wrong inside Wingman. The prompt says so in plain words, and the error is in Wingman's log.

Wingman starts Grok Bot only after your own close of the Grok Bot it asked you to close, or for **Open Grok Bot** pressed once that Grok Bot is gone (Open window starts it directly only when no Grok Bot runs). It never closes Grok Bot unless you said Yes. "Wingman is opening Grok Bot..." shows only once that start is decided, after the update and exit checks.

If Grok Bot did open but the Helpers didn't connect, the prompt says "Grok Bot is open, but Wingman couldn't connect its Helpers." with **Try again**; when that is the exact Grok Bot process Wingman opened itself, it says "Wingman opened Grok Bot, but couldn't connect its Helpers." instead. When Wingman couldn't look at Grok Bot, it says "Wingman couldn't connect Grok Bot's Helpers." and guesses neither way. Once Wingman has started Grok Bot, the prompt holds that exact Grok Bot, so a later check never calls it opened again without Wingman, also when Wingman couldn't look. Wingman records that Grok Bot the moment Windows has started it. If the run then ends before the Helpers connect (an error writing its port down, say, Wingman's stop, or Windows shutting down), the prompt still holds that Grok Bot and says "Wingman opened Grok Bot, but something went wrong before it connected its Helpers." (or that it stopped, or that Windows began to shut down or sign out); no connect was tried, so once that Grok Bot listens with Wingman's port the next check connects it and the prompt closes. Another Grok Bot open by then is handed over by the one rule above. The same record means a Grok Bot Wingman opened itself, from Open window say, never gets a prompt saying it was reopened without Wingman. If Grok Bot closed while Wingman was connecting them, it says "Grok Bot closed before Wingman could connect its Helpers." instead. If Wingman stayed busy for 60 seconds and so didn't try, it says "Wingman was busy, so it didn't connect Grok Bot's Helpers.", with **Try again** while Grok Bot is open. **Try again** on an open Grok Bot whose Helpers didn't connect connects them once Wingman is free, with the same 60-second limit.

Grok Bot 0.78.1 starts its installer while it is still shutting down, for up to a few seconds. So when an installer appears while Wingman waits for your quit, Wingman keeps waiting, for up to 30 seconds, for that same Grok Bot to exit, and then waits for the update as above. When Wingman never saw your quit, **Open Grok Bot** checks again: it waits up to 30 seconds for that Grok Bot to exit, waits for an update it finds, and starts nothing while another Grok Bot runs. If that Grok Bot is still running, **Open Grok Bot** asks you to close its window, as **Reconnect** does.

## Connecting to a Grok Bot that already has a port

There is no setting for this. When Grok Bot runs unconnected with a debugging port on `127.0.0.1`, Wingman records the port in `grok-runtime.json` and connects to it, with no prompt and no restart. It always checks at startup and when you choose Grok Bot in Wingman's menu. It also checks every 2 seconds while Grok Bot is unconnected, but only once Grok Bot is set up in Wingman.

Set up means a Grok Bot port is recorded, because Wingman opened Grok Bot or connected to its port before, or the Start menu option below is on. That holds for both 2-second checks: this one and the one that shows the reconnect prompt. Being installed isn't enough. A Grok Bot that Wingman has never had stays unwatched until Wingman starts or you choose Grok Bot in its menu.

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
- "Still running" (before Reconnect, `Open window` or `Repair` starts Grok Bot) means any Grok Bot.exe in your session. In 0.78.1 the only ones besides Grok Bot itself are its `--type=` children, including its local-exec daemon (a `--type=utility` child), and they end with it.
- `Open window` and `Repair` use the same installer guard when no Grok Bot runs: they start nothing while a Grok Bot installer or uninstaller runs or Grok Bot.exe changed, and say why in a message box. With Grok Bot running they ask Yes/No as before. After your Yes they ask Grok Bot to close and show the prompt (title **Grok Bot**), whose run (`GrokBotPromptRun`, step `ApprovedClose`) waits up to 20 seconds for Grok Bot to close (`GrokBotRelaunch.AfterCloseRequestAsync`). If that close starts Grok Bot's update, also while Grok Bot is still closing, the prompt says "Grok Bot is updating..." while Wingman waits for it as after **Reconnect** (`GrokBotRelaunch.AfterApprovedCloseAsync`, up to 5 minutes), then opens Grok Bot and connects the Helpers. Every result is shown as in [Every result after you act](#every-result-after-you-act).
- The prompt is bound to one Grok Bot main process by id and start time. During the quit wait Wingman holds that process by its own process handle, so Windows can't give the id to another process, and reads its exit code there: only exit code 0 (the user's quit) is followed by a start, and an installer, uninstaller or Grok Bot.exe change is checked first, so an installer ending Grok Bot counts as an update. The once-a-second installer check lists processes on the thread pool.
- Installers are recognised by their program file, and Wingman looks only at programs named `sand-update-…`, `Uninstall…`, `Un_A` or `Au_`; no other program counts, also not one in Grok Bot's install folder. A `sand-update-…` program (the staged `%TEMP%\sand-update-…\sand-update-<version>-….exe`) counts wherever it runs; an `Uninstall…`, `Un_A` or `Au_` program counts when it runs from Grok Bot's install folder (such as `Uninstall Grok Bot.exe`). Grok Bot.exe changing or disappearing counts too. Every NSIS app's uninstaller runs from a `%TEMP%\~nsu….tmp` copy (`Un_A.exe` or `Au_.exe`); such a copy counts only when its `_?=` argument names Grok Bot's install folder or its parent runs from there, so another app's uninstall doesn't hold up Grok Bot.
- The update wait after the quit is `GrokBotRelaunch.AfterUpdateAsync`; it ends in the same guarded start as every other reopen. An installer seen while Grok Bot is still closing counts as after the quit: the run waits up to 30 seconds (`GrokBotRelaunchLimits.Exit`) for that held process to exit, then takes the update path with the proof. **Open Grok Bot** without that proof is `GrokBotRelaunch.OpenAfterPressAsync`.
- The shortcut record is written as pending before the shortcut is saved and confirmed after, so the shortcut never carries Wingman's flags without a record. Wingman and the restore command take turns through a named lock.
- Outcomes are logged as `grok-bot-reconnect-outcome`.
