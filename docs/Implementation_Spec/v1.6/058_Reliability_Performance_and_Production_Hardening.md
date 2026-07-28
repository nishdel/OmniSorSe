# Specification 058: Reliability, Performance, and Production Hardening

## Status

Implemented on `v1.6-reliability-performance`. This specification preserves the
complete v1.5 feature set and all persisted schemas. It does not create or
authorize a release tag, package, installer, merge, or published release.

## Objective

OpenSorSe v1.6 is a production-hardening release. It improves durability,
concurrency, cancellation, bounded memory, observer isolation, cross-platform
path behavior, diagnostics, accessibility, automated verification, and
maintenance quality without adding an autonomous or unattended file-management
feature.

## Compatibility contract

- Existing settings, catalogs, saved searches, AI decisions, content indexes,
  semantic indexes, structure history, workflow libraries, watched-folder
  configuration/catalogues/activity, plugin state, Change Plans, and Operation
  Journals remain readable without migration.
- JSON schema versions and serialized property names are unchanged.
- The local-first privacy boundary and the v1.1 reviewed Change Plan,
  validation, explicit confirmation, journal, rollback, recovery, and Undo
  boundary remain unchanged.
- `IResultsSnapshotProjector.Project(ProcessingSessionResult)` remains valid.
  The cancellation-aware overload is additive and has a default interface
  implementation.
- `ActionPlanner` retains its existing constructor call shape; optional path
  semantics injection is additive.
- No new source-file mutation path is introduced.

## Persistence reliability

All application-owned JSON stores use one shared bounded atomic-write
implementation:

1. require a fully qualified application-owned destination;
2. serialize to a unique sibling on the same filesystem;
3. enforce the store's existing encoded-size bound;
4. flush asynchronously and request a durable disk flush;
5. check cancellation before replacement;
6. atomically replace only the exact owned destination; and
7. remove only the temporary sibling on failure.

A process-local normalized-path coordinator serializes access across independent
store instances. This covers settings, AI decision history, Saved scans, saved
searches, content and semantic indexes, structure history, workflows, watched
folders, plugin state, Change Plans, and the Operation Journal. It prevents
load-modify-write transactions from losing records when recovery, diagnostics,
or tests construct more than one instance. It is deliberately not an
inter-process lock.

## Responsiveness and bounded resources

- Duplicate detection performs one preparation pass with hash counts and
  allocates member collections only for hashes that are actually duplicated.
- Results search tokenizes once per query, applies inexpensive filters before
  ranked text matching, avoids per-file signal sorting, and keeps the existing
  bounded page sizes and stable ordering.
- Results projection checks cancellation throughout file, directory,
  operation, duplicate, and issue projection.
- The Desktop performs large projection work off the UI thread with the active
  cancellation token.
- Processing-session history retains at most 256 recent non-running sessions;
  active sessions are never evicted.
- Background task progress is synchronized, clamped, and ignored after a
  terminal state.

## Failure isolation and lifecycle

- Background-task, event-bus, processing-session, watcher-source, watcher-state,
  and watcher-activity observers are invoked independently. One observer cannot
  suppress later observers or fail the owning workflow.
- Subscriber-local cancellation is isolated; cancellation requested by the
  operation owner still propagates.
- Watched-folder initialization and disposal are idempotent under concurrent
  callers. Owned consumer and availability loops are cancelled and awaited.
- Fire-and-forget watcher operations are observed, logged safely, and mark
  reconciliation when appropriate.
- Watched-folder hint de-duplication and rule planning use explicit host path
  semantics instead of unconditional case-insensitive comparison.

## Accessibility and UI polish

The existing layout and MVVM navigation remain intact. Critical scan,
watched-folder, Change Plan, Operation History, plugin, notification, and global
status controls expose automation names. Dynamic status and progress surfaces
use polite live-region announcements. Navigation items expose their bound
labels to automation clients.

## Diagnostics and version consistency

- Observer and background-operation failures use the existing bounded,
  redacted centralized logger.
- `ApplicationVersionInfo` is the single runtime version source for About,
  workflow provenance/exports, plugin initialization, and current capability
  messages.
- Assembly, file, informational, Desktop project, and Windows manifest versions
  are `1.6.0` / `1.6.0.0`.

## Automated acceptance

The suite includes unit, integration, persistence, recovery, stress,
concurrency, cancellation, lifecycle, cross-platform semantics, accessibility,
and repository-policy coverage. CI runs restore, Debug and Release builds and
tests, zero-skip validation, analyzers, style and whitespace formatting, and
documentation policy on Windows, Ubuntu, and macOS.

Exact executed evidence belongs in
[the v1.6 validation report](../../V1.6_VALIDATION_REPORT.md).
