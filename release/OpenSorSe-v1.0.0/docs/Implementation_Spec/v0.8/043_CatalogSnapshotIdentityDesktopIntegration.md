# Specification 043 - Catalog Snapshot Identity Desktop Integration

| Field | Value |
| --- | --- |
| Component | Saved Catalog ViewModel/view, shell persistence, and search context |
| Target release | v0.8 |
| Depends on | Specification 042 and v0.5 Catalog Search |

## Functional requirements

`MainViewModel` shall copy the completed desktop `ScanRequest.FolderPaths` into the new catalog entry. It shall not reconstruct scope from result directories or paths. The processing workflow awaits application-owned catalog persistence asynchronously before publishing its terminal shell state; failures remain recoverable and never change the completed Results snapshot. Persisted-tag change events likewise await their owned update path rather than abandoning an unobserved task.

`CatalogViewModel` shall expose a display-name input and a cancellable save command. It shall load the selected entry, replace only its display name through `IResultsCatalogStore.SaveAsync`, refresh summaries while preserving the selected ID, and raise `CatalogChanged` so dependent historical surfaces invalidate stale context. Whitespace clears the name. Invalid text is rejected before store access.

Catalog rows shall show name, timestamp, source scope, and existing counts. Legacy rows show `Unnamed snapshot` and `Source scope unknown`. Catalog search hits shall carry the optional name from their source entry; ranking, matching, hit bounds, and opening behavior remain unchanged.

## State and failure matrix

| State | Behavior |
| --- | --- |
| Disabled | No catalog read/write; name command disabled. |
| Empty | No selection or editable target; explanatory state remains. |
| Valid selection | Input reflects current label and can set/replace/clear it. |
| Selection changes | Input updates to the new row; stale selection is never written. |
| Invalid input | Local validation status; no store access. |
| Missing entry | Refresh guidance; no replacement is created. |
| Cancelled/superseded | Prior published list remains usable. |
| Corrupt/unavailable | Generic error; application-owned data is preserved. |
| Maximum source scope | Scan results remain available; catalog save is declined. |

## Accessibility and testing

The input has visible text, placeholder, and a direct button label. State does not rely on color. List rows have a useful name fallback and text source-scope fallback. Tests cover selection, set/replace/clear, invalid values, repeated execution, disabled/empty/missing states, source-root capture, search context, catalog invalidation, and navigation refresh.
