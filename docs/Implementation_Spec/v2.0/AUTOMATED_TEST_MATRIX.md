# v2.0 Knowledge Graph automated test matrix

## Evidence rules

This matrix specifies mandatory future tests. It does not mark any test as
implemented or passing.

All fixtures are synthetic and disposable. Tests use fake clocks, injected
fault boundaries, controlled stores/providers, deterministic schedulers,
bounded channels, and temporary application-data roots. They never scan a
developer folder, require live OCR/Ollama, retry a flaky result without
diagnosis, use arbitrary sleep for coordination, or modify a real source file.

Suites:

- **PR:** deterministic normal-CI unit/integration/provider/ViewModel gates.
- **Extended:** fault matrices, larger fixtures, and multi-process/provider
  cases run on hosted release validation.
- **Performance:** separate reproducible benchmarks/regressions, not universal
  latency claims.
- **Manual:** automated setup/assertion may support the separate unchecked
  manual checklist but cannot mark it complete.

## Migration and store lifecycle

| ID | Required scenario | Suite/layer | Required oracle |
| --- | --- | --- | --- |
| M01 | Clean v1.9 schema-3 first enable | PR/provider | Schema shape and canonical logical/user-decision manifests are unchanged; SQLite bytes are not the oracle; both sidecars initialize and bootstrap is queued outside migration. |
| M02 | Upgrade an exact frozen v1.7 schema-1 fixture | Extended/provider/fault injection | A reason-pinned verified backup is committed first; every 1→2→3 boundary survives interruption; canonical logical manifests preserve all version-owned data before graph bootstrap. |
| M03 | Upgrade an exact frozen v1.8 schema-2 fixture | Extended/provider/fault injection | The same backup/boundary/logical-manifest gates pass; Search/privacy survives, while schema-3 relationship tables become available without falsely claiming migration populated their projection. |
| M04 | Interrupted decision migration at every transaction boundary | Extended/fault injection | Old or complete new decision store is valid; verified backup remains. |
| M05 | Interrupted graph migration at every transaction boundary | Extended/fault injection | Old or complete new graph schema is valid; active projection remains readable. |
| M06 | Interrupted v1.9 manual-decision mirror import after every page | Extended/provider | Replay is idempotent; every legacy decision is mirrored exactly once without transferring authority from schema 3. |
| M06a | Legacy capture concurrent with decision mutation/retention cleanup | Extended/provider | Capture is revision-consistent or aborts; no committed decision is silently missed. |
| M07 | Unsupported newer v1.9/decision/graph schemas | PR/provider | Each store is rejected and unchanged; only independently healthy/supported features remain available, and graph reads fail closed when decision/privacy authority is affected. |
| M08 | Current version missing table/column/index/history | PR/provider | Shape corruption is detected even if `quick_check` is `ok`. |
| M09 | `foreign_key_check` and application-invariant corruption | PR/provider | Affected store/component is classified; no destructive reset. |
| M10 | Backup interrupted at every `Staging`/`Verified`/database-promotion/`Committed` boundary | Extended/fault injection | Incomplete records are ineligible and recoverable; the prior committed backup/current store remains; orphan files are quarantined or cleaned deterministically. |
| M11 | Backup/restore manifest, checksum, WAL/SHM, and same-volume promotion | PR/provider | Only a compatible committed copy is activated through the pointer protocol; current corrupt file remains evidence and post-promotion validation passes. |
| M12 | Backup retention by reason/count/bytes | PR/provider | Reason-pinned legacy and current privacy-floor recovery copies cannot be evicted by rolling graph/decision backup retention. |
| M13 | Rollback launch with v1.9 | Extended/compatibility | v1.9 reopens logically unchanged schema 3 and ignores sidecars; later v2 reconciles legacy edits. |
| M14 | Two processes contend during first enable/migration/restore | Extended/multi-process | One outer lifecycle lock owns mutation before either sidecar transaction; loser never observes/promotes partial state. |
| M15 | Lifecycle lock abandoned before a store opens and during staged promotion | Extended/fault injection | Recovery is bounded and evidence-based; no nested decision/graph transaction or guessed store reset occurs. |
| M16 | Privacy action and decision-backup recovery floor at every crash boundary | PR/privacy/fault injection | Completion is not acknowledged until a committed post-decision backup advances the minimum-restorable sequence; older copies never activate and an unreadable floor fails graph access closed. |

