# v2.0 Knowledge Graph recovery and repair plan

**Status:** Proposed design; no v2.0 recovery behavior has been implemented or
validated yet.

## Recovery priorities

Recovery follows this order:

1. protect original files and existing v1.9 stores;
2. preserve authoritative legacy decisions and the graph-native decision
   ledger;
3. keep ordinary Search/indexing/relationships available;
4. retain the last valid graph projection while repairing a replacement;
5. repair the smallest affected component;
6. rebuild all derived graph data only as a reviewed last resort.

Graph recovery never grants source-file mutation authority.

## Startup reconciliation

On startup the graph coordinator:

1. validates decision and graph store lifecycle state;
2. resumes a persisted `CancelRequested` intent as cancellation;
3. atomically reclaims expired `Running` leases as
   `RetryableFailure(Interrupted)` without consuming a processor retry;
4. moves repeatedly interrupted claims to `RepairRequired`;
5. validates the active projection pointer and its manifest;
6. replays unapplied ordered graph-native decisions idempotently and reconciles
   legacy decision mirrors from the latest immutable completed manifest;
7. resumes import from a keyset cursor over an immutable completed manifest;
   an interrupted manifest capture is discarded/restarted and never advances
   source reconciliation or retires rows;
8. schedules bounded stale/orphan/invariant scans;
9. exposes graph health and coverage without blocking application startup.

Claims from another non-expired coordinator are not stolen. The provider must
enforce a single writer/coordinator lease even if two desktop processes open
the same application-data directory.

## Selective verification and repair

| Scope | Verify | Repair action |
| --- | --- | --- |
| Edge | Endpoints, type, evidence, versions, decisions, privacy | Reproject/delete that edge and evidence only. |
| Node/entity | Stable/candidate key, alias bounds, collision inputs, incident degree | Re-resolve bounded component; retain manual identity decisions. |
| File | Completed-manifest row/hash, deletion/exclusion, related nodes/edges | Reproject the file component from existing indexed data. |
| Collection/context | Current v1.9 membership/overrides/tombstone and member ceiling | Reproject collection component; keep rename/pin/manual intent authoritative. |
| Source/folder | Completed paged manifest, terminal count/hash, privacy, stale/orphan records | Reconcile only that source; never declare unseen files deleted after interrupted scan. |
| Job | State, lease, idempotency key, cursor, attempts | Reclaim, retry, cancel, or mark `RepairRequired` without altering graph output. |
| Active projection | Manifest, schema, counts, referential/application invariants | Activate a validated prior/candidate projection or build a replacement. |
| Decision ledger | Schema, sequence, unique IDs/import keys, bounds | Restore verified backup/staging copy; never rebuild from derived graph guesses. |

Repair records retain safe scope ID, category, prior/new state, algorithm
version, counts, duration, and outcome. They do not retain document content,
OCR, summaries, aliases, full evidence text, or absolute paths in default
diagnostics.

## Stale and orphan handling

- A changed source/config/decision/algorithm fingerprint marks only dependent
  components `Stale`.
- Stale prior output may remain visible only when clearly labelled and when
  privacy/deletion rules still permit it. It cannot support a current automatic
  fact explanation.
- Query-time authoritative deletion/exclusion checks hide private results before
  asynchronous cleanup completes.
- If current source or decision privacy authority is unavailable, graph results,
  traversal, explanations, and contextual expansion are suppressed. A last
  validated cache may make behavior more restrictive but may not authorize
  display. Independent v1.9 fallback remains available only when its own
  authority is healthy.
- Orphans are first quarantined/recorded, then deleted in bounded transactions
  after a completed manifest comparison proves they are not merely unseen
  because of interruption. Timestamps and notifications cannot prove absence.
- Operational history, old staging/projections, quarantine records, and backup
  manifests have separate bounded retention. Manual decisions and active
  privacy tombstones are not pruned as low-value history.

## Forget and exclusion barrier

Privacy changes cross independent stores and therefore use an ordered barrier:

1. commit the exclusion/forget decision or observe the authoritative v1.9
   privacy decision;
2. update query filtering so the identity/source is immediately hidden;
3. enqueue idempotent graph removal keyed by decision ID or completed manifest
   ID and canonical source-row hash;
4. transactionally remove derived nodes/edges/evidence and update coverage;
5. reconcile all affected collections/Search expansions;
6. record completion while retaining the minimum tombstone needed to prevent
   unintended recreation.

