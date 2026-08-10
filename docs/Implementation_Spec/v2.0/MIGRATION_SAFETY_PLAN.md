# v2.0 Knowledge Graph migration safety plan

**Status:** Sidecar bootstrap/migration design implemented by the source
candidate; final migration, recovery, rollback, and RC evidence pending.

The implemented migration is an additive schema-1 bootstrap for each sidecar,
not a `deep-index.db` migration. Each provider validates its own application
ID, `user_version`, metadata marker, migration-history checksum, required
tables, and integrity settings transactionally. Unsupported newer or corrupt
stores fail with an actionable category. Final fault-matrix evidence remains a
release gate and is not claimed by this status note.

## Chosen migration boundary

The first Knowledge Graph release does not migrate `deep-index.db` schema 3 in
place. It creates two sidecar stores:

- `knowledge-decisions.db` schema 1 for authoritative graph-native choices and
  graph-only settings, with separately identified non-authoritative legacy
  decision mirrors;
- `knowledge-graph.db` schema 1 for rebuildable projection, work, integrity, and
  diagnostics.

The existing v1.9 provider remains the only owner of schema 3. v2 reads it
through a new additive, provider-neutral `IGraphProjectionSource` contract that
can export complete, revision-consistent manifests. Existing relationship
operations remain the normal mutation boundary, but they do not expose every
pair override, member override, forgotten-collection tombstone, or other row
needed for a complete bootstrap and therefore are not an import API. This keeps
v1.9 rollback possible and makes graph bootstrap an interruptible background
process rather than a startup database rewrite.

Graph-only enablement and resource settings are stored in dedicated lifecycle
tables in `knowledge-decisions.db`, not added to the v1.9 JSON settings
document. A v1.9 process may
rewrite that typed JSON document and discard fields it does not understand;
sidecar ownership prevents such a save from erasing v2 graph settings. Shared
settings may gain v2 fields only after unknown-field round-trip preservation is
implemented and tested.

## First-enable sequence

The graph is not built merely because a user launches the upgraded app. The UI
first explains storage/privacy implications and obtains an explicit enable
decision. Existing Search and Relationships continue before, during, and after
bootstrap while their schema-3 source is healthy and supported. If that source
is corrupt or has an unsupported newer schema, only independent compatible
fallbacks remain available; the graph must not disguise that condition.

1. Acquire the provider-neutral outer application-data lifecycle lock before
   either sidecar is opened for lifecycle mutation. A second instance cannot
   create, migrate, restore, or promote either sidecar while this lock is held.
2. Validate application settings and the v1.9 source provider. Reject
   unsupported/corrupt source state through its existing boundary without
   changing the source database.
3. Preflight free disk for decision backup/staging, graph schema/staging, WAL,
   expected bounded bootstrap growth, and a safety reserve.
4. Open or create `knowledge-decisions.db`. Validate header, schema version,
   schema shape, `quick_check`, `foreign_key_check`, sequence continuity, and
   application invariants.
5. Before any existing decision-store migration, create and publish a verified
   reason-specific backup. Apply DDL/history/version in one transaction and
   validate again after commit.
6. Acquire a bounded legacy-decision capture lease from the existing provider.
   It serializes v1.9 decision mutation/retention cleanup long enough to export
   one complete, revision-consistent, count/byte-bounded manifest. The export is
   written to staging in deterministic primary-key pages and becomes usable
   only after its terminal record validates expected row count, canonical
   aggregate hash, source schema, and scope. An interrupted or oversized export
   is discarded/restarted and cannot retire existing rows.
7. Import the immutable completed manifest in deterministic keyset pages. Use
   stable legacy import keys, the manifest ID, and canonical row hashes. Do not
   delete or rewrite legacy rows. Existing v1.9 operations remain authoritative
   for their own decision types; `LegacyRelationshipDecisionMirror` is a
   replayable mirror, not a second decision authority.
8. Open/create `knowledge-graph.db`, validate or transactionally migrate only
   its structural schema, and record the applicable completed legacy-manifest
   ID and graph-native decision sequence.
9. Commit the enabled lifecycle/configuration state, close or quiesce lifecycle
   connections, and release the outer lock. Normal work then acquires a new
   monotonically fenced coordinator lease; neither store transaction is nested
   inside the other.
10. Export a complete observation manifest through `IGraphProjectionSource`.
   A provider-held consistent read snapshot is paged by stable primary key into
   durable staging and finalized with row counts and canonical aggregate hashes.
   Do not extract entities or backfill edges inside a schema transaction.
11. Enumerate the immutable completed manifest in bounded deterministic pages.
    Build graph components as durable idempotent work. Import progress may use a
    keyset cursor over this immutable manifest and can resume after restart.
12. Capture and compare a subsequent completed manifest to reconcile writes
    that occurred during bootstrap. Only a completed manifest may retire an
    unseen source or decision row. Wall-clock timestamps, `(updated time,
    stable ID)` cursors, and notifications may prioritize work, but are hints
    only: schema 3 has no monotonic cross-table change sequence and they cannot
    establish absence or crash-recovery authority.
