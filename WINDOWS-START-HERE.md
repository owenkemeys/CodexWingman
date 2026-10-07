# Launch Wingman on Windows

Extract the whole ZIP into a folder you intend to keep. Run `CodexWingman.exe` once.
Wingman adds **Wingman Codex** and **Wingman T3 Code** to your Start menu.
Right-click either entry to pin it to Start or the taskbar. Use the matching
Wingman shortcut whenever you want to open that app with Helpers connected.

The shortcut starts Wingman if needed. If Wingman is already running, it asks
that instance to open another window of the selected app. If the app was opened
without its helper connection, Wingman asks before restarting it.

Keep the executable and its `Helpers` folder together. If you move the extracted
folder, run the executable from its new location to refresh the Start shortcuts;
remove and recreate any old taskbar pin. The package requires Windows and the
.NET 9 Desktop Runtime. Install Codex and/or T3 Code separately.
