# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 024 |
| Component | Scan Orchestrator |
| Project | OpenSorSe.Application |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The Scan Orchestrator coordinates the complete OpenSorSe processing pipeline.

It executes each processing stage in the correct order and manages communication between the UI and backend services.

---

# Why

The user should only need to perform one action ("Start Scan").

The Scan Orchestrator hides the complexity of the processing pipeline by coordinating all required components.

---

# Responsibilities

The Scan Orchestrator shall:

- Receive scan requests.
- Execute pipeline components in the correct order.
- Report overall progress.
- Handle cancellation.
- Stop processing on fatal errors.
- Return the final processing result.

---

# Does NOT

The Scan Orchestrator must NOT:

- Scan files itself.
- Read metadata.
- Calculate hashes.
- Execute rules.
- Move files.
- Contain business logic.
- Modify the filesystem directly.

---

# Inputs

- Scan request.
- User configuration.

---

# Outputs

The component returns:

- ScanResult.
- Progress updates.
- Processing summary.

---

# Workflow

1. Receive scan request.
2. Start File Scanner.
3. Start Metadata Extraction.
4. Start File Hasher.
5. Start File Classifier.
6. Start Duplicate Detector.
7. Start Rule Engine.
8. Start Move Planner.
9. Start Conflict Resolver.
10. Return results to the UI.

---

# Assumptions

- All required services are available.
- Configuration has been loaded.
- User input has been validated.

---

# Acceptance Criteria

The implementation is complete when:

- Pipeline executes in the correct order.
- Progress is reported.
- Cancellation works.
- Fatal errors stop the pipeline safely.
- Results are returned successfully.
- Unit tests pass.

---

# Future

Not part of v0.1:

- Parallel pipeline execution.
- Pipeline plugins.
- Custom processing stages.
- Distributed processing.

---

# Dependencies

Depends on:

- 001–008 Core Pipeline Components

Required by:

- 015 Folder Selection
- 016 Scan Progress
- 017 Results View

---

# Autonomous v0.1 Decisions

The draft places orchestration in an unspecified Application project and omits rules input, result composition, stage progression, and cancellation-result semantics. v0.1 adds `OpenSorSe.Application`, accepts an explicit `ProcessingRequest` containing the established Scanner request and ordered rules (empty is valid), and returns all reached immutable stage results in `ProcessingResult`.

Stages run sequentially in this exact order: scanner, metadata, hashing, classification, duplicate detection, rule evaluation, planning, then lexical conflict resolution. A scanner-level cancelled result returns a partial cancelled processing result. Cancellation thrown by later stages remains standard `OperationCanceledException` because those stages cannot return their own partial results. No executor is invoked; actions remain planned and conflict-resolved only.
