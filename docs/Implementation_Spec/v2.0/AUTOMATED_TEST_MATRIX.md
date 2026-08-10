# v2.0 Knowledge Graph automated test matrix

## Evidence rules

This matrix specifies candidate acceptance tests. Source test files determine
which cases are implemented; this matrix does not mark any result as passing.
Exact totals and outcomes belong in the final validation report.

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

## Candidate source traceability audit

This audit maps the candidate source tree to the requirements above. It is a
source-coverage review, not a test-result report. `Direct` means a targeted
test contains the row's primary deterministic oracle. `Partial` means a
related test exists but does not cover every required boundary, process,
ordering, provider, or recovery condition. `No targeted source` means the
review found no test dedicated to that requirement; it does not imply that the
corresponding production code is absent. Extended and Performance rows remain
release-candidate work even when a smaller PR test exercises a related path.

The principal source evidence used below is:

- `SqliteGraphConsentBoundaryIntegrationTests`,
  `SqliteGraphStorageLifecycleTests`,
  `SqliteGraphDecisionStoreDurabilityTests`,
  `SqliteGraphStoreEndToEndTests`, and
  `SqliteGraphProjectionSourceTests` for provider durability;
- `GraphProjectionCoordinatorTests`, `GraphReleaseGateMatrixTests`,
  `GraphIdentityAndStateTests`, `GraphProjectionBuilderTests`,
  `GraphDeterminismSecurityRegressionTests`,
  `GraphDerivedStoreRecoveryServiceTests`, and
  `GraphApplicationServiceTests` for Application behavior;
- `GraphSearchResilienceMatrixTests` and
  `KnowledgeGraphSearchIntegrationTests` for Search/privacy behavior; and
- `KnowledgeGraphViewModelTests`, `KnowledgeGraphAccessibilityTests`, and the
  two Knowledge Graph performance-regression fixtures for UI and bounded-cost
  behavior.

### Migration and lifecycle traceability

| ID | Coverage | Concrete source evidence and residual scope |
| --- | --- | --- |
| M01 | Direct | Consent-boundary `DirectApplicationCallBeforeConsentDoesNotCreateGraphArtifacts` and `ExplicitEnableProvisionsBothSidecarsBeforeApplicationReadsBecomeAvailable` verify the no-sidecar boundary and explicit first enable. |
| M02-M05 | No targeted source | No frozen schema-1/schema-2 fault fixture or every-transaction-boundary decision/graph migration matrix was located. These remain Extended gates. |
| M06 | Partial | `LegacyMirrorPagingResumesAfterRestartAndPublishesAtomically`, `Reconcile_InterruptedLegacyDecisionMirror_ResumesWithoutDuplicatePublication`, and Application replay tests cover restart/idempotency, not interruption after every page boundary. |
| M06a | No targeted source | No deterministic concurrent legacy capture versus decision mutation/retention schedule was located. |
| M07 | Partial | Newer deep-index and decision schemas are rejected by `UnsupportedNewerDeepIndexSchemaFailsClosed` and `InitializationRejectsCheckpointCorruptionAndNewerSchema`; the every-store independent-health matrix is not complete. |
| M08-M09 | Partial | Checkpoint corruption, malformed projection, integrity gates, and invalid-record tests exist, but no table/column/index/history plus `foreign_key_check` fault matrix covers each sidecar. |
| M10-M12 | Partial | Interrupted restore promotion, verified recovery, privacy-floor rejection, bounded backups, and quota failure are tested. Every staging/verification/promotion boundary, WAL/SHM combination, reason pin, and byte boundary is not. |
| M13 | No targeted source | No automated rollback-launch fixture executes v1.9 against the unchanged schema-3 authority. |
| M14 | Partial | `ConcurrentProvisionIsSerializedAndIdempotent` and `LifecycleLockHasBoundedContentionAndDecisionIdsRejectPaths` are in-process evidence only; the required two-process owner/loser proof remains Extended. |
| M15 | Partial | Interrupted decision restore and journaled derived-store promotion recovery are covered, but abandoned outer-lock recovery before open and during each staged promotion is not. |
| M16 | Partial | Privacy backup/floor publication and stale-floor rejection are targeted; the every-crash-boundary privacy/backup recovery matrix is not. |

### Durable work traceability

