# v0.5 Implementation Decisions

## Search reuse

Catalog search reuses the v0.3 deterministic Results query engine rather than adding a second scoring algorithm, a search package, or a persisted index. It intentionally inherits bounded paging and match explanations, then aggregates already-safe page results across the v0.4 bounded catalog.

## Maintenance authorization

Deleting application-owned catalog data is permissible only through an explicit user action. Clearing all entries requires a second confirmation command because it is irreversible for historical snapshots. The feature never presents these actions as operations on scanned files.

## Scope boundary

The local catalog remains a compact JSON snapshot cache, not the future database subsystem. Catalog-wide search is deterministic metadata search, not semantic search. This preserves v0.3/v0.4 behavior and makes future schema/index choices independent.
