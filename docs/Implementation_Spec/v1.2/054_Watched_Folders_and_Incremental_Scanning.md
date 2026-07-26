# 054 — Watched Folders and Incremental Scanning

## Release identity

- Version: `1.2.0`
- Release name: **Watched Folders and Incremental Scanning**
- Branch: `v1.2-watched-folders`

Release branches follow `v<version>-<primary-feature>`, for example `v1.1-safe-file-operations`, `v1.2-watched-folders`, and `v1.3-plugin-foundation`.

## Status

Implemented in source with automated Debug/Release validation; manual GUI/filesystem/platform checklist remains required before packaging.

## Objective

Persist selected watched roots, treat operating-system events as hints, debounce and verify real state, incrementally update the application-owned catalogue, reconcile missed/offline changes, and optionally create existing v1.1 Change Plans.

> Watched folders automate detection and analysis, not file modification.

## Architecture

- `WatchedFolderManager` owns validated configuration lifecycle and rejects duplicate/overlapping canonical roots.
- Versioned atomic JSON stores own configuration, watched catalogues, and grouped activity independently.
- `FileSystemWatcherEventSource` is a replaceable operating-system event collector.
- `WatchedFolderCoordinator` owns lifecycle, bounded queue, debounce, availability, startup/resume/reconnect/daily reconciliation, cancellation, backpressure, status, and activity.
- `PhysicalWatchedFileSystem` owns root-confined traversal/probes and stable file identity.
- `FileStabilityChecker` defers files that do not settle across observations.
- `WatchedFolderProcessor` calculates the smallest safe analysis set, preserves unchanged derived data, reuses scanner/rules/content/catalogue services, and updates the saved results snapshot.
- `WatchedSuggestionService` reuses v1.1 Change Plan adapters and existing AI gates, limits, retry, and validation.
- `OperationJournalWatchedExecutionCorrelation` matches journal actions to verified path/identity evidence and prevents recursive suggestions.
- `WatchedFoldersViewModel` presents state and commands only; watcher/scanning logic remains in application services.

## Policy decisions

Overlapping roots are rejected rather than deduplicated. Watcher hints outside the canonical root and reparse points are rejected. Missing roots remain configured. A root rename is not guessed. The bounded queue holds 256 batches. Default quiet period is two seconds. Reconciliation runs on startup/resume/reconnect/overflow, manually, and at least daily while running.

Optional watched-folder AI defaults off. It additionally respects global/capability/model readiness, uses at most 12 affected files per request and 120 per processing cycle, propagates cancellation, records per-file pending/completed/failed state and provenance, leaves larger backlogs pending, retries pending/failed work only, and cannot block deterministic catalogue updates.

The processor never injects into `IChangePlanExecutionService`. The only route to mutation remains the v1.1 Review Changes workflow and explicit Apply.

## v1.1 audit

v1.1 already provides versioned Change Plans, approval/edit/revalidation, rename/move/directory proposals, safe non-overwriting execution, durable Operation Journal, verification, rollback, interruption recovery, conflict-aware Undo, and AI robustness. v1.2 reuses these capabilities.

Documented v1.1 limitations remain: manual UI/platform validation was pending, portable identity is not a permanent cross-platform handle, and filesystem operations are transaction-like rather than an operating-system transaction. During final v1.2 validation, one v1.1 Review Changes presentation race was observed: a late asynchronous progress callback could replace the verified terminal status after execution returned. v1.2 integration fixes that defect by ignoring progress callbacks after a terminal execution result; the Change Plan/execution contract is unchanged.
