# v2.0 Knowledge Graph failure-mode analysis

## Status and method

This is the accepted failure analysis used by the implementation candidate,
not passing evidence. The candidate was created from exact validated design
tip `a2a9a071600de74759937f05a7be61f85e9d5d93`; source and tests define its
exact implemented boundary. Final fault-injection and RC evidence remains open.

Severity meanings:

- **Blocker:** v2.0 cannot merge or publish while reproducible.
- **High:** requires a deterministic automated gate and actionable recovery.
- **Contained:** acceptable only when the stated fallback and diagnostics are
  proven.

## Existing risks the graph must not amplify

The v1.9 audit found several boundaries that are safe enough for the current
bounded relationship feature but unsuitable as assumptions for a larger graph:

- relationship analysis is still between `SearchIndexUpdated` and
  `FileFullyIndexed`, so expanding it would increase indexing blast radius;
- an unexpected worker/store exception can leave a live claim `Running` until
  restart recovery;
- claims have no owner lease or heartbeat, and a crash while `Cancelling` is
  recovered as ordinary `Running` work;
- every SQLite read, write, backup, and maintenance call shares one process
  semaphore, and synchronous SQLite commands cannot be cancelled mid-command;
- busy, locked, full-disk, and corruption errors are not all distinguished at
  the provider-neutral boundary;
- `quick_check` does not prove schema shape, foreign-key integrity, enum domains,
  or application invariants;
- backup retention mixes reasons and does not manifest/checksum every copy;
- some current relationship evidence is based on whole-field fingerprints,
  production tags are not populated into relationship candidates, and accepted
  edges can outlive changed evidence until revalidation;
- relationship algorithm changes participate in the broader indexing processor
  fingerprint and can therefore invalidate more work than a graph change should;
- collection materialization and relationship candidate loading are bounded but
  not designed for a large interactive graph.

The v2 design isolates graph work and adds its own leases, four-axis states,
completed manifests, observation/applied watermarks, integrity checks, stores,
and resource ceilings. It does not silently redefine the v1.9 guarantees.

## Four-axis failure-state taxonomy

The durable model never collapses unrelated state into one enum. Product/UI
status is a projection over four independent axes.

| Axis | Values | Failure interpretation and user-visible meaning |
| --- | --- | --- |
| Run control | `Pending`, `Running`, `PauseRequested`, `Paused`, `CancelRequested`, `Cancelled`, `Complete` | Records durable user intent and admission. A request state remains visible while claims drain; terminal cancellation is never inferred from a button click alone. |
| Job execution | `Pending`, `Running`, `Complete`, `Cancelled`, `RetryableFailure`, `PermanentFailure`, `WaitingForDependency`, `WaitingForResources` | Describes one logical-work key. Attempts are append-only; wait states retain a bounded reason and eligibility time. |
| Component freshness | `Current`, `Stale` | States whether published output matches source, decision, configuration, and algorithm revisions. Stale output is never explained as current fact. |
| Component integrity | `Valid`, `RepairRequired` | Records whether invariants permit use. Quarantine is a repair disposition; it does not make stale data current. |

Legal run transitions are `Pending → Running`, `Pending → CancelRequested`,
`Running → PauseRequested`, `PauseRequested → Paused`, a newer Resume through
`PauseRequested → Running` or `Paused → Running`, any non-terminal state to
`CancelRequested`, `CancelRequested → Cancelled`, and `Running → Complete`.
`Cancelled` and `Complete` are terminal for that run.

Legal execution transitions start at `Pending`; a claim moves to `Running`, and
bounded work moves to completion, cancellation, one failure state, or one wait
state. A satisfied wait/backoff or permitted explicit retry appends a new
attempt before returning the logical job to `Pending`. `Complete` is immutable
for the exact logical-work key. `PermanentFailure` cannot retry unchanged input
without explicit reviewed intent. Freshness changes only by revision comparison
and validated replacement. Integrity returns from `RepairRequired` only after
successful verification or repair.

