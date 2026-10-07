# Clickable file links

Helper ID: `clickable-file-links`

Manifest version: 1.5.0

## Purpose and behavior

This helper opens native Codex file-reference controls for absolute local Windows paths in their registered applications. Remote-to-Windows mappings are optional and empty by default. Printed paths remain ordinary text. The helper ID is `clickable-file-links` and its host capability is `system.openFilePath`.

In T3 Code, its separate renderer adds a small **Open in Windows** button beside recognized file chips in rendered messages. T3's own chip click and context menu remain available. The button accepts absolute Windows paths or configured remote-to-Windows mappings and asks Wingman to open the validated local file.

## Sharp edges and failure behavior

The Helper depends on Codex's explicit `data-file-reference` and `data-prompt-link-href` contract. Codex also keeps the same destination in React props attached to that native control, so the Helper translates exact matching path values in those attached props as well as the DOM destination and matching `title`. Unknown controls and ordinary message text are ignored. Cleanup restores the claimed DOM attributes and any still-current React props it changed, removes the click interception, and returns the native Codex behavior.

In T3 Code, the separate renderer recognizes only `chat-markdown-file-link` chips inside timeline messages. It never changes a chip's destination or native click. A button appears only for a valid absolute Windows path or a configured mapping to Windows; exclusions and path traversal checks still apply. Cleanup removes that button and its style. Plain text and the composer are ignored.

## Personal copy and updates

Configure this helper through `helperConfig` in the current user's Wingman settings, not by editing sealed package files. Public mappings are empty. See `docs/AGENT_SETUP.md` and `docs/configuration.md` in the source repository. For Obsidian use `uriAction: "open"` without a plugin; Wait for Note is an explicit optional integration. Native Codex file-reference controls use `system.openFilePath` for local Windows file/folder links; remote translation is optional.
