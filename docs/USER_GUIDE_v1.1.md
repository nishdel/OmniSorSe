# OpenSorSe 1.1 User Guide

## Preview-first organization

1. Scan a selected folder or open a current result set.
2. Generate a rename or folder-structure suggestion, or prepare a deterministic folder plan.
3. Accepting a suggestion creates a **Change Plan**. No source file changes at this point.
4. Open **Review Changes** and inspect current path, proposed path, action type, source, reason, warnings, conflicts, and approval state.
5. Use **Approve all safe**, **Deselect all**, or approve/reject one action. Edit a proposed filename or destination when needed.
6. Filter by action type or warning/conflict state. Review approved, rejected, invalid, and conflicting counts.
7. Select **Validate Plan**. Fix or reject blocking conflicts.
8. Select **Apply Plan**, inspect the final rename/move/folder/excluded/warning/overwrite summary, then confirm.
9. Open **Operation History** to inspect the verified result, rollback information, or debugging report.
10. Use **Undo** only when offered and confirm it. OpenSorSe revalidates the current files before restoring anything.

The Apply button stays disabled until at least one approved action has passed the latest validation and no approved blocking conflict remains. Editing or changing approval invalidates the previous validation.

## Supported actions

- Rename a regular file within its current directory.
- Move a regular file to another path under the selected root.
- Create a required directory under the selected root.

OpenSorSe never overwrites an existing file. It does not append numeric suffixes or replace a destination automatically. Permanent deletion is not supported in v1.1.

## Conflicts and stale plans

Validation reports user-safe categories such as source missing, source renamed externally, source changed, source locked, invalid filename, destination occupied, duplicate destination, conflicting source actions, missing parent, linked/out-of-root destination, execution-order conflict, and stale scan.

Revalidate without rerunning AI analysis. If the filesystem changed after approval, OpenSorSe blocks Apply until the revised state is reviewed. AI failures or retries do not alter an existing plan or any file.

## Undo guarantees and limits

A successful rename or move is undoable while the resulting file still exists, remains the same file, was not materially modified, and its original path is free. A directory is removed only when OpenSorSe created it and it is still empty.

Undo is blocked when restoration could overwrite newer data, when another OpenSorSe operation depends on the current path, or when identity/current-state checks fail. Safe actions in the same Undo request may still complete; the journal then reports a partial Undo.

OpenSorSe provides transaction-like safeguards, not a true filesystem transaction. Storage, hardware, permission, power, or external-process failures can require manual recovery.

## Operation History and reports

**Operation History** persists across restarts. Select an operation to see timestamps, status, root, succeeded/failed/skipped/rolled-back counts, paths, suggestion source, validation/execution state, errors, rollback, Undo, and optional AI model/request correlation IDs.

Use **Copy operation report** to copy a bounded human-readable report. Inspect it before sharing because it contains file paths. Reports do not include file contents, extracted text, AI prompts, credentials, or raw model responses.

By default the two v1.1 stores are:

- `%LOCALAPPDATA%\OpenSorSe\change-plans.json`
- `%LOCALAPPDATA%\OpenSorSe\operation-journal.json`

## Interrupted Operations

On startup, OpenSorSe inspects operations left Pending or Running. It marks them **Interrupted** and records only states supported by actual path and identity evidence. Ambiguous actions require manual review; OpenSorSe does not guess that a move or directory creation succeeded.

> OpenSorSe does not apply AI-generated or bulk file changes without a user-reviewed Change Plan. Supported file operations are recorded in the Operation Journal and are reversible unless later external changes make automatic restoration unsafe.
