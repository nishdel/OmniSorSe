# Implementation history

This cumulative technical record preserves implementation decisions and owner reports, including incomplete candidate checkpoints. [Architecture Overview](ARCHITECTURE_OVERVIEW.md) owns the living model; [Release Status](RELEASE_STATUS.md) owns readiness. Exact snapshots remain in Git and CI.

## Chapter index

- [v3.0.0](#implementation-v3-0-0): Published candidate and visual follow-up — 2026-10-08; Historical implementation checkpoint.
- [v2.0](#implementation-v2-0): Repository basis; Implemented architecture; Persistence and recovery; Stable product behavior; Security, privacy, and performance work; Compatibility and deferred work.
- [v1.9](#implementation-v1-9): Release identity and base; Discovery and reuse; Application architecture; Durable pipeline and persistence; Smart Collections, context, and user control; Search integration.
- [v1.8](#implementation-v1-8): Basis and scope; Reused v1.7 components; New application components; Provider and migration; Search and UI behavior; Privacy, security, and AI-optional behavior.
- [v1.7](#implementation-v1-7): Repository basis; Implementation; Reliability defects found during integration; Deliberate limits.
- [v1.6](#implementation-v1-6): Baseline audit; Interrupted-session recovery; Implemented production changes; Automated coverage added; API and schema review.

## Version history


<a id="implementation-v3-0-0"></a>
## v3.0.0

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/OWNER_REPORT_v3.0.0.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="implementation-v3-0-0-published-candidate-and-visual-follow-up--2026-10-08"></a>
#### Published candidate and visual follow-up — 2026-10-08

PR #54 merged as `df3984fab5eaf94424ec6cd032e91468799d62d6`; the matching
v3.0.0-rc.1 prerelease is published. The outstanding visual documentation is
addressed by [PR #55](https://github.com/nishdel/OmniSorSe/pull/55): seven real
synthetic-data captures in the [README gallery](../README.md#v3-screenshot-gallery),
with [source/harness hashes and evidence](images/README.md#v3-capture-provenance).
The architecture and Search diagrams render on GitHub. Production source,
schema, protocol, published tag and binaries are unchanged by this follow-up.
The [living release status](RELEASE_STATUS.md) owns current validation evidence;
PR #55 records its final documentation commit, checks and merge identity. All
24 human acceptance rows remain Not run. The earlier implementation report below
is preserved at its original checkpoint and does not supersede those records.

<a id="implementation-v3-0-0-historical-implementation-checkpoint"></a>
#### Historical implementation checkpoint

The task is to implement the agreed 38-item milestone and publish an installable
major-version testing candidate, including learned-vector hybrid Search before
the first v3 RC. The current code adds progressive local-AI
index enrichment, existing-library enablement, automatic inferred Search and
relationship evidence, an editable Organize page and storage controls. Richer
image/audio/video understanding remains explicitly future-facing.

The existing SQLite index and proposal/executor pipeline retain authority.
Enrichment changes application-owned data only; inferred output is structurally
validated and labeled, while file moves still require reviewed approval and retain
History/Undo. Schema 8 retains per-source AI policy and adds disposable,
model-versioned vector tables inside the existing SQLite provider. Independent
keyword and semantic retrieval use reciprocal rank fusion with exact-filename
priority; Related Files presents similarity separately from retained evidence.
Storage relocation uses a locked,
verified copy and atomic receipt; old generations remain recovery material.

Verified local evidence and actual OmniLAB contributions/fallbacks are in
[Validation](VALIDATION.md#validation-v3-0-0). Independent review corrected OCR suppression,
durable preference eviction and storage recovery gaps. Candidate lessons are in
[the retrospective](engineering/RETROSPECTIVE_v3.0.0.md); none was self-promoted.
Human acceptance remains [Not run](MANUAL_TESTING.md#manual-v3-0).

The beginner guide, Organize/storage instructions, current architecture and system
map, contributor/agent boundaries, compatibility, release notes and acceptance map
were updated. The [release status](RELEASE_STATUS.md) owns current integration,
package and publication evidence rather than this pre-release implementation note.

The earlier enrichment/Organize/storage implementation was committed and pushed
on `codex/v3-progressive-enrichment` in
[PR #54](https://github.com/nishdel/OmniSorSe/pull/54). The learned-vector
continuation has further working-tree changes under validation; this report
does not claim those changes are committed, pushed or merged. The recorded
integration baseline is main `727ce2d09ce9870f6e6ce9e4c3baa467c7e4d5de`;
the final remote-main SHA remains unassigned. The original dirty worktree was
preserved. Public Explorer Protocol stays 1.0; assembly/profile identities stay
compatible. In-place downgrade is unsupported after schema/storage migration.

Earlier evidence: Windows hosted validation at `c6999de` passed all 1,957 tests in both
configurations and every gate; actual local-AI and native Windows smoke also
passed. Remaining uncertainty: final macOS/Linux validation, installer lifecycle,
and human acceptance. Native macOS failures and the concurrent GitHub Actions
runner incident are recorded in the validation report. OmniLAB provided useful
safety/recovery advice but mostly unusable implementation output; direct Codex
fallback produced the majority of finished code.

The 2026-10-07 continuation adds modular embedding/vector contracts, the local
Ollama embedding provider, background incremental refresh, model/privacy/freshness
gates, vector storage controls, search explanations and explicit fallback. Code
review corrected the existing 100-ID hydration bound and a legacy-path/stable-ID
duplicate in fusion; both have targeted regressions. Further independent review
corrected shell wiring, rapid-selection state, freshness after asynchronous work,
and timeout/failure fallback preserving valid lexical results. Learned RRF now
uses a lexical-only input rather than giving feature-hash similarity another vote.
The continuation's documentation includes the hybrid query diagram, SQLite/vector
authority, model controls and coverage limits. All 24 human checklist rows remain
Not run. Specific continuation OmniLAB contributions and rejected claims are
recorded with request IDs in Validation.

The central runner progressed from 215 focused tests to **2,069 passing tests in
each of Debug and Release**, zero failed/skipped, with all three formatting checks
and the vulnerability audit passing. After three later lexical-ranking regressions,
the complete rerun passed **2,072 tests in each configuration**, zero failed/skipped,
with zero-warning/error builds and all whitespace/style/analyzer checks passing.
The earlier online audit found zero vulnerabilities; dependencies were unchanged.
This supersedes the 2,069-test checkpoint while remaining working-tree evidence.

The real Ollama `qwen3-embedding:4b` benchmark initially failed its unchanged
relevance gates (Recall@5 0.857143, zero paraphrase gains). A narrow function-word
correction in the lexical fusion input then passed: Recall@5 1.0, MRR 0.928571,
one paraphrase gain, exact filename first, and 564.9 ms reported upper-median query
latency. All source-integrity, restart, one-file incremental, unavailable-model
fallback and nonempty similarity-suggestion gates passed. These measurements cover
12 synthetic files and seven judged queries, not arbitrary libraries. Both JSON
reports and the exact model digest are recorded in [Validation](VALIDATION.md#validation-v3-0-0).

The rebuilt self-contained Windows production startup/shutdown smoke also passed
with an isolated profile. Benchmark and smoke use
`da5b313813fe47007913c6ef7bafb8ed15bcce92-working-tree-20261007`; this identifies
the working-tree base, not a final commit containing the changes. The smoke is
not installer, interactive or screenshot evidence.

Required real screenshots remain an incomplete deliverable and explicit blocker
to full task completion. Following the earlier permission wait and two native
capture timeouts, two retries with a visible rebuilt window timed out again.
The final self-contained launch through the computer-use launcher triggered a new
app-access approval that timed out; no screenshot was captured.
Provider/chunker/document-draft OmniLAB requests produced no usable
implementation, and a separate schema-review escalation was rejected by automatic
approval review. Codex did not work around that rejection. Direct fallback still
accounts for most finished code.

Status at this source checkpoint: prerelease integration pending, with final
hosted/release gates and real screenshots incomplete. Exact merged source,
tag, installer assets and checksums must be verified on the official
[v3 release page](https://github.com/nishdel/OmniSorSe/releases/tag/v3.0.0-rc.1)
and [issue #53](https://github.com/nishdel/OmniSorSe/issues/53) when available.
Begin human testing only after the merged v3 source and matching published
installer are available; historical v2.13 packages are not that test target.

</details>

<a id="implementation-v2-0"></a>
## v2.0

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/V2.0_IMPLEMENTATION_REPORT.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

**Release:** Knowledge Graph

**Branch:** `v2.0-knowledge-graph`

**State:** implementation candidate; unmerged; local automated validation
complete; exact-tip hosted validation, release-candidate stabilization, and
interactive validation are separate gates

<a id="implementation-v2-0-repository-basis"></a>
#### Repository basis

The branch was created directly from the validated
`v2.0-knowledge-graph-design` tip
`a2a9a071600de74759937f05a7be61f85e9d5d93`. That design branch was itself
based on the validated v1.9 line. The v1.7, v1.8, v1.9, and v2.0 design branches
remain unchanged and unmerged from `main`.

Development was later recovered after storage failure. The copied Git object
database failed full integrity validation, so committed history was restored
from the authoritative remote into a clean clone. The previously inventoried
64 tracked modifications and 71 intended untracked implementation files were
then compared by path and content and transplanted onto the exact design tip.
No zero-byte, truncated, conflicted, generated-database, result, log, binary, or
machine-path implementation artifact was accepted as source.

<a id="implementation-v2-0-implemented-architecture"></a>
#### Implemented architecture

- `OpenSorSe.Application.KnowledgeGraph` defines provider-neutral identities,
  projection records, four-axis state, queries, decisions, privacy, repair,
  diagnostics, suggestion validation, Search context, and lifecycle contracts.
- A conservative identity resolver and deterministic projection builder reuse
  stable schema-3 file/source/relationship/collection facts. They do not infer
  identity from a merely similar label or semantic result.
- A durable coordinator ingests only completed, hashed, row-counted manifests;
  queues idempotent work; uses epoch/token fencing; validates source and
  decision authority before publication; and supports pause, resume,
  cancellation, retry, restart recovery, and bounded shutdown.
- `OpenSorSe.Indexing.Sqlite.KnowledgeGraph` isolates two schema-1 providers:
  the rebuildable graph sidecar and the non-rebuildable decision sidecar. The
  existing schema-3 deep index is accessed through an adapter and is unchanged.
- Application query, Search, privacy, repair, decision, and diagnostics services
  revalidate authority and bounds independently of the desktop.
- Desktop MVVM exposes a paged Knowledge Graph surface and adds an independent,
  accessible Search-context option and coverage message. Views do not know
  SQLite details.

<a id="implementation-v2-0-persistence-and-recovery"></a>
#### Persistence and recovery

`knowledge-graph.db` contains derived manifests, inbox rows, jobs, generations,
nodes, edges, evidence, aliases, component state, watermarks, maintenance, and
bounded diagnostics. It may be repaired or rebuilt from authoritative input.

`knowledge-decisions.db` contains append-only graph-native decisions,
checkpoints, materialized manual entities/aliases, exclusions, and verified
recovery metadata. It is not treated as disposable. Existing schema-3 v1.9
decisions remain authoritative; their graph mirror is reconciled projection.

The implementation candidate adds a provider-neutral
`IGraphDecisionRecoveryService` maintainer/integration path; it does not claim
an end-user restore button. Recovery-point enumeration is bounded and
privacy-safe. Restore requires `RESTORE GRAPH DECISIONS`, verifies the selected
copy again, rejects corrupt/foreign/newer-schema and privacy-floor-stale points,
and promotes through a durable same-volume journal that is resolved on the next
initialization after interruption. Source files and `deep-index.db` remain
untouched. Focused application and SQLite-provider tests cover confirmation,
valid restore, missing/corrupt/foreign/newer/stale points, and interrupted
promotion recovery; those tests are implementation evidence, not manual or RC
approval.

The candidate also adds `IGraphDerivedStoreRecoveryService` for reviewed
maintainer/integration recovery of a corrupt rebuildable sidecar; no end-user
button is claimed. Exact confirmation starts a journaled same-volume quarantine
and promotion of a validated empty graph store. The provider validates the
decision ledger before and after, resumes a journaled promotion after restart,
and rejects healthy, decision-corrupt, and unsupported-newer cases without
replacement. Focused tests verify quarantine retention and byte-for-byte
preservation of the decision store, synthetic `deep-index.db`, and synthetic
source fixture.

`RelationshipGraphAuthorityBridge` closes the legacy command boundary without
creating a second authority. Projected v1.9 Related File unlink/reject actions
resolve the exact canonical file pair and commit through `IRelationshipService`;
projected Smart Collection membership removals use the existing split-member
override. A successful legacy commit schedules graph reconciliation and no
graph-native ledger entry is appended. Missing, ambiguous, stale, unsupported,
or unavailable legacy authority fails closed. Other v1.9 collection controls
remain on the existing Collections surface, while structural graph edges are
inspect-only.

Schema initialization and validation are transactional. Application IDs,
schema markers, migration history, foreign-key/integrity settings, unsupported
newer-version detection, and corruption categories prevent ambiguous opening.
Cross-store lifecycle operations use an outer lock instead of pretending that
SQLite can provide one nested atomic transaction across independent files.

Projection records distinct ingestion and applied watermarks for source,
decision, and privacy authority. Completed generations are swapped atomically.
Claims heartbeat every 5 seconds, expire after 30 seconds, and use epoch plus
claim-token fencing. Shutdown allows a 5-second cooperative grace interval;
expired claims become resumable on startup.

The recovery audit corrected the implementation-candidate blockers without
changing either schema. Graph/decision connections are short-lived,
non-pooled, and owned by their operation; read snapshots use a separate bounded
WAL admission path. Disposal first fences new work, drains admitted readers and
writers, lets already-queued calls observe the disposed state, and releases all
database handles deterministically. Source-adapter disposal follows the same
rule. No global SQLite pool clear can invalidate another live store.

Completed-manifest pruning now runs only after the durable completion watermark
advances, retaining the active/recovery generations while enforcing the
documented one-or-two-manifest ceiling. Cancellation is a durable terminal
intent: later pause/resume cannot revive a cancelling run, repeated cancellation
is safe, restart acknowledges pending cancellation, and old epochs/tokens cannot
publish. Coordinator and job heartbeats remain independent, and forward/backward
clock tests prove fencing epochs remain monotonic.

Exact-tip hosted validation then exposed a Unix-only source-stamp defect: raw
header reads could conflict with an otherwise valid SQLite handle on macOS and
Linux. Source replacement detection now uses the existing cross-platform file
identity provider plus bounded file/WAL metadata instead of opening database
bytes outside SQLite. Linux retains native device/inode identity, Windows uses
volume/file identity, and macOS uses the documented metadata fallback. This
preserves replacement detection without competing with SQLite locking.
The associated regression fixture now builds its replacement through SQLite's
backup API, removes obsolete WAL/SHM sidecars, and assigns an observable
metadata stamp; it no longer models an invalid main-file-only WAL copy as a
healthy replacement.

A later exact-tip Windows run exposed a separate scheduler-contention defect in
the deterministic relationship identifier scanner. A 50 ms wall-clock regex
timeout could reject an otherwise valid bounded 4 KiB input while the runner
was busy. Identifier and version patterns now use the linear-time
non-backtracking engine over the existing hard input bound, and each target
identifier is computed once per discovery pass. A synthetic maximum-candidate
near-match regression covers deterministic completion without weakening the
input or candidate ceilings.

<a id="implementation-v2-0-stable-product-behavior"></a>
#### Stable product behavior

The implemented stable node set is File, Source, Folder, Collection, Document
Set, and Manual Entity. The stable edge set is Related File, Owned by Source,
Located in Folder, Member Of, Same Document Set, and Manual. Every visible edge
has current retained evidence and a deterministic confidence level.

Knowledge Graph is disabled by default. It never opens source files, initiates
OCR/AI, moves files, or extends the Change Plan executor. Graph-only privacy and
repair actions explicitly state that original files are unaffected.

Search expansion is opt-out and bounded: at most 16 ordinary seeds, 50
graph-only expansions, 100 contextual expansions shared with v1.9, and one
stable hop. Existing exact/literal ranking and direct v1.9 relationship
authority remain higher. Graph failure produces a coverage message and ordinary
Search fallback, not a failed Search.

<a id="implementation-v2-0-security-privacy-and-performance-work"></a>
#### Security, privacy, and performance work

Stable identifiers, labels, evidence, aliases, pages, candidate buckets,
components, traversals, retries, suggestions, and Search expansions have hard
ceilings. SQL remains parameterized inside the provider. Generation validation,
cycle/self-edge constraints, duplicate canonicalization, stale/integrity
filtering, cancellation, busy/corruption classification, and privacy-floor
recovery protect hostile or damaged input paths.

SQLite write contention has a finite cumulative deadline. Operations with a
cooperative cancellation token use short native busy slices and retry the whole
transaction only within that deadline, allowing cancellation between slices.
Provider tests hold independent writer locks to prove prompt graph and decision
cancellation, unchanged decision state, bounded `Busy` classification, and
successful reuse after the lock is released.

Diagnostics expose bounded IDs, states, watermarks, categories, and aggregate
counts/timings. They omit source content, OCR text, complete queries, model
payloads, and unnecessary paths by default. Optional-suggestion contracts are
treated as untrusted, bounded, disabled by default, and non-authoritative. No
live producer is wired, and validation alone cannot establish identity.

Incremental fingerprints avoid rebuilding unchanged components. Completed
manifests use bounded paging, candidate generation is bucketed, queries are
paged, stable traversal is one hop/100 nodes, and Search expansion is bounded.
The final reproducible local performance gates passed 9 Application and 5
SQLite regression tests. These bounded synthetic checks guard candidate
selection, projection, query, and Search costs; they are not universal latency
or scale claims.

<a id="implementation-v2-0-compatibility-and-deferred-work"></a>
#### Compatibility and deferred work

The implementation preserves `deep-index.db` schema 3 and existing catalogs,
saved scans, watchers, duplicate detection, workflows, plugins, Change Plans,
Operation Journal, recovery, Undo, v1.8 Search, and v1.9 relationships and
Collections. The graph is an isolated optional projection and can be disabled
without removing those capabilities.

Tag nodes; live entity-suggestion generation; automatic Person, Place, Event,
and Topic identity; unrestricted or
multi-hop stable browsing; a graph canvas; remote providers; synchronization;
conversation; and autonomous action remain deferred. Project, Purchase, Trip,
and Organization suggestion contracts remain experimental, unwired, and
confirmation-gated by design.

<a id="implementation-v2-0-validation-state"></a>
#### Validation state

The final clean local run passed non-incremental Debug and Release builds with
zero warnings/errors and 1,468 tests in each configuration with zero failures
or skips. Search, Knowledge Graph, performance, formatting/analyzer, policy,
vulnerability, patch, and four-runtime cross-target gates also passed. Exact
evidence is recorded in
[VALIDATION.md#validation-v2-0](VALIDATION.md#validation-v2-0). The commit, push, and
exact-tip hosted run are post-commit evidence for the final handoff. Every
interactive scenario and RC/release-readiness gate remains unchecked.

</details>

<a id="implementation-v1-9"></a>
## v1.9

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/V1.9_IMPLEMENTATION_REPORT.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="implementation-v1-9-release-identity-and-base"></a>
#### Release identity and base

- Release: **OpenSorSe v1.9 — Relationships, Context & Smart Collections**
- Branch: `v1.9-relationships-context`
- Exact base: validated v1.8 branch tip
  `01899f9701f58d3bf2e5c0eaadc5c87efe68ea2d`
- Integration status: v1.7, v1.8, and v1.9 remain unmerged from `main`.
- Manual status: no interactive scenario is claimed; the v1.9 checklist is
  intentionally fully unchecked.

<a id="implementation-v1-9-discovery-and-reuse"></a>
#### Discovery and reuse

The v1.8 tree already provided provider-neutral durable indexing, schema-2
SQLite storage, a reserved no-op `RelationshipAnalysisCompleted` stage,
retained metadata/text/OCR/summary/keywords/chunks/semantic data, hybrid
exact-first Search, explanations, progressive coverage, privacy/forget/repair,
diagnostics, MVVM composition, and extensive tests. It did not persist actual
relationships, evidence, collections, memberships, corrections, or timelines,
and no relationship data affected Search or the desktop UI.

v1.9 reuses those systems. It does not create another indexing coordinator,
Search engine, extraction path, OCR/AI client, file identity, source-ownership
model, or source-file operation route.

<a id="implementation-v1-9-application-architecture"></a>
#### Application architecture

`OpenSorSe.Application.Relationships` adds provider-neutral records and four
boundaries:

- `IRelationshipEngine` for deterministic feature extraction and discovery;
- `IRelationshipStore` for provider-independent durable operations;
- `IRelationshipService` for configuration, analysis, inspection, manual
  control, privacy, rebuild, repair, and diagnostics;
- `IRelationshipSearchSource` for optional bounded Search expansion.

The deterministic engine retains the precise evidence used, algorithm/version,
created/validated times, user decision, and Low/Medium/High/Confirmed
confidence. Duplicate hashes, document identifiers, specific filename terms,
text/OCR/summary fingerprints, accepted tags, keywords, folder/time
corroboration, and corroborating semantic similarity are supported. Semantic
similarity or shared folder alone cannot create an automatic edge.

Automatic inference currently classifies a conservative subset of the full
extensible categories. Same Person, Same Organization, Same Event, Manual, and
Custom remain available for explicit user links rather than being guessed from
weak evidence.

<a id="implementation-v1-9-durable-pipeline-and-persistence"></a>
#### Durable pipeline and persistence

The existing relationship pipeline stage now calls the relationship service.
Its processor fingerprint includes enablement, exclusions, candidate/edge/
member policy, and algorithm-sensitive configuration. Completed compatible
work remains reusable through the v1.7 durable job model.

SQLite schema 3 adds versioned feature rows, canonical relationship edges,
evidence, pair overrides, Smart Collections, membership, member exclusions,
forgotten-collection tombstones, aggregate diagnostics, and per-file
relationship suppression. Migration from the exact v1.8 schema 2 uses the
existing transaction, recovery copy, integrity check, and unsupported-newer-
schema rejection lifecycle. Foreign keys, parameterized commands, disposal,
and repair/cleanup retain provider ownership.

Candidate selection uses indexed bounded keys; each pass caps candidates,
relationships, and evidence. Collection insertion and manual merge cap
membership. Queries are direct and non-recursive, preventing cyclic traversal
and relationship explosions.

<a id="implementation-v1-9-smart-collections-context-and-user-control"></a>
#### Smart Collections, context, and user control

Automatic evidence can suggest a stable virtual collection. Collections retain
title, description, relationship summary, context type, confidence, creation
source, update time, pin/rename state, and member count. The inspector exposes
members, actual edges/evidence, and a bounded timeline made only from retained
creation/modification timestamps.

Transactional manual controls cover link, unlink, confirm, reject, always
relate, never relate, rename, pin, merge, and split. Pair corrections, member
overrides, and forgotten context tombstones survive automatic refresh. No
operation moves or edits a user file.

<a id="implementation-v1-9-search-integration"></a>
#### Search integration

`SearchRequest.IncludeRelationshipContext` defaults to true and is exposed in
the Search UI. After ordinary v1.8 ranking, the service asks only for bounded
direct relationships of already-ranked seed files. The ranker adds an explicit
`RelationshipContext` component from persisted evidence. Exact and literal
tiers remain above contextual-only results, tie-breaking stays deterministic,
and disabling the option performs ordinary Search. Recoverable relationship
storage failure degrades to v1.8 Search.

<a id="implementation-v1-9-privacy-diagnostics-repair-and-security"></a>
#### Privacy, diagnostics, repair, and security

The Collections page supports file/source/collection forgetting, future
analysis suppression, selected-file rebuild, and derived-row repair. Privacy
filters apply to Related Files, membership, timelines, and Search expansion.
Watched/manual source ownership is preserved. Storage breakdown and privacy
inspection count relationship-derived data, while source files remain
unchanged.

Diagnostics contain aggregate counts, duration, candidate/output counts,
algorithm version, exclusions, corrections, and repairs. They omit document
text, OCR text, summaries, semantic vectors, source-derived evidence text, and
unnecessary paths.

Malformed Unicode, identifiers, timestamps, enum rows, semantic vectors, and
oversized values are bounded or rejected. Evidence, candidates, edges,
memberships, direct Search expansions, and UI reads are capped and cancellable.
Repair removes orphan or corrupt derived state without interpreting indexed
content as code.

<a id="implementation-v1-9-desktop-and-accessibility"></a>
#### Desktop and accessibility

The new **Collections** navigation destination owns a provider-neutral
`CollectionsViewModel`. Smart Collections and Related Files tabs expose lists,
inspectors, evidence, confidence, provenance, timeline, manual control,
privacy, rebuild, repair, and aggregate diagnostics. Controls have meaningful
accessible names; the status surface is a live region; keyboard and pointer/
touch activation use ordinary Avalonia controls. Settings expose relationship
enablement, defensive bounds, and extension exclusions. Search exposes its
context opt-out.

<a id="implementation-v1-9-compatibility-and-limits"></a>
#### Compatibility and limits

Existing saved scans/catalogs, JSON Search, watched folders, duplicate
detection, workflows, plugins, Change Plans, Operation Journal, recovery, and
Undo retain their contracts. v1.9 adds no PostgreSQL/server dependency and no
mandatory OCR or Ollama dependency.

No Knowledge Graph, recursive graph query, conversational assistant, identity
resolution, face recognition, GPS clustering, learned confidence percentage,
package, tag, merge, or publication is implemented. Knowledge Graph integration
remains explicit future work.

<a id="implementation-v1-9-tests-added-or-changed"></a>
#### Tests added or changed

- `RelationshipEngineTests`: evidence, categories, conservative negative
  cases, deterministic output, suppression, cancellation, malformed input, and
  rank priority/ties.
- `RelationshipSearchIntegrationTests`: exact-first contextual expansion,
  per-query opt-out, and fallback.
- `SqliteRelationshipStoreTests`: persistence, evidence, collections,
  timelines, corrections, forgetting, ownership, tombstones, corruption
  repair, schema-2 migration, and bounded candidates.
- `CollectionsViewModelTests` and desktop layout tests: projection, operations,
  failure containment, navigation, and accessibility metadata.
- `SearchPerformanceRegressionTests`: bounded synthetic relationship discovery.
- Existing version, workflow-provenance, navigation, documentation-policy,
  indexing, Search, and compatibility assertions were updated rather than
  weakened or removed.

The final suite contains 1,128 tests, up from the valid 1,086-test v1.8
baseline. Debug and Release both execute all 1,128 with zero failures or skips.

<a id="implementation-v1-9-corrective-findings-during-review"></a>
#### Corrective findings during review

The release-readiness review fixed several issues before the canonical pass:

- added the v1.9 branch to the existing three-host GitHub Actions push filter;
- made full index rebuild clear stale automatic graph data while preserving
  explicit manual links;
- prevented privacy-suppressed files from acting as Related Files/Search seeds;
- rejected malformed titles, evidence, collection proposals, timestamps, and
  generated graph records at provider boundaries;
- made database-busy/timeout relationship expansion fall back to ordinary
  Search;
- changed the Collections file picker to a lightweight projection so it does
  not load extracted text, OCR, summaries, or semantic vectors for display;
- exposed the persistent **Always relate** correction for existing suggestions.

Exact build, test, analyzer, policy, target, audit, and hosted-CI evidence is in
`VALIDATION.md#validation-v1-9`.

</details>

<a id="implementation-v1-8"></a>
## v1.8

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/V1.8_IMPLEMENTATION_REPORT.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="implementation-v1-8-basis-and-scope"></a>
#### Basis and scope

The branch `v1.8-search-intelligence-privacy` was created from exact validated
v1.7 tip `cce0d8a2e01ecba679f05c2baa02191c034c8365`. v1.7 and `main` were not
merged, rebased, rewritten, or modified. Discovery confirmed that v1.7 already
provided provider-neutral indexing contracts, schema-versioned SQLite storage,
durable stages, metadata/text/OCR/summary/chunk/semantic data, progressive
Search, levels, controls, source ownership, quotas, resource modes, progress,
and recovery.

<a id="implementation-v1-8-reused-v17-components"></a>
#### Reused v1.7 components

- `IDeepIndexStore`, `IProgressiveSearchSource`, `BackgroundIndexingService`,
  and `SqliteDeepIndexStore`
- stable identity, fingerprints, shared content, source ownership, durable
  jobs/stages/failures, recovery, quota, cleanup, backup, and compaction
- compatible JSON semantic index and feature-hashing representation
- existing MVVM Search page, indexing progress, failure inspection, and
  diagnostics window
- OCR/content extraction and optional local-AI enrichment boundaries

<a id="implementation-v1-8-new-application-components"></a>
#### New application components

- provider-neutral Search requests, visible filters, candidates, ranking
  components, snippets, execution results, privacy inspection, and repair
  contracts;
- conservative clock/locale-aware deterministic query interpreter;
- tiered `ISearchRanker`/`HybridSearchRanker` and bounded
  `ISearchSnippetFactory`/`SearchSnippetFactory`;
- `SearchQualityEvaluator` and deterministic synthetic corpus metrics;
- compatible/deep store failure isolation, explicit excluded-path
  reconciliation,
  four-query concurrency gate, cancellation, and privacy-minimized Search
  diagnostics;
- `IIndexPrivacyStore` and `IIndexPrivacyService` with file/source inspection,
  forget, policy, selective clear, and targeted repair.

<a id="implementation-v1-8-provider-and-migration"></a>
#### Provider and migration

Embedded SQLite remains the only deep-index provider and is isolated in
`OpenSorSe.Indexing.Sqlite`. Schema version 2 adds
`index_privacy_rules`. Initialization migrates schema 1 inside one transaction
after a managed backup. Unsupported newer schemas and database integrity
failures retain existing v1.7 recovery behavior.

Privacy actions are parameterized transactions. Forget rules retain only the
source-relative exclusion needed to suppress rediscovery and compatible legacy
Search results. Clear rules suppress affected processing so data is not
immediately regenerated. Repair rules persist the selected restart stage and
force flag, then reuse ordinary queue/progress/cancellation/restart behavior.

<a id="implementation-v1-8-search-and-ui-behavior"></a>
#### Search and UI behavior

Exact/literal tiers precede fuzzy and related-concept-only results.
Deterministic tie-breakers use source priority, full coverage, modification
time, and ordinal path. Actual ranking components populate **Why this
result?**, matched flags, and snippet selection.

The Search UI adds visible filter chips, individual removal, clear-all,
accessible explanations/snippets, specific coverage limitations, indexed-data
inspection, confirmed forget actions, metadata-only/exclusion controls, and
file/source repair. No View or ViewModel creates SQL, parses FTS, computes
weights, or calls Ollama.

<a id="implementation-v1-8-privacy-security-and-ai-optional-behavior"></a>
#### Privacy, security, and AI-optional behavior

Inspection exposes category presence/counts, not complete contents or raw
vectors. Source files are never modified by an index privacy/repair action.
Global settings can independently disable OCR, optional local AI, summaries
and keywords, and related-concept data/chunks. Processor fingerprints include
these policy choices.

Search accepts bounded text and conservative filters, normalizes common
separators/diacritics, bounds typo work, sanitizes snippets, validates malformed
stored ranking JSON, uses parameterized SQLite, and records no complete query
by default. Ollama is never required for interpretation, ranking, filtering,
snippets, explanations, OCR text Search, or metadata/filename fallback.

<a id="implementation-v1-8-tests-and-performance"></a>
#### Tests and performance

The 987-test v1.7 baseline is preserved. Final Debug and Release validation
each passed 1,086 tests with zero failures or skips. v1.8 adds relevance, ranking,
interpretation, locale/clock, filter, normalization, fuzzy, snippet, malformed
input, fallback, exclusion, concurrency, cancellation, diagnostics privacy,
settings, schema migration, inspection, forget, shared data, anti-regeneration,
repair, SQL injection, corrupt-record, query-plan, compressed-part,
archive-traversal/nesting, ViewModel, and accessibility tests. Synthetic
100/1,000/5,000 candidate regression checks cover cold and warm
ranking/filter/snippet cost, allocations, and cancellation. No developer
filesystem is scanned and no personal corpus is used.

Final clean counts and all validation gates are recorded in
[OpenSorSe 1.8 Validation Report](VALIDATION.md#validation-v1-8). Interactive
outcomes are deliberately absent; see the unchecked
[manual checklist](MANUAL_TESTING.md#manual-v1-8).

</details>

<a id="implementation-v1-7"></a>
## v1.7

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/V1.7_IMPLEMENTATION_REPORT.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="implementation-v1-7-repository-basis"></a>
#### Repository basis

Implementation began from clean authoritative `main` commit
`58b8a22312f09875e93df632143d24abf6397e26` on branch
`v1.7-deep-indexing-foundation`. The existing MVVM composition, bounded atomic
JSON stores, semantic Search, content/OCR pipeline, watched folders, workflows,
plugins, Change Plans, Operation Journal, recovery, and Undo were inspected and
extended rather than reimplemented.

<a id="implementation-v1-7-implementation"></a>
#### Implementation

- Added `DeepIndexingSettings`, Basic/Standard/Deep policy, resource modes, and
  validated conservative bounds.
- Added provider-neutral Application contracts/models, physical discovery,
  resource eligibility, default staged processing, coordinator, progressive
  Search source, and privacy-safe diagnostics.
- Added `OpenSorSe.Indexing.Sqlite` with schema 1, migrations/backups,
  integrity checks, transactions, WAL/full synchronization, recovery, stable
  identity, shared content, retention, quota policy, search projection, and
  correct synchronous/asynchronous disposal.
- Integrated startup/shutdown, watched sources, completed manual scans,
  progressive Search, settings, progress/control UI, coverage, storage, and
  accessible help.
- Preserved internal `Semantic*` and persisted identifiers while renaming
  user-facing Meaning Search text to Search.
- Added focused persistence, migration, concurrency, stage, cancellation,
  restart, dependency, quota, progressive Search, settings, accessibility, and
  performance-regression tests.

<a id="implementation-v1-7-reliability-defects-found-during-integration"></a>
#### Reliability defects found during integration

- Cleared SQLite connection pools on corruption and backup lifecycle paths so
  Windows recovery copies and cleanup are not held open.
- Qualified an ambiguous completion query and checkpointed after compaction so
  quota reporting reflects physical storage.
- Corrected skipped-stage terminal state, superseded-run handling, nullable
  time-window validation, and minimum practical test quota.
- Made safe cancellation stop active discovery as well as stage workers and
  synchronize both job and per-stage durable rows.
- Resumed interrupted discovery in the same run without resetting completed
  jobs; preserved explicit pause/cancel state across restart; and made durable
  resource/dependency waits automatically eligible at their retry time.
- Separated watched-folder-owned sources from manual sources so watcher
  configuration changes cannot remove or requeue unrelated indexing sources.
- Degraded safely when derived storage is corrupt or from a newer schema,
  preserving the existing Search path until an explicit rebuild retains a
  bounded recovery copy and creates a fresh schema.
- Added an inspectable privacy-minimized failure list, current-run diagnostics
  navigation, and run/stage/retry/throughput/maintenance diagnostic facts.
- Bounded recovery copies and their SQLite sidecars under the same three-copy
  policy as manual and pre-migration backups.
- Pinned SQLitePCLRaw 2.1.12 after the final advisory snapshot identified
  `GHSA-2m69-gcr7-jv3q` against the provider's older transitive native bundle.
- Preserved `DeepIndexing` through legacy diagnostics migration and environment
  override copies.
- Added synchronous disposal for dependency-container compatibility.

<a id="implementation-v1-7-deliberate-limits"></a>
#### Deliberate limits

Relationship and optional AI contracts are foundations, not claims of a final
relationship graph or conversational intelligence. Idle/power/battery settings
are retained and degrade gracefully until supported platform adapters exist.
The deterministic existing representation provider remains local. No
PostgreSQL dependency, database server, source-file copy, cloud index, new
mutation path, tag, or release artifact is added.

Exact validation, final test totals, commit, push, and GitHub Actions evidence
are recorded in [the validation report](VALIDATION.md#validation-v1-7).

</details>

<a id="implementation-v1-6"></a>
## v1.6

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/V1.6_IMPLEMENTATION_REPORT.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="implementation-v1-6-baseline-audit"></a>
#### Baseline audit

The branch was created from clean `main` at commit `3cafe92`, matching
`origin/main`. Before implementation, Debug restore/build and all 850 v1.5
automated tests passed with zero failures and zero skipped tests. The audit
covered the solution graph, Core services, scanner/rules/executor,
application orchestration, AI, Desktop MVVM, JSON persistence, catalogs,
content/OCR, semantic search, workflow profiles and recipes, watched folders,
plugins, Change Plans, Operation Journals, recovery/Undo, diagnostics,
platform services, documentation, and CI.

<a id="implementation-v1-6-interrupted-session-recovery"></a>
#### Interrupted-session recovery

After a host power loss, the complete intended source, test, CI, and
documentation diff was recovered unstaged on the v1.6 branch. Git had no active
operation or conflict, and no changed file was empty or partially written.
Ignored Debug output contained six TRX files while Release contained only five,
so all generated output was discarded and validation restarted from a clean
restore. No recovered production change needed to be reverted or rewritten.

<a id="implementation-v1-6-implemented-production-changes"></a>
#### Implemented production changes

<a id="implementation-v1-6-persistence-and-recovery"></a>
##### Persistence and recovery

- Added `AtomicJsonFile`, a shared fully-qualified-path, same-directory,
  capacity-bounded, cancellation-safe, durable JSON replacement primitive.
- Added `ApplicationFileAccessCoordinator`, a host-path-aware process-local
  transaction gate shared across store instances.
- Migrated settings, AI history, Saved scans, saved searches, content,
  semantic index, structure history, workflows, watched-folder stores, plugin
  state, Change Plans, and Operation Journal writes without changing schemas.
- Preserved existing capacity exception types and invalid-file recovery
  behavior.

<a id="implementation-v1-6-performance-and-memory"></a>
##### Performance and memory

- Reworked duplicate detection to count hashes before allocating duplicate
  member lists.
- Reworked results queries to tokenize once, filter cheaply first, and select
  ranked signals without per-file sorting.
- Bounded non-persistent processing-session history at 256 terminal records.
- Synchronized active task snapshots and made late progress terminal-safe.

<a id="implementation-v1-6-cancellation-lifecycle-and-reliability"></a>
##### Cancellation, lifecycle, and reliability

- Added cancellation-aware result projection while retaining the original
  projector API.
- Moved Desktop result projection off the UI thread with the active token.
- Isolated task, event, session, watcher-source, watcher-state, and
  watcher-activity observer failures.
- Distinguished owner-requested cancellation from unrelated subscriber or task
  cancellation.
- Serialized watched-folder initialization and made asynchronous disposal
  idempotent; shutdown awaits owned loops.
- Observed background watcher tasks and retained reconciliation recovery.

One pre-final full-suite run exposed a real debounce race: a superseding
watched-folder hint could cancel and dispose the prior token source before its
delay coroutine had started. The resulting `ObjectDisposedException` could be
misclassified as overflow recovery under load. Ownership was corrected so the
delay coroutine disposes its own source in `finally`; superseding and shutdown
paths cancel without prematurely disposing it. Targeted watcher tests and both
final complete suites pass with the correction.

<a id="implementation-v1-6-cross-platform-behavior"></a>
##### Cross-platform behavior

- Injected optional `IPathSemantics` into action planning.
- Applied host path comparison to watcher hint normalization.
- Added host-independent lexical handling for persisted and untrusted paths so
  Windows and POSIX roots, separators, filenames, plugin manifests, workflow
  destinations, and semantic index labels are interpreted consistently.
- Added explicit Windows/Linux case-policy tests.
- Expanded hosted validation to Windows, Ubuntu, and macOS.

<a id="implementation-v1-6-accessibility-diagnostics-and-maintenance"></a>
##### Accessibility, diagnostics, and maintenance

- Added automation names and polite live announcements to critical workflows.
- Centralized runtime version provenance in `ApplicationVersionInfo`.
- Enabled .NET analyzers explicitly and retained warnings-as-errors.
- Added CI gates for zero skipped tests, analyzers, code style, whitespace,
  patch formatting, and documentation policy.

<a id="implementation-v1-6-automated-coverage-added"></a>
#### Automated coverage added

The suite grew from 850 to 895 tests. The 45 additional test cases cover:

- atomic replacement, partial serialization, oversize preservation, temporary
  cleanup, gate cancellation, and ambiguous path rejection;
- multi-instance settings, AI history, catalog, saved-search, Change Plan, and
  Operation Journal concurrency;
- event-bus and observer isolation;
- background task failure, progress, cancellation, and 32-task concurrency;
- 25,000-file duplicate stress and deterministic mid-pass cancellation;
- 50,000-file result-query stress and mid-query cancellation;
- result projection cancellation;
- bounded and concurrent processing-session lifecycle;
- concurrent watched-folder initialization/disposal, observer isolation, and
  host-correct case de-duplication;
- path-semantics-aware action planning;
- Windows/POSIX path-syntax recognition independent of the executing host;
- critical-view accessibility metadata; and
- a repository-wide no-skipped-test policy.

<a id="implementation-v1-6-api-and-schema-review"></a>
#### API and schema review

No existing public member was removed. The projector overload and optional
planner dependency are additive. No persisted schema version changed. No file
mutation capability, autonomous AI path, cloud dependency, package installer,
or updater was added.

Both final configurations pass all 895 tests with zero failures and zero
skips. Exact gate and per-project results are recorded in
[V1.6 Validation Report](VALIDATION.md#validation-v1-6).

</details>