| ID | Coverage | Concrete source evidence and residual scope |
| --- | --- | --- |
| J01 | Partial | Projection-source manifests, source commit fencing, and periodic missed-notification reconciliation are tested; inbox insertion/ingestion-watermark crash atomicity is not exhaustively injected. |
| J02-J04 | Partial | Intermediate-stage fencing, expired-stage recovery, durable stage history, and malformed-publication atomicity are tested. The complete crash matrix for extraction, identity, evidence, publication, and applied watermark is not. |
| J05 | No targeted source | No interrupted maintenance/compaction fault matrix was located. |
| J06 | Partial | `Projection_CancellationAtEachPureDurableStage_DoesNotPublish` covers every pure builder stage with no publication or watermark advance; capture, inbox, publication-transaction, and maintenance boundaries still need provider fault coverage. |
| J07-J08 | Partial | Pause/Resume and Cancel/Retry behavior is covered, but repeated idempotency plus append-only retry-attempt history is not proved for every ordering. |
| J09 | Direct | `RestartFencesNonExpiredClaimAndAcknowledgesRepairCancellation` persists `CancelRequested`, restarts before the claim's wall-clock expiry, fences the obsolete attempt, and acknowledges the run and staged repair as cancelled. |
| J10 | Direct | `HealthyCoordinatorHeartbeatDoesNotKeepExpiredJobClaimAlive` proves a non-expired claim is not stolen, then independently expires and fences only that claim before one new attempt is admitted. Existing restart tests prove expired claims recover and publish once. |
| J11 | Partial | Bounded superseded-run recovery and classified failures exist, but repeated infrastructure interruption to `RepairRequired` is not directly exercised. |
| J12 | Direct | Duplicate/no-op manifest, notification coalescing, legacy replay, and decision replay tests verify one effective component/cursor result. |
| J13-J14 | No targeted source | Multi-owner writer fencing and non-cooperative hard-deadline shutdown remain Extended gates. |
| J15-J16 | Partial | Authority/config/decision revalidation, paused runtime admission, and resume behavior exist; captured-fingerprint invalidation and restart in both pause states at every stage/page are incomplete. |
| J17 | Direct | `Reconcile_HeartbeatRejected_FencesWithoutStaleRunningClaim` verifies immediate self-fencing after coordinator renewal rejection. |
| J18 | Partial | Stale claim-token and authority-change publication rejection exist; late publishers after reclaim, Cancel, and shutdown deadline are not all injected. |
| J19 | Partial | `CoordinatorEpochRemainsMonotonicAcrossForwardAndBackwardClockJumps` proves conservative backward-clock refusal, monotonic epochs after forward takeover, and stale epoch/token publication rejection. The complete delayed-heartbeat/token-reuse schedule remains RC fault-matrix work. |
| J20 | Partial | Provider component replacement and stage claims are transactional and fenced in targeted tests; every before/after inbox, ingestion, publication, and applied-watermark crash point is not. |
| J21-J22 | Direct | `Projection_UnexpectedPoisonJob_DoesNotBlockLaterValidObservation` proves an unexpected permanent failure is observed, leaves no running claim, and does not block later valid work. |
| J23 | No targeted source | The required shutdown-at-every-boundary matrix remains Extended. Chunk-cancellation tests are related but not a substitute. |
| J24 | Partial | `Projection_CancellationDuringResourceWait_ExitsWithoutClaimOrWatermarkAdvance` covers resource wait, while `LockedGraphWrite_CancellationInterruptsBusyWaitAndStoreRecovers` and `LockedDecisionWrite_CancellationIsPromptAtomicAndRecoverable` cover transactional database-busy cancellation. Dependency and backoff waits remain. |
| J25 | Partial | `GraphReleaseGateMatrixTests` exhausts all defined state vectors, all pairwise run/job transitions, and unknown values against the committed concurrency-model rules. Provider enforcement and append-only retry/cancellation preconditions remain unproved. The audit also corrected the validator so `Pending` cannot jump directly to `Paused` or `Cancelled`. |
| J26 | Partial | Source/authority and decision changes during a claim reject publication; configuration/algorithm revisions and the exactly-one replacement-key oracle are not complete. |
| J27 | Partial | `CancellationWinsLatePauseAndResumeRequests` proves durable cancellation precedence and safe repeated cancellation, in addition to the individual control paths. Every Pause/Resume/Cancel/lease-expiry permutation under a deterministic scheduler remains RC work. |
| J28 | Direct | `HealthyCoordinatorHeartbeatDoesNotKeepExpiredJobClaimAlive` renews the coordinator independently, proves the live job is initially retained, then fences only the expired job claim and admits a new token/attempt without changing the coordinator epoch. |
| J29 | Partial | Decision-ledger sequence history is durable, but immutable logical-job attempt rows and exactly-one appended explicit retry are not directly proved. |

