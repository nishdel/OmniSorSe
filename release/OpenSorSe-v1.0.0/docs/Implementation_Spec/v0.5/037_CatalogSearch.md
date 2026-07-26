# Specification 037 — Catalog-wide Deterministic Search

| Field | Value |
| --- | --- |
| Component | Desktop catalog search coordinator and presentation state |
| Target release | v0.5 |
| Depends on | v0.3 `ResultsQueryEngine`; v0.4 `IResultsCatalogStore` |

## Functional design

`CatalogSearchViewModel` takes the current configuration and catalog store. A text change starts a versioned asynchronous evaluation. Blank text does not enumerate stored entries and presents guidance. Disabled catalog settings produce an opt-in status without store access.

For each newest-first summary, the coordinator loads the corresponding entry, constructs accepted tags grouped by file, and repeatedly evaluates the existing `ResultsQueryEngine` at its supported page size until all matching files in that snapshot have been observed or cancellation occurs. It converts each result to a `CatalogSearchHitRow` carrying the saved UTC timestamp, catalog ID, display-safe file fields, match score, and existing match explanation.

The final results are ordered by descending score, descending saved UTC timestamp, ordinal filename, ordinal path, catalog ID, and file ID. The first 200 are published. This is a metadata search only: score signals remain exact filename, filename prefix/contains, extension, deterministic category, path, and accepted tag signals. No file is opened to search it.

## View states

| State | Behavior |
| --- | --- |
| Empty query | Explain which saved metadata can be searched. |
| Running | Show progress text; prior rows remain until newest evaluation completes. |
| No matches | Show a safe no-match state. |
| Results | Show saved timestamp, file name/path, match explanation, and bounded count. |
| Open | Raise a loaded `CatalogEntry` to the shell; existing Results view owns navigation. |
| Failure | Preserve previous rows and show generic local-catalog unavailable text. |

## Cancellation and memory

The view model owns a cancellation source and monotonically increasing query version. A new query cancels previous load/evaluation work. It never publishes stale rows. Entry cache and hit collection are replaced, not appended across queries. The maximum rendered result set is 200; v0.4 catalog bounds keep source enumeration finite.

## Acceptance tests

- A tag-only match and a filename match have the documented explanation and deterministic ordering.
- Multiple pages and snapshots aggregate before final top-200 cap.
- A blank/disabled query does not call the store.
- Selecting and opening a hit returns the exact saved entry without filesystem access.
- A later query wins over a slow earlier query and leaves a usable status.
