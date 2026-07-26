# v0.7 Implementation Decisions

## Scope selection

Specification 033 explicitly named saved searches as deferred work. Once v0.5 supplied catalog-wide search and v0.6 supplied user-managed searchable metadata, repeatable named queries became the next complete user workflow. The release deliberately stops at presets: it does not introduce an index, scheduler, content reader, semantic provider, or database.

## Separate persistence file

Saved query text is stored in `saved-catalog-searches.json`, not inside `catalog.json`. A malformed preset must not make historical snapshots unavailable, catalog clear must not silently erase a separate user-curated workflow, and a query schema can evolve independently. Both files remain in the same disclosed local application-data boundary.

## Current results, not stored hits

A preset contains only name and query text. Running delegates to v0.5 search against the current catalog, including v0.6 tags. This prevents stale hit persistence, keeps capacity small, and preserves existing deterministic ranking and cancellation semantics.

## Capacity without eviction

The store rejects a distinct twenty-sixth preset. Silent oldest-item eviction is appropriate for scan-cache retention but not for explicit user-curated presets. The user must remove a preset before adding another.

## Corruption recovery

Normal operations preserve malformed JSON for diagnosis. The store's clear operation does not parse first, because a separately confirmed reset is the explicit user authorization to remove even corrupted saved-query data. The operation can target only the absolute file configured at composition.

## Disabled catalog behavior

Catalog disablement blocks saving and running presets but not listing/removing/resetting them. Users must retain the ability to inspect and delete private query text without re-enabling snapshot persistence.
