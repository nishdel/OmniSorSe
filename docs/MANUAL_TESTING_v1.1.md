# OpenSorSe 1.1 Manual Smoke-Test Checklist

Use only a disposable temporary directory containing copied test files. Never test against personal originals, the repository, or a synchronized production folder.

## Review Changes

- [ ] Scan the disposable root and generate one rename suggestion.
- [ ] Accepting it opens a Change Plan and does not rename the file.
- [ ] Approve/reject one action and verify counts.
- [ ] Edit filename and destination; verify prior validation becomes invalid.
- [ ] Filter rename/move/folder and warning/conflict rows.
- [ ] Approve all safe and deselect all.
- [ ] Validate and confirm Apply remains disabled for an occupied destination.
- [ ] Verify the final summary counts and explicit confirmation.
- [ ] Return to Files without Apply and verify no mutation.

## Execution and journal

- [ ] Apply one rename and verify old/new paths and Operation History.
- [ ] Apply a move into a newly created folder and verify all three supported action types.
- [ ] Test spaces, German characters, and other Unicode filenames.
- [ ] Test a case-only rename on Windows.
- [ ] Introduce a destination collision after approval; Apply must fail pre-execution without overwrite.
- [ ] Modify/delete/rename a source after approval; revalidation must mark it stale.
- [ ] Request cancellation during a multi-action disposable plan; verify a safe terminal/rollback state.
- [ ] Close/restart the app and confirm history and Undo availability remain visible.
- [ ] Copy a report and verify it includes paths/results but no file contents or AI prompt.

## Undo

- [ ] Undo a rename, move, and complete combined plan.
- [ ] Undo an OpenSorSe-created empty directory.
- [ ] Put a file in an OpenSorSe-created directory; Undo must preserve it and report a conflict.
- [ ] Modify a resulting file; Undo must be blocked.
- [ ] Occupy the original path; Undo must not overwrite it.
- [ ] Apply a later dependent operation; earlier Undo must be blocked.
- [ ] Verify partial Undo is labelled partial and every action detail is journalled.

## Interruption and errors

- [ ] Using a disposable copy/debug harness, leave a running journal record and restart; it must become Interrupted after path inspection.
- [ ] Simulate access denied/file lock where supported; verify safe category and retry after release.
- [ ] Verify rollback-partial wording never claims complete rollback.
- [ ] Inspect Change Plan and Operation Journal corrupt-data behavior with backed-up application data.

## Regression

- [ ] Scan/cancel, duplicates, Files filters/details, OCR availability, Meaning Search, Saved scans/search/comparison, tags, Settings, Help, diagnostics, and Structure history.
- [ ] AI disabled: no provider request.
- [ ] AI malformed response: no plan mutation and no filesystem work.
- [ ] Duplicate review offers no deletion.
- [ ] Check About reports 1.1.0 and executable properties report 1.1.0.0.
