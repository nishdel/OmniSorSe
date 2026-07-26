# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 008 |
| Component | Conflict Resolver |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The Conflict Resolver validates the Move Plan and resolves any conflicts that would prevent successful execution.

It ensures that planned operations can be executed safely without unintended data loss.

---

# Why

During planning, multiple files may target the same destination or conflict with existing files.

Resolving these conflicts before execution prevents failed operations and allows the Move Executor to process a validated plan.

---

# Responsibilities

The Conflict Resolver shall:

- Detect filename conflicts.
- Detect destination path conflicts.
- Detect duplicate move operations.
- Resolve conflicts according to the configured strategy.
- Mark operations that cannot be resolved.
- Produce a validated Move Plan.

---

# Does NOT

The Conflict Resolver must NOT:

- Move files.
- Rename files on disk.
- Delete files.
- Modify the filesystem.
- Execute the Move Plan.
- Evaluate organization rules.

---

# Inputs

- MovePlan
- Application configuration.

---

# Outputs

The component returns:

- Validated MovePlan.
- Conflict resolution results.
- Conflict statistics.

---

# Workflow

1. Receive the MovePlan.
2. Validate every planned operation.
3. Detect conflicts.
4. Apply the configured conflict strategy.
5. Mark unresolved conflicts.
6. Return the validated MovePlan.

---

# Acceptance Criteria

The implementation is complete when:

- Filename conflicts are detected.
- Destination conflicts are detected.
- Duplicate operations are detected.
- Resolvable conflicts are handled.
- Unresolvable conflicts are reported.
- No filesystem modifications occur.
- Unit tests pass.

---

# Future

Not part of v0.1:

- Interactive conflict resolution.
- AI-assisted conflict resolution.
- Versioned filenames.
- Automatic merge strategies.
- User-defined conflict policies.

---

# Dependencies

Depends on:

- 007 - Move Planner

Required by:

- 009 - Move Executor

---

# v0.1 Contract

Specification 008 resolves only lexical, intra-plan conflicts among `PlannedOperation` values. It performs no filesystem inspection or changes. Its sole supported strategy is `ConflictResolutionStrategy.KeepFirst`, configured through `ConflictResolutionOptions`; null options use `ConflictResolutionOptions.Default`.

The public service is `Task<ConflictResolutionResult> IConflictResolver.ResolveAsync(IReadOnlyCollection<PlannedOperation> operations, ConflictResolutionOptions? options = null, CancellationToken cancellationToken = default)`. It returns accepted original operations, `ConflictResolutionStatistics`, and ordered `ConflictResolutionIssue` values. The implementation is constructed with `ILoggingService` and `IErrorHandler` and registered as `AddSingleton<IConflictResolver, ConflictResolver>()`.

Each valid operation must have a unique non-empty ID, supported kind, file, rooted source path, and a rooted destination for Move, Copy, or Rename. Delete requires a null destination. Before comparison, both `SourcePath` and `File.FullPath` are required to be rooted and normalized lexically with `Path.GetFullPath`; they must compare equal. Windows uses `StringComparer.OrdinalIgnoreCase`; other platforms use `StringComparer.Ordinal`.

The resolver processes input sequentially. It accepts the first valid non-conflicting operation and uses indexed O(n) lookups for operation IDs, exact signatures, accepted destinations, and accepted sources. A duplicate operation ID returns `DuplicateOperationId`. An identical accepted kind/source/destination signature returns `DuplicateOperation` and takes precedence over destination or source conflicts, producing exactly one issue. Later same-destination operations return `DestinationConflict`. Operations sharing a source conflict whenever either operation mutates its source; Copy-to-different-destination operations may share a source. Invalid operations return `InvalidOperation`. Rejected operations do not create later conflicts.

All returned operations and their `FileEntry` values remain immutable and preserve input order. At most one recoverable issue is returned per operation. Cancellation returns no partial result and is not reported as an error. Unexpected operation failures are logged, reported through `IErrorHandler`, and rethrown. User-facing issue messages contain no raw exception details.

v0.1 excludes live conflict checks, overwrite policies, suffixing, execution, rollback, undo, persistence, events, progress, UI, AI, and operation reordering.
