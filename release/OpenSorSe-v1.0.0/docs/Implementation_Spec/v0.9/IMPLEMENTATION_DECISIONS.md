# v0.9 Implementation Decisions

## Scope selection

v0.8 made saved scans identifiable and recorded their source scope. Comparing two explicit historical snapshots is the next complete user workflow and uses every catalog capability already implemented. It provides meaningful pre-1.0 value without importing the unimplemented Reports, Database, content-reader, semantic-search, watcher, or execution architectures.

## Historical metadata, not live verification

Comparison never opens a stored path or checks whether it still exists. This preserves deterministic behavior, cross-platform catalog review, privacy, and the read-only guarantee. The UI labels every result historical. Live verification would require a separately authorized rescan design.

## Path-based matching without rename inference

Records match by normalized stored full path. Windows syntax folds case and separators even on a non-Windows host; Unix syntax preserves case. A path removed on one side and added on the other is reported exactly that way. Without persisted content hashes, inferring a rename would be misleading.

## Tags are part of application metadata change

Accepted non-deterministic normalized tag sets are compared after mapping through each snapshot's opaque file ID. This makes v0.6 user curation visible while avoiding unstable tag IDs, display casing, and deterministic extension-tag duplication.

## Duplicate identity policy

Overlapping historical inputs can theoretically contain repeated path identities. The service chooses the deterministic first record and reports an aggregate ignored-record count. Failing the whole comparison would discard otherwise useful bounded data; silently choosing would hide ambiguity.

## Bounds and no persistence

The service holds at most 4,000 immutable changes and the UI publishes at most 500 rows. Comparison results, filters, and selections are process-local. This avoids another schema, stale reports, silent disk growth, and rollback cleanup.

## Deferred v1.0 alternatives

Live monitoring, rename inference, content/hash comparison, export, charts, database indexes, and automatic actions remain deferred. Each changes a safety, data-format, or performance boundary and deserves a dedicated proposal.
