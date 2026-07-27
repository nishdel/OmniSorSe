# OpenSorSe 1.2 Manual Smoke-Test Checklist

Use only disposable temporary folders and copied test files. Do not use personal originals, the repository, a production synchronized folder, or the root of a drive.

## Configuration and persistence

- [ ] About reports `1.2.0`; executable properties report `1.2.0.0`.
- [ ] Add one watched folder and confirm a startup reconciliation summary.
- [ ] Restart OpenSorSe and confirm the configuration, catalogue identity, timestamps, settings, and activity remain.
- [ ] Pause and resume; verify files and history remain and resume queues reconciliation.
- [ ] Remove from watch list after confirmation; verify the real folder and files remain.
- [ ] Add the same path with separator/case differences; verify it is rejected.
- [ ] Add parent and child roots; verify the overlap explanation prevents duplicate ownership.
- [ ] Disconnect removable storage; verify **Unavailable** without catalogue deletion. Reconnect and verify reconciliation.
- [ ] Rename the watched root externally; verify it remains unavailable and OpenSorSe does not guess another path.

## Watcher hints, debounce, and stability

- [ ] Create, modify, rename, move, and delete files and directories.
- [ ] Copy a large file slowly; verify analysis waits until stable or reports deferred/incomplete.
- [ ] Save from an application that uses temporary-name-and-replace; verify one final catalogue entry.
- [ ] Generate a burst of changes; verify one grouped summary rather than one notification per raw event.
- [ ] Lock a file temporarily; verify deferral and successful later reconciliation.
- [ ] Create `*.tmp`, `*.part`, `*.crdownload`, hidden, oversized, exact-ignored, directory-ignored, and pattern-ignored items; verify they are absent from analysis and AI.
- [ ] Change an ignored pattern and run full reconciliation.
- [ ] Use a debug/fake-watcher harness to report overflow; verify the visible warning and required reconciliation.

## Incremental and offline reconciliation

- [ ] After a baseline, change one file in a large disposable folder; verify unchanged files retain analysis timestamps.
- [ ] Rename and move one unchanged file; verify identity/path update without duplicate entries or repeated OCR/AI.
- [ ] Modify content; verify metadata/content extraction, hash, classification, duplicate group, and affected rules update.
- [ ] Delete a file externally; verify the current catalogue reports removal but historical saved data is not erased.
- [ ] Close OpenSorSe, make additions/removals/renames, restart, and verify the offline summary.
- [ ] Make changes while paused, resume, and verify the same.
- [ ] Select **Scan changes now** and **Full reconciliation** and compare precise status wording.
- [ ] Run a no-change reconciliation; verify no content reanalysis or AI request.
- [ ] Cancel a processing operation at a safe boundary and reconcile again.

## Change Plans and self-generated events

- [ ] Enable a deterministic sorting recipe and make one matching change; verify a Change Plan appears without filesystem mutation.
- [ ] Enable watched-folder AI plus global AI/capability/model settings; verify suggestions are batched and review-only.
- [ ] Disable watched-folder AI; verify no model request.
- [ ] Make Ollama/model unavailable; verify deterministic catalogue updates succeed and only AI remains retryable.
- [ ] Complete AI analysis for one file and fail it for another; verify **Retry failed AI analysis** sends only the pending/failed item.
- [ ] Review, approve, validate, and explicitly apply a disposable plan through **Review Changes**.
- [ ] Verify watcher events from Apply update the catalogue but do not create a recursive plan, repeated suggestion, or repeated AI request.
- [ ] Verify Operation History, rollback facts, restart recovery, and Undo remain functional.
- [ ] Modify a resulting file or occupy its original path; verify existing v1.1 Undo blocking still applies.

## Resource and presentation checks

- [ ] Add/edit/pause/resume repeatedly and observe that old watcher instances are disposed.
- [ ] Stress a fake watcher beyond the 256-batch bound; verify busy/backpressure and reconciliation-required states.
- [ ] Verify availability, state, queued count, processing status, last change/scan/reconciliation, summary, plans, warning, and error presentation.
- [ ] Verify notifications group meaningful changes and never say files were organized before Apply.
- [ ] Verify **None**, **Errors only**, plan-ready, and unavailable notification preferences are saved and honored.
- [ ] Inspect ordinary logs: they may contain policy decisions and redacted paths but no file contents, extracted text, AI prompt, credentials, or raw model response.

## Regression and completion

- [ ] Complete the [v1.1 checklist](MANUAL_TESTING_v1.1.md) on disposable data.
- [ ] Scan/cancel, Files, duplicates, OCR, Meaning Search, Saved scans/search/comparison, tags, Settings, Help, diagnostics, Folder plans, Review Changes, Operation History, recovery, rollback, and Undo.
- [ ] Run complete Debug and Release tests with no skip.
- [ ] Run `git diff --check` and inspect generated/untracked output.