Race precedence is privacy/deletion/exclusion authority first, then coordinator
fencing epoch and claim token, then durable cancellation, then the latest
pause/resume control sequence. A revision change rejects an old publication and
makes prior output stale. Lease expiry under cancellation finishes cancellation;
otherwise it is infrastructure recovery. Resource/dependency restoration does
not override a paused or cancelling run.

Unknown values, unlisted transitions, missing or reused claim tokens,
non-monotonic fencing epochs, `Running` without heartbeat/expiry, or impossible
cross-axis combinations are corrupt records. They do not default to `Pending`.

## Failure analysis

| Failure | Detection | Durable outcome and containment | Recovery | Severity |
| --- | --- | --- | --- | --- |
| v1.9 source database is unsupported newer schema | Existing source provider rejects version before graph access. | Graph is unavailable; index file is unchanged; compatible Search path reports source-provider status. | Install a compatible OpenSorSe version or restore a reviewed compatible backup. Never downgrade in place. | Blocker if modified or misreported. |
| Decision or graph schema is newer than supported | Header/version plus schema-shape preflight. | Reject only that store; do not create tables, migrate, or reset. | Compatible application or explicit reviewed side-by-side recovery. | Blocker if overwritten. |
| Migration interrupted before transaction commit | Fault-injection marker and next-start inspection. | SQLite rollback retains the prior schema; partial backup staging is not published. | Revalidate source and replay migration. | Blocker if prior store is not readable. |
| Migration commit succeeds but process stops before acknowledgement | Version/history/checksum agree on restart. | Treat migration as complete; do not rerun non-idempotent actions. | Resume bootstrap/import from durable cursor. | High. |
| Legacy manual-decision import interrupted | Import key and source revision are unique. | Already imported records remain valid; no source row is deleted. | Replay bounded page idempotently. | Blocker if decisions duplicate or disappear. |
| Legacy decision is purged while first-enable capture runs | Source-provider capture lease and revision-consistent bounded snapshot. | Enablement aborts rather than accepting a partial decision set. | Retry capture after resolving capacity/maintenance state. | Blocker if a committed correction is missed. |
| `knowledge-graph.db` header/page corruption | Open error, `quick_check`, `foreign_key_check`, shape/invariant validation. | Disable graph projection only; preserve file and sidecars as quarantine evidence. | Read prior valid generation/backup if safe, otherwise rebuild derived graph from source plus decisions. | Blocker if Search/indexing fails or decisions are lost. |
| Individual graph row is malformed | Typed row parser, constraints, invariant scan. | Quarantine row ID/reason; hide it and mark affected component `RepairRequired`. | Selective component repair from source and decision revision. | High. |
| Decision store corruption | Integrity/shape/sequence validation. | Graph reads, expansion, publication, and mutation fail closed; no correction is accepted or silently discarded. Independently healthy v1.9 behavior remains. | Restore the newest compatible `Committed` backup at or above the validated privacy-recovery floor; retain corrupt copy for review. | Blocker if graph data is exposed or no verified recovery path exists. |
| Backup creation is interrupted or corrupt | Temporary suffix, manifest/checksum, integrity validation. | Unverified copy is never published or counted as known-good. | Delete only the verified temporary artifact; retry when resources permit. | High. |
| Low disk before backup/migration/build | Free-space preflight includes database, WAL, staging, backup, and reserve. | Transition to `WaitingForResources`; keep active graph readable. | Free space, lower configured quota, or reviewed cleanup; then resume. | Blocker if existing data is deleted silently. |
| Disk fills during write | Provider maps `SQLITE_FULL`/I/O category. | Transaction rolls back; job waits for resources; existing projection remains. | Free space and retry from idempotency key. | High. |
| Database busy or externally locked | Provider maps busy/locked separately with the documented command/attempt deadline. | One bounded attempt may be `RetryableFailure`; three consecutive eligible attempts or thirty cumulative seconds become `WaitingForResources(DatabaseBusy)`. UI does not hang. | A successful health probe returns the job to pending; diagnostics expose category, not paths/content. | High. |
| Coordinator renewal fails or wall clock jumps | Injected wall/elapsed clocks, heartbeat sequence, and compare-and-set renewal. | Owner self-fences immediately; no new claim or publication. A reclaim increments the fencing epoch. | Reacquire after health/clock stabilization; prior epoch remains permanently invalid. | Blocker if two epochs publish. |
| Job worker hangs while coordinator remains healthy | Independent per-job heartbeat and expiry. | Only that claim expires; coordinator heartbeat does not keep it alive. | Claim-token/epoch guarded infrastructure recovery. | High. |
| Old worker publishes after reclaim or shutdown | Publication compares coordinator epoch, claim token, state, and input revisions. | Transaction is rejected; active component and applied watermark remain unchanged. | Dispose pure late computation; new owner continues. | Blocker if late output appears. |
| Process stops during candidate extraction | Lease expires; no publication transaction occurred. | Prior component remains active. | Recovery count increments without consuming processor retry; stage resumes. | High. |
| Process stops during identity resolution | Lease/input revision plus staged-work state. | No unvalidated identity becomes visible. | Discard/reuse idempotent staging and rerun bounded component. | Blocker if half-merge is visible. |
| Process stops after inbox insert but before ingestion acknowledgement | Inbox rows and ingestion watermark share one graph transaction. | Either both exist or neither exists. | Replay the source page idempotently. | Blocker if observation is lost. |
| Process stops after ingestion acknowledgement before claim | Durable inbox is independent from applied watermark. | Later observations remain ingestible; unclaimed work remains pending. | Scheduler resumes without rewinding published components. | High. |
| Process stops during edge publication | SQLite transaction and epoch/claim-token compare-and-set. | Either old or complete new component plus applied watermark is visible, never half an edge/evidence set. | Replay is idempotent. | Blocker if partial objects appear. |
| Poison or permanently failing observation | Separate ingestion/applied watermarks and logical-job isolation. | Failure is bounded to its component; later observations ingest and execute. | User repairs/retries only the failed component. | Blocker if source progress stalls indefinitely. |
| Process stops during maintenance/cleanup | Maintenance journal and small independent batches. | Active projection/decisions are not removed; incomplete batch is detectable. | Resume or retry batch; full rebuild remains last resort. | High. |
| Pause or shutdown during a wait/backoff | Run-control sequence and linked cancellation token. | No hidden claim; wait slice ends within its bound and retains its structured reason. | Resume only when both run control and the wait gate permit. | High. |
| Crash after cancellation request | Cancellation intent is committed before signals are sent. | Startup resumes cancellation, not ordinary work. | Finalize owned/expired claims as `Cancelled`. | Blocker if cancelled work restarts silently. |
| Cancellation intent cannot be persisted | Busy/full/unavailable classification. | Request is reported as not accepted; UI remains on the prior truthful state. Shutdown still self-fences local work. | Restore store availability and retry; never claim terminal cancellation. | High. |
| Non-cooperative processor during shutdown | Drain deadline, epoch revocation/self-fencing, and claim-token publication guard. | Stop new claims; exit after deadline; detached pure work owns no store or application disposable. | Next start reclaims expired facts; late publisher is rejected. | Blocker if UI cannot exit or disposed storage is touched. |
| OCR or Ollama unavailable | Existing source coverage plus dependency health. | Stable mechanical graph remains usable; optional suggestion work waits/skips. | Dependency restoration or disabling experimental stage. | Contained. |
| Parser/model output is malformed or oversized | Strict schema/type/count/Unicode validation. | Reject candidate batch; never persist model text as fact. | Retry only after provider/version/input change; deterministic path continues. | High. |
| Evidence is missing or stale | Evidence reference and source fingerprint revalidation. | Automatic edge becomes `Stale` or quarantined and is excluded from current explanations/expansion. | Selective reprojection; manual edges remain labelled manual. | Blocker if stale evidence is presented as current fact. |
| Alias is malformed, cyclic, or excessive | Canonicalization, cycle/degree/count checks. | Reject/quarantine affected identity component. | User-visible repair or manual split. | High. |
| Identity candidates are ambiguous | Contradiction and bucket-size rules. | Keep separate; no automatic merge. | Optional user confirmation/merge. | Blocker if ambiguity auto-merges. |
| False merge is later corrected | Durable split/never-merge decision keyed to candidates. | Replacement component is staged; prior merge marked stale. | Publish split and reapply on every rebuild. | Blocker if correction is lost. |
| File renamed or moved | Source-scoped file identity plus source revision/reconciliation. | A within-source rename/move updates File/Folder edges under existing identity policy. A cross-source move creates a new File ID and never silently transfers manual decisions; exact-content evidence may only suggest a relationship. | Incremental affected-component projection with visible ambiguity where identity breaks. | High. |
| File modified | Content/metadata and graph algorithm fingerprints. | Stale only dependent graph components; no broad reindex triggered by graph version. | Reproject from first affected graph stage. | High. |
| File deleted, forgotten, or source excluded | Authoritative deletion/privacy snapshot checked at ingest, pre-publication, and every query. | Hide immediately at query barrier; queue graph removal and orphan cleanup. Cleanup convergence never disables the barrier. | Reconciliation proves removal; tombstone retention prevents resurrection. | Blocker if stale graph leaks it. |
| Privacy decision commits but its post-decision recovery point cannot be committed | Decision sequence is ahead of the recovery catalog's minimum-restorable privacy sequence. | Keep graph reads/expansion fail closed and report the action as applied but recovery finalization pending; no older backup is eligible. | Create and validate the bounded post-decision backup, atomically advance the recovery floor, then acknowledge completion. | Blocker if completion is claimed early or an older backup can activate. |
| Privacy/deletion authority is unavailable | Bounded authoritative point read fails or schema is unsupported. | Omit graph-derived result/expansion fail-restrictive; ordinary v1.9 Search may continue only through its healthy path. | Retry graph supplement after authority recovers. | Blocker if cached graph data is disclosed. |
| Source removal notification is missed | Periodic stable-ID reconciliation from page zero and completed-generation manifest. | Unseen data is not deleted during an interrupted scan. Candidate absence is point-revalidated before retirement. | Only a completed reconciliation plus current absence proof retires source records. | High. |
| Updated-time collision, clock rollback, or update behind hint cursor | Hint cursor is explicitly non-authoritative; full stable-ID generations ignore modification time. | No completeness/deletion decision uses the hint cursor. Prior component becomes stale when the revision mismatch is observed. | Notification or next completed generation ingests the revision. | High. |
| Source mutates while reconciliation scans | Per-generation durable seen manifest, stable-ID paging, and pre-publication/absence point checks. | Candidate generation stays hidden; passed-row updates may wait for the next generation; no false deletion or stale publication is labelled current. | Complete current generation conservatively, then reconcile again. | Blocker if a concurrent row is retired or stale output is presented as current. |
| File stable ID is deleted then recreated | Source revision/fingerprint and current ownership point check. | Old component is stale/retired; recreated input receives its current logical key and cannot inherit incompatible evidence. | Reproject the recreated file and retain applicable explicit decisions only. | High. |
| Duplicate job/event delivery | Unique idempotency key and inbox/decision sequence. | Coalesce/replay without duplicate graph output. | No user action. | Blocker if duplicates expand graph. |
| Decision commits before graph inbox/application | Authoritative sequence and graph ingested/applied decision watermarks. | Decision remains durable; affected graph expansion is withheld while sequence lags. | Reconciliation inserts/replays every gap in order. | Blocker if stale contradictory graph output is shown. |
| Legacy v1.9 decision changes after initial import | Revisioned non-authoritative mirror plus periodic reconciliation. | Schema-3 owner remains authoritative for the legacy decision; graph mirror is stale until replay and cannot override it. | Import changed/removal observation idempotently and reproject affected components. | Blocker if two writable authorities diverge. |
| Durable inbox/job/storage limit reached | Row/byte/quota preflight before page acknowledgement. | Stop ingestion without advancing its watermark; enter `WaitingForResources`; retain every durable decision and existing projection. | Drain/cleanup derived history or increase reviewed quota, then resume. | Blocker if work/decision is silently dropped. |
| Oversized merge/split/component cascade | Affected-node/edge preflight and paged-operation limit. | Require a durable paged reviewed operation or reject before publication; no partial cascade becomes active. | Narrow/split the action or explicitly approve a supported higher policy. | Blocker if memory/transaction bounds are bypassed. |
| High-degree/pathological record | Candidate/degree/evidence ceilings before allocation/publication. | Mark component ambiguous or `RepairRequired`; truncate nothing silently. | Exclude, split, or reviewed policy change. | Blocker if work is unbounded. |
| Cyclic graph traversal | Visited set, depth/node/edge/time ceilings. | Return bounded complete page plus limit reason. | Narrow query; cycle itself is not corruption. | Blocker if recursion is unbounded. |
| Concurrent Search and graph publication | Separate WAL read snapshot and atomic component publication. | Search sees old or new valid component; exact/literal results remain usable. | Retry graph supplement only; do not discard ordinary Search. | Blocker if corrupt partial hit returned. |
| Maintenance overlaps reads | Persisted maintenance lease and row/byte/time-bounded batches. | Existing read snapshots finish; new graph reads may show maintenance coverage. | Resume after batch/checkpoint. | High. |
| Disk fills during WAL checkpoint, backup/restore, or generation activation | Reserve preflight plus SQLite/filesystem fault category and staged manifest. | Current verified store/projection remains; incomplete staging is never promoted. | Free space, retain evidence, and retry from the last verified boundary. | Blocker if current data is replaced or a partial generation activates. |
| Outer lifecycle lock cannot be acquired | Atomic application-data lock owner/timeout diagnostics. | Migration/backup/restore does not open a nested lifecycle transaction; ordinary compatible reads may continue. | Wait for owner or recover a provably abandoned lock without rewriting a store. | High. |
| Cross-store callback attempts nested transaction | Lock-order assertion/fault scheduler. | Abort the operation before the second store lock; first committed decision remains authoritative. | Enqueue/reconcile after releasing the first transaction. | Blocker if deadlock or partial authority occurs. |
| UI receives a very large result/collection | Paging/virtualization and bounded ViewModel collections. | No full graph materialization or accessibility-tree flood. | User pages/filters; diagnostics record bound reached. | Blocker if UI hangs. |
| Diagnostics/export path fails | Bounded redacted event store and explicit export transaction. | Product work continues; no source content is logged as fallback. | Retry export after review. | Blocker if private content appears. |
| Graph action attempts source mutation | Capability boundary lacks write operation; source hash test fixture. | Reject as programming/policy error. | Fix before release; no recovery should be necessary because original is unchanged. | Blocker. |

