# Obsidian links

Helper ID: `obsidian-links`

Manifest version: 1.3.1

## Purpose and behavior

This helper turns configured Obsidian Markdown paths in rendered user and assistant messages into configured Obsidian links. `wingman.json` supplies `config.mappings`; `apply.js` adds the link, Obsidian mark, and exact-path copy control; `remove.js` restores the original text and links. It declares the narrow `system.openObsidianUri` capability because Codex does not pass custom-scheme anchors to Windows; Wingman validates the exact Vault and Markdown note parameters before asking Windows to open it.

Standard `obsidian://open` requires no plugin. The optional Wait for Note mode requires its matching plugin in each destination vault.

Heading and block fragments are supported. Existing link labels and their markup are preserved unless the label is only the raw destination, in which case it is shortened to a readable note label.

Codex currently renders agent-authored Markdown links as `data-file-reference` prompt-link controls rather than ordinary anchors. The helper handles both forms, preserves the authored label, replaces the visible native file icon with the exact supplied purple Obsidian artwork, and routes the control through the same validated Host Action. Generated plain-path links use the same mark as their prefix icon and theme-aware link styling so they read like normal clickable links.

A whole-line folder path beneath a configured Vault root can also open its same-named folder note. For example, `.../Projects/CodexWingman/` opens `Projects/CodexWingman/CodexWingman.md`. This follows the Vault folder-note convention and avoids relying on a model to print the final `.md` filename. Codex's exact inline-code path spans are also safe to transform, so an inline path can be opened without requiring a special model formatting pattern.

To avoid consuming nearby prose, a heading or block fragment in plain text is linked only when the full path occupies its whole trimmed line and does not end in sentence punctuation. Put ambiguous inline fragment paths on their own line without trailing prose, or use a labeled Markdown link; existing anchor destinations remain exact and can contain multi-word or punctuated headings. Plain matching also requires a real `.md` file boundary, so backup, query-like, and longer-extension tokens remain unchanged while a final note beneath a `.md`-named folder is still recognized.

## Sharp edges and failure behavior

The helper fails closed and cleans up its prior controller when `config.mappings` is missing, malformed, or changed during reload. It ignores code, controls, the composer, Wingman-owned content, non-message content, ordinary paths, URLs, and unrelated links. Folder fallback is deliberately limited to a folder path that occupies the whole trimmed line, has no fragment or query, and ends in a path separator; inline prose and ambiguous folders remain unchanged. The sole exception is Codex's exact inline-code path span, where the entire span must be one valid configured path; it does not scan or partly rewrite code-like content. It assumes the Vault's same-named folder note exists and therefore does not attempt filesystem discovery from the renderer. Cleanup removes only `obsidian-links` markers and restores recorded source state. If the Host Action is unavailable, the link remains visible and copyable but clicking cannot launch Obsidian.

## Personal copy and updates

Configure this helper through `helperConfig` in the current user's Wingman settings, not by editing sealed package files. Public mappings are empty. See `docs/AGENT_SETUP.md` and `docs/configuration.md` in the source repository. For Obsidian use `uriAction: "open"` without a plugin; Wait for Note is an explicit optional integration. Native Codex file-reference controls use `system.openJarvisPath` for local Windows file/folder links; remote translation is optional.
