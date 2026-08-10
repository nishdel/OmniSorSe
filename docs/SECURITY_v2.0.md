# OpenSorSe v2.0 Knowledge Graph security notes

**Status:** implementation-candidate security boundary; final validation and RC
fault campaign pending

The Knowledge Graph consumes untrusted local indexed data and optional
untrusted suggestions. It never interprets indexed content as code, never opens
source files, and never extends the supported Change Plan mutation authority.

## Trust boundaries

- Application contracts accept typed provider-neutral queries and commands;
  Views and ViewModels cannot send SQL or SQLite FTS syntax.
- SQLite statements remain provider-owned and parameterized. Separate
  application IDs and schema markers prevent opening an unrelated database as
  graph or decision state.
- v1.9 schema-3 relationships, collections, privacy, and decisions remain
  authoritative. A stale graph mirror cannot override them.
- Model/provider suggestion contracts are untrusted, disabled by default,
  bounded, and evidence-key validated. No live producer is wired; validation
  alone never creates a stable identity.
- Diagnostics and normal errors use bounded codes/categories, not document
  content, complete queries, raw model output, secrets, or unnecessary paths.

## Structural and resource defenses

The implementation validates stable IDs/codes, Unicode/text lengths, labels,
aliases, evidence, timestamps, hashes, row counts, page cursors, states,
algorithm versions, and integer ranges. It rejects self-edges, incompatible
endpoints, invalid confidence/origin combinations, duplicate canonical records,
manifest count/hash mismatch, unsupported schemas, corrupt generations, stale
claims, and privacy/authority lag.

Hard ceilings apply to pages, edges/evidence/aliases per node, candidate
buckets, component size, retries, Search seeds/expansions, suggestions, and
traversal. Stable traversal is one hop/100 nodes. Experimental traversal is
separate and capped at two hops/500 nodes. This prevents a pathological graph,
cycle, alias set, or Search request from creating unbounded recursive work.

Cancellation is cooperative across manifest capture, projection, queries,
Search, repair, privacy, backup, and shutdown. Epoch/token fencing rejects late
publication after lease loss. Generation publication prevents readers seeing a
partially written component.

## Store and recovery defenses

`knowledge-graph.db` is rebuildable derived state;
`knowledge-decisions.db` is non-rebuildable user authority. Both use
transactional schema initialization, integrity metadata, controlled lifecycle
locking, disposed connections/commands, and classified busy/corrupt/newer-store
failures. Cross-store work does not claim false nested transaction atomicity.

Decision recovery points are staged, verified, and committed with bounded
metadata. Restore must satisfy the current privacy floor; an older or invalid
backup cannot resurrect forgotten graph data. Privacy, decision, and source
watermarks are checked at the point of use.

## Deferred security work

The candidate does not claim encrypted SQLite storage. It does not implement a
custom encryption scheme, remote provider, multi-user authorization, graph
sync, or plugin sandbox. Application data must be protected using appropriate
operating-system account, disk, backup, and device controls. These boundaries
must be reassessed before any future server or cross-device provider.

See [Safety and Privacy](SAFETY_AND_PRIVACY.md),
[Knowledge Graph](KNOWLEDGE_GRAPH_v2.0.md), and the
[RC stabilization plan](V2.0_RC_STABILIZATION_PLAN.md).
