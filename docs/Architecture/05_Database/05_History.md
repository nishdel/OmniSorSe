# History

> This document defines the History component, which is responsible for recording the lifecycle and processing history of documents managed by OpenSorSe.

## Implementation status

The broader document-lifecycle/database design remains future architecture. OpenSorSe 1.1 implements a separate bounded local **Operation Journal** in `operation-journal.json`. It is an action-level audit and recovery record for attempted Change Plan mutations, rollback, interruption, and Undo—not a general document event database or live filesystem monitor.

The existing catalog and `structure-history.json` remain separate and readable. Change Plans live in `change-plans.json`; they record proposed/review state, not actual attempts. The journal is versioned, atomically replaced after every state transition, accepts legacy raw-array/schema-0 data, and fails gracefully on corrupt or unsupported data.

Each operation records source plan, timestamps, app version, initiating feature, root, status, cancellation, summary, and bounded actions with original/intended/actual paths, pre/post identity, suggestion source, validation/execution/error, rollback, Undo, and optional AI correlation fields. It excludes file contents, extracted text, prompt bodies, and raw AI responses.

---

## Purpose

The History component maintains a persistent record of significant events that occur throughout a document's lifecycle.

Its primary purpose is to provide traceability, auditing, diagnostics, and historical context for documents processed by the application.

History records describe **what happened**, **when it happened**, and **which component performed the operation**.

---

# Responsibilities

The History component is responsible for:

* Recording document lifecycle events.
* Maintaining processing history.
* Supporting auditing and diagnostics.
* Tracking significant document changes.
* Providing historical information to other subsystems.

---

# Scope

### In Scope

* Scan history
* Processing events
* AI processing history
* Rule execution history
* File operation history
* Audit information

### Out of Scope

The History component is **not** responsible for:

* Application logging
* Error reporting
* AI results
* Metadata storage
* User settings
* Database backups

These responsibilities belong to other architectural components.

---

# Architectural Overview

The History component records events generated throughout the document processing pipeline.

```mermaid id="pcnq8m"
flowchart LR

Scanner["Scanner"]

Readers["Readers"]

AI["AI"]

Rules["Rules"]

History["History"]

Database["Database"]

Scanner --> History

Readers --> History

AI --> History

Rules --> History

History --> Database
```

History provides a chronological record of significant processing events without affecting application behavior.

---

# Event Lifecycle

A typical history record may be created during the following stages:

1. Document discovered.
2. Metadata extracted.
3. Content extracted.
4. AI enrichment completed.
5. Rule executed.
6. User action performed.
7. Document updated or removed.

Additional events may be introduced as the application evolves.

---

# Recorded Information

History records may include information such as:

| Information         | Description                             |
| ------------------- | --------------------------------------- |
| Timestamp           | When the event occurred.                |
| Event Type          | Type of operation performed.            |
| Component           | Component responsible for the event.    |
| Document Identifier | Document associated with the event.     |
| Operation Result    | Success, warning, or failure.           |
| Additional Context  | Optional details relevant to the event. |

The exact information recorded depends on the event type.

---

# Event Categories

Examples of recorded events include:

* Document discovered
* Scan completed
* Metadata updated
* OCR completed
* AI classification generated
* Summary generated
* Rename suggestion created
* File renamed
* File moved
* Rule executed
* User approval recorded

History should record significant events rather than every internal operation.

---

# Design Principles

The History component should remain:

* Append-only where practical.
* Chronological.
* Reliable.
* Independent of business logic.
* Easy to query.

History should provide an accurate representation of the document lifecycle.

---

# Error Handling

Failures to record history should not prevent normal application operation.

Examples include:

* Database write failures.
* Invalid history records.
* Missing event context.
* Storage limitations.

Whenever practical, the application should continue operating even if individual history events cannot be recorded.

---

# Future Considerations

The architecture should support future enhancements, including:

* User activity history.
* Undo and rollback support.
* Event filtering.
* Event retention policies.
* Timeline visualization.
* Plugin-defined history events.

These enhancements should preserve the component's primary responsibility of recording document lifecycle events.

---

# Related Documents

* [Database Overview](00_Overview.md)
* [Metadata](04_Metadata.md)
* [Settings](06_Settings.md)
* [Logging](../01_Core/03_Logging.md)
* [Rules Overview](../07-Rules/00_Overview.md)
