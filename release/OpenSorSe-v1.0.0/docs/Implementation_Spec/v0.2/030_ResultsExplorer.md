# 030 — Results Explorer

| Field | Value |
| --- | --- |
| Spec ID | 030 |
| Component | Results Explorer |
| Project | `OpenSorSe.Desktop` |
| Target release | v0.2 |
| Status | Implemented |
| Depends on | 029 Results Snapshot Projection; existing v0.1 Results destination and notification infrastructure |
| Required by | 031 Exact-Duplicate Review uses its filtering and selection route |

## Purpose

Extend the existing Avalonia Results destination into a bounded, read-only explorer for a completed in-memory `ResultsSnapshot`. Users can locate a result by text, filter it by known analysis properties, sort it deterministically, page through it, and inspect its details without starting another scan or modifying any file.

This is not a replacement for a future library search subsystem. It is a local, process-lifetime review of one completed scan.

## User value

The v0.1 static list is practical for a handful of files but inadequate for a realistic scan. The explorer makes results understandable and usable: a user can narrow to duplicate files, a file type, a category, or an operation status, then inspect exactly what OpenSorSe observed.

## Responsibilities

- Load and retain one immutable `ResultsSnapshot` supplied by the application layer.
- Offer bounded text query, deterministic filters, sorting, paging, and row selection.
- Display a concise summary of the active result set and the total snapshot.
- Present selected file metadata, duplicate state, and planned-operation information as read-only details.
- Provide clear loading, empty, error, and no-results states.
- Expose an internal navigation event/command that opens the duplicate-review pane in the existing Results destination.
- Preserve the existing Results summary, directories, operations, and warning concepts where they remain useful; do not silently drop v0.1 information.

## Non-responsibilities

The explorer must not:

- Perform a filesystem search, read a file, test whether a path still exists, or rescan.
- Persist query text, filters, sorting, selections, result data, or recent searches.
- Implement full-text, semantic, OCR, or content search.
- Edit planned operations, rules, classification, duplicate groups, or metadata.
- Open, reveal, copy, move, rename, delete, overwrite, or otherwise act on user files.
- Invoke `IActionExecutor`, `IUndoEngine`, `IProcessingOrchestrator`, or a shell integration.
- Introduce a top-level navigation destination, database, index, package dependency, or configuration option.

## Inputs

| Input | Requirement |
| --- | --- |
| `ResultsSnapshot` | Required for a populated explorer. It is owned by the caller and treated as immutable. |
| `ResultsQuery` | Optional query state. Invalid filter enums, negative page indexes, zero page size, and unsupported sort fields are rejected or reset to safe defaults before presentation. |
| User interactions | Text change, filter selection, sort selection/direction, page navigation, row selection, clear filters, and duplicate-review navigation. |

## Outputs and proposed presentation models

Models may live in `OpenSorSe.Desktop.ViewModels` or a small Desktop presentation-model namespace. They must not leak Avalonia control types into the application layer.

```csharp
public sealed record ResultsQuery(
    string? Text,
    ResultDuplicateFilter DuplicateFilter,
    string? Extension,
    FileCategory? Category,
    ResultPlannedOperationFilter PlannedOperationFilter,
    ResultsSortField SortField,
    SortDirection SortDirection,
    int PageIndex,
    int PageSize,
    string? DuplicateGroupId = null);

public sealed record ResultsPage(
    IReadOnlyList<ResultFile> Items,
    int PageIndex,
    int PageSize,
    int TotalItemCount,
    int TotalPageCount);
```

`FileCategory` above refers to the existing deterministic Scanner enum or a display-safe mapping of it. If classification can be unavailable, the UI must offer an explicit “Unclassified/unknown” state rather than fabricating a category.

### Query rules

