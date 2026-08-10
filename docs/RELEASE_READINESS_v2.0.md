# OpenSorSe v2.0 release-readiness checklist

**Status:** Implementation-candidate/RC checklist. Every gate is intentionally
unchecked.

This checklist is completed only against an exact release-candidate commit.
Feature implementation, automated validation, manual validation, integration,
tagging, packaging, and publication are separate facts.

## Scope and architecture

- [ ] Stable, experimental, and deferred scope matches the accepted stability design and candidate source.
- [ ] No deferred person/biometric, graph canvas, conversation, autonomous,
      remote, or unrestricted-traversal capability entered the release.
- [ ] Accepted Tag nodes remain deferred unless a separately reviewed
      provider-neutral accepted-tag identity, ownership, persistence, and
      rename/move reconciliation contract is implemented and validated.
- [ ] Provider-neutral Application contracts contain no SQLite/UI details.
- [ ] Knowledge Graph work is outside `FileFullyIndexed` and cannot block
      existing indexing or Search.
- [ ] `deep-index.db` remains schema 3 and v1.9 compatibility is demonstrated.
- [ ] v1.9 relationship/Smart Collection decisions retain their sole existing
      authority; imported graph observations are non-authoritative and the new
      ledger owns only graph-native decisions.
- [ ] Existing relationship/context types are not presented as automatically
      resolved real-world entities, and merge/split rejects incompatible node
      kinds.
- [ ] The implementation does not treat the audited v1.9 metadata fingerprint,
      absent relationship tags, unused Workflow evidence, whole-field text/OCR
      fingerprints, or modification-only timeline as richer facts.

## Migration, backup, and compatibility

- [ ] Clean v1.7, v1.8, and v1.9 fixture upgrades pass.
- [ ] Every meaningful decision/graph migration interruption boundary passes.
- [ ] Every legacy manual-decision import boundary is idempotent and complete.
- [ ] A completed stable-primary-key manifest with terminal counts/hashes is
      the reconciliation authority; timestamp cursors and notifications are
      demonstrably hints only, and interrupted generations retire nothing.
- [ ] Observation ingestion and component-applied watermarks are independent
      and atomic at their own transaction boundaries, so poison work cannot
      block or hide later observations.
- [ ] Legacy decision updates/removals made before import, during normal v2 use,
      and during a rollback launch with v1.9 reconcile without dual authority
      or resurrection.
- [ ] Backup manifests/checksums/integrity and staged restore pass.
- [ ] The outer lifecycle lock is acquired before either sidecar lifecycle
      transaction, and no decision/graph transaction or writer gate is nested.
- [ ] Last known-good recovery/pre-migration copy cannot be pruned incorrectly.
- [ ] Unsupported newer and current-version malformed schemas are preserved and
      rejected actionably.
- [ ] Low-disk preflight and write-time full-disk recovery pass.
- [ ] Rollback launch with v1.9 is demonstrated without sidecar data loss.
- [ ] Catalog, scan, watcher, duplicate, workflow, plugin, Change Plan, journal,
      recovery, and Undo compatibility gates pass.

## Determinism, integrity, and user decisions

- [ ] Identical input produces canonical identical graph output repeatedly and
      on supported hosts using the documented canonical serialization/export,
      not SQLite database bytes.
- [ ] Ambiguous/fuzzy/semantic-only identities never auto-merge.
- [ ] Every automatic edge has current resolvable evidence and a truthful
      explanation.
- [ ] Manual merge, split, alias, edge, rejection, exclusion, and forget intent
      survives migration, restart, selective repair, and full rebuild.
- [ ] Corrupt individual graph records are quarantined/selectively repaired.
- [ ] Graph corruption never requires deleting the complete deep index.
- [ ] Decision corruption has a verified recovery path without guessing from
      derived rows.
- [ ] An unvalidated/corrupt decision store fails graph list/detail reads and
      Search expansion closed unless an exact validated decision snapshot is
      available; ordinary v1.9 Search remains usable.
- [ ] Rename, move, modification, deletion, source removal, and stale evidence
      invalidate only the intended components.

## Lifecycle and failure recovery

- [ ] Run control, job execution, component freshness, and component integrity
      are stored as orthogonal axes with explicit valid combinations and
      transitions. `PauseRequested` and `CancelRequested` intent is durable;
      the projected Pending, Running, Complete, Paused, Cancelled,
      RetryableFailure, PermanentFailure, WaitingForDependency,
      WaitingForResources, Stale, and RepairRequired states are durable and
      tested.
