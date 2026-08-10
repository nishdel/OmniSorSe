# v2.0 graph-integrity and deterministic-generation model

**Status:** Integrity model implemented by the source candidate; final clean
validation and RC corruption/failure campaign pending.

## Integrity layers

Graph validity is checked at five independent layers:

1. **Physical:** SQLite open, page checks, WAL state, and verified backup.
2. **Structural:** expected version, migration history, tables, columns,
   indexes, constraints, and foreign keys.
3. **Record:** valid enums, booleans, timestamps, IDs, lengths, counts, JSON
   shapes, and decision sequence.
4. **Graph:** endpoints, edge direction/type, evidence, degree, alias, privacy,
   active-component, completed-manifest, and sequence invariants.
5. **Semantic contract:** deterministic identity, manual-decision precedence,
   stale invalidation, Search priority, and source-file immutability.

`PRAGMA quick_check` alone is insufficient. Initialization and explicit Verify
also run `foreign_key_check`, schema-shape validation, and bounded application
invariant scans.

## Stable identifiers

- File, Source, and Collection nodes reuse existing provider-neutral v1.9 IDs.
  File identity is source-scoped: moving unchanged content across sources creates
  a new v1.9 file ID and therefore a new File node. Exact hash evidence may
  suggest a relationship, but the graph must not silently transfer identity or
  manual decisions across that boundary.
- Folder identity combines source ID, path-semantics version, and normalized
  source-relative folder key. Absolute paths are not graph identity.
- Accepted Tag nodes are experimental/deferred until a provider-neutral tag
  observation contract supplies stable tag and provenance identity. Stored
  Search text that happens to look like a tag is not sufficient authority, and
  absent production tag data does not become an empty/default Tag node.
- Document Set identity uses a validated exact content hash plus hash algorithm
  version. It is not a claim that different files are the same real-world
  document version.
- Manual Entity identity is an opaque ID assigned in the decision store and
  survives rename/alias changes.
- Experimental candidate identity includes type, scope, normalization version,
  and exact normalized candidate. It does not imply confirmed identity.

Deterministic hash IDs retain the canonical collision inputs. A collision with
different inputs is `RepairRequired`; it is never merged. Hashing supplies a
stable identifier, not encryption or authentication.

## Node invariants

Every active node has:

- known type and stable key within documented length bounds;
- display data from a permitted bounded source or manual decision;
- completed source-manifest ID, canonical observation hash, and
  algorithm/config revision;
- created/validated/freshness state;
- privacy/visibility state;
- a valid active projection/component;
- no impossible timestamps or unknown state codes.

Derived nodes with no permitted binding/incident edge are orphans eligible for
quarantine and cleanup. Manual entities may intentionally have zero links and
are not auto-pruned.

## Edge and evidence invariants

- Edge IDs and endpoint ordering are canonical and deterministic.
- Both endpoints exist and are visible under current privacy/deletion rules.
- The edge type permits its node-type/direction combination.
- Automatic edges retain at least one evidence reference, evidence kind,
  explanation template code, completed source-manifest ID, canonical
  observation hash, and algorithm version.
- Evidence references resolve to current permitted v1.9 relationship,
  collection, hash, folder, metadata, or provider-authoritative tag facts when a
  future tag observation contract exists.
- Explanation text is rendered from retained evidence fields used by the
  algorithm; it is not inferred after projection.
- Manual edges are explicitly manual and do not fabricate evidence/confidence.
- Duplicate logical edges collapse by canonical key; conflicting automatic
  edge types remain distinct or require review according to the type contract.
- Self-loops are rejected except where a specifically reviewed edge type makes
  them meaningful. Cycles across nodes are valid and query-bounded.

## Decision precedence

Decision application order is deterministic:

1. active privacy/forget/exclusion barrier;
2. manual split/never-merge/rejection;
3. manual merge/alias/entity/edge;
4. mechanically exact projection;
5. confirmed experimental suggestion;
6. unconfirmed suggestion (not an active fact).

Later automatic algorithms cannot override an earlier user decision.
Conflicting graph-native manual decisions in the same sequence are rejected
transactionally. Rebuild reads the ordered graph-native decision ledger from
the beginning or a verified checkpoint and combines it with a completed legacy
decision manifest to produce the same effective overlay.

Two decision namespaces prevent accidental dual authority:

- `LegacyRelationshipDecisionMirror` contains a non-authoritative projection of
  v1.9 manual relationships, pair decisions, Smart Collection membership
  overrides, rename/pin state, and tombstones. Schema 3 remains authoritative.
  Each mirror row records a stable legacy key, completed
  legacy-decision-manifest ID, canonical row hash, and active/retired state.
  These rows are not appended to the graph-native authoritative decision
  sequence.
- `GraphNativeDecision` contains new v2 entity, alias, merge, split, manual-edge,
  and graph-specific privacy/control decisions. The ordered decision ledger is
  authoritative for this namespace only.

An operation that affects a legacy-owned type commits through the existing v1.9
provider first, then reconciles the mirror idempotently from a completed
decision manifest. Failure of the mirror/projection step leaves the legacy
decision authoritative and records pending repair; it never rolls the legacy
decision back or promotes the mirror to authority. A missing legacy row retires
its mirror only after a complete manifest proves absence, producing a
`RetiredByLegacySource` mirror tombstone so stale imports cannot resurrect it.
An interrupted or partial scan cannot retire anything.