### Identity and deterministic-graph traceability

| ID | Coverage | Concrete source evidence and residual scope |
| --- | --- | --- |
| G01 | Direct | `GraphIdentityAndStateTests` covers stable File/Folder/Source/Document Set mechanics and path semantics; projection-builder tests cover Collection identity. |
| G02 | Partial | Identical projection and identity inputs are deterministic; shuffled and genuinely concurrent canonical-export execution is not separately exercised. |
| G03 | No targeted source | Cross-host canonical fixture comparison remains an Extended validation gate. |
| G04-G07 | Direct | Missing evidence, malformed/cyclic aliases, ambiguous candidates, and the adversarial false-merge corpus all fail closed without hidden merges. |
| G08-G10 | Partial | Manual/rejected relationship projection, never-merge decisions, legacy mirror replay, and decision replay after rebuild exist; every merge/split/alias/rejection persistence path across update, restart, repair, and rebuild is not complete. |
| G11 | Partial | Duplicate aliases/logical expansions and conflicting duplicate node identity fail closed; an explicit cryptographic-hash collision fixture was not located. |
| G12-G15 | Direct | Cyclic traversal, hostile Unicode/null/timestamps/sizes, malformed suggestion output, and confidence/explanation determinism have targeted security/unit tests. |
| G16 | Direct | Default suggestions remain inactive and provider-neutral contracts do not claim production Tag-node identity. |

### Invalidation, privacy, and repair traceability

| ID | Coverage | Concrete source evidence and residual scope |
| --- | --- | --- |
| I01 | Direct | `RenameAndMoveRetainStableFileIdentityAndInvalidatePathObservation` verifies stable file identity with path-only observation change. |
| I02-I04 | Partial | Move, metadata-only, and content-change identity tests exist; cross-source ambiguity and exact dependent-component invalidation are not exhaustively asserted. |
| I05 | Direct | Delete/tombstone and completed-manifest absence tests hide dependent observations conservatively. |
| I06 | Partial | Forget decisions, authoritative query barriers, and privacy backup floors are tested; every crash point of restrictive persistence is not. |
| I07-I08 | Direct | Exclusions suppress graph output, and snapshot/rebuild operations preserve original fixture bytes and timestamps. |
| I09 | Partial | Malformed publication rejection and selective repair exist; node/edge/evidence/alias/job corruption is not injected one record kind at a time. |
| I10-I11 | Partial | `ReviewedDerivedRecoveryQuarantinesCorruptionAndPreservesAuthority`, `JournaledDerivedRecoveryResumesAfterRestart`, and verified decision recovery preserve authority inputs. A complete v1.9-fallback/rebuild validation and the full Extended corruption/quarantine/restart matrices remain. |
| I12 | Partial | Generation retirement and graph clearing are bounded, but a targeted orphan-versus-active/manual cleanup corpus is not complete. |
| I13 | Partial | Completed manifests prove physical absence and interrupted rebuild avoids delete-first exposure; interrupted source-removal plus current point revalidation remains. |
| I14-I15 | Partial | Selective repair and restartable full rebuild preserve the old validated generation until atomic replacement; every scope, cancellation point, and quarantine failure remains. |
| I16-I18 | Partial | Updated-time collision, missed notification, persistent authority replacement, deletion, and stable-ID recreation have targeted tests; clock rollback/forward, update-behind-page, mutation-during-manifest, and all recreate orderings remain. |
| I19 | Direct | Provider tests retain the validated generation through interrupted selective/full rebuild, atomically promote the replacement, and bound superseded run/manifest history. |
| I20 | Partial | Ordered mirror/replay paging is resumable and idempotent; commit-before-inbox, out-of-order/duplicate delivery, and explicit sequence-gap withholding are not fault-injected as one matrix. |
| I21 | No targeted source | Post-import/rollback legacy schema-3 mutation reconciliation remains Extended compatibility work. |
| I22-I23 | Direct | Application query/Search tests recheck privacy/control/source revisions before and during reads, fail restrictive on unavailable authority, and discard stale expansions. |

### Resource, concurrency, Search, and UI traceability

