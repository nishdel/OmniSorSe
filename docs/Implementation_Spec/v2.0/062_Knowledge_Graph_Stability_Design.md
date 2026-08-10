# 062 — Knowledge Graph stability design

## Status

Accepted design target implemented as an unmerged v2.0 source candidate.
Source and tests define exact behavior. Clean local automated validation is
complete; exact-tip hosted, RC stabilization, and interactive validation remain
separate incomplete gates.

## Required contracts

The candidate Application layer defines product operations rather than storage
mechanics:

- `IGraphProjectionSource`: bounded, ordered snapshots/change pages from v1.9
  files, relationships, collections, privacy, decisions, and deletion state;
- `IGraphProjectionCoordinator`: scheduling, state transitions, pause/resume,
  cancellation, retry, reconciliation, and selective rebuild;
- `IGraphStore`: schema lifecycle, work claims, staged component replacement,
  bounded query, integrity, backup, repair, cleanup, and diagnostics;
- `IGraphDecisionStore`: atomic manual entities, aliases, merge/split,
  edge/rejection, exclusion, and retention decisions;
- `IGraphIdentityResolver`: deterministic conservative candidate resolution;
- `IGraphQueryService`: paged nodes/edges/evidence/coverage with bounded
  traversal;
- `IGraphSearchSource`: optional bounded expansion carrying actual ranking
  evidence;
- `IGraphPrivacyService` and `IGraphRepairService`: inspect, forget, verify,
  repair, and rebuild without source-file mutation.

Views and ViewModels must not open databases, construct SQL, resolve identity,
calculate graph confidence, call OCR/Ollama, or traverse raw provider data.

## Data ownership

| Data | Authority | Rebuildable |
| --- | --- | --- |
| Indexed files/content/coverage/privacy | Existing v1.9 index owners | Per existing contracts |
| File relationships/Smart Collections | Existing v1.9 relationship owner | Per existing contracts |
| Manual graph entities and decisions | `knowledge-decisions.db` | No; preserve/backup |
| Graph nodes/edges/evidence references | Graph projection store | Yes |
| Graph jobs/cursors/diagnostics | Graph projection store | Yes |
| Original user files | User/filesystem | Never graph-owned |

Existing v1.9 file-relationship and Smart Collection decisions remain
authoritative in schema 3 and are mutated only through their existing services.
Any graph-side import is a non-authoritative, revisioned observation that must
follow later legacy updates and removals. `knowledge-decisions.db` is
authoritative only for graph-native manual entities, aliases, compatible-kind
merge/split decisions, graph-only edges, suggestion decisions, and graph
exclusions. No user action writes the same logical decision independently to
both owners.

Current v1.9 inputs must be interpreted conservatively: relationship
`MetadataText` from the SQLite projection is a metadata fingerprint, accepted
tags are not populated into production relationship candidates, Workflow
evidence is declared but not emitted, extracted/OCR matching is whole-field
fingerprint equality, and the current relationship timeline uses modification
time. A future graph source must introduce a truthful provider-neutral
observation for a missing signal or leave that node/evidence unavailable.

## Durable projection stages

1. `ObservationCaptured` — idempotently persist an observation/inbox key,
   source stable key, canonical source fingerprint, decision revision, and
   algorithm configuration. Ingestion acknowledgement is not publication.
2. `CandidatesExtracted` — derive bounded candidates from already-indexed
   fields; never reopen the source file.
3. `CandidatesNormalized` — validate Unicode, types, timestamps, aliases, and
   length/count ceilings.
4. `IdentityResolved` — apply deterministic automatic rules followed by the
   authoritative manual-decision overlay.
5. `EdgesPrepared` — build bounded typed edges with resolvable evidence.
6. `ComponentValidated` — check referential integrity, collision inputs,
   decision conflicts, privacy, and graph ceilings.
7. `ComponentPublished` — revalidate the current source/decision revision,
   replace the affected component, and advance that component's applied
   revision in one short graph-store transaction.
8. `StaleRowsCleaned` — retire superseded rows and bounded operational history.

Non-applicable stages complete with an explicit reason. Dependency/resource
waits do not erase prior valid graph data. Stage output and publication are
idempotent by `(work key, source fingerprint, decision revision, algorithm
version)`.

## State model

The required product states are a projection of four orthogonal persisted axes,
not one enum that permits impossible combinations:

