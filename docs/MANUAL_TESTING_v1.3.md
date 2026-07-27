# OpenSorSe 1.3 Manual Testing

Run on a disposable test tree. Never use irreplaceable files for mutation/Undo checks.

## Release identity

- [ ] Branch is `v1.3-workflow-profiles`.
- [ ] About displays `1.3`; binary product/informational version is `1.3.0`; file/assembly version is `1.3.0.0`.
- [ ] Workflows appears in primary navigation.

## Library and editor

- [ ] All five profiles and four recipes appear after first start.
- [ ] Built-ins can be exported/duplicated but not edited, disabled, archived, or deleted.
- [ ] Create, rename, edit, disable/enable, archive/restore, and safely delete an unreferenced user profile.
- [ ] Create/duplicate/edit/archive/restore a recipe.
- [ ] Search and each origin/capability/file/archive filter produce understandable lists.
- [ ] Usage counts update for watched assignments and profile-recipe dependencies.
- [ ] Deletion of a referenced recipe/profile is blocked with a dependency message.

## Template safety

- [ ] Preview the invoice date/vendor example and inspect every explanation field.
- [ ] Try missing required values and fallback values.
- [ ] Try `../Outside`, a rooted destination, invalid characters, `CON`, `NUL.txt`, a long name/path, Unicode, and an occupied destination.
- [ ] Confirm invalid previews never create files/directories.
- [ ] Put braces and path separators in an AI-marked representative value; confirm they remain data and cannot become template syntax.

## Manual and watched integration

- [ ] Select each profile on Scan and verify its summary.
- [ ] Apply one-time narrowing; confirm the saved profile revision is unchanged.
- [ ] Save adjusted settings as a new profile.
- [ ] Assign one profile and multiple permitted recipes to a watched folder and restart.
- [ ] Edit the assigned profile; verify a reconciliation records the new revision.
- [ ] Archive/disable the assigned profile and recipe in turn; verify **Profile unavailable — review configuration** and no fallback.
- [ ] Test a migrated `default` profile and deliberately replace any legacy `current` recipe.

## Change Plan boundary

- [ ] Generate a recipe proposal and inspect source profile/recipe/revisions, values, evidence, deterministic/AI status, warnings, and unresolved fields.
- [ ] Confirm source files and destination directories are unchanged before Apply.
- [ ] Reject an action and confirm it is not executed.
- [ ] Approve, validate, explicitly Apply on disposable files, verify Operation History, then Undo.
- [ ] Retest occupied destination, stale source, locked source, cancellation, interruption recovery, rollback, and non-empty directory Undo protection from the v1.1 checklist.

## AI

- [ ] Global-off blocks a profile that permits AI.
- [ ] Profile-off blocks globally enabled AI.
- [ ] Test missing-classification, after-extraction, selected-file-type, and explicit-retry policies.
- [ ] Missing model/provider and failed/cancelled requests remain safe and retryable.
- [ ] Deterministic failures do not automatically invoke AI.

## Import, recovery, privacy

- [ ] Export/import profile and recipe JSON as copy.
- [ ] Exercise skip, cancel, and confirmed user-item replacement conflicts.
- [ ] Confirm built-in replacement, missing dependencies, future schema, malicious traversal, deep, large, and destructive-rule inputs are rejected.
- [ ] Corrupt a disposable workflow store; verify the original and diagnostic copy remain and built-ins load.
- [ ] Export diagnostics and confirm no document contents, provider endpoint/model, credentials, or secrets appear.

## v1.2 regression

- [ ] Run create/modify/rename/move/delete, pause/resume, offline restart, reconnect, overflow, daily/manual reconciliation, ignore, stability, and OpenSorSe self-event scenarios.
- [ ] Confirm no watched cycle calls Apply automatically.
