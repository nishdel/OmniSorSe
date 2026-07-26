# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 010 |
| Component | Undo Engine |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The Undo Engine restores the filesystem to its previous state by reversing operations performed by the Move Executor.

It provides a safe rollback mechanism for completed move operations.

---

# Why

File organization should never be irreversible.

The Undo Engine allows users to recover from mistakes, incorrect rules, or unwanted organization changes.

---

# Responsibilities

The Undo Engine shall:

- Read the execution history.
- Reverse completed move operations.
- Restore files to their original locations.
- Continue processing when recoverable errors occur.
- Record the result of every undo operation.
- Produce an undo summary.

---

# Does NOT

The Undo Engine must NOT:

- Create move plans.
- Execute organization rules.
- Detect duplicates.
- Classify files.
- Calculate hashes.
- Use AI services.
- Modify files that were not part of the original execution.

---

# Inputs

- Execution history.
- Application configuration.

---

# Outputs

The component returns:

- Undo results.
- Successful undo operations.
- Failed undo operations.
- Undo statistics.
- Undo errors.

---

# Workflow

1. Load the execution history.
2. Validate recorded operations.
3. Reverse each completed move.
4. Restore original file locations.
5. Record undo results.
6. Return the undo summary.

---

# Assumptions

- Execution history exists.
- The history has not been modified.
- Files are still accessible.
- The user has confirmed the undo operation.

---

# Acceptance Criteria

The implementation is complete when:

- Previously moved files are restored.
- Undo operations are recorded.
- Recoverable errors do not stop processing.
- Files not managed by OpenSorSe remain untouched.
- Unit tests pass.

---

# Future

Not part of v0.1:

- Multi-level undo.
- Selective undo.
- Redo support.
- Undo history browser.
- Automatic rollback.

---

# Dependencies

Depends on:

- 009 - Move Executor

Required by:

- None

---

# v0.1 Contract

The Undo Engine consumes explicit `UndoRecord` values only; it does not load or persist history. It processes records in caller-supplied order, supports Move and Rename restoration through non-overwriting moves, and supports Copy undo by deleting only the explicit regular non-reparse `ResultPath`. Copy undo is path-based best effort because undo records carry no fingerprint.

All records are validated before mutation: IDs are ordinal-unique, paths are rooted and normalized, paths differ, timestamps are UTC, and kinds are supported. The engine never creates directories, overwrites paths, reverses caller order, performs redo, accesses history/database storage, or infers alternative paths. Cancellation retains completed outcomes and stops later records. Per-record issues are user-safe and do not expose exception details.


flowchart LR

A["001 File Scanner"]
--> B["002 File Metadata"]
--> C["003 File Hasher"]
--> D["004 File Classifier"]
--> E["005 Duplicate Detector"]
--> F["006 Rule Engine"]
--> G["007 Move Planner"]
--> H["008 Conflict Resolver"]
--> I["009 Move Executor"]
--> J["010 Undo Engine"]
