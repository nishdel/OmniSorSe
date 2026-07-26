# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 026 |
| Component | Application Controller |
| Project | OpenSorSe.Application |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The Application Controller coordinates communication between the user interface and the application services.

It manages application state and routes user actions to the appropriate components.

---

# Why

UI components should never communicate directly with backend services.

The Application Controller provides a single entry point for application workflows.

---

# Responsibilities

- Receive UI commands.
- Route commands.
- Manage application state.
- Coordinate navigation.
- Coordinate long-running operations.

---

# Does NOT

- Execute business logic.
- Scan files.
- Move files.
- Calculate hashes.
- Execute rules.

---

# Inputs

- User commands.
- Application events.

---

# Outputs

- Service requests.
- UI state updates.

---

# Workflow

1. Receive command.
2. Validate request.
3. Forward to appropriate service.
4. Update application state.
5. Notify UI.

---

# Acceptance Criteria

- UI communicates only through the controller.
- State updates correctly.
- Navigation functions correctly.
- Unit tests pass.

---

# Dependencies

Depends on:

- 024 Scan Orchestrator

Required by:

- All UI Views

---

# Autonomous v0.1 Decisions

The draft does not define UI command models, navigation abstractions, controller result types, or destructive-execution authority. v0.1 introduces a narrow UI-agnostic controller that accepts only an explicit `ProcessingRequest`, delegates to the session manager, and returns its terminal result. UI navigation remains presentation-owned; no Desktop types depend on the Application project through the controller.

The controller does not trigger execution, undo, persistence, events, dialogs, or background task management. Future controller methods that would authorize file operations require an explicit user-confirmation and execution safety contract.