A crash at any boundary replays toward more restrictive privacy. It never
temporarily restores forgotten data. If the latest decision sequence or v1.9
privacy state cannot be read, the barrier remains closed and graph-derived
results are withheld rather than served from stale state.

## Corruption recovery

### Derived graph store

1. Stop graph writers and keep v1.9 functionality active.
2. Preserve the database/WAL/SHM and a redacted failure manifest.
3. Attempt schema/invariant-local selective repair on a staged copy.
4. If repair validates, publish the candidate and atomically replace the single
   active-store pointer through the committed pointer/manifest protocol while
   retaining the previous file.
5. Otherwise create a fresh derived store and rebuild from completed v1.9
   manifests plus the intact graph-native decision ledger.

Deleting/resetting `deep-index.db` is never a graph recovery step.

### Decision store

1. Stop decision mutation and graph publication. A last validated in-memory
   snapshot may enforce additional hiding, but cannot authorize graph-derived
   display or expansion while current privacy authority is unavailable.
2. Preserve corrupt files and sidecars.
3. Locate the newest verified compatible backup whose manifest reached
   `Committed`, using its checksum rather than modification time alone. For a
   decision store, its covered privacy sequence must be at or above the
   recovery catalog's validated minimum-restorable privacy sequence; an older
   backup cannot be activated to recover availability.
4. Restore into staging, validate integrity/schema/invariants/sequence, publish
   the candidate file, then atomically replace the single active-store pointer
   through the committed pointer/manifest protocol while keeping the corrupt
   current file as evidence.
5. If no verified, committed recovery exists, block graph mutation and request
   maintainer recovery. Do not infer decisions from derived graph rows.

Loss of a committed manual decision without a reviewed recovery path blocks the
release.

## Rebuild behavior

Selective rebuild is available for an edge, file, entity, collection, or
source. It:

- snapshots the completed source and legacy-decision manifest IDs, graph-native
  decision sequence, and configuration/algorithm revisions;
- consumes only an immutable completed source manifest with terminal count and
  canonical hash; wall-clock cursors are scheduling hints, not authority;
- leaves the current valid component readable;
- builds and validates replacement staging rows;
- publishes with a short compare-and-set transaction;
- aborts/retries if the snapshot became stale;
- preserves every decision/tombstone and existing v1.9 row;
- supports progress, cancellation, restart, and failure inspection.

A full graph rebuild is allowed only when integrity validation proves selective
repair cannot establish a trustworthy active projection. It must preflight
space, preserve/quarantine the old graph, use the completed legacy-decision
manifest and graph-native decision ledger as inputs,
remain cancellable between batches, and prove deterministic equivalence on
unchanged fixtures. It never clears the decision store or original index.

## Failure recovery categories

| Category | Default handling |
| --- | --- |
| Busy/locked | Bounded backoff and deadline; retryable/resource wait. |
| Full/low disk/quota | `WaitingForResources`; no destructive compaction assumption. |
| Optional OCR/AI unavailable | `WaitingForDependency` only for experimental work; stable graph continues. |
| Transient I/O | Bounded retry with idempotency key. |
| Malformed source/model record | Isolate component; permanent failure or repair required. |
| Integrity/foreign-key/schema-shape failure | Stop publication; staged repair/quarantine. |
| Unsupported newer version | Preserve unchanged; disable the affected graph surface and require a compatible application. Only independent healthy v1.9 fallback remains available. |
| Cancellation | Persist intent, acknowledge at safe boundary, never report success. |
| Repeated infrastructure interruption | Repair required; do not burn processor retry indefinitely. |

## Recovery acceptance gates

The implementation must demonstrate with deterministic fault injection:

- restart/cancellation at every stage and maintenance boundary;
- no stale running claims or duplicate active leases;
- manual decisions survive graph corruption and every rebuild scope;
- corrupt individual rows are repairable without whole-store deletion;
- graph-store corruption never disables healthy, independently supported
  existing Search/indexing;
- decision-store recovery uses a verified backup and preserves sequence;
- privacy barriers fail restrictive at every cross-store crash point;
- source or decision authority loss suppresses graph-derived results rather
  than authorizing them from a stale cache;
- interrupted/partial manifests never retire rows or advance reconciliation;
- low disk, locks, dependency loss, and shutdown have actionable states;
- original source fixtures remain byte-for-byte unchanged.
