# 061 — Relationships, Context and Smart Collections

## Status

Implemented on `v1.9-relationships-context`; automated and interactive release
evidence are tracked separately. This specification describes source behavior,
not a merge, tag, package, or published release.

## Objective

Build a provider-neutral, deterministic relationship layer on the existing
durable index and v1.8 Search. Every automatic edge must retain inspectable
evidence. Original files remain outside this feature's mutation authority.

## Implemented contracts

- provider-independent files, features, relationships, evidence, collections,
  members, timelines, Search contexts, diagnostics, and operation results;
- deterministic bounded discovery with versioned algorithms and non-numeric
  confidence levels;
- durable SQLite schema 3 with transactional schema-2 migration;
- incremental pipeline integration through the existing durable relationship
  stage and processor fingerprint;
- virtual Smart Collections and bounded timeline projection;
- manual link/unlink, pair decisions, collection rename/pin/merge/split;
- relationship inspection, forgetting, rebuilding, and repair;
- optional direct relationship expansion in Search without changing exact and
  literal priority;
- accessible Collections, Related Files, inspectors, evidence, privacy, and
  maintenance surfaces;
- aggregate privacy-safe diagnostics and bounded regression coverage.

## Invariants

1. An automatic relationship without retained evidence is invalid.
2. Confidence is deterministic and versioned; the product does not invent a
   percentage.
3. Semantic similarity can corroborate but cannot independently create an
   automatic relationship.
4. Smart Collections are virtual and never move, rename, edit, or delete source
   files.
5. Manual edges and user corrections survive incremental automatic refresh.
6. A forgotten collection/member is not immediately regenerated.
7. Relationship privacy exclusions affect inspection, collections, and Search.
8. Search remains useful when relationship analysis is disabled or unavailable.
9. Exact/literal Search ranks above relationship-only expansion.
10. All candidate selection, graph reads, timeline reads, and expansions are
    bounded and cancellable.

## Non-goals

- conversational assistance;
- a general Knowledge Graph or graph-query language;
- learned user profiling;
- face/person recognition or speculative identity claims;
- modifying source files from Collections;
- PostgreSQL, server, cloud, or new mandatory AI dependencies;
- claiming interactive validation before a maintainer performs it.

## Compatibility

Schema 1→2 behavior remains unchanged. Schema 2→3 is additive and
transactional. Existing catalog, saved scan, watched-folder, duplicate,
workflow, plugin, Change Plan, Operation Journal, recovery, and Undo contracts
are not renamed or replaced. Public v1.8 Search entry points remain valid; the
new context flag defaults on and remains optional.

## Validation intent

Automated coverage verifies discovery/evidence, conservative false-positive
behavior, deterministic ordering, manual overrides, collection lifecycle,
privacy/forgetting, repair/corruption, migration, bounded candidate selection,
Search priority/fallback, ViewModel behavior, accessibility metadata, and
performance bounds using synthetic or temporary data only. The v1.9 manual
checklist intentionally records no completed observations in source.
