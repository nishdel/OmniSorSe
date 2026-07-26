# OpenSorSe 1.2.0 Version Notes

Release name: **Watched Folders and Incremental Scanning**

Release branch: `v1.2-watched-folders`

OpenSorSe 1.2 adds persistent, opt-in watched folders while preserving the v1.1 review and execution boundary.

> Watched folders automate detection and analysis, not file modification.

## Highlights

- Persistent **Watched Folders** management with pause, resume, settings, immediate incremental scan, full reconciliation, open-folder, recent activity, review, and safe configuration removal.
- Operating-system watcher events are bounded, debounced hints. OpenSorSe verifies the real filesystem before changing its catalogue.
- Stable Windows file identities where available, with a portable best-effort fallback, detect external rename and move operations without relying only on paths.
- Incremental processing preserves unchanged metadata, hashes, content/OCR cache results, classifications, and duplicate state. Content-changing files alone are re-extracted, re-hashed, and reclassified.
- Full metadata reconciliation finds changes missed during shutdown, pause, disconnection, watcher overflow, or operating-system event loss without automatically reanalysing unchanged content.
- Built-in and configurable canonical ignore rules exclude temporary, incomplete-download, hidden, oversized, linked, internal, exact-path, extension-pattern, and filename-pattern items. Ignored items never enter AI analysis.
- Optional per-folder AI is off by default, additionally requires the global AI/capability gates and an available selected Ollama model, uses batches of at most 12 files and a 120-item per-cycle backlog bound, records per-file pending/completed/failed state, retries only pending/failed work, and fails independently from catalogue updates.
- Deterministic and optional AI suggestions create existing v1.1 Change Plans. They never call the execution service automatically.
- Operation Journal correlation recognizes verified OpenSorSe-generated rename/move/directory events, reconciles affected paths, and suppresses recursive suggestions.
- Versioned atomic `watched-folders.json`, `watched-catalogues.json`, and grouped `watched-activity.json` stores.
- A bounded 256-batch processing queue, per-folder quiet period, cancellation, stability retries, daily reconciliation, availability checks, and truthful busy/overflow/unresolved states.

## Compatibility

Product/informational version is `1.2.0`; assembly/file/manifest version is `1.2.0.0`. Existing v1.0 and v1.1 settings, saved catalogues, searches, decisions, content cache, semantic index, structure history, Change Plans, Operation Journal, rollback, recovery, and Undo data remain readable.

Overlapping watched roots are rejected. For example, `Documents` and `Documents/Invoices` cannot both be registered. This explicit policy prevents ambiguous ownership and duplicate analysis.

## Limitations

`FileSystemWatcher` cannot guarantee delivery or perfect ordering. OpenSorSe therefore reconciles on startup, resume, reconnect, overflow, at least daily while running, and on demand. A root folder renamed or moved externally is shown as unavailable; v1.2 does not search arbitrary drives to guess its new location.

Stable file identity is strongest on Windows local filesystems. The portable fallback uses creation time and length and can become ambiguous; collisions fall back to path-qualified identities and may be reported as remove/add rather than rename.

Files that keep changing or remain locked are deferred and make the batch incomplete. They are not immediately recorded as permanently failed. Network shares, removable media, permissions, external applications, and power loss can still require a later reconciliation.

The sorting recipe ID `current` resolves only to rules saved in the current application session's Rule Editor. v1.2 does not persist a named recipe library, so those current-session rules must be saved again after restart.

The scan-profile identifier is persisted for configuration compatibility, but v1.2 ships only the existing `default` scan behavior; it does not add a profile-library editor.

Watching operates only while OpenSorSe runs. Changes made while it is closed are detected by the next startup reconciliation, not by a background service.

No v1.2 packaged binary, signature, installer, or interactive platform validation is claimed until the manual checklist is completed.