## Durable stages, restart, cancellation, and scheduling

| ID | Required scenario | Suite/layer | Required oracle |
| --- | --- | --- | --- |
| J01 | Restart during observation capture | PR/integration | Inbox plus ingestion watermark resume atomically without duplicate output or false deletion; applied watermark remains independent. |
| J02 | Restart during candidate extraction | PR/integration | Prior component remains; expired lease retries without processor-attempt loss. |
| J03 | Restart during identity resolution | PR/integration | No half-merge is visible; replay is deterministic. |
| J04 | Restart during edge/evidence preparation/publication | PR/provider | Old or complete new component is visible; no edge lacks evidence. |
| J05 | Restart during maintenance/compaction | Extended/provider | Active graph/decisions remain; bounded batch resumes safely. |
| J06 | Cancellation during every durable stage | PR/theory | Intent persists, unpublished applied watermark does not advance, durably ingested observations remain, and no stale running claim survives. |
| J07 | Repeated Pause/Resume | PR/integration | Idempotent run control; waiting reasons remain accurate; no duplicate claims. |
| J08 | Repeated Cancel/Retry | PR/integration | Terminal history retained; explicit retry creates one new attempt; limits hold. |
| J09 | Crash after cancel request before worker acknowledgement | PR/fault injection | Startup finishes cancellation rather than resuming ordinary processing. |
| J10 | Stale lease startup and periodic recovery | PR/provider | Expired claim is reclaimed atomically; live non-expired claim is not stolen. |
| J11 | Repeated infrastructure interruption | PR/provider | Recovery counter is bounded and transitions to `RepairRequired`. |
| J12 | Duplicate notification/job/replay | PR/integration | Unique idempotency key yields one effective component and one cursor advance. |
| J13 | Two coordinators/process owners | Extended/provider | At most one valid writer lease; reader fallback remains responsive. |
| J14 | Non-cooperative worker at shutdown | Extended/integration | New claims stop; desktop returns within hard deadline; next start recovers. |
| J15 | Configuration/algorithm change during work | PR/integration | Job uses captured fingerprint; only dependent components become stale. |
| J16 | Restart in `PauseRequested` and `Paused` at every stage/reconciliation page | PR/theory | Request resumes draining; acknowledged pause admits no ingestion/claim; Resume preserves waiting reasons. |
| J17 | Coordinator renewal fails while work is active | PR/fake store/time | Owner immediately self-fences; no claim/publication occurs until a higher epoch is acquired. |
| J18 | Late worker publishes after lease reclaim, Cancel, and shutdown deadline | PR/provider | Older epoch or claim token is rejected; component and applied watermark are unchanged. |
| J19 | Forward/backward clock jump, delayed heartbeat, and epoch/token ABA | PR/fake `TimeProvider` | Conservative reclaim is bounded; fencing epochs are monotonic; a reused-looking token cannot authorize output. |
| J20 | Crash before/after inbox insert, ingestion watermark, publication, and applied watermark | PR/fault injection | Each same-store pair is all-or-none; replay loses no observation and exposes no partial component. |
| J21 | Poison/permanent job followed by valid observations | PR/integration | Later observations ingest and project; failed component alone remains actionable. |
| J22 | Unexpected worker/store exception in the live process | PR/integration | Claim leaves `Running` through guarded failure/fencing without requiring application restart; exception is observed and diagnosed. |
| J23 | Shutdown during extraction, resolution, publication, decision write, backup, restore, and maintenance | Extended/fault injection | Hard deadline, transaction integrity, store lifetime, and late-publication rejection hold at every boundary. |
| J24 | Cancellation while dependency/resource/busy/backoff wait is active | PR/theory | Bounded wait exits promptly, cancellation intent wins, and neither retry nor wait restoration reclaims work. |
| J25 | Every legal and illegal four-axis state transition | PR/unit/provider | Legal transitions preserve independent axes; unknown/impossible transitions are rejected as corruption, never defaulted. |
| J26 | Source/decision/configuration revision changes while a worker runs | PR/integration | Pre-publication revalidation rejects old output, marks prior output stale, and queues exactly one new logical key. |
| J27 | Pause/Resume/Cancel/lease-expiry races in every ordering | PR/deterministic scheduler | Incrementing control sequence and documented precedence produce one deterministic state without duplicate claims. |
| J28 | Hung job heartbeat with healthy coordinator heartbeat | PR/fake time/provider | Only the job claim expires and is fenced; unrelated work continues. |
| J29 | Retry identity and immutable attempt history | PR/provider | One logical-work key owns ordered attempt rows; terminal attempt is not rewritten and explicit retry appends exactly one attempt. |

