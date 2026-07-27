# OpenSorSe 1.2 User Guide

## Safety boundary

> Watched folders automate detection and analysis, not file modification.

Watching follows:

`automatic detection and analysis → manual review → explicit approval → safe execution`

No watched-folder event can automatically rename, move, delete, deduplicate, or restructure a real folder. Suggestions become ordinary v1.1 Change Plans and must be reviewed, approved, revalidated, and explicitly applied. Existing Operation History, rollback, recovery, and Undo remain responsible for attempted file changes.

## Add and manage a watched folder

1. Open **Watched Folders**.
2. Enter an absolute folder path and optional display name.
3. Select **Add watched folder**.
4. Open the configuration and review subfolder, analysis, AI, notification, quiet-period, size-limit, scan-profile, sorting-recipe, ignored-path, and ignore-pattern settings.
5. Select **Save watched-folder settings** after editing.

AI analysis is disabled for a new watched folder unless explicitly enabled there. The global AI switch, compatible capability, endpoint, and selected available Ollama model must also be ready before any request can run.

Folder-structure AI uses requests of at most 12 files and processes at most 120 affected items in one scan/retry cycle. A larger backlog remains explicitly pending for a later retry, keeping startup and offline reconciliation cancellable and bounded.

The sorting recipe value `current` means the rules most recently saved in the current session's **Rule Editor**. v1.2 does not yet include a persistent named recipe library; an unrecognized recipe ID safely resolves to no rules, and the current-session rules must be saved again after restart.

The scan-profile field is persisted, but the shipped processing behavior is the existing `default` profile. v1.2 does not include a separate scan-profile library/editor.

Use:

- **Pause** to stop watching while preserving the configuration and catalogue.
- **Resume** to restart watching and reconcile changes made while paused.
- **Scan changes now** to enumerate current metadata and incrementally analyse only differences.
- **Full reconciliation** to compare the saved catalogue with the real folder after suspected missed events.
- **Review suggestions** to open the newest non-mutating watched-folder Change Plan.
- **Retry failed AI analysis** to retry only catalogue items marked pending or failed, without repeating completed AI or successful deterministic catalogue work.
- **Open folder** to ask the operating system to show the selected root.
- **Remove from watch list** to remove only the configuration. The real folder, its files, dedicated watched catalogue, and grouped activity history are not deleted. The separate opt-in Saved scans catalogue is not used or evicted by watcher updates.

## Incremental scanning

Watcher events are hints. OpenSorSe waits for the configured quiet period, groups duplicates and bursts, rejects paths outside the root, applies ignore rules, and probes the actual filesystem.

For a changed file, OpenSorSe compares stable identity, path, size, timestamps, and attributes. New or content-changing files wait for two stable observations. Unchanged files retain previous analysis. Metadata-only changes do not trigger hashing, OCR, or content extraction. Rename and move changes retain analysis when identity and content evidence are unchanged. Deleted files are removed from the current watched catalogue only after reconciliation evidence; the history is preserved.

Exact-duplicate groups are recalculated from retained and new hashes without re-reading unchanged file content. Deterministic rules run only for affected files. Suggestions are recorded with deterministic or AI provenance.

## Full reconciliation, restart, pause, and offline storage

OpenSorSe reconciles enabled watched folders at startup before treating the saved catalogue as current. This detects additions, edits, removals, moves, and renames that occurred while the application was closed.

Resume and reconnect also queue reconciliation. Watcher overflow is visible and requires reconciliation. While OpenSorSe remains running it requests a full metadata reconciliation at least every 24 hours. Manual reconciliation is always available.

A missing, disconnected, or inaccessible root remains configured. OpenSorSe does not erase its catalogue or history. When the exact root path returns, watching restarts and a reconnect reconciliation is queued. If the root itself was renamed, edit by removing the old configuration and adding the new root; v1.2 does not search the drive and guess.

## Ignore rules

Exact ignored paths can be absolute or relative to the watched root. A directory ignore excludes that directory and its descendants. Patterns use `*`, `?`, and `**`; examples are `*.bak` and `Archive/**`.

Built-in visible policy excludes:

- `~$*`, `*.tmp`, `*.temp`, `*.part`, `*.partial`;
- `*.crdownload`, `*.download`, `*.opdownload`;
- `*.swp`, `*.swo`, `.~lock.*`;
- `.DS_Store`, `Thumbs.db`, and `desktop.ini`;
- `.opensorse` and OpenSorSe internal store filenames;
- reparse points;
- hidden files when **Ignore hidden files** is selected;
- files above the configured maximum size.

Canonical root checks prevent an ignore rule from affecting unrelated sibling paths. Ignored items are never passed to optional AI.

## Quiet period and unstable files

The default quiet period is two seconds and may be set from 0.25 to 300 seconds. It groups multi-stage saves and copy bursts.

Before metadata extraction, OCR, hashing, or AI, a changed file must remain the same size and modification time across two observations and be readable. OpenSorSe retries three observations in the immediate batch. A still-changing or locked file is marked deferred; the scan summary remains incomplete and reconciliation remains required. A later reconciliation retries deferred analysis even if the final metadata now matches the deferred observation.

## Activity and notifications

The page reports availability, watcher state, queue count, processing state, last detected change, last successful scan, last reconciliation, latest grouped summary, pending Change Plans, and errors.

Activity history stores meaningful batches such as watcher start/pause/resume, availability, overflow, batch detection, scan/reconciliation start and completion, deferral, AI attempt, Change Plan creation, manual scan, and configuration changes. Raw operating-system events are not written one by one.

Notification level can be **None**, **Errors only**, or **Summaries**, with separate choices for plan-ready and unavailable-folder notices. Notices are grouped and deduplicated. They say that suggestions or a plan are ready and never claim that files were organized when only analysis occurred.

## Review and apply suggestions

1. Select **Review suggestions**.
2. Inspect every proposed rename, move, or directory action.
3. Approve or reject actions individually; edit when appropriate.
4. Select **Validate Plan**.
5. Select **Apply Plan** only after reviewing the final summary and explicit confirmation.
6. Inspect **Operation History**.
7. Use existing conflict-aware **Undo** only when offered.

OpenSorSe-generated file events are matched to journal actions and verified resulting identities. They update the watched catalogue without generating a recursive plan or repeated AI analysis.
