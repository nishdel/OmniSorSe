# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 009 |
| Component | Move Executor |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The Move Executor performs the filesystem operations defined in the validated Move Plan.

It is the only component responsible for modifying the filesystem.

---

# Why

Separating execution from planning ensures that all operations have been validated before any changes are made to the user's files.

This minimizes the risk of data loss and allows the application to present a complete preview before execution.

---

# Responsibilities

The Move Executor shall:

- Execute the validated Move Plan.
- Create destination directories when required.
- Move files safely.
- Record the result of every operation.
- Continue processing after recoverable errors.
- Report execution progress.
- Produce an execution summary.

---

# Does NOT

The Move Executor must NOT:

- Create move plans.
- Evaluate organization rules.
- Detect duplicates.
- Classify files.
- Calculate hashes.
- Use AI services.
- Resolve move conflicts.

---

# Inputs

- Validated MovePlan.
- Application configuration.

---

# Outputs

The component returns:

- Execution results.
- Successful operations.
- Failed operations.
- Execution statistics.
- Execution errors.

---

# Workflow

1. Receive the validated MovePlan.
2. Execute each planned operation.
3. Create destination folders if required.
4. Move the file.
5. Record the operation result.
6. Continue until all operations are processed.
7. Return the execution summary.

---

# Assumptions

- The MovePlan has already been validated.
- All conflicts have been resolved.
- The filesystem is accessible.
- The user has confirmed execution.

---

# Acceptance Criteria

The implementation is complete when:

- Files are moved correctly.
- Destination folders are created when necessary.
- Recoverable errors do not stop execution.
- Every operation is recorded.
- Progress reporting works.
- Unit tests pass.

---

# Future

Not part of v0.1:

- Parallel execution.
- Copy operations.
- Delete operations.
- Transaction-like execution.
- Automatic retry policies.

---

# Dependencies

Depends on:

- 008 - Conflict Resolver

Required by:

- 010 - Undo Engine

---

# v0.1 Contract

The Action Executor sequentially executes only accepted Move, Copy, and Rename operations. Delete is returned as an `UnsupportedOperation` skip. It revalidates source and destination state immediately before each operation, never overwrites or creates a directory, and produces ordered outcomes and deterministic `undo:<input-index>` records. Copy is streamed asynchronously with a 64 KiB pooled buffer and create-new destination semantics. Cancellation stops future operations and returns completed outcomes and undo records; an active cancelled copy is failed and its newly created partial destination is removed best-effort.

The executor uses `IActionExecutor.ExecuteAsync(IReadOnlyCollection<PlannedOperation>, IProgress<ActionExecutionProgress>?, CancellationToken)` and is registered as `IActionExecutor`. It performs no planning, conflict resolution, persistence, events, UI behavior, AI, undo execution, overwrite handling, retries, rollback, or automatic destination naming.