## Identity, evidence, and deterministic graph behavior

| ID | Required scenario | Suite/layer | Required oracle |
| --- | --- | --- | --- |
| G01 | File/Source/Folder/Collection/Document Set identities | PR/unit | Stable keys follow documented mechanical inputs and path semantics. |
| G02 | Identical input repeated and shuffled/concurrent execution | PR/unit | Canonical nodes/edges are identical with deterministic ordering. |
| G03 | Cross-host deterministic fixture | Extended | Windows/Linux/macOS canonical export agrees except documented path semantics. |
| G04 | Missing automatic-edge evidence | PR/unit/provider | Edge is rejected/stale/repair-required and absent from current explanation/Search. |
| G05 | Malformed/oversized/cyclic aliases | PR/unit/provider | Component is rejected/quarantined within alias and memory bounds. |
| G06 | Ambiguous real-world entities | PR/unit | Candidates remain separate; no hidden merge. |
| G07 | False-merge prevention corpus | PR/relevance | Adversarial similar names never merge without exact permitted basis/confirmation. |
| G08 | Manual merge persistence | PR/provider | Decision sequence wins after incremental update, restart, repair, and rebuild. |
| G09 | Manual split/never-merge persistence | PR/provider | Candidates remain separate after every algorithm/config rebuild. |
| G10 | Rebuild preserves manual entities/edges/aliases/rejections | PR/integration | Decision-store canonical snapshot is unchanged; derived graph reapplies it. |
| G11 | Duplicate logical edges and hash collision inputs | PR/provider | Canonical duplicate collapses; distinct collision input becomes repair required. |
| G12 | Cyclic graph | PR/unit | Cycle is retained as valid structure; traversal terminates deterministically. |
| G13 | Hostile Unicode/null/invalid timestamp/overflow | PR/security | Invalid record fails closed without corrupt object or unbounded allocation. |
| G14 | Malformed parser/model output | PR/security | Output is rejected as untrusted; stable graph remains usable. |
| G15 | Deterministic confidence/explanation | PR/unit | Named rule/evidence exactly matches stored edge; no invented percentage/reason. |
| G16 | Deferred/experimental Tag-node scope | PR/unit/contract | Stable production graph does not claim Tag identity without a provider-neutral indexed-tag observation contract; any Tag-node experiment remains disabled and clearly partial. |

## Incremental invalidation, privacy, and repair