| Axis | Persisted values |
| --- | --- |
| Run control | `Pending`, `Running`, `PauseRequested`, `Paused`, `CancelRequested`, `Cancelled`, `Complete` |
| Job execution | `Pending`, `Running`, `Complete`, `Cancelled`, `RetryableFailure`, `PermanentFailure`, `WaitingForDependency`, `WaitingForResources` |
| Component freshness | Current or `Stale` |
| Component integrity | Valid or `RepairRequired` |

Run control does not rewrite every job when paused. `Stale` never masquerades as
a job failure, and `RepairRequired` does not erase the last valid freshness or
run-control state. The UI maps the axes to one primary status plus disclosed
secondary conditions.

`PauseRequested` stops new claims and reaches `Paused` only after active claims
publish or persist a resumable boundary. `CancelRequested` is durably recorded
before cooperative cancellation is signalled; it reaches `Cancelled` only when
active claims have acknowledged cancellation or have been fenced. A concurrent
privacy decision wins over publish, cancellation wins over pause, and a newer
source/decision revision makes older staged output stale rather than complete.

| State | Meaning | Exit rule |
| --- | --- | --- |
| `Pending` | Durable work exists but is not claimed. | Eligible scheduler claim or explicit cancellation. |
| `Running` | One lease/token owns the attempt. | Atomic completion, wait, failure, cancellation acknowledgement, or lease recovery. |
| `Complete` | The stage output was validated and, where applicable, published. | New source/config/decision fingerprint creates stale work; state is not rewritten in place. |
| `Paused` | The run accepts no new claims. | Explicit resume or cancellation. |
| `Cancelled` | Cooperative cancellation was durably acknowledged. | Explicit retry creates a new attempt. |
| `RetryableFailure` | A classified transient failure retained attempt/backoff facts. | Retry time plus policy gate, or explicit retry/cancel. |
| `PermanentFailure` | Unchanged input cannot safely succeed. | Input/config/algorithm change or explicit reviewed repair. |
| `WaitingForDependency` | Optional provider input is unavailable. | Dependency health transition or user disables the optional stage. |
| `WaitingForResources` | Disk/quota/power/time-window/busy policy prevents work. | Resource gate clears or user changes policy. |
| `Stale` | Published data remains readable but no longer matches source/decision version. | Successful replacement, forgetting, or repair escalation. |
| `RepairRequired` | Integrity/ambiguity prevents safe automatic action. | Verified selective repair or reviewed decision. |

Allowed transitions and cross-axis combinations are explicit and tested.
Unknown persisted values are corruption, not `Pending`. Expired `Running`
leases recover on startup; no heartbeat-free claim can remain indefinitely
active, and a late publisher holding an obsolete fencing epoch is rejected.

## Identity and evidence invariants

1. Existing File and Collection IDs are reused, never reinterpreted.
2. Automatic mechanical keys include type, scope, normalization version, and
   an existing stable ID, exact folder key, or exact content hash. Accepted Tag
   nodes are deferred until their owner exposes a provider-neutral stable
   identity and rename/move reconciliation contract.
3. Real-world Project, Organization, Purchase, Trip, Person, Place, Event, and
   Topic candidates do not merge automatically in the stable scope; an
   experimental suggestion requires confirmation.
4. Ambiguous, oversized, malformed, or contradictory buckets do not merge.
5. Manual decisions apply after automatic rules and always win.
6. Every automatic edge has at least one resolvable retained evidence record.
7. Semantic/model output can suggest but cannot establish identity or fact.
8. Confidence is a deterministic level derived from named evidence rules, not
   a fabricated probability.
9. Algorithm/version changes stale only affected derived components.
10. Existing `SameProject`, `SamePurchase`, `SameTrip`, and `SameTopic` edges
    and Collection context metadata do not establish separate real-world
    entity identity.
11. Merge/split applies only to compatible manual or experimental entity
    candidates. Mechanical File, Source, Folder, Collection, and Document Set
    identities are linked or unlinked, never merged across kinds.
12. Repeating projection with identical source and decision snapshots produces
    byte-equivalent canonical serialization/export of nodes and edges,
    excluding documented operational timestamps; SQLite file bytes are not the
    comparison oracle.

## Query and Search rules

- Stable UI queries are paged list/detail and direct-neighbor operations.
- Stable traversal depth is one; optional experimental depth two retains a
  visited set and hard node/edge/time ceilings.
