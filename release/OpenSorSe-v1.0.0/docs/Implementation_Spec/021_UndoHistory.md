# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 021 |
| Component | Operation History |
| Project | OpenSorSe.Desktop |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The Operation History view explains the current operation-history state.

In v0.1, it normally explains that no operation history exists because scans are read-only and no file operations are performed.

---

# Why

Users should understand why a completed scan does not appear as file-operation history and be confident that v0.1 did not change files.

---

# Responsibilities

The Operation History view shall:

- Display a clear v0.1 review-only empty state when no supplied session exists.
- Display explicitly supplied operation sessions if a later workflow provides them.
- Never fabricate sessions from completed scans.

---

# Does NOT

The Undo History view must NOT:

- Execute filesystem operations directly.
- Move files.
- Delete files.
- Scan folders.
- Calculate hashes.
- Execute organization rules.
- Modify undo history.

---

# Inputs

- Explicit operation-history sessions, when supplied by a later workflow.
- User interaction.

---

# Outputs

The component provides:

- Read-only operation-history presentation state.

---

# Workflow

1. Load the page.
2. Explain the review-only empty state when no session is supplied.
3. Display explicitly supplied session details when they exist.

---

# Assumptions

- No persistent history is required for v0.1.

---

# Acceptance Criteria

The implementation is complete when:

- The empty state explains why scans do not create history.
- No undo or file-operation control is exposed in v0.1.
- UI tests pass.

---

# Layout

+------------------------------------------------------------+
| Undo History                                               |
+------------------------------------------------------------+

Previous Sessions

------------------------------------------------------------

Today 14:25

✔ 214 files organized

------------------------------------------------------------

Yesterday 19:42

✔ 83 files organized

------------------------------------------------------------

Selected Session

Files Moved: 214

Folders Created: 8

Duration: 00:00:47

------------------------------------------------------------

[ View Details ]

[ Undo ]

[ Close ]

------------------------------------------------------------

Status

2 Undo Sessions Available

---

## Corrected v0.1 layout

The implemented Operation History page replaces the illustrative draft layout above. Its normal empty state says that OpenSorSe v0.1 is review-only, no file operations have been performed, and completed scans are not stored as persistent history. If a later workflow supplies operation-history sessions, they can be listed for review; no undo, confirmation, or filesystem-operation control is displayed by the v0.1 Desktop view.

# Future

Not part of v0.1:

- Selective undo.
- Multi-level undo.
- Redo support.
- Search.
- Undo previews.
- Session comparison.

---

# Dependencies

Depends on:

- 010 - Undo Engine
- 009 - Move Executor

Required by:

- None

---

# Autonomous v0.1 Decisions

No history repository, session model, or database contract exists. v0.1 accepts explicit ordered `UndoHistorySession` values from a later controller. A session wraps caller-supplied ordered `UndoRecord` values and a UTC completion time; this page does not discover, persist, or alter them.

The v0.1 Desktop heading is **Operation history**. Because v0.1 performs no file operations, a completed scan never creates an operation-history record. Its normal empty state explains that OpenSorSe is review-only, no file operations have been performed, and completed scans are not stored as persistent history. The Desktop view does not expose inert undo or confirmation controls. Persistent history, execution/undo orchestration, selective undo, redo, sessions inferred from files, and automatic ordering reversal remain deferred.