Stable v2.0 exposes only two graph-initiated legacy mutations for which the
v1.9 application contract has an exact equivalent: unlink/reject an existing
Related File relationship, and split an existing Smart Collection membership.
`RelationshipGraphAuthorityBridge` resolves the exact canonical file pair or
collection/member IDs, calls `IRelationshipService`, and requests reconciliation
only after that authoritative commit succeeds. It never appends the same intent
to `GraphNativeDecision`. A missing or ambiguous relationship, unavailable
bridge, stale authority fence, or unsupported legacy edge fails closed.

Legacy collection rename/pin/merge/forget remains on the v1.9 Collections
surface. Stable structural graph edges are inspectable but not unlinkable.
Graph-native merge/split remains limited to manual or confirmed experimental
entities, preventing a generic graph command from bypassing legacy ownership.

Conflicting graph-native and current legacy decisions do not use wall-clock
last-write-wins. Privacy and explicit never/reject/split decisions apply most
restrictively. Other contradictions are isolated as `RepairRequired` and block
publication of the affected component until resolved. A retained legacy
privacy or forgotten-collection tombstone remains active authority, not
prunable operational history.

## Deterministic generation

For a fixed completed source manifest, completed legacy-decision manifest,
graph-native decision sequence, normalization/configuration, and algorithm
version:

- candidate enumeration is ordered by stable key;
- normalization is culture-explicit and platform/path-semantics aware;
- buckets use deterministic keys and stable tie-breaking;
- rule evaluation order is fixed and named;
- output nodes, edges, aliases, and evidence are canonically ordered;
- operational times, worker IDs, and durations do not enter derived IDs or
  confidence;
- concurrency may change completion order but not canonical output;
- canonical logical export comparison is byte-equivalent across repeated runs
  and supported hosts, except explicitly documented path-semantics differences;
  this is not a requirement that SQLite database files remain byte-identical.

Confidence levels map from named evidence-rule sets. There are no invented
percentages or model-provided confidence values.

## Incremental invalidation

| Change | Minimum invalidation |
| --- | --- |
| File rename/path move | File display/Folder edges and path-derived evidence. |
| Metadata timestamp/size change | Metadata-dependent edge/component only. |
| Content hash change | Document Set and content-derived components. |
| Existing v1.9 relationship/evidence change | Projected direct edge, affected collection/entity suggestion. |
| Collection membership/rename/pin/split change | Collection node/membership and dependent context only. |
| Privacy forget/exclusion | Immediate query barrier plus all affected graph projections. |
| Manual merge/split/alias/edge decision | Bounded affected identity component and incident edges. |
| Graph algorithm/normalization change | Only components produced by that version. |
| OCR/AI availability change | Experimental dependent suggestions only; stable mechanical graph remains. |
| Source deletion | Source subtree projection after completed reconciliation. |

Graph configuration has its own fingerprint. It never changes the v1.9 global
indexing processor fingerprint or causes text/OCR extraction to rerun.

## Explosion and hostile-input defenses

- Candidate retrieval uses indexed exact keys and bounded buckets, never
  unbounded all-pairs comparison.
- Malformed Unicode, nulls, oversized labels/aliases/evidence, invalid dates,
  unknown enum values, corrupt JSON/model output, and extreme numeric values
  fail closed at the record/component boundary.
- High-degree nodes, excessive aliases, repeated duplicate events, and
  pathological cycles reach explicit ceilings and become ambiguous or repair
  required.
- Traversal uses a visited set, depth/node/edge/time/cancellation bounds, and
  deterministic ordering.
- SQL is parameterized. Indexed text is data, not SQL/FTS/JSON-path syntax.
- Graph projection never opens archives or source files; existing v1.7
  extraction/archive security limits remain authoritative.
- No raw embedding vector or untrusted model output is exposed as ordinary
  graph content.

## Integrity scan and repair

A bounded incremental scan validates active components by stable-key pages
within an immutable completed manifest. A manifest is authoritative only after
its terminal row count and canonical aggregate hash validate. Wall-clock
timestamps, `(updated time, stable ID)` cursors, and notifications may schedule
work but cannot establish completeness or absence.
It records a redacted category and quarantines only the affected component.
Provider repair may rebuild that component from authoritative inputs. The
scan never deletes a row solely because an interrupted reconciliation did not
observe it.

A complete offline/maintenance Verify additionally checks every active
component, active pointer, migration/history checksum, sequence gap, decision
application revision, orphan, and bounds aggregate. It is cancellable between
pages and leaves prior integrity evidence intact if interrupted.

Full derived graph rebuild is the last resort. Decision-store rebuild from
derived graph is forbidden.

## Integrity release gates

- deterministic canonical rebuild matches across repeated runs and hosts;
- no false merge in ambiguous/adversarial fixtures;
- every automatic edge explanation resolves actual retained evidence;
- manual merge/split/reject/forget intent survives every rebuild and migration;
- legacy mirror and graph-native decision conflicts resolve deterministically
  without transferring authority or using wall-clock last-write-wins;
- cross-source moves do not silently preserve File identity or transfer manual
  decisions;
- corrupt single rows are isolated and selectively repairable;
- newer schema and corrupt decision store fail without destructive reset;
- stale/deleted/excluded nodes cannot leak through Search during cleanup;
- bounded high-degree/cyclic graphs cannot exceed memory/traversal limits;
- graph corruption cannot require deletion of the complete deep index;
- source fixtures remain byte-identical.
