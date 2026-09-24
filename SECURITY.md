# Security and privacy

CodexPad operates locally. A running copy may contain personal task identifiers, diagnostic paths and custom shortcut settings. Never publish its entire working directory.

Use the allowlisted source export and release ZIP produced by `package.ps1`. Packaging excludes runtime settings, logs, screenshots of a user's live tasks and backups. If adding files to that allowlist, review them for personal data before publishing.

For a security issue, use the hosting repository's private reporting channel if one has been enabled. Until such a channel exists, contact the repository owner privately and do not post secrets or private chat data in public issues. No security support deadline or response SLA is promised for this preview.

No kernel driver or firmware update is included. Explicit device setup does overwrite the six key mappings of the supported pad. The distributed executable is unsigned; runtime components retain their upstream signatures where provided.