- [ ] Restart and cancellation pass at every durable stage.
- [ ] Repeated Pause/Resume and Cancel/Retry are idempotent.
- [ ] Expired leases recover without stale running jobs or duplicate claims.
- [ ] Coordinator fencing epochs and per-job claim tokens reject publication
      after renewal failure, reclaim, cancellation, or shutdown.
- [ ] Crash during cancellation preserves cancellation intent.
- [ ] Database busy/locked, low disk, dependency loss, malformed input, and
      repeated interruption reach the documented state.
- [ ] Concurrent Search/publication and maintenance/read tests pass.
- [ ] Safe shutdown meets the validated hard deadline.

## Bounds, performance, and responsiveness

- [ ] No automatic path performs unbounded all-pairs work.
- [ ] Candidate, bucket, alias, degree, evidence, traversal, Search expansion,
      queue, history, storage, and memory ceilings pass boundary/adversarial
      tests.
- [ ] Durable inbox/job row-and-byte limits, generation retention, transaction
      size/time, component cascades, and backup reserve fail with explicit
      backpressure rather than dropping work or decisions.
- [ ] Stable traversal remains one hop; any two-hop experiment is bounded and
      opt-in.
- [ ] Large synthetic graph, cancellation, restart, cleanup, and selective/full
      rebuild performance gates pass without unsupported scale claims.
- [ ] Search responsiveness and exact/literal ordering do not regress during
      graph build/repair/maintenance.
- [ ] Existing relationship and graph expansions share the documented seed and
      combined-result ceilings, deduplicate by File ID, never double-score one
      projected relationship, and expose independent visible controls.
- [ ] Indexing coverage and graph-projection coverage remain separately
      calculated, displayed, diagnosed, and represented in no-result states.
- [ ] No UI-thread blocking, accessibility-tree explosion, or shutdown hang is
      reproducible.
- [ ] Async refresh preserves or deterministically relocates selection/focus;
      keyboard, click/touch, high contrast, text scaling, non-color status, and
      accessible confirmation gates pass.

## Privacy, diagnostics, and source safety

- [ ] Forget/exclusion is restrictive at every cross-store crash boundary.
- [ ] Disable, clear-derived, clear-decisions, and forget have distinct
      disclosed effects; older backups cannot resurrect a completed privacy
      decision, and minimum tombstone retention is documented.
- [ ] Each acknowledged privacy action has a committed post-decision recovery
      point; restore rejects backups below the validated minimum-restorable
      privacy sequence and fails closed if that floor cannot be proven.
- [ ] UI and privacy documentation identifies graph stores, decision stores,
      backups, aliases, labels, evidence references, and quarantine copies as
      sensitive local metadata and makes no encryption-at-rest claim.
- [ ] Default logs/diagnostics omit document text, OCR, summaries, aliases,
      complete queries, prompts, vectors, secrets, and unnecessary paths.
- [ ] Diagnostics/export review and redaction tests pass.
- [ ] Original synthetic source files remain byte-for-byte unchanged after
      every graph/privacy/repair action.
- [ ] Ollama/OCR unavailability does not block stable graph or ordinary Search.
- [ ] Graph data inspection, retention, storage, backup, forget, and repair
      documentation matches actual behavior.

## Complete validation

- [ ] Forced restore passes.
- [ ] Non-incremental Debug build and complete Debug suite pass.
- [ ] Non-incremental Release build and complete Release suite pass.
- [ ] Independently parsed test totals show zero failed and zero skipped.
- [ ] Static analyzers, formatting, architecture, dependency, and documentation
      policies pass.
- [ ] Vulnerability audit has no unresolved critical/high finding.
- [ ] Supported cross-target builds and native SQLite asset checks pass.
- [ ] Search, relationship, graph, migration/recovery, security, and performance
      regression suites pass.
- [ ] `git diff --check` and full artifact/private-data diff audit pass.
- [ ] Hosted CI succeeds for the exact candidate commit on every supported host.

## Manual and release process

- [ ] Every item in [the v2.0 manual checklist](MANUAL_TESTING_v2.0.md) has
      maintainer evidence.
- [ ] A dedicated release-candidate stabilization phase completed after feature
      freeze.
- [ ] Only blocker fixes entered the candidate after freeze and were fully
      revalidated.
- [ ] The final repository is clean and contains no generated graph/database,
      test result, diagnostics, log, credential, or machine-specific artifact.
- [ ] Merge, tag, package, and publication decisions were each explicitly
      authorized and accurately documented.

Any reproducible data loss, lost decision, nondeterministic projection, stale
running job, unbounded graph work, Search regression, whole-index graph repair,
UI hang, diagnostics disclosure, source-file mutation, skipped test, or
critical/high vulnerability leaves this checklist blocked.
