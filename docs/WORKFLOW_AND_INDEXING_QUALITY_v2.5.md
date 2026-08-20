# OmniSorSe v2.5 — Workflow Completion & Indexing Quality

**Status:** implementation branch; not released

**Branch:** `v2.5-workflow-indexing-quality`

## Release purpose

v2.5 completes two existing product journeys and one narrow companion bridge. A reviewed Change Plan now
converges Files and indexing projections on the journalled filesystem outcome,
including partial rollback and Undo. Durable indexing can publish broad,
inexpensive Search coverage before enabled media and Content Intelligence
finishes. The Related Files surface can launch a separately installed OmniBrille
through a scoped one-time local bootstrap. No new database, Search engine,
mutation authority, service, or Explorer Protocol version is introduced.

## Post-operation reconciliation

`ChangePlanReviewViewModel` emits one terminal operation result for Apply and
Undo, regardless of full success, failure, rollback, or partial rollback.
`ChangePlanReconciliationService` validates that the journal belongs to the
reviewed plan, resolves each action against journal status and current
filesystem existence, and produces one bounded reconciliation result:

- verified current path per action;
- old, intended, and actual affected paths;
- an updated immutable Results snapshot;
- an ambiguity flag and user-safe summary;
- affected roots for durable indexing refresh.

Successful rename/move outcomes preserve the Results file ID, remove obsolete
path projections, update metadata that can be read cheaply, clear completed
planned-operation markers, and rebuild duplicate membership/statistics.
Selection is restored by file ID when it still belongs to the active query.
Duplicate recovery moves continue to remove the unwanted copy from the active
snapshot rather than expose the application-owned recovery path as an ordinary
result.

For failure, rollback, rollback failure, and Undo, current filesystem truth
wins over intended plan state. Both-path and neither-path cases are marked
ambiguous. Mixed results trigger re-discovery only for registered sources that
intersect an affected old/new path. The durable store's stable identity matching
preserves completed derived data for an unchanged moved file where the platform
identity is available; correctness falls back to path/content reconciliation.

This is transaction-like convergence, not a claim that a filesystem is fully
transactional. External changes can make an outcome ambiguous, in which case
the UI warns and targeted source discovery becomes authoritative.

## Progressive initial indexing

The new persisted `InitialScanDepth` setting controls durable job scheduling:

- **Fast — searchable first** (`BaseFirst`, default): claims eligible work
  breadth-first by stage. File identity, path, filename, basic metadata, and
  inexpensive applicable evidence become broadly available before enabled OCR,
  transcription, frame analysis, summaries, or relationship work dominates the
  queue.
- **Deep initial analysis** (`DeepInitialAnalysis`): claims work by file before
  moving broadly across the source, allowing explicitly requested deeper
  analysis to finish earlier for individual files.

The setting does not enable capabilities. Basic/Standard/Deep indexing levels
and the existing OCR, media, transcription, frame, Content Intelligence, and AI
switches remain authoritative. Changing scheduling alone does not change the
processor fingerprint or repeat completed expensive work.

Both schedules retain the same SQLite jobs, stage states, retries, dependency
waits, quotas, pause/cancel controls, and startup recovery. Base evidence is
served from the existing progressive Search provider, and later evidence
updates the same durable file ID. Search therefore cannot produce a duplicate
result merely because deeper evidence arrived.

## Truthful progress

Indexing progress now distinguishes:

1. discovering files;
2. building base Search coverage;
3. files searchable while deeper analysis continues;
4. paused, waiting, cancelled, failed, or complete.

The phase percentage reports coverage for the phase being described. Once
discovery is committed and all known visible files have name/metadata coverage,
the UI says that files are searchable and reports deeper-analysis coverage
separately. ETA remains limited to work for which the existing estimator has a
meaningful sample.

## Organization UX

The Files action card describes the human-reviewed journey as Suggest, Review
Changes, then execute. Folder-structure proposals explicitly say that they use
only the current bounded Results page and are not a whole-library plan. No
prompt or candidate bound was enlarged.

## Optional OmniBrille handoff

**Open in OmniBrille** is an additive desktop integration over the released
Explorer Protocol v1 boundary. Discovery runs only when the user invokes the
action and checks, in order, an explicitly configured executable, an explicit
development/administrative environment override, the application directory,
small platform-specific installed locations, and `PATH`. It never recursively
searches a disk and OmniSorSe startup does not require OmniBrille.

After locating the companion, OmniSorSe authorizes only enabled indexed source
IDs with raw paths disabled, starts the existing current-user local named-pipe
host, and creates an independent 15-minute Protocol v1 session. The launch
bootstrap itself is separate from Protocol v1: a random 128-bit handoff-pipe
name is passed as `--omnisorse-handoff`, and the session grant is written once
through that current-user-only local pipe. No token is written to a file,
environment variable, log, or command line.

The grant JSON matches OmniBrille's established Stage 4 receiver, is strict,
and is limited to 4 KiB. The one-instance pipe closes after that single write,
making the handoff single-use. OmniSorSe reports connection only after the exact
session makes its first authenticated, Protocol-v1-compatible request; an
incompatible request does not satisfy acknowledgement. The authenticated
Explorer session then retains Protocol v1's existing bounded lifetime. Handoff
failure or timeout, missing compatible authentication, or early process exit
revokes the issued session. Companion process exit also revokes it. Repeated
user actions issue independent handoff endpoints, sessions, and secrets.

The residual threat is the established local same-user boundary: a malicious
process already running as the same user may inspect the random endpoint on the
child command line and race the intended client. Current-user pipe isolation,
an unpredictable endpoint, one connection, short wait, and non-durable token
delivery reduce exposure but are not an operating-system security boundary
against a fully compromised user account. Cross-user pipe access remains
denied by both the handoff and existing Protocol v1 transport.

See [the bootstrap contract](OMNIBRILLE_COMPANION_HANDOFF_v2.5.md).

## Privacy and safety

- Source changes still require explicit Change Plan review and confirmation.
- The executor still refuses overwrite and journals operations and recovery.
- Reconciliation does not infer that intended operations succeeded.
- Indexing and reconciliation use local application-owned state.
- No cloud provider, telemetry, autonomous organization, embedding/vector
  store, graph renderer, or file-operation protocol method is added.
- Explorer Protocol v1 DTOs, operations, authentication, limits, and wire
  version are unchanged; the companion bootstrap is a separate desktop concern.

## Persistence and compatibility

No schema migration is required. The setting is additive JSON configuration
with a base-first default when absent. Existing schema 5 data, jobs, processing
fingerprints, profiles, and v2.4 compatibility identities remain unchanged.

## Known limits

- A configured source refresh is source-bounded rather than a single-row
  database mutation so discovery remains the authority for path races and
  cross-root moves.
- Stable identity preservation depends on the platform/filesystem identity
  provider. Without it, discovery may need content/path reconciliation.
- Selection is retained only if the logical file remains visible under the
  current query and filters.
- The scan-depth setting schedules enabled work; it cannot make unavailable
  OCR/media/transcription providers available.
- Windows DPI, mouse, keyboard, and focus behavior for issues #29 and #31 needs
  genuine interactive validation; source inspection and automated tests are not
  represented as that evidence.

## Deliberate non-goals

Explorer Protocol expansion, OmniBrille rendering/Context work, server/cloud architecture,
autonomous mutation, embeddings/vector databases, graph visualization, voice,
media playback/editing, new cloud AI providers, and broad folder-tree work are
outside v2.5.
