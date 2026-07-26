# v0.2 Implementation Decisions

| Field | Value |
| --- | --- |
| Release | v0.2 |
| Status | Implemented and automated-test validated |
| Related specifications | 029, 030, 031 |

## Architectural corrections

- A completed scan may have no conflict-resolution result. The results projector preserves the read-only file review, exposes no planned operations, and adds a safe limitation instead of rejecting all results.
- An empty duplicate-group list cannot distinguish a successful detector run with no exact duplicates from unavailable detector output. `ResultsSnapshot.IsDuplicateDataAvailable` makes that state explicit without adding persistence or detector behavior.
- The built-in .NET `TimeProvider` is injected into the stateless projector when deterministic timestamps are required. No broad Core clock abstraction or configuration was added.
- The exact-group route is an opaque in-memory filter field. It supports the required return-to-explorer flow but is never displayed, logged, persisted, or interpreted as a hash.

## Implementation boundaries

- `OpenSorSe.Application` owns immutable result projection and contains no Avalonia, filesystem, executor, undo, or persistence dependency.
- `OpenSorSe.Desktop` owns local query, bounded paging, selection, and duplicate-review state. The query worker is versioned and cancellation only supersedes stale local work.
- The existing Results destination is retained; no top-level navigation, package, settings, database, or configuration change was added.
- The UI never receives raw `ProcessingResult`, raw duplicate hash values, or a file-system command.

## Validation

The full Debug solution build and test suite pass. Automated coverage includes projector invariants, optional-output limitations, deterministic query semantics, paging, selection, duplicate group routing, stale-query-safe view-model behavior, and existing application-shell regression tests. Manual GUI testing remains necessary for keyboard, resize, visual layout, and real-world large-result responsiveness.
