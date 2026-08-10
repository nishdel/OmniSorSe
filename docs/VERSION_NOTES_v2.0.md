# OpenSorSe v2.0 — Knowledge Graph

**Status:** unmerged implementation candidate; local automated validation
complete; exact-tip hosted, interactive, and release-candidate validation are
separate incomplete gates

v2.0 adds an optional, local, evidence-backed Knowledge Graph over the durable
index and relationship foundation from v1.7–v1.9. It is designed for bounded
inspection and contextual Search, not conversation or autonomous file work.

## What is included

- Provider-neutral graph, decision, privacy, query, Search, repair, suggestion,
  diagnostics, projection, and lifecycle contracts.
- A conservative stable graph of files, sources, folders, existing Smart
  Collections, exact-content document sets, and manual entities.
- Typed evidence, deterministic confidence levels, algorithm versions,
  freshness, integrity, origin, and user-decision provenance.
- Isolated SQLite schema-1 sidecars for rebuildable derived graph data and
  non-rebuildable graph-native decisions. The existing `deep-index.db` remains
  schema 3 and is not migrated.
- Completed-manifest ingestion, durable incremental jobs, generation-based
  publication, pause/resume/cancel/retry, expired-claim recovery, fencing, and
  selective repair.
- A bounded, accessible Knowledge Graph page with progress, coverage,
  list/detail inspection, direct neighbors/evidence, manual decisions, privacy,
  and maintenance controls.
- Optional one-hop Search expansion that preserves exact/literal and v1.9
  direct-relationship priority and falls back cleanly when graph data is
  unavailable.
- Privacy inspection, exclusions, index-only forgetting, verified decision
  recovery points, privacy-safe diagnostics, and source-file safety wording.

## Defaults and compatibility

Knowledge Graph processing is disabled by default and requires informed user
consent. Search context can be disabled separately. OpenSorSe continues to work
without Ollama, OCR, or the graph. No database server is required.

The graph projects existing indexed data and never opens or modifies source
files. v1.9 relationships, Smart Collections, corrections, privacy rules, and
Search behavior retain authority. Existing saved scans, watched folders,
duplicate detection, workflows, plugins, Change Plans, the Operation Journal,
recovery, and Undo keep their prior contracts.

## Deliberate limits

The candidate does not implement tag nodes, automatic real-world entity
identity, unrestricted traversal, a graph canvas, a conversational assistant,
autonomous organization, cloud synchronization, or a remote graph provider.
Provider-neutral entity-suggestion contracts and strict bounded validation are
prepared, but no live suggestion producer is wired. Validation is disabled by
default and cannot establish identity.

See [Knowledge Graph](KNOWLEDGE_GRAPH_v2.0.md) for operational details,
[Compatibility Matrix](V2.0_COMPATIBILITY_MATRIX.md) for upgrade/rollback
expectations, and the fully unchecked [Manual Testing](MANUAL_TESTING_v2.0.md)
and [Release Readiness](RELEASE_READINESS_v2.0.md) records for work still
required.