| ID | Required scenario | Suite/layer | Required oracle |
| --- | --- | --- | --- |
| I01 | File rename with stable identity | PR/integration | File ID/compatible work reused; Folder/path edges alone update. |
| I02 | File move within/across source | PR/integration | Within-source identity follows policy; cross-source ambiguity preserves decisions safely. |
| I03 | Metadata-only modification | PR/integration | Only metadata-dependent graph components invalidate. |
| I04 | Content modification | PR/integration | Content/Document Set components invalidate; unrelated extraction/index work does not rerun. |
| I05 | File deletion and retention cleanup | PR/integration | Query hides immediately; graph cleanup occurs after authoritative reconciliation. |
| I06 | Forgotten file/source | PR/privacy | Restrictive barrier survives every crash point; no stale Search/graph leak. |
| I07 | Excluded source/file type | PR/privacy | No candidate/edge/query output; existing graph projection is removed/suppressed. |
| I08 | Original source bytes and timestamps | PR/integration | All graph/forget/repair/rebuild actions leave original fixture byte-identical. |
| I09 | Corrupt individual node/edge/evidence/alias/job | PR/provider | Selective quarantine/repair restores only affected component. |
| I10 | Corrupt graph database with intact decisions | Extended/recovery | Graph disables; v1.9 works; replacement rebuild preserves decisions. |
| I11 | Corrupt decision database | Extended/recovery | Mutations block; verified backup restore preserves ordered decisions. |
| I12 | Orphan and stale-record cleanup | PR/provider | Only proven orphans/expired derived history removed; active/manual data retained. |
| I13 | Interrupted reconciliation/source removal | PR/fault injection | Unseen records are not deleted until a completed manifest plus current point check proves absence. |
| I14 | Selective edge/file/entity/collection/source repair | PR/integration | Progress/cancellation/restart work and full rebuild is unnecessary. |
| I15 | Full derived rebuild last-resort path | Extended/recovery | Quarantines old graph, preserves decisions/index, produces deterministic valid replacement. |
| I16 | Updated-time collision, timestamp granularity, and clock rollback/forward | PR/fake time/provider | Hint cursor may duplicate/miss latency hints but stable-ID reconciliation still ingests every current row and makes no false deletion. |
| I17 | Update behind page cursor, missed notification, and mutation during reconciliation | PR/deterministic provider | Current generation remains conservative; next full generation observes the update; stale revision cannot publish as current. |
| I18 | Delete/recreate same stable ID before, during, and after a manifest | PR/integration | Absence is point-revalidated; incompatible old evidence is not inherited; no live recreated row is retired. |
| I19 | Completed and interrupted generation promotion/retention | PR/provider | Candidate rows remain hidden; at most the documented generations remain; only a complete validated generation becomes active. |
| I20 | Decision commit before graph inbox, out-of-order delivery, duplicate delivery, and sequence gap | PR/fault injection | Ledger remains authoritative; affected expansion is withheld; ordered replay reaches one applied sequence without lost correction. |
| I21 | Legacy v1.9 decision changes/removals after import or rollback use | Extended/compatibility | Schema-3 owner remains authoritative and reconciliation updates the non-authoritative mirror exactly once. |
| I22 | Privacy forget/exclusion before and after cleanup convergence | PR/privacy/fault injection | Every graph query rechecks current authority; unavailable authority fails restrictive; stale cached graph data never leaks. |
| I23 | Factual source revision becomes stale between query snapshot and expansion | PR/integration | Result is omitted or labelled stale/partial according to contract; stale graph evidence is never explained as current. |

## Dependencies, resources, concurrency, Search, and UI

| ID | Required scenario | Suite/layer | Required oracle |
| --- | --- | --- | --- |
| R01 | Ollama unavailable/restored | PR/fake provider | Stable graph/Search work; experimental stage waits and later resumes once. |
| R02 | OCR unavailable/restored | PR/fake provider | Partial coverage is honest; no stable mechanical stage blocks. |
| R03 | Low disk before backup/build/maintenance | PR/fault provider | `WaitingForResources`; active graph/decisions/index remain. |
| R04 | `SQLITE_FULL` during transaction | Extended/provider | Transaction rolls back; classified resource wait; replay succeeds after recovery. |
| R05 | Independent database busy/locked connection | Extended/provider | Finite deadline/backoff; Search returns v1.9 fallback; no corruption classification. |
| R06 | Concurrent Search and graph publication | Extended/integration | Reader sees old/new valid snapshot; exact/literal result ordering is unchanged. |
| R07 | Concurrent maintenance and reads | Extended/provider | Reads remain valid/bounded; maintenance resumes without data loss. |
| R08 | Search during bootstrap/stale/repair/disabled/corrupt graph | PR/integration | Existing Search remains useful with accurate graph coverage message. |
| R09 | Graph Search expansion explanation and opt-out | PR/Search | One-hop bounded additions use actual evidence, rank below exact/literal, opt-out yields v1.9 result set. |
| R10 | Repeated rapid/overlapping query cancellation | PR/integration | Admission, time, seed, expansion, and cancellation bounds hold. |
| R11 | Large synthetic graph and high-degree nodes | Performance | No all-pairs behavior; bucket/degree/queue/memory ceilings hold. |
| R12 | Bounded traversal and graph-explosion attempts | PR/security/performance | Depth/visited/edge/time ceilings terminate deterministically with reason. |
| R13 | Cancellation latency regression | Performance | Every bounded stage/query/shutdown stays below measured release threshold. |
| R14 | Memory/allocation regression | Performance | Increasing synthetic sizes remain within documented bounded-growth envelope. |
| R15 | Paged/virtualized ViewModel collections | PR/ViewModel | UI never materializes whole graph; navigation/focus/state are deterministic. |
| R16 | No UI-thread database/blocking work | PR/ViewModel/integration | Controlled scheduler proves service work is asynchronous and cancellable. |
| R17 | Accessibility | PR/XAML/ViewModel plus Manual | Names, roles, focus, keyboard, non-hover actions, live state, and bounded announcements exist. |
| R18 | Diagnostics redaction | PR/security | No document/OCR/summary/alias/query/prompt/vector/secret/full path in default events/export. |
| R19 | Diagnostics state/actionability | PR/integration | Safe run/job/stage/lease/cursor/lag/bounds/busy/low-disk/repair facts are retained. |
| R20 | In-memory and durable queue row/byte boundaries below/at/above limit | PR/provider | Admission stops before overflow, ingestion watermark does not advance for unaccepted pages, and drain resumes without loss. |
| R21 | Transaction and merge/split/cascade row/byte/time boundaries | PR/provider/security | Work commits in bounded staged pages or is rejected before publication; no partial component or memory spike. |
| R22 | Eco/Balanced/Fast, idle, power, battery, and time-window policies | PR/fake resource adapters | Concurrency changes only between claims; an enabled unavailable adapter waits actionably and restoration resumes once. |
| R23 | Disk full during decision commit, WAL checkpoint, backup/restore, and graph activation | Extended/fault provider | Existing verified data stays active; unsaved decision is reported honestly; no partial staging is promoted. |
| R24 | Decision/graph/Search/maintenance lock-order stress | Extended/deterministic scheduler | No nested cross-store transaction, deadlock, lock inversion, UI wait, or authority loss. |
| R25 | Busy threshold below/at/above command and cumulative limits | PR/fake provider/time | Classification deterministically moves from bounded retry to `WaitingForResources(DatabaseBusy)` and health probe resumes once. |
| R26 | Reconciliation generation, inbox retention, and storage-reserve limits | PR/provider | Paged work stays on disk, expired applied history alone is pruned, and quota never removes decisions/privacy/current evidence. |
| R27 | Candidate-text, CPU-yield, alias, degree, and evidence limits | PR/security/performance | First exceeded bound terminates or pages deterministically without source-file reads or silent fact truncation. |
| R28 | Decision-ledger quota/reserve exhausted | PR/fault provider/ViewModel | New decision is rejected before commit with actionable status; existing decisions and source files are unchanged. |
| R29 | Query privacy authority unavailable during rapid concurrent expansion | PR/security/integration | Graph supplement fails restrictive under load while bounded ordinary Search remains responsive. |

