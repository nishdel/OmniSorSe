# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 013 |
| Component | Main Window |
| Project | OpenSorSe.Desktop |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The Main Window serves as the primary user interface for OpenSorSe.

It provides access to all application features and hosts the major UI components.

---

# Why

The Main Window is the central entry point for users. It provides a consistent interface for managing scans, viewing results, and accessing application settings.

---

# Responsibilities

The Main Window shall:

- Display the application interface.
- Host all primary views.
- Manage navigation.
- Display application status.
- Coordinate user interactions.
- Handle application startup and shutdown events.

---

# Does NOT

The Main Window must NOT:

- Scan files.
- Execute business logic.
- Move files.
- Calculate hashes.
- Classify files.
- Execute organization rules.
- Access the filesystem directly.

---

# Inputs

- User interactions.
- Application state.
- Events from application services.

---

# Outputs

The component provides:

- Navigation events.
- User commands.
- UI state updates.

---

# Workflow

1. Start the application.
2. Load the main interface.
3. Display the default view.
4. Handle user interactions.
5. Update displayed information.
6. Close gracefully.

---

# Assumptions

- Application services have been initialized.
- Required configuration has been loaded.

---

# Acceptance Criteria

The implementation is complete when:

- The application starts successfully.
- The main interface is displayed.
- Navigation functions correctly.
- The application closes cleanly.
- Unit/UI tests pass.

---

# Future

Not part of v0.1:

- Multiple window support.
- Dockable panels.
- Workspace layouts.
- Themes.
- Plugin windows.

---

# Dependencies

Depends on:

- 011 - Logging Service
- 012 - Configuration Manager

Required by:

- 014 - Dashboard
- 015 - Folder Selection
- 016 - Scan Progress
- 017 - Results View
- 018 - Rule Editor
- 019 - Settings

---

# Autonomous v0.1 Decisions

The existing `OpenSorSe.Desktop` project is the implementation project; no separate UI project is introduced. The shell presents a title region, persistent navigation list, content host, and status bar. The unused `Application` menu placeholder is intentionally absent so no visible control suggests an unavailable action. Navigation is an immutable ordered enum list, defaults to Dashboard, and includes a Diagnostics destination. Selecting an unsupported destination throws before state changes.

The shell hosts the implemented v0.1 pages and owns presentation-level navigation. Application startup and shutdown remain owned by the existing `App` and `IApplicationHost` composition path. The shell forwards folder-selection requests to `IApplicationController`, presents progress and cancellation, and displays read-only results; it never invokes filesystem execution.

## Layout

| Toolbar                                              |
+----------------------+-------------------------------+
| Navigation           | Main Content                  |
|                      |                               |
|                      |                               |
|                      |                               |
+----------------------+-------------------------------+
| Status Bar                                           |
+------------------------------------------------------+
