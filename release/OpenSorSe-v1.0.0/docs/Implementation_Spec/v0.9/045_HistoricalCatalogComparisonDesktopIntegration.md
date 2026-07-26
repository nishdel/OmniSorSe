# Specification 045 - Historical Catalog Comparison Desktop Integration

| Field | Value |
| --- | --- |
| Component | Comparison ViewModel, rows, View, shell, navigation, and DI |
| Target release | v0.9 |
| Depends on | Specification 044 and v0.4-v0.8 historical Results opening |

## Functional requirements

`CatalogComparisonViewModel` shall list catalog summaries only while catalog is enabled, preserve valid selections across refresh, require two distinct selections, load both complete entries through the store, and run the comparison service away from the UI thread. It shall publish only the latest operation.

The default filter is Changed. Added, Removed, Modified, Unchanged, and All filters plus a case-insensitive stored filename/path filter shall republish at most 500 rows from the immutable result. Invalid text over 512 characters shall publish no rows and explain the limit without discarding the underlying result. Aggregate counts always reflect the complete comparison.

The ViewModel shall expose explicit cancel, refresh, compare, open-baseline, and open-current commands. Opening raises the same `CatalogEntry` event used by Catalog and Catalog Search; `MainViewModel` loads its snapshot/tags, marks it historical, updates the dashboard, and navigates to Results. Catalog maintenance or label changes invalidate result state and refresh selectors.

## UI and state matrix

| State | Required presentation |
| --- | --- |
| Disabled | Opt-in explanation; no store read. |
| Fewer than two entries | Explain that two saved scans are required. |
| Ready | Baseline/current selectors and enabled compare only for distinct values. |
| Busy | Compare/refresh/open disabled and cancel enabled. |
| Cancelled | Selectors preserved; no stale result publication. |
| Empty snapshots | Zero-count completed comparison with empty-state wording. |
| Different/unknown scope | Prominent historical-scope warning. |
| Duplicate stored paths | Aggregate ignored-record warning without diagnostic path leakage. |
| Filter has no rows | Preserve aggregate statistics and show filter-empty state. |
| Missing/corrupt entry | Generic recoverable status; prior Results state remains. |

## Accessibility, lifecycle, and testing

Selectors and buttons have descriptive text. Change kind and field names are rendered as text rather than color-only state. Rows wrap long stored paths. The 500-row cap bounds accessibility-tree size. The shell selection event awaits `MainViewModel.NavigateAsync`, making navigation refresh repeatable and observed; disposal cancels and releases token sources; no event subscription survives shell disposal.

Tests cover command availability, disabled/empty/duplicate selection, success, filter order, text limit, presentation cap, cancellation, missing and failed store operations, invalidation, repeated refresh/compare, opening both snapshots, MainViewModel navigation, DI construction, and existing catalog/search/tag/saved-query regressions.
