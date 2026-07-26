# 053 — Safe File Operations and Robustness

## Status

Implemented for stable OpenSorSe 1.1.0; automated validation complete, manual UI/platform checklist pending.

## Goal

Allow approved organization suggestions to rename/move files and create required folders without giving AI or bulk suggestions direct filesystem access.

## Implemented requirements

- Separate versioned Change Plan and Operation Journal records.
- Stable plan/action/operation IDs, portable identities, provenance, reason, edit/approval/validation/conflict state, timestamps, root, scan source, status, AI correlation, rollback, and Undo facts.
- Actions: `RenameFile`, `MoveFile`, `CreateDirectory`; unknown values fail validation.
- Review Changes screen with counts, filters, edits, approvals, validation, final summary, explicit Apply, progress, result, return, cancellation, and Undo.
- Creation/review/pre-execution validation for existence, identity/size/time/hash when present, lock, path/name/root/parent/reparse safety, collisions, duplicates, conflicts, order, case-only rename, and staleness.
- No overwrite, silent suffix, replace, deletion, or autonomous application.
- Deterministic mutation through `IChangePlanExecutionService` and `IFileSystemGateway`, durable journal transitions, verification, rollback, structured safe errors, and safe action-boundary cancellation.
- Whole or selected Undo with reverse order, identity/modification/occupancy/dependency/directory-empty checks, verification, and partial/conflict statuses.
- Startup inspection of Pending/Running records and explicit Interrupted status.
- Persistent history/detail/report workflow.
- Existing deterministic restructuring applies through the shared v1.1 service; AI acceptance creates a plan only.
- Atomic bounded plan/journal stores with absent-v1.1 compatibility, legacy journal-array normalization, and corrupt-data fail-safe behavior.

## Persistence

`change-plans.json` uses schema 1 and retains at most 100 plans with 1,000 actions per plan. `operation-journal.json` uses schema 1, retains at most 500 operations, caps the file at 128 MiB, and accepts a legacy raw array/schema-0 record representation. Both live beside `settings.json` under local application data.

Existing v1.0 settings/catalog/search/decision/content/semantic/structure data formats are unchanged.

## Safety invariants

1. Suggestion generation and parsing are read-only.
2. A plan is non-mutating until explicit confirmed Apply.
3. Only approved, currently valid actions are frozen for execution.
4. Pending/Running journal state is durable before user-file mutation.
5. Existing destinations are never overwritten.
6. Every supported successful action has recorded inverse information.
7. Success, rollback, and Undo require verification.
8. Unsafe recovery/Undo is blocked and explained.
9. ViewModels contain no raw user-file mutation.
10. Permanent deletion is unsupported.

## Verification

Tests use unique temporary directories and cover suggestion conversion, plan decisions/edits, validation/conflicts/staleness, serialization/migration, rename/move/directory/combined execution, Unicode/spaces, case-only rename, collision, cancellation, partial failure, rollback and verification failure, Undo success/blocking/partial/dependency/directory safety, journal restart/recovery/export, and ViewModel apply/history behavior. The complete v1.0 regression suite remains required.

## Honest limitations

Real filesystems do not provide a portable multi-action transaction. External processes, permissions, storage, hardware, or power failures can prevent rollback. Identity uses portable file metadata plus an optional content hash, not a persistent OS file handle. Manual recovery can be required and remains explicit in Operation Details.