## Compatibility regression gates

The complete existing suite must remain unchanged and passing. Targeted
regression fixtures must additionally prove:

- catalogs and saved scans load;
- watched-folder ownership/reconciliation remains isolated;
- duplicate detection and exact-content reuse remain correct;
- workflows/plugins retain their existing public authority and APIs;
- Change Plans, Operation Journal, recovery, and Undo are unaffected;
- Search without graph, AI, OCR, or a readable graph store remains functional;
- v1.9 relationships/collections/manual overrides/tombstones retain meaning;
- no graph operation reaches a source-file write boundary.

No test may be weakened, deleted, ignored, or skipped to meet these gates.

## Lifecycle release-gate traceability

An exact release-candidate commit is blocked unless its independently parsed
results prove all of the following, in addition to the complete unchanged
baseline suite:

- M14-M15 prove the outer lifecycle lock and absence of nested cross-store
  transactions;
- J17-J20 and J28 prove renewal self-fencing, independent claim leases, monotonic
  epochs, late-publisher rejection, and independent ingestion/applied
  watermarks at every crash boundary;
- I13 and I16-I19 prove authoritative stable-ID generations under clock change,
  concurrent mutation, missed notifications, interruption, and recreation;
- I20-I23 and R29 prove decision-order withholding, factual source revalidation,
  and the always-authoritative fail-restrictive privacy barrier;
- J16, J23-J27 prove pause, cancellation, shutdown, wait, revision, and illegal
  transition behavior at every durable boundary;
- R20-R28 prove durable queue, transaction, cascade, generation, policy,
  busy-deadline, storage-reserve, and immutable-decision bounds; and
- no case is skipped, quarantined, retried without diagnosis, or replaced by an
  unchecked manual observation.

## Performance evidence

Separate reproducible datasets should cover increasing files, nodes, edges,
degree, aliases, decisions, and stale records. Measure cold/warm graph query,
projection throughput, incremental update, decision replay, reconciliation,
Search impact, maintenance, memory/allocation, cancellation, restart recovery,
and selective/full rebuild.

Thresholds are set only after measuring a reviewed baseline on controlled CI
hosts. Results are regression guardrails, not a claim of million-file support
or universal latency.
