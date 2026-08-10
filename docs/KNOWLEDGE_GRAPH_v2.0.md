# OpenSorSe v2.0 Knowledge Graph

**Status:** implementation candidate on `v2.0-knowledge-graph`; unmerged and
awaiting release-candidate and interactive maintainer validation

The Knowledge Graph is an optional, local projection over information already
retained by OpenSorSe indexing. It connects stable files, sources, folders,
Smart Collections, exact-content document sets, and user-created entities. It
does not open source files, move files, replace v1.9 relationships, or grant a
new file-mutation path.

The feature is disabled by default. Enabling it requires informed consent that
the graph will retain additional local, searchable relationships and evidence.
Search, indexing, Collections, Change Plans, and Undo continue to work when the
graph is disabled or unavailable.

## Stable scope

The stable node kinds are:

- **File** — an indexed file identified by the existing stable provider ID;
- **Source** — an indexing source, preserving manual and watched ownership;
- **Folder** — a source-scoped relative folder identity;
- **Collection** — an existing v1.9 virtual Smart Collection;
- **Document Set** — files sharing a validated exact content hash; and
- **Manual Entity** — an identity explicitly created by the user.

The stable edge kinds are **Related File**, **Owned by Source**, **Located in
Folder**, **Member Of**, **Same Document Set**, and **Manual**. Every edge
retains typed evidence. Confidence is a deterministic level—Low, Medium, High,
or Confirmed—not a fabricated probability.

Automatic identity is deliberately conservative. Existing provider IDs,
source ownership, exact source-relative folders, exact hashes, authoritative
v1.9 relationships/collection membership, and explicit user decisions are
usable evidence. A similar name, summary, keyword, OCR phrase, date, or semantic
vector is not by itself permission to merge identities.

Tag nodes, automatic Person/Place/Event/Topic identity, unrestricted traversal,
a graph canvas, conversational behavior, autonomous file actions, and remote
or cross-device graphs are not implemented. Provider-neutral contracts and a
strict bounded validator prepare for optional entity suggestions, but no live
suggestion producer is wired in the candidate. Validation is disabled by
default and never makes a suggestion an identity. Existing experimental rows,
if supplied by a future reviewed provider, remain inactive and can be ignored;
stable identity still requires an explicit compatible user decision.

## Storage and compatibility

The Application layer owns provider-neutral graph, decision, query, privacy,
repair, and diagnostics contracts. SQLite remains isolated in
`OpenSorSe.Indexing.Sqlite`. The desktop and ViewModels do not construct SQL or
open database connections.

v2.0 adds two application-owned sidecars under the indexing data directory:

| Store | Schema | Authority | Recovery role |
| --- | ---: | --- | --- |
| `knowledge-graph.db` | 1 | Rebuildable derived projection | May be selectively repaired or rebuilt from completed authoritative input. |
| `knowledge-decisions.db` | 1 | Non-rebuildable graph-native user decisions and privacy state | Requires verified recovery points and must not be silently reset. |
| `deep-index.db` | 3 | Existing v1.9 index, relationships, collections, privacy, and legacy decisions | Remains unchanged and authoritative for all v1.9 behavior. |

The sidecar schema-1 bootstrap is the v2.0 persistence migration. It does not
alter `deep-index.db`, whose schema stays at 3. Store application IDs, schema
markers, migration history, and integrity checks distinguish an empty store, a
supported store, corruption, and an unsupported newer schema. Opening a newer
schema fails safely.

The graph never uses a nested transaction spanning multiple databases. An
application-data lifecycle lock serializes graph/decision initialization,
backup, restore, clear, and rebuild operations. Projection uses immutable,
completed source manifests; each manifest has a stable ID, canonical row count,
canonical hash, and bounded pages. An incomplete or hash-mismatched manifest
cannot replace the active one.

The decision store keeps append-only commands and validated checkpoints.
Existing v1.9 relationship decisions remain authoritative in schema 3; the
graph holds only a reconciled, non-authoritative mirror. A graph-native v2.0
decision never rewrites v1.9 history.

When you unlink an existing Related File relationship or remove a file from an
existing Smart Collection in the Knowledge Graph, OpenSorSe updates the same
v1.9 relationship service used by the Collections page first. The graph then
refreshes from that authoritative change; it does not save a competing graph
decision. If the relationship is missing, ambiguous, unavailable, or stale,
the action stops safely. Collection rename, pin, merge, and forget remain in
Collections. Structural source, folder, and exact-document-set links are
inspectable and cannot be unlinked.

## Incremental projection and recovery

Projection is outside the v1.7 `FileFullyIndexed` stage. Terminal indexing
changes request coalesced reconciliation; unchanged completed input is not
needlessly projected again. The coordinator records durable jobs, input
fingerprints, algorithm versions, output generations, retry classifications,
and explicit wait states.

Four state axes stay separate:

- **run control:** pending, running, pause requested, paused, cancel requested,
  cancelled, or complete;
- **job execution:** pending, running, complete, cancelled, retryable failure,
  permanent failure, waiting for dependency, or waiting for resources;
- **freshness:** current or stale; and
- **integrity:** valid or repair required.

This separation prevents “complete” from hiding stale or damaged output. New
generation rows become visible only after validation and an atomic publication
step; readers never consume half-published components.