| Dimension | Required semantics |
| --- | --- |
| Text | Trim leading/trailing whitespace. An empty/whitespace query means no text restriction. Match file name, full path, normalized extension, and category display text using `StringComparison.OrdinalIgnoreCase`. It is not a wildcard, regular expression, fuzzy match, or content query. |
| Duplicate filter | `All`, `ExactDuplicatesOnly`, `UniqueOnly`, `UnknownOrUnavailable`. Exact duplicates are only rows whose upstream status is `Duplicate`. |
| Extension | One selected normalized extension or all. Do not infer MIME type. Include an explicit no-extension option if the snapshot contains such rows. |
| Category | One selected upstream deterministic category, unknown/unclassified, or all. |
| Planned-operation filter | `All`, `HasAcceptedOperation`, or `NoAcceptedOperation`. It is descriptive only. |
| Sorting | Support `Name`, `Path`, `Extension`, `Size`, `ModifiedTime`, and `DuplicateState`. Use a deterministic secondary tie-breaker of full path, then source index/ID. Missing size/time sort after known values in ascending order and before known values in descending order only if that convention is consistently documented and tested. |
| Paging | Default page size 200. Permit only a small approved fixed set such as 50, 100, 200, and 500; never allow an unbounded “all” page. Page index is zero-based internally and one-based in user text. Clamp/reset a page after filters change. |

The initial feature intentionally excludes a free-form query language, saved searches, arbitrary page size, and grouping in the main file grid.

`DuplicateGroupId` is an internal opaque route used only by specification 031 to show known group members in the explorer. It is never rendered, logged, persisted, or accepted as arbitrary user input.

### ViewModel state

The existing `ResultsViewModel` may be evolved or a child `ResultsExplorerViewModel` may be added. Prefer a child model if it keeps current v0.1 summary/review concepts readable. The selected approach must preserve a clear single owner for snapshot lifetime and avoid duplicate mutable copies of all rows.

Minimum observable state:

- current snapshot availability and a user-safe `StatusText`;
- current query/filter options and active-query summary;
- a bounded `ResultsPage` for binding;
- selected row and `SelectedResultDetails` derived from the immutable row;
- `IsLoading`, `HasResults`, `HasFilterResults`, `CanGoPreviousPage`, and `CanGoNextPage`;
- command availability that updates when query/page/selection changes;
- a `DuplicateReviewRequested` event/command with no executor or filesystem payload.

`SelectedResultDetails` shows only data already in the snapshot: full local path, size/modified time if available, classification, duplicate state, optional opaque group reference, and descriptive planned-operation rows. It must not show a raw hash and must not contain a “take action” button.

## Concrete services and processing flow

No new application service is required after 029. Querying can be a pure Desktop helper used by the ViewModel. If it becomes complex enough to require independent tests, introduce an internal `ResultsQueryEngine` with no filesystem, UI, or service-provider dependency.

```mermaid
flowchart LR
    A["Immutable ResultsSnapshot"] --> B["Copy/validate query state"]
    B --> C["Filter rows in memory"]
    C --> D["Stable deterministic sort"]
    D --> E["Take bounded page"]
    E --> F["ResultsPage + selected details"]
    F --> G["Avalonia Results view"]
```

1. On completed v0.1 session, the shell supplies a `ResultsSnapshot` produced by 029.
2. The ViewModel sets safe default query state: all filters, name ascending, page size 200, first page, no selected row.
3. A user query or filter change invalidates page selection, resets the page to the first valid page, and starts/restarts projection work.
4. The query engine filters, sorts, and returns only the requested page plus totals.
5. The ViewModel validates that a previous selection still exists; otherwise it clears selection.
6. The UI displays the page, current range, totals, selected details, and read-only safety message.
7. Selecting duplicate review changes only child/pane state in Results; returning restores the last valid explorer query when practical.

## Loading, cancellation, and threading

Query evaluation must not block the Avalonia UI thread on a large result set.

