# OpenSorSe release history

**Document type:** Living historical index

**Authority:** Concise version/branch/date/integration summary; detailed
release behavior and validation remain in the linked historical records

**Repository history reviewed:** 2026-07-30

This document indexes implemented version milestones without replacing their
implementation specifications, version notes, validation reports, or manual
checklists.

“Date” is the repository date of the relevant implementation/branch tip (or
tag for v1.0), not necessarily a public release date. “Tests” records the final
automated total stated in repository evidence where available; it is historical
evidence, not a current-suite comparison.

## Version overview

| Version | Branch | Date | Summary | Major additions | Tests | Merged status |
| --- | --- | --- | --- | --- | ---: | --- |
| v0.1 | `coding/v0.1` | 2026-07-17 | Read-only processing foundation. | Scan pipeline, metadata, hashing, classification, duplicates, rules/planning, initial desktop/orchestration. | Not recorded | Implementation lineage is in `main`; branch tip has one later unmerged README-only commit. |
| v0.2 | `coding/v0.2` | 2026-07-17 | Read-only result exploration. | Immutable snapshots, filters, sorting, paging, details, exact-duplicate review. | 233 | Merged to `main`. |
| v0.3 | `v0.3` | 2026-07-17 | Optional local suggestions and ranked exploration. | Ollama-compatible provider, validated proposals, decisions/tags, deterministic ranking. | 251 | Merged to `main`. |
| v0.4 | `v0.9` (batched line) | 2026-07-18 | Opt-in local catalog. | Bounded atomic snapshot persistence and historical reopening. | 260 | Merged to `main`; no dedicated remote v0.4 branch. |
| v0.5 | `v0.9` (batched line) | 2026-07-18 | Catalog Search and maintenance. | Cross-snapshot metadata/tag Search, removal, two-step clear. | 267 | Merged to `main`; no dedicated remote v0.5 branch. |
| v0.6 | `v0.9` (batched line) | 2026-07-18 | User-managed result tags. | Bounded tag editing, Search refresh, catalog-backed persistence. | 274 | Merged to `main`; no dedicated remote v0.6 branch. |
| v0.7 | `v0.9` (batched line) | 2026-07-18 | Saved catalog searches. | Separate bounded query presets, rerun/remove/reset. | 283 | Merged to `main`; no dedicated remote v0.7 branch. |
| v0.8 | `v0.9` (batched line) | 2026-07-18 | Snapshot identity and scope. | Catalog schema 2, names, source roots, legacy read compatibility. | 290 | Merged to `main`; no dedicated remote v0.8 branch. |
| v0.9 | `v0.9` | 2026-07-18 | Historical snapshot comparison. | Bounded metadata/tag comparison, scope warnings, filters and cancellation. | 330 | Merged to `main`. |
| v0.9.1 | `v0.9.1` | 2026-07-24 | Optional AI and feature controls. | Default-off gates, strict structured output, provider hardening, diagnostics, Help, Duplicate View. | 453 | Merged to `main`. |
| v1.0 | `v1.0` | 2026-07-26 | Integrated local understanding and structure history. | Extraction/OCR, local semantic retrieval, tags, Advanced Diagnostics, structure planning/history, Windows package. | 627 | Merged to `main`; tagged `v1.0.0`. |
| v1.1 | `v1.1` | 2026-07-26 | Safe File Operations and Robustness. | Change Plans, Review Changes, journal-before-mutation, verification, rollback, recovery, Undo. | 659 | Merged to `main`. |
| v1.2 | `v1.2-watched-folders` | 2026-07-26 | Watched Folders and Incremental Scanning. | Reconciled watcher hints, incremental catalogues, ignores, stability/retry, reviewed suggestions. | 724 | Merged to `main`. |
| v1.3 | `v1.3-workflow-profiles` | 2026-07-26 | Workflow Profiles and Recipe Library. | Typed profiles/recipes, safe templates, snapshots, assignments, import/export, provenance. | 761 | Merged to `main`. |
| v1.4 | `v1.4-plugin-foundation` | 2026-07-27 | Plugin Foundation and Extension SDK. | Standalone SDK, eight bounded extension points, local packages, grants, integrity/lifecycle isolation. | 836 | Merged to `main`. |
| v1.5 | `v1.5-cross-platform-foundation` | 2026-07-27 | Cross-Platform Foundation and Linux Preview. | Platform adapters, XDG paths, Linux semantics, plugin RIDs, source CI foundation. | 850 | Merged to `main`. |
| v1.6 | `v1.6-reliability-performance` | 2026-07-28 | Reliability, Performance and Production Hardening. | Shared atomic persistence, bounded resources, lifecycle/cancellation hardening, accessibility, native CI. | 895 | Merged to `main`. |
| v1.7 | `v1.7-deep-indexing-foundation` | 2026-07-29 | Deep Indexing Foundation. | Provider-neutral durable indexing, embedded SQLite, progressive Search, quotas, recovery and controls. | 987 | Not merged to `main`. |
| v1.8 | `v1.8-search-intelligence-privacy` | 2026-07-29 | Search Intelligence, Quality and Privacy. | Hybrid ranker, visible filters, explanations/snippets, coverage, index privacy/repair, relevance gates. | 1,086 | Not merged to `main`. |

## Evidence and detail

Use these sources instead of expanding this index into duplicate release
reports:

- [Changelog](docs/CHANGELOG.md) — detailed user-visible changes by version.
- [Implementation Specification Index](docs/Implementation_Spec/README.md) —
  version proposals, decisions, acceptance boundaries, and numbered
  specifications from v0.1 through v1.8.
- [Release Status](docs/RELEASE_STATUS.md) — current integration, validation,
  manual, packaging, tag, and publication state.
- [v1.0 Version Notes](docs/VERSION_NOTES_v1.0.md) and the frozen
  [v1.0 package release notes](release/OpenSorSe-v1.0.0/RELEASE_NOTES.md).
- [v1.6 Implementation Report](docs/V1.6_IMPLEMENTATION_REPORT.md) and
  [Validation Report](docs/V1.6_VALIDATION_REPORT.md).
- [v1.7 Implementation Report](docs/V1.7_IMPLEMENTATION_REPORT.md) and
  [Validation Report](docs/V1.7_VALIDATION_REPORT.md).
- [v1.8 Implementation Report](docs/V1.8_IMPLEMENTATION_REPORT.md) and
  [Validation Report](docs/V1.8_VALIDATION_REPORT.md).

Versioned User Guides, Troubleshooting guides, Manual Testing checklists, and
Version Notes under `docs/` remain release snapshots. The complete
`release/OpenSorSe-v1.0.0/` tree remains the immutable packaged v1.0 snapshot.

## Integration notes

- The v0.4-v0.9 specifications were added together by commit
  `c885393` on `v0.9`; separate remote branch names for v0.4-v0.8 do not exist.
- `coding/v0.1` ends in an unmerged README-only commit, while the foundational
  implementation commits are ancestors of `main`. The table reports both facts
  rather than flattening the branch to a misleading yes/no.
- `main` currently resolves to the v1.6 integration line.
- v1.7 and v1.8 contain implemented source and automated evidence but remain
  unmerged. Their manual checklists are not completed.
- The only repository release tag is `v1.0.0`. No v1.7 or v1.8 tag, package, or
  published release is present in the reviewed repository.
