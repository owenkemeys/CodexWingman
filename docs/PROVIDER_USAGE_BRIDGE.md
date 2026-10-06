# Optional Grok billing companion

T3's current Grok adapter returns an empty quota list when `config.creditUsagePercent` is absent, even with a valid unified weekly billing config. Grok's own UI maps that config to zero and retains its reset time. This was verified with the installed Grok CLI's real `/usage` screen and independent billing command, without sending a model prompt.

The reference implementation is [Grok's native billing interpretation](https://github.com/xai-org/grok-build/blob/main/crates/codegen/xai-grok-pager/src/app/effects/helpers.rs#L1582). The companion performs the same read-only billing request using credentials on the provider machine. It conservatively applies the absent-field zero only to a valid, current unified period. It clears its output on a failed read, and never writes credentials to the snapshot.

Run `tools/provider_usage_bridge.py` on the machine that owns the Grok subscription credentials, with `--environment-id`, `--instance-id`, and `--output`. Its minimum interval is 60 seconds; `--once` performs one read. Run it under the account that owns those credentials. Configure the Usage Dials helper's `providerUsageFile` to that snapshot's path as seen by the Wingman host. The companion and its service are installed separately from T3, so T3 updates do not replace them.

The host reads only that configured file, limits it to 64 KiB and a fixed JSON schema, and projects only quota fields. The renderer requires matching environment, instance and account, current provider authentication, and a reading no older than three minutes. It never treats an empty T3 list alone as zero and always prefers native nonempty quota. The snapshot contains the account email for identity matching and should remain private to the same user. Credentials remain solely on the provider machine.

Without an explicitly configured companion, Codex/Claude/native Grok behavior remains unchanged. A missing or stale companion does not establish zero usage.

The producer writes a private file atomically and clears it on failure. A managed installation should copy the producer to an immutable version directory and run it as the provider account; verify the service and two successful refresh timestamps before claiming automatic updates. Preserve any existing Wingman settings when adding the optional file configuration.