- For small snapshots, synchronous evaluation is acceptable only if measured to be imperceptible.
- For a sizeable snapshot or any input-changing query, evaluate on a background worker/task and marshal final state changes to the UI thread.
- Assign each evaluation a monotonically increasing request/version ID and a linked cancellation token. Discard a result if it is cancelled or no longer the latest request.
- Coalesce rapid text changes with a short, documented debounce (for example 150–250 ms) only when it demonstrably improves responsiveness; tests must control the scheduler/timer.
- `IsLoading` means query work is pending; continue showing the last valid bounded page where possible and avoid clearing it into a blank flicker.
- Cancellation here cancels only stale result-query work. It never cancels a completed scan and never changes user files.
- Do not show a fictional percentage. A concise “Updating results…” state is sufficient.

## Validation and error handling

| Scenario | Required behaviour |
| --- | --- |
| No snapshot | Show “No completed scan results are available” with a route back to Scan; disable explorer-specific actions. |
| Null/malformed query input | Restore documented defaults and log safe structural context; never crash the shell. |
| Filter returns zero rows | Preserve the snapshot, show the active-filter empty state, and make Clear filters visible. |
| Page index beyond total pages | Clamp to the final page or reset to the first page after a filter change. |
| Selection no longer in filtered set | Clear it and do not retain stale detail values. |
| Background query fails unexpectedly | Preserve last known valid page, clear `IsLoading`, show a generic user-safe error, and log exception/counters without row paths. |
| Snapshot is replaced after a later scan | Cancel outstanding query work, clear selection, reset to safe defaults, and load only the new snapshot. |

## Logging, privacy, and safety

- Log only lifecycle events, selected filter categories, aggregate counts, and unexpected exceptions. Do not log query text, complete paths, filenames, rule names, destination paths, or selected file metadata by default.
- All filtering occurs locally in application memory. No analytics, network call, index, or persistence is allowed.
- Displaying a path is not permission to verify, resolve, follow, open, or mutate it. The explorer must never call `File.Exists`, `Directory.Exists`, `FileInfo`, or `DirectoryInfo` for result rows.
- The ViewModel and XAML must contain no command that reaches `IActionExecutor`, `IUndoEngine`, system shell APIs, clipboard APIs, or the native picker.
- A visible footer/help text states: “Results are read-only. OpenSorSe does not change files from this screen.”

## UI integration

Modify the existing `ResultsView.axaml` and its existing presentation model; do not redesign `MainWindow`, the shell navigation list, Scan, or other v0.1 pages.

Suggested layout within the existing Results content host:

```text
Results
[query________________] [duplicate v] [extension v] [category v] [operation v] [Clear]
Showing 1–200 of 12,345 files                         [Exact duplicates (42)]
---------------------------------------------------------------------------------
Name | Path | Type | Size | Modified | Duplicate | Planned operation
... bounded page rows ...
---------------------------------------------------------------------------------
[Previous] Page 1 of 62 [Next]

Selected file (read-only)
Path, metadata, classification, duplicate state, planned-operation summary
OpenSorSe does not change, move, rename, or delete files from this screen.
```

Use Avalonia controls already available through the existing package set. Prefer a virtualized item control/data grid only after confirming its accessibility and testability in the installed Avalonia version; paging is still mandatory even if the control virtualizes. Do not add a third-party grid package solely for this release.

### Empty and error states

- **No scan:** no completed results exist; primary action navigates to Scan.
- **No filter matches:** the scan contains results but active filters match none; Clear filters is the recovery action.
- **No files:** a completed scan found no files; directories/scan warnings remain available and duplicate review is disabled.
- **Projection/query failure:** user-safe explanation, previous valid state preserved where possible, no technical error text in UI.
- **Duplicate review unavailable:** shown only when 029 reported that duplicate data was unavailable; the explorer remains usable.

## Dependency injection and configuration

No DI service is required if the query engine remains private and pure. If a query engine is introduced for testability, register it as a singleton only if it is stateless; otherwise construct it directly in the ViewModel. Do not add configuration, settings UI, environment variables, application-data files, databases, or package dependencies.

## Platform-specific behaviour

