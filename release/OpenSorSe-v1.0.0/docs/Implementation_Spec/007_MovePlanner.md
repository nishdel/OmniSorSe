# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 007 |
| Component | Move Planner |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The Move Planner converts the decisions produced by the Rule Engine into a safe execution plan.

It prepares every required file operation but does not perform any filesystem changes.

---

# Why

Separating planning from execution allows OpenSorSe to:

- Preview planned changes.
- Validate operations before execution.
- Detect problems early.
- Support Undo.
- Prevent accidental file modifications.

---

# Responsibilities

The Move Planner shall:

- Create a move plan for every file with an action.
- Determine the source and destination paths.
- Verify that destination paths are valid.
- Skip files that require no action.
- Produce a complete execution plan.
- Return planning statistics.

---

# Does NOT

The Move Planner must NOT:

- Move files.
- Rename files.
- Delete files.
- Create directories.
- Resolve filename conflicts.
- Modify the filesystem.
- Execute operations.

---

# Inputs

- Collection of `FileEntry` objects.
- Rule Engine decisions.
- Application configuration.

---

# Outputs

The component returns:

- MovePlan
- Planned move operations.
- Planning statistics.
- Planning errors.

---

# Workflow

1. Receive processed files.
2. Read the Rule Engine decision.
3. Calculate the destination path.
4. Validate the planned operation.
5. Add the operation to the MovePlan.
6. Repeat for all files.
7. Return the completed MovePlan.

---

# Acceptance Criteria

The implementation is complete when:

- Every actionable file receives a planned operation.
- Destination paths are generated correctly.
- Invalid plans are reported.
- Files requiring no action are ignored.
- No filesystem modifications occur.
- Unit tests pass.

---

# Future

Not part of v0.1:

- Move optimization.
- Batch operations.
- Copy operations.
- Delete planning.
- Cloud storage planning.

---

# Dependencies

Depends on:

- 006 - Rule Engine

Required by:

- 008 - Conflict Resolver

---

# v0.1 Contract

Specification 007 is a deterministic, side-effect-free Action Planner. It consumes `RuleDecision` values directly, plans `Move`, `Copy`, `Rename`, and `Delete` actions, and ignores `NoAction`. It neither reads nor changes the filesystem, re-evaluates rules, resolves live conflicts, persists plans, publishes events, or uses AI.

`IActionPlanner.PlanAsync(IReadOnlyCollection<RuleDecision> decisions, CancellationToken cancellationToken = default)` returns immutable `PlannedOperation` values, planning statistics, and at most one recoverable `ActionPlanningIssue` per failed decision. Operation IDs are deterministic input-position values (`plan:<index>`). Operations and issues preserve input order and retain the original immutable `FileEntry` and selected-rule values.

Move and copy lexically combine a rooted destination directory with `FileMetadata.FileName`. Rename resolves case-sensitive `{name}`, `{extension}`, and `{category}` tokens and lexically combines the resulting filename with the source directory. Delete has no destination. The planner validates only lexical paths and names; existence, permissions, directories, disk space, overwrites, and live conflicts are deferred. Cancellation returns no partial result. Unexpected operation failures are logged, reported through `IErrorHandler`, and rethrown. The implementation is registered as `AddSingleton<IActionPlanner, ActionPlanner>()`.