13. Validate the candidate projection and atomically mark it active. Keep the
    prior valid projection until retention permits cleanup.

Notifications may accelerate projection scheduling in steps 10–12, but they
are hints because there is no
cross-database transaction. Startup and periodic completed-manifest comparison
are the crash-recovery authority. A partially captured manifest never advances
reconciliation state.

## Decision namespaces and cross-store writes

`LegacyRelationshipDecisionMirror` contains only mirrored v1.9 relationships,
pair decisions, collection overrides, rename/pin state, and tombstones. Its
authoritative record remains in schema 3. Each mirror row records the canonical
legacy key, completed legacy-decision-manifest ID, canonical row hash, and
presence or retirement state. A row missing from one interrupted/partial scan
is unchanged; only a later completed manifest may record a
`RetiredByLegacySource` mirror tombstone. A retained v1.9 privacy or
forgotten-collection tombstone remains an active source decision rather than
operational history.

`GraphNativeDecision` contains only new v2 entity, alias, merge, split, manual
edge, and graph-specific privacy/control choices. It is authoritative in
`knowledge-decisions.db` and is never back-written into guessed schema-3 rows.

When a v2 command changes a decision type already owned by v1.9, it uses an
ordered saga rather than pretending there is a cross-database transaction:

1. validate the command and current privacy state;
2. commit through the existing provider operation first;
3. capture/reconcile the resulting legacy row in a completed decision manifest;
4. import the mirror idempotently and enqueue graph projection;
5. report graph update pending or failed without rolling back the committed
   legacy decision.

The saga persists its own idempotency ID and verifies the legacy postcondition
by canonical target key. Any legacy mutation that cannot already be retried
idempotently requires an additive adapter before v2 uses it. A mirror failure
cannot transfer authority to the sidecar. If a graph-native decision conflicts
with a current legacy decision, privacy and explicit rejection remain most
restrictive; other contradictions become `RepairRequired` and block affected
publication. Wall-clock last-write-wins is forbidden.

## Transactional schema migration protocol

Each sidecar store has its own provider-neutral lifecycle and migration history.
A migration record includes version from/to, migration ID, implementation
checksum, started/completed time, application version, and validation result.

Protocol:

1. reject negative or unsupported newer versions;
2. validate current store and required source bounds;
3. stage a backup to a unique temporary file;
4. validate backup integrity, schema shape, invariants, size, and checksum;
5. publish the backup through the committed backup-manifest state machine below;
6. begin an immediate SQLite transaction;
7. apply one reviewed migration with idempotent object names;
8. write migration history and schema version last in the same transaction;
9. commit;
10. reopen on a fresh connection and rerun integrity/shape/invariant checks;
11. mark the backup as superseded only after the new store is proven usable.

No data backfill, OCR, Ollama request, graph traversal, compaction, or long
projection work occurs in this transaction.

SQLite graph schema changes that must keep a prior projection readable use a
shadow store and a validated active-store pointer. An in-place migration may
instead block graph reads for its bounded duration; it must not claim that the
old schema remains concurrently readable. Decision-store migration blocks
graph publication, and privacy-sensitive graph reads fail closed until the
current decision authority is available.

## Committed backup-manifest state machine

A database file and a separate manifest cannot be published atomically as one
filesystem operation. Backup recovery therefore uses an ordered state machine:

1. create database and manifest under unique `Staging` names on the target
   volume;
2. close/quiesce the backup connection, account for SQLite WAL/SHM state, flush
   where supported, and validate the staged database;
3. write a `Verified` staged manifest containing immutable backup ID, reason,
   source/store/schema versions, size, checksum, creation time, validation,
   and, for decisions, the covered decision and privacy sequences;
4. atomically rename the database to its final same-volume name;
5. atomically publish a final `Committed` manifest referencing that exact final
   name and checksum;
6. consider the backup recovery-eligible only after the committed manifest is
   durable; quarantine/clean orphan final files or staged manifests after a
   crash;
7. transition the manifest to `Superseded` only after replacement validation
   and retention rules permit it.

Recovery never selects by modification time alone and never treats `Staging`
or `Verified` as committed. Promotion of a restored or shadow graph store uses
the same committed-pointer principle: publish and validate the candidate, then
atomically replace one active-store pointer; reconcile orphan candidates on
startup.

## Fault-injection boundaries

Automated migration tests must terminate execution deterministically:

- before/after free-space preflight;
- during temporary backup creation;
- after backup bytes but before validation;
- after staged-manifest validation but before final database promotion;
- after final database promotion but before committed-manifest publication;
- after committed-manifest publication but before migration transaction;
- after each meaningful DDL/history group;
- immediately before/after schema-version write;
- immediately before commit and immediately after commit;
- before/after post-commit validation;
- after every legacy-decision import page;
- during every complete-manifest page and before/after its terminal record;
- during every projection stage and before active publication.

