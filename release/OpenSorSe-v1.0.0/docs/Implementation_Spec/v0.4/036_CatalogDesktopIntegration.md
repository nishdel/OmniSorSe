# Specification 036 — Catalog Desktop Integration

| Field | Value |
| --- | --- |
| Component | Settings, catalog list, Results restoration, shell composition |
| Target release | v0.4 |
| Depends on | Specification 035 and v0.3 Results Explorer |

## Functional requirements

Add `CatalogSettings.Enabled`, defaulting to `false`, with validation that the group is present. Add a labelled Settings checkbox explaining that enabling it stores display-safe scan metadata and accepted tags locally, not beside scanned files. Saving the setting retains existing configuration behavior and active-service restart messaging.

Add `NavigationDestination.Catalog` and a `CatalogViewModel`. Its list is initially empty, can be refreshed through an async command, exposes loading/empty/error status, and exposes an `Open` command only for a selected summary. Opening requests a complete entry through the store and raises an event to the shell; it must not independently navigate or access the filesystem.

On completed scan, `MainViewModel` first loads the normal in-memory snapshot. If catalog storage is enabled, it then stores an entry without delaying or invalidating the scan result on catalog failure. A successful save refreshes the catalog surface. Opening an entry loads the snapshot plus its accepted tags into `ResultsViewModel`, updates the dashboard, and navigates to Results. The results status must identify it as a saved snapshot, not live data.

When accepted tags change on a catalog-backed result snapshot, the shell replaces that same entry with the filtered accepted tag snapshot. Failed tag persistence is non-blocking and visible as a warning notification. Results stay usable.

## UI and states

| State | Required presentation |
| --- | --- |
| Disabled | Catalog is not written during scans; catalog screen explains how to opt in. |
| Empty | No saved scan snapshots are available. |
| Loading | Refresh/open is in progress; duplicate actions are disabled. |
| Persisted entry | Saved UTC time, file count, folder count, warning count, and snapshot disclaimer. |
| Unavailable | A generic local-catalog unavailable explanation; never raw paths, exception text, or JSON. |
| Oversized snapshot | Current scan remains available; a warning says it was not catalogued because it exceeded the documented limit. |

## Threading and cancellation

Commands use `AsyncRelayCommand`; all UI collection changes occur after awaits return to the captured context. Store calls use cancellation supplied by the view-model lifetime or command. Results local query cancellation continues to work unchanged. No catalog operation blocks scanning, UI rendering, or application shutdown.

## Test requirements

- Settings draft/configuration preserve the new default and enabled value.
- A completed scan with catalog disabled does not call the store.
- A successful enabled scan creates one entry; failure does not replace Results state.
- Refresh and open display an existing entry in Results with accepted tags restored.
- Repeated refresh/open and cancellation leave the view model in a usable state.

## Documentation requirements

README and safety statements must say the catalog is opt-in, local, bounded, and stores path metadata. Architecture text must not describe it as the future database or semantic-search index.