- Cycles are valid graph structure and never cause recursive unbounded work.
- Search graph expansion is optional per request, one hop, and lower priority
  than exact/literal v1.8/v1.9 results.
- Existing v1.9 relationship context and graph context have separate visible
  controls. Disabling graph context returns the same eligible result set and
  explanations as v1.9 without disabling direct relationship context.
- Both supplements share at most 16 original ranked seed File IDs and one
  combined ceiling of 100 contextual candidates. Graph-only expansion may use
  at most 50 places within that ceiling and cannot seed from a relationship-
  expanded result.
- Results are deduplicated by File ID before final ranking. The same contextual
  relationship projected through both sources contributes at most one score
  component and one explanation; existing direct relationship evidence wins,
  otherwise the strongest deterministic graph evidence is used.
- Explanations come from the same graph edge/evidence used to expand; no reason
  is generated after ranking.
- Deep-index coverage and graph-projection coverage are displayed and diagnosed
  separately. Neither is used as a proxy for the other. No-result states do not
  imply exhaustive graph coverage when projection is incomplete, and graph
  incompleteness does not diminish already-complete literal-index coverage.
- Graph query cancellation and timeout return no corrupt partial result
  objects. Existing Search results may still be returned with graph-unavailable
  coverage.
- If current privacy/decision authority cannot be validated, graph reads and
  Search expansion fail closed; ordinary eligible v1.9 Search remains usable.

## UI and accessibility rules

- Stable UI is paged/virtualized overview, list, detail, evidence, privacy, and
  repair; it never materializes the complete graph or accessibility tree.
- Every action works by keyboard and click/touch without hover dependence, with
  meaningful names, roles, states, errors, and confirmations.
- Focus is restored deterministically after paging, async refresh, merge,
  split, forget, repair, or removal; superseded async responses cannot replace
  a newer selection.
- Confidence, freshness, failure, and limit states are not communicated by
  color alone. High contrast and text scaling retain all content and controls.
- Index-only destructive actions state exactly what application-owned data is
  affected and that original files remain unchanged.
- Live announcements are coalesced to meaningful transitions.

## Privacy and safety invariants

- No graph contract exposes source-file write operations.
- Existing source/file exclusion and relationship suppression is applied
  before candidate extraction and again before publication/query.
- Forget removes or suppresses graph-owned data and queues orphan cleanup; it
  never deletes a source, watched-folder registration, scan, or file.
- The decision ledger contains bounded labels/keys and decisions, not document
  bodies, OCR text, summaries, prompts, or vectors.
- Sidecars, decision backups, labels, aliases, evidence references, and
  quarantine copies remain sensitive local metadata. No encryption-at-rest is
  claimed.
- Disable retains graph-owned data but stops graph work/query. Clearing derived
  graph data is distinct from irreversibly clearing graph-native decisions and
  their backups; both require accessible confirmation and leave the graph
  disabled until explicitly enabled again.
- A forget is complete only after the current privacy sequence and a verified
  post-decision recovery point prevent an older backup from resurrecting the
  forgotten data. Minimum retained tombstone keys and their retention are
  disclosed.
- A corrupt/unvalidated decision store disables graph reads, expansion, and
  mutation unless a validated matching decision snapshot exists. It never
  degrades to a readable graph that may omit a privacy decision.
- Diagnostics are aggregate/redacted by default and exports remain reviewable.
- Ollama and OCR are never required for filename, metadata, relationship,
  collection, manual graph, or ordinary Search behavior.

## Compatibility requirements

- `deep-index.db` stays at v1.9 schema 3 for this design.
- First graph bootstrap is a background projection, not an in-place migration
  of indexed content.
- Existing JSON stores, catalogs, scans, watchers, duplicates, workflows,
  plugins, Change Plans, journal/recovery, and Undo are unchanged.
- Existing v1.9 relationship/Collection decisions retain their sole authority;
  graph-native decisions do not silently change v1.9 behavior during rollback.
- Public contracts are additive. No stable plugin graph API ships in the first
  release unless separately reviewed and versioned.
- v1.9 can ignore the graph sidecar and continue operating. Older v1.7/v1.8
  downgrade limits that already exist for schema 3 are documented honestly.

## Completion rule

The implementation is not complete until every release-blocking automated
gate and the manual RC checklist pass at an exact commit. Scope must be reduced
if bounds, deterministic identity, migration recovery, correction
preservation, Search fallback, or UI responsiveness cannot be demonstrated.
