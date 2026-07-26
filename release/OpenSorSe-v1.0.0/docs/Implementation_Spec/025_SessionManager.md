# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 025 |
| Component | Session Manager |
| Project | OpenSorSe.Application |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The Session Manager is responsible for creating, tracking, and closing application sessions.

Each scan performed by OpenSorSe is represented as a unique session.

---

# Why

Grouping all processing under a session provides a consistent way to track scans, organize logs, support undo operations, and maintain processing history.

---

# Responsibilities

The Session Manager shall:

- Create a new session.
- Generate a unique session identifier.
- Track session status.
- Record session start time.
- Record session completion time.
- Record session outcome.
- Close completed sessions.

---

# Does NOT

The Session Manager must NOT:

- Scan files.
- Move files.
- Execute rules.
- Calculate hashes.
- Modify the filesystem.
- Store business logic.

---

# Inputs

- Scan requests.
- Pipeline status updates.

---

# Outputs

The component returns:

- Session information.
- Session identifier.
- Session statistics.

---

# Workflow

1. Receive a scan request.
2. Create a new session.
3. Assign a unique session ID.
4. Track pipeline progress.
5. Record completion status.
6. Close the session.

---

# Assumptions

- Logging Service is available.
- Scan Orchestrator is available.

---

# Acceptance Criteria

The implementation is complete when:

- Every scan creates a unique session.
- Session status updates correctly.
- Completion status is recorded.
- Failed sessions are tracked.
- Unit tests pass.

---

# Future

Not part of v0.1:

- Session resume.
- Session comparison.
- Cloud session synchronization.
- Multi-user sessions.

---

# Dependencies

Depends on:

- 024 - Scan Orchestrator
- 011 - Logging Service

Required by:

- 020 - Log Viewer
- 021 - Undo History

---

# Autonomous v0.1 Decisions

The draft does not define session models, concurrency, failure behavior, closure semantics, or persistence. v0.1 adds process-lifetime immutable `ProcessingSession` snapshots. Each run creates a GUID-derived `session:` identifier, records UTC lifecycle timestamps, and exposes creation-order snapshots. The manager consumes the completed Specification 024 orchestrator directly; it does not create tasks, scan itself, persist data, or execute planned actions.

Unexpected orchestrator exceptions produce a user-safe failed session rather than discarding the record. Cancellation produces a terminal cancelled session with any reached processing result when supplied. Closing changes only in-memory terminal session state. Session persistence, resume, multi-user ownership, history integration, and session comparison remain deferred.
