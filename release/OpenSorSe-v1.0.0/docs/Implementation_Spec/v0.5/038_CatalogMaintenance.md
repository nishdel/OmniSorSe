# Specification 038 — Catalog Maintenance Controls

| Field | Value |
| --- | --- |
| Component | Store removal/clear contract and Catalog UI controls |
| Target release | v0.5 |
| Depends on | v0.4 catalog schema and view model |

## Store requirements

`RemoveAsync(entryId, token)` returns false for an absent ID and otherwise writes an atomically updated envelope containing every remaining entry. `ClearAsync(token)` removes only the configured rooted catalog file when it exists. Both operations preserve malformed/unsupported-file safety: malformed data is reported and never automatically deleted. Both honor cancellation before lock acquisition and before I/O.

## UI requirements

The Catalog page shows **Remove selected snapshot** only when an entry is selected. It refreshes the list after success and updates status. **Clear all saved snapshots** is a request action only; it reveals user-safe scope text and a separate **Confirm clear all** action. A cancellation command hides confirmation without store access. Confirmation is disabled while work is active. These actions never appear as file-operation controls and must explicitly say they affect only local OpenSorSe catalog data.

## Error and safety requirements

Removal and clear catch I/O, authorization, invalid catalog, cancellation, and unknown failures with generic status. They do not remove settings, decision history, log files, selected folders, or user files. The catalog store's own temporary replacement files remain cleaned on failure.

## Acceptance tests

- Remove selected removes exactly that entry and leaves another entry searchable/openable.
- Clear request alone changes no persisted data; confirmation clears it.
- Disabled catalog and cancellation perform no store mutation.
- Malformed catalog remains on disk after remove/clear attempt.
