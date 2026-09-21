# Jarvis file links

Helper ID: `jarvis-file-links`

Manifest version: 1.3.5

## Purpose and behavior

This helper opens native Codex file-reference controls for absolute local Windows paths in their registered applications. Remote-to-Windows mappings are optional and empty by default. Printed paths remain ordinary text. The historical helper and capability IDs remain compatible.

## Sharp edges and failure behavior

The Helper depends on Codex's explicit `data-file-reference` and `data-prompt-link-href` contract. Codex also keeps the same destination in React props attached to that native control, so the Helper translates exact matching path values in those attached props as well as the DOM destination and matching `title`. Unknown controls and ordinary message text are ignored. Cleanup restores the claimed DOM attributes and any still-current React props it changed, removes the click interception, and returns the native Codex behavior.

## Personal copy and updates

Configure this helper through `helperConfig` in the current user's Wingman settings, not by editing sealed package files. Public mappings are empty. See `docs/AGENT_SETUP.md` and `docs/configuration.md` in the source repository. For Obsidian use `uriAction: "open"` without a plugin; Wait for Note is an explicit optional integration. Native Codex file-reference controls use `system.openJarvisPath` for local Windows file/folder links; remote translation is optional.
