# Reliability Architecture

**Version scope:** Introduced in v1.6. This is the canonical subject document;
version-specific statements below retain that scope. Consult [Current State](../../CURRENT-STATE.md)
and source/tests for subsequent authority, schema and runtime changes.
Exact prior text: [Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/Architecture/00_System/09_v1.6_Reliability_Architecture.md).

OpenSorSe v1.6 hardens the existing layered system without changing feature
ownership or the file-operation authorization boundary.

## Durable application-owned state

```mermaid
flowchart LR
    Store["Versioned JSON store"] --> Gate["Normalized path coordinator"]
    Gate --> Validate["Schema and capacity validation"]
    Validate --> Temp["Unique same-directory sibling"]
    Temp --> Flush["Async write and durable flush"]
    Flush --> Replace["Atomic owned-file replacement"]
    Replace --> Reload["Bounded validated reload"]
```

The gate is process-local and covers the complete load-modify-write
transaction. The temporary sibling and destination are always in the same
directory. Failure before replacement leaves the previous valid document
untouched. This mechanism applies only to OpenSorSe application data and never
to selected user files.

## Responsive in-memory work

```mermaid
flowchart LR
    Snapshot["Immutable results snapshot"] --> Cheap["Cheap structured filters"]
    Cheap --> Ranked["Tokenized ranked matching"]
    Ranked --> Stable["Stable deterministic ordering"]
    Stable --> Page["Bounded page"]
    Cancel["Cancellation token"] --> Cheap
    Cancel --> Ranked
    Cancel --> Stable
```

Duplicate preparation uses a compact value array and one hash-count map.
Duplicate member lists exist only for hashes with more than one member.
Processing-session history is bounded independently of persistent history.

## Failure containment

Lifecycle owners snapshot their observer lists and call handlers independently.
Exceptions are redacted and logged; caller-owned cancellation is the only
cancellation allowed to cross the event or task boundary. The watched-folder
coordinator serializes initialization, refresh, and disposal and awaits its
owned long-running loops during shutdown.

## Compatibility

No v1.6 persistence schema changes are required. Existing MVVM pages, services,
workflow profiles, watched folders, plugins, AI contracts, Change Plans,
Operation Journals, and Undo records retain their existing ownership and
meaning. The complete v1.5 platform architecture remains the foundation:
[v1.5 Platform Architecture](08_Platform_Architecture.md).
