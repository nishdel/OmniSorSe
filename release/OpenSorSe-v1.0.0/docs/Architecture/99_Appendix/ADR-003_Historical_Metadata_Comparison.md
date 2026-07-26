# ADR-003 - Historical Metadata Comparison Without Live Verification

| Field | Value |
| --- | --- |
| Status | Accepted for v0.9 |
| Date | 2026-07-18 |
| Decision owners | OpenSorSe maintainers |

## Context

The bounded catalog can retain, name, search, and reopen display-safe snapshots. Users need to understand differences between two saved scans, but the stored model intentionally excludes file contents and raw hashes, and every current catalog surface is historical rather than a live filesystem view.

A comparison design could rescan stored roots, introduce monitoring, infer renames, export a report, or compare only the metadata already owned by OpenSorSe. The first alternatives expand safety, privacy, cross-platform, persistence, or correctness boundaries immediately before v1.0.

## Decision

OpenSorSe v0.9 compares two explicit already-loaded catalog entries through a pure Application service. Matching uses platform-neutral stored-path identity: drive-letter and UNC forms fold separators/case, while Unix paths preserve case. Results classify added, removed, modified, and unchanged stored records. Modified fields include bounded metadata and accepted non-deterministic tag sets.

The service performs no I/O, live verification, rename inference, logging, persistence, AI call, or execution. It accepts at most 2,000 records per side and produces at most 4,000 immutable changes. The Desktop renders at most 500 filtered rows, exposes cancellation, warns when v0.8 source scopes differ or are unknown, and may open either complete historical snapshot through the existing Results workflow.

## Consequences

- Results are deterministic, testable, cross-platform, bounded, and safe for selected user files.
- A comparison is evidence about two stored observations, not proof of current disk state.
- Path changes appear as removed plus added. Case-only Windows spelling and separator changes retain one identity.
- Duplicate stored path identities use the deterministic first record and produce an aggregate warning.
- Comparison adds no application-data file and has simple rollback to v0.8.
- Live monitoring, content/hash comparison, rename inference, reports/export, and database indexes require later decisions.

## Alternatives considered

| Alternative | Reason not selected |
| --- | --- |
| Rescan roots before comparison | Accesses live files, changes cancellation/failure semantics, and cannot preserve historical determinism. |
| Persist comparison reports | Adds stale application data, schema, retention, privacy, and cleanup requirements without current need. |
| Infer renames by size/time | Produces unsafe false equivalence without persisted content identity. |
| Introduce SQLite/indexing | Disproportionate to ten entries and explicitly deferred by the current roadmap. |
| Compare raw hashes/content | Those values are intentionally absent from display-safe catalog snapshots. |

## Related documents

- [v0.9 release proposal](../../Implementation_Spec/v0.9/00_v0.9_Release_Proposal.md)
- [Comparison service specification](../../Implementation_Spec/v0.9/044_HistoricalCatalogComparisonService.md)
- [Catalog comparison GUI](../08_Gui/12_Catalog_Comparison_Page.md)