- Matching is ordinal case-insensitive for deterministic result review, regardless of host filesystem case sensitivity. This is display/query behaviour, not filesystem identity comparison.
- Present long, Unicode, Windows drive/UNC, and POSIX-style paths as strings without attempting to validate them on the current platform.
- Layout must remain usable at the supported local desktop scale and with a narrow Results pane. Full cross-platform certification remains out of scope.
- The feature does not follow symbolic links or reparse points; it only displays prior scanner output.

## Test requirements

### Unit and integration tests

- Text matching for file name, full path, extension, and category; whitespace, Unicode, casing, punctuation, and no-match inputs.
- Every filter independently and representative combinations of filters.
- Stable sorting, secondary ordering, null size/date ordering, and unsupported enum/input defence.
- Page count/range math for zero, one, exact-boundary, and 10,000+ row datasets.
- Snapshot replacement, stale selection removal, and stale background-result suppression.
- Integration from a 029 snapshot to explorer output, preserving warnings, operations, directories, and group references.

### ViewModel tests

- Default/no-snapshot state; populated initial state; empty completed scan; no-filter-match state.
- Query reset, filter reset, sort changes, next/previous bounds, page-size changes, row selection, and selected-detail values.
- Loading state, cancellation of stale query work, error retention, property notifications, and command enablement.
- Duplicate-review request only changes presentation/navigation state and supplies no mutable/execution object.

### Manual GUI tests

- Run a small scan, a controlled duplicate scan, and a realistic-folder scan; filter and sort results in each.
- Type rapidly into the query, resize the window, change pages, clear filters, and switch to/from duplicate review without a UI freeze.
- Verify a result's displayed path and metadata agree with the existing v0.1 result data without opening the file.
- Confirm all empty/error/read-only messages are understandable and accessible by keyboard.
- Close the application during no query, during a large query, and after result exploration; no crash or write occurs.

### Filesystem and safety tests

Use fixture outputs that include inaccessible folders, deleted-during-scan entries, reparse/symbolic-link entries skipped or reported by the scanner, duplicate files, unique files, absent metadata, permissions issues, and long/unicode paths. Capture a before/after manifest of the fixture root and assert it is unchanged. Static review plus a fake/spy must demonstrate no executor, undo, shell, file-open, or write API is wired.

## Acceptance criteria

- A user can filter, sort, page, and inspect a completed scan result without starting a new scan.
- Text and categorical filters follow the documented deterministic semantics.
- The UI never renders more than the configured bounded page of result rows, even for a large snapshot.
- Query changes do not freeze the UI or apply stale results over newer input.
- Selected file details exactly reflect immutable snapshot data and contain no action control.
- All empty, loading, error, cancellation-adjacent, and no-results states are clear and safe.
- No filesystem, persistence, executor, undo, shell, network, or configuration behaviour is added.
- Existing v0.1 Results summary/warnings remain accessible and focused tests pass.

## Definition of done

- The existing Results destination hosts the explorer without a shell redesign.
- Automated unit, integration, ViewModel, large-data, and safety tests are added and pass where the environment permits.
- Manual GUI validation confirms responsiveness and read-only behaviour on a realistic result set.
- Review confirms no destructive affordance, mutation path, or unbounded result rendering was introduced.
- 031 can consume the explorer’s group-filter/navigation contract without duplicating query logic.

## Deferred work

- Saved searches, result/session persistence, result export, clipboard copy, file opening/revealing, tags, annotations, column customization, bulk selection, custom filters, and global search.
- Keyword/semantic/content search, indexing, OCR, readers, AI explanations, and AI classification.
- Editing rules or plans, file actions, live filesystem validation, execution, undo, and history.

## Implementation note (v0.2)

The implementation uses a pure `ResultsQueryEngine` plus a versioned, cancellable background evaluation task in `ResultsViewModel`. A newer query cancels only stale local query work; it cannot cancel the completed scan. The UI binds only the bounded page rows and derives selected details from the immutable snapshot. No debounce was added because the versioned cancellation path keeps query semantics deterministic without introducing timer or scheduler state.
