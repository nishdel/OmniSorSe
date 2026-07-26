# ADR-002: Bounded Saved Queries in Separate Atomic JSON

| Field | Value |
| --- | --- |
| Status | Accepted for v0.7 |
| Date | 2026-07-18 |
| Decision | Store names/query text separately from catalog snapshots and never persist hits |

## Context

v0.5 introduced deterministic catalog search and v0.6 made explicit user metadata tags searchable. Users need to repeat useful queries after restart, but the roadmap does not yet authorize a database or persistent search index. Query terms can be personal metadata, catalog JSON can become malformed independently, and stored hits would become stale whenever catalog entries or tags change.

## Decision

`OpenSorSe.Application.CatalogSearch` owns a small provider-neutral persistence contract and a JSON implementation. `saved-catalog-searches.json` is a sibling of settings/catalog data but is not embedded in either file. It contains at most 25 opaque-ID records with a bounded display name, bounded query text, and UTC timestamps. Writes are complete-envelope temporary-file replacements under a store lock.

Running a preset delegates to the existing catalog search against current entries and tags. Hits, file contents, hashes, and indexes are never persisted. Normal malformed data is preserved for diagnosis; the separate two-step reset is explicit authorization to delete even a malformed saved-query file. Catalog disablement prevents save/run but not review or deletion of existing private query text.

## Consequences

Saved searches are deterministic, inspectable, independently recoverable, and require no new dependency. Catalog corruption cannot block preset storage and preset corruption cannot block ad hoc catalog search. The cost is another small application-data file and no support for background execution, full filter presets, cross-device sync, or database queries.

## Alternatives considered

- Add presets to `catalog.json`: rejected because it couples failure/recovery and makes catalog clear semantics ambiguous.
- Persist hits with each query: rejected because results become stale and unnecessarily retain path metadata.
- Introduce SQLite/search indexes: rejected as premature for 25 small explicit presets and contrary to the current roadmap boundary.
- Keep presets process-local: rejected because it does not solve the restart workflow.