After every interruption, one of these must be true:

- the prior valid store remains active;
- the complete migrated store is active;
- the feature is safely disabled with an intact reviewed recovery copy.

A mixture that cannot identify its version/state is `RepairRequired`, never
silently recreated.

## Backup policy

Decision-store backups protect irreplaceable user intent. They are:

- created through temporary paths and made eligible only through the committed
  backup-manifest state machine after validation;
- accompanied by a manifest/checksum, reason, schema/app version, byte size,
  and validation result;
- split into pinned pre-migration/recovery and rolling backup classes;
- bounded by both count and total bytes;
- pruned only after a newer verified, committed copy exists;
- restored through a validated staging file while preserving the current file
  as recovery evidence.

A privacy forget/exclusion is not acknowledged complete until a verified,
committed post-decision backup advances the recovery catalog's monotonic
minimum-restorable privacy sequence. A backup below that floor is ineligible
for activation even if it is otherwise valid; it may be retained only as
quarantined recovery evidence. If the floor or its committed manifest cannot
be validated, graph reads fail closed rather than selecting an older copy.

The derived graph store may have a bounded acceleration/diagnostic backup, but
its primary recovery is rebuild from v1.9 source plus the decision ledger.
Graph backup pruning can never prune decision backups.

When v2 first opens an exact schema-1 or schema-2 legacy index, it must create a
separate reason-pinned, verified pre-upgrade backup before allowing the existing
provider to migrate it to schema 3. This legacy backup is not part of rolling
graph/decision retention and remains pinned until the documented downgrade
window closes or the user explicitly reviews its removal. A sidecar backup is
not a substitute for this legacy deep-index backup.

## Legacy schema-1/schema-2 migration hardening

Keeping the graph outside schema 3 does not exempt the existing legacy upgrade
path from v2 release gates. Tests use clean frozen synthetic schema-1 and
schema-2 fixtures built from their version-owned DDL, not a current database
with newer objects dropped. Expected logical manifests cover every retained
file, source, stage, content, privacy, and decision row applicable to that
version.

Deterministic fault injection covers backup creation/validation/publication,
every meaningful DDL/history group, version write, commit, reopen validation,
disk-full, busy/locked, cancellation, and repeated restart. Validation includes
`quick_check`, `foreign_key_check`, exact schema shape, application invariants,
and logical before/after manifests. The design reuses the existing migration
logic only after these tests pass; current partial-fixture success is not itself
a v2 safety claim.

## Unsupported, malformed, and corrupt state

- Unsupported newer v1.9, graph, or decision schema is rejected unchanged.
- An unsupported newer deep-index schema disables graph projection and its
  progressive indexed surfaces; only independent compatible fallback remains.
- A current version missing required table/column/index/history is corrupt even
  if `quick_check` says `ok`.
- `foreign_key_check`, enum/boolean/timestamp domains, sequence continuity,
  uniqueness, evidence requirements, bounds, and active-projection pointers are
  validated explicitly.
- Corrupt decision data blocks mutations and triggers reviewed backup recovery.
- Corrupt graph data disables graph only and triggers quarantine/selective
  repair or rebuild.
- No initialization catch maps every SQLite error to corruption: busy, locked,
  full disk, permission, unsupported schema, cancellation, and invariant
  failures have distinct outcomes.

## Downgrade and rollback

v1.9 can reopen its unchanged schema-3 index and ignores the two v2 sidecars.
The sidecars remain in place so returning to v2 retains decisions and graph
settings. v2 does not place graph-only settings in the shared typed v1.9 JSON
document, whose unknown fields are not currently guaranteed to round-trip.

Running v1.7 or v1.8 against a database already upgraded by v1.9 remains
subject to the pre-existing schema-3 downgrade limitation. v2 does not claim to
make that in-place downgrade possible; use the reason-pinned, verified legacy
pre-upgrade backup created before migration. The compatibility matrix records
this explicitly.

## Migration acceptance criteria

Migration/first enable is a release blocker until tests prove:

- clean v1.7, v1.8, and v1.9 upgrade paths using exact frozen synthetic
  fixtures and logical expected manifests;
- every interruption boundary recovers deterministically;
- every v1.9 manual decision type imports once and survives replay/rebuild;
- legacy capture is revision-consistent, complete, and cannot race retention
  cleanup;
- interrupted manifests cannot retire rows or advance reconciliation state;
- graph onboarding performs no schema-3 schema or user-decision writes, and
  logical source state changes only through normal existing owners;
- unsupported/corrupt/newer stores remain preserved;
- low-disk and busy/locked conditions wait/fail without UI hangs;
- a prior compatible graph remains readable during shadow bootstrap/migration,
  or graph reads are explicitly unavailable during bounded in-place migration;
- graph-disabled and rollback-to-v1.9 behavior remains fully functional.