Coordinator ownership uses a monotonically increasing fencing epoch plus an
opaque per-claim token. Workers heartbeat every 5 seconds; claims have a
30-second lease. Shutdown requests cooperative cancellation and waits up to 5
seconds before leaving durable resumable work. A late worker cannot publish
after its epoch or token has expired. Startup recovers expired claims and never
leaves them permanently reported as running.

The graph records separate **ingested** and **applied** watermarks for source
manifests, graph-native decisions, and authoritative privacy state. Data may be
durably captured without yet being safe to expose. Reads and Search expansion
fail closed until the applicable source, decision, and privacy authority has
been applied.

## Browsing and Search

The Knowledge Graph page uses paged lists and inspectors rather than an
unbounded canvas. Ordinary pages default to 50 items and cannot exceed 100.
Stable browsing is one hop and at most 100 visited nodes. Experimental browsing
is explicitly separate and limited to two hops and 500 nodes. Provider cursors
are opaque; ordering and tie-breaking are deterministic.

Search context is independently optional. The Search pipeline:

1. ranks ordinary v1.8/v1.9 exact, filename, literal, metadata, text, OCR,
   semantic, and direct-relationship results;
2. takes at most 16 already-ranked file seeds;
3. asks the graph for at most 50 current, valid, one-hop file expansions; and
4. enforces a combined v1.9-relationship-plus-graph contextual ceiling of 100.

Exact and literal matches remain more important than contextual-only results.
When the same target is supplied by v1.9 relationships and the graph, the v1.9
relationship contribution wins. Graph explanations come from the actual
current edge and evidence; they are not generated after ranking. Graph coverage
is reported separately from indexing coverage. If graph authority, integrity,
maintenance, or storage is unavailable, Search omits graph context and retains
ordinary Search results.

## User control and privacy

Users can pause, resume, cancel, retry, or request reconciliation. They can
inspect bounded nodes, edges, aliases, evidence, origins, state, and coverage;
create or rename manual entities; add/remove aliases; link/unlink; and apply
compatible merge/split or never-merge decisions. Stable identifiers remain
internal; labels and explanations are bounded and accessible.

Privacy inspection reports categories and counts, not complete source text.
Graph-only actions can forget derived data for a file/source/collection, exclude
or include a scope in future projection, clear all derived graph data, or—only
after explicit confirmation—clear graph-native decisions. These actions never
delete or modify an original file. Existing source ownership and v1.9 privacy
rules are rechecked at the point of use so stale projection data cannot bypass
an exclusion.

Forgetting is acknowledged only after the mutation is durable and the decision
store has a valid recovery point at or above the required privacy floor.
Verified decision backups use staged and committed metadata and remain distinct
from the rebuildable graph store. If decision or privacy authority cannot be
verified, graph browsing and Search expansion stop rather than guessing.

The current candidate recovery surface is the provider-neutral
`IGraphDecisionRecoveryService` maintainer/integration path; v2.0 does not claim
an end-user restore button. Recovery-point listings expose only bounded IDs,
sequence/generation values, commit time, pin/restorable state, and a status
code; database paths and document content are not included. Restore requires
the exact confirmation `RESTORE GRAPH DECISIONS`. A selected point is verified again and
is rejected when it is corrupt, foreign, from an unsupported newer schema, or
below the retained privacy floor. Atomic promotion is journaled; the next
initialization completes or rolls back an interrupted promotion. Decision
recovery does not modify source files or `deep-index.db`.

Diagnostics contain bounded run IDs, stages, queue/count/timing groups,
watermarks, recovery/repair categories, graph size, and state axes. Normal
diagnostics omit document text, OCR text, complete queries, model payloads,
secrets, and unnecessary absolute paths. Exports remain reviewable before
sharing.

## Repair and operational limits

Selective operations can verify integrity, reproject a component/file/source,
reconcile the legacy decision mirror, repair evidence, remove verified orphans,
or rebuild derived graph data while preserving decisions. Cancellation is
cooperative and durable. Corrupt derived data is quarantined or marked for
repair; non-rebuildable decision corruption requires an explicit recovery path.

For whole-store derived corruption, the current candidate exposes the
provider-neutral `IGraphDerivedStoreRecoveryService` maintainer/integration
path; no end-user recovery button is claimed. The exact confirmation
`REBUILD DERIVED GRAPH STORE` starts a same-volume journaled operation that
quarantines the corrupt graph database family, validates and publishes a fresh
empty derived store, and validates the authoritative decision store before and
after promotion. A new lifecycle instance can resume an interrupted promotion.
The quarantine is retained for review; graph-native decisions, `deep-index.db`,
and original files remain outside the operation. A healthy store or an
unsupported newer schema is never reset through this path.

Hard bounds cover pages, traversal depth and nodes, evidence per edge, aliases,
incident edges, candidate buckets, components, retry attempts, Search seeds,
Search expansions, and suggestion input/output. These bounds are correctness
and denial-of-service protections, not performance claims. The graph does not
read archives, extracted files, or source documents, and it does not trigger
OCR or Ollama processing.

## Release-candidate boundary

This branch is an implementation candidate, not a published v2.0 release.
Automated evidence belongs in [the v2.0 validation report](V2.0_VALIDATION_REPORT.md)
after the final clean validation is complete. Every item in the
[manual checklist](MANUAL_TESTING_v2.0.md) and
[release-readiness checklist](RELEASE_READINESS_v2.0.md) remains unchecked.
The mandatory [RC stabilization plan](V2.0_RC_STABILIZATION_PLAN.md) must be
completed before any merge, tag, package, or publication decision.
