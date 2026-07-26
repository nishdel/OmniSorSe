# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 006 |
| Component | Rule Engine |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The Rule Engine evaluates every discovered file against the user's configured organization rules.

It determines what action, if any, should be taken for each file.

The Rule Engine never performs filesystem operations.

---

# Why

Discovering and classifying files is not enough.

The Rule Engine transforms information into decisions by applying the user's organization preferences consistently.

---

# Responsibilities

The Rule Engine shall:

- Evaluate every eligible file.
- Apply all configured organization rules.
- Determine the appropriate action for each file.
- Resolve rule priority when multiple rules match.
- Mark files that require no action.
- Produce a decision for every processed file.

---

# Does NOT

The Rule Engine must NOT:

- Move files.
- Rename files.
- Delete files.
- Modify the filesystem.
- Resolve filename conflicts.
- Execute the move plan.
- Access AI services.
- Calculate hashes.

---

# Inputs

- Collection of `FileEntry` objects.
- User-defined rules.
- Application configuration.

---

# Outputs

The component returns:

- Updated collection of `FileEntry` objects.
- Rule evaluation results.
- Rule evaluation statistics.

---

# Workflow

1. Receive the collection.
2. Load active rules.
3. Evaluate each file.
4. Determine the highest-priority matching rule.
5. Store the decision.
6. Continue until all files are processed.
7. Return the updated collection.

---

# Acceptance Criteria

The implementation is complete when:

- Every eligible file is evaluated.
- Matching rules are applied correctly.
- Rule priority is respected.
- Files with no matching rule remain unchanged.
- No filesystem modifications occur.
- Unit tests pass.

---

# Future

Not part of v0.1:

- AI-generated rules.
- Natural language rules.
- Rule simulation mode.
- Rule debugging tools.
- Conditional rule groups.

---

# Dependencies

Depends on:

- 004 - File Classifier
- 005 - Duplicate Detector

Required by:

- 007 - Move Planner

---

# v0.1 Contract

Specification 006 defines a deterministic, side-effect-free evaluator. It accepts enriched `FileEntry` values and ordered `FileRule` values, evaluates all enabled rules, and returns proposed `RuleDecision` values. It never accesses the filesystem, executes actions, invokes later execution components, persists data, publishes events, or uses AI.

Supported proposed action kinds are `NoAction`, `Move`, `Copy`, `Rename`, and `Delete`. Actions are validated without resolving paths or templates. Conditions are AND-combined and support category, duplicate status, case-insensitive extension and exact filename, and inclusive minimum or maximum metadata size. Every condition uses exactly its documented value field. Rules are validated as a complete request before any file is evaluated.

Every enabled matching rule ID is returned in supplied rule order. The winning rule is the matching rule with the highest numeric priority; ties retain supplied order. A file with no matching rule receives a `NoAction` decision. Results preserve file order and duplicate inputs; `FileEntry` values are not changed. Empty rules are valid and produce `NoAction` decisions.

The service contract is `Task<RuleEvaluationResult> IRuleEngine.EvaluateAsync(IReadOnlyCollection<FileEntry> files, IReadOnlyList<FileRule> rules, CancellationToken cancellationToken = default)`. Cancellation produces no partial result. Invalid input or rules throw before processing. Unexpected failures are logged, reported through `IErrorHandler`, and rethrown. The implementation is registered as `AddSingleton<IRuleEngine, RuleEngine>()`.

flowchart LR

subgraph Discovery
A["001 Scanner"]
B["002 Metadata"]
C["003 Hasher"]
D["004 Classifier"]
end

subgraph Analysis
E["005 Duplicate Detector"]
F["006 Rule Engine"]
end

subgraph Planning
G["007 Move Planner"]
H["008 Conflict Resolver"]
end

subgraph Execution
I["009 Move Executor"]
J["010 Undo Engine"]
end

A --> B --> C --> D --> E --> F --> G --> H --> I --> J