| ID | Coverage | Concrete source evidence and residual scope |
| --- | --- | --- |
| R01-R02 | Partial | Existing synthetic indexing tests cover Ollama/OCR unavailable waits and ordinary Search fallback; graph-specific dependency restoration and exactly-once resume are not a complete matrix. |
| R03 | Partial | Quota pressure enters actionable resource wait and reviewed maintenance is invoked; low disk at backup/build/maintenance boundaries is not fully injected. |
| R04 | No targeted source | Injected `SQLITE_FULL` transaction rollback and replay remain Extended provider work. |
| R05 | Direct | `LockedGraphWrite_IsClassifiedBusyWithinFiniteDeadlineAndStoreRecovers` holds an independent SQLite writer, proves bounded `Busy` classification rather than corruption, releases it, and verifies the same store writes successfully. The two cancellation variants prove prompt bounded cancellation and atomic recovery for graph and decision writes. |
| R06-R07 | Direct | `SqliteGraphReaderConcurrencyTests` proves independent bounded reader admission while the writer gate is held, one old-or-new WAL snapshot across a concurrent publication, queued-operation disposal fencing, and deterministic database-file release. Application fallback and Search-order tests cover the consumer side. Sustained maintenance stress remains RC work. |
| R08-R10 | Direct | Bootstrap/stale/repair/disabled fallback, bounded evidence-backed expansion/opt-out, overlapping requests, and cancellation have targeted tests. |
| R11 | Partial | High-degree expansion and multi-scale provider/Application regressions are bounded; a reviewed larger graph envelope remains Performance work. |
| R12 | Direct | Depth, visited, edge, time/input, and high-degree ceilings terminate hostile expansion deterministically. |
| R13-R14 | Partial | Projection cancellation latency, database-busy cancellation, and multi-scale cost ceilings exist; every stage/query/shutdown threshold and allocation envelope is not measured. |
| R15-R18 | Direct | Paging/virtualization, asynchronous startup and cancellable service work, XAML/ViewModel accessibility, and diagnostic redaction have targeted tests. Manual accessibility observations remain unchecked. |
| R19 | Partial | Redacted diagnostic aggregates, progress, and failure actionability are tested; the full lease/cursor/lag/busy/low-disk fact set is not. |
| R20 | Partial | Quota-blocked ingestion preserves the watermark, but row and byte limits below/at/above both in-memory and durable queue ceilings are not parameterized. |
| R21 | Partial | Oversized/malformed projection is rejected atomically; merge/split/cascade row, byte, time, and allocation boundaries are not covered. |
| R22 | Partial | Eco/Balanced/Fast concurrency, an unavailable adapter, and time-window eligibility are tested; idle/power/battery combinations and exactly-once restoration are incomplete. |
| R23 | Partial | Quota/free-space preflight and active-generation preservation exist for rebuild; injected disk-full at decision commit, WAL checkpoint, backup/restore, and activation remains Extended. |
| R24 | No targeted source | No deterministic decision/graph/Search/maintenance lock-order stress fixture was located. |
| R25 | Partial | Independent-lock tests prove the finite ordinary deadline, prompt cancellable slices, correct `Busy`/cancellation classification, atomicity, and recovery. A fully parameterized below/at/above cumulative-threshold matrix remains RC work. |
| R26 | Partial | Recovery reserve and bounded run/manifest history are covered; inbox retention age/row boundaries and all quota-retention invariants are not. |
| R27 | Partial | Alias, evidence, query, degree, traversal, and maximum-candidate bounded identifier text are targeted; CPU-yield ceilings plus every first-exceeded-bound ordering are not. |
| R28 | Partial | Combined decision-ledger quota failure leaves the ledger unchanged; reserve boundaries and actionable ViewModel status are not fully parameterized. |
| R29 | Direct | Concurrent authority-unavailable expansion fails restrictive while bounded ordinary exact Search remains responsive. |

### Remaining release-candidate gaps identified by the audit

The current source mapping does **not** justify a release-ready claim. Highest
risk remaining work is:

- true two-process lifecycle ownership and abandoned-lock recovery (M14-M15);
- the residual delayed-heartbeat/token-ABA schedule in J19, plus the remaining
  every-boundary crash/shutdown/race matrices in J16, J18, J20, and
  J23-J27;
- stable-ID generation behavior under clock change, concurrent mutation, and
  recreate orderings (I13, I16-I18), ordered decision-gap withholding (I20),
  and rollback-era legacy reconciliation (I21);
- sustained concurrent maintenance/read stress beyond the direct R06-R07 WAL
  snapshot and admission tests; and
- parameterized queue/transaction/reserve/decision-quota boundaries and
  lock-order stress (R20-R28).

Those rows remain Extended or release-candidate gates exactly as specified;
manual observation cannot replace them.

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