## Release-blocking invariants

The following are non-negotiable release blockers:

1. any reproducible source, v1.9 index, or manual-decision loss;
2. any interrupted migration/import without deterministic recovery;
3. any rebuild that changes user decisions or produces nondeterministic canonical
   output for identical inputs;
4. any automatic identity merge with ambiguity or without exact retained basis;
5. any automatic edge whose explanation cannot resolve its evidence;
6. any stale `Running` claim after lease recovery or duplicate active claim;
7. any older fencing epoch/claim token publication, or renewal failure that does
   not immediately self-fence;
8. any false deletion from a partial manifest/hint cursor, ingestion/applied
   watermark loss, or privacy query that trusts cleanup convergence;
9. any illegal four-axis transition or terminal state inferred without its
   durable acknowledgement;
10. any unbounded candidate, degree, traversal, Search expansion, durable queue,
   cascade, transaction, generation, history, storage, or memory behavior;
11. any graph failure that blocks ordinary indexing/Search/relationships;
12. any whole-index reset required to repair graph-only corruption;
13. any UI hang, shutdown hang, diagnostics disclosure, source-file mutation,
    skipped test, unresolved policy failure, or critical/high vulnerability.

Scope must be reduced, not the invariant weakened, if a blocker cannot be
closed predictably.
