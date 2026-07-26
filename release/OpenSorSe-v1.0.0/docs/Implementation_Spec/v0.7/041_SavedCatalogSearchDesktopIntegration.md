# Specification 041 — Saved Catalog Search Desktop Integration

| Field | Value |
| --- | --- |
| Component | Catalog Search ViewModel, view, shell composition, and workflow tests |
| Target release | v0.7 |
| Depends on | Specification 040, v0.5 catalog search, v0.6 user-managed tags |

## Functional requirements

`CatalogSearchViewModel` shall retain ad hoc v0.5 query behavior and add a separately bounded saved-search collection. It shall expose a name input, selected preset, saved-search status, loading/reset state, and commands to refresh, save the current query, run the selected preset, remove the selected preset, request reset, confirm reset, and cancel reset.

Saving requires catalog enabled, non-empty trimmed name/query, no case-insensitive duplicate name, and an available saved-search store. It creates a new opaque ID and UTC timestamps. Store success refreshes the list, selects the saved value, clears the name input, and does not automatically run or retain hits.

Running requires catalog enabled and a selected preset. It assigns the stored query to the current `QueryText` and awaits the existing `SearchAsync`, preserving its current catalog enumeration, v0.6 tag inputs, ranking, 200-hit cap, stale-version protection, status, and historical-open event.

Remove and confirmed reset operate even when catalog storage is disabled so users can remove private query text. Reset is two step. A malformed store may fail refresh/remove/save but must still permit confirmed reset. Catalog removal invalidates only current hits; it does not remove saved query text.

## Composition, lifecycle, and changed user flow

The Desktop composition root creates `JsonSavedCatalogSearchStore` beside `settings.json`. DI registers one singleton for `ISavedCatalogSearchStore`. `MainViewModel` accepts it through its full production constructor, creates the combined Catalog Search view model, refreshes presets at startup and when navigating to Catalog Search, and relies on existing disposal to cancel both query and preset operations.

No change is made to `ResultsQueryEngine`, catalog entry schema, catalog store, or v0.6 tag persistence. This ensures a saved query can find a user-managed tag only when that tag is already part of a current saved snapshot.

## UI states and accessibility

The Catalog Search view shall clearly separate **Saved searches** from current hits. Each row displays name, query text, and updated UTC time. Buttons use descriptive labels, commands expose disabled state, and status text explains empty, disabled, unavailable, capacity, duplicate, cancellation, and reset outcomes. The saved-query privacy disclosure must state that query text—not hits—is stored in OpenSorSe application data.

The view must remain usable with zero catalog entries, zero presets, malformed presets, catalog disabled, a running search, repeated navigation, and repeated commands. Bounded lists prevent large-control rendering.

## Failure recovery and tests

Preset failures never clear current search hits or disable ad hoc catalog search. Search failures never erase saved presets. A cancelled or superseded preset refresh retains the last valid list. Confirmed reset clears the visible list only after store success. Disposal cancels active preset and search operations and unsubscribes no new external events.

Desktop tests cover initial refresh, disabled save/run with permitted maintenance, successful save/run, query-to-v0.6-tag matching, duplicate-name rejection, remove, two-step reset, corrupt reset, cancellation, repeated commands, and unavailable stores. MainViewModel tests cover initialization/navigation wiring and preserve all v0.1-v0.6 regression behavior.
