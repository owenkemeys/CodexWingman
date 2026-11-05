# Source workflow

Use this repository as source authority. Keep changes on scoped branches, preserve unrelated work, verify the exact commit and commit local changes. Follow [Agent setup](AGENT_SETUP.md) for building and installation. Do not require the author's infrastructure or credentials. Local settings stay outside release files.

## Releases

Pushing a `vX.Y.Z` tag on the published main commit builds the sealed download and creates a **draft** release (`release.yml`). Nothing is offered to users yet: Check for updates never offers drafts. After the draft's download has been checked, push `publish-vX.Y.Z` on the same commit; `release-publish.yml` downloads the draft again, checks its SHA-256 and hygiene, and publishes it as the latest release.
