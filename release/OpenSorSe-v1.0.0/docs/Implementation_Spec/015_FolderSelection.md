# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 015 |
| Component | Folder Selection |
| Project | OpenSorSe.Desktop |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The Folder Selection view allows users to choose one or more folders for OpenSorSe to analyze.

It serves as the starting point of every scan.

---

# Why

Before OpenSorSe can organize files, it needs to know which locations the user wants to process.

The Folder Selection view provides a simple, safe, and intuitive way to define scan targets.

---

# Responsibilities

The Folder Selection view shall:

- Allow users to add one or more folders.
- Allow users to remove selected folders.
- Display the selected folders.
- Validate selected paths.
- Start a scan.
- Remember recently used folders.

---

# Does NOT

The Folder Selection view must NOT:

- Scan folders.
- Move files.
- Read file contents.
- Execute organization rules.
- Calculate hashes.
- Classify files.
- Access AI services.

---

# Inputs

- User folder selections.
- Application configuration.
- Recent folder history.

---

# Outputs

The component provides:

- Selected folder list.
- Scan request.
- Navigation events.

---

# Workflow

1. Open the Folder Selection view.
2. Add or remove folders.
3. Validate folder paths.
4. Display the selected folders.
5. User starts the scan.
6. Pass the scan request to the application.

---

# Assumptions

- The application has started successfully.
- Folder picker is available.
- Selected folders are accessible.

---

# Acceptance Criteria

The implementation is complete when:

- Users can add folders.
- Users can remove folders.
- Invalid folders are detected.
- Recently used folders are available.
- The Scan button starts a scan request.
- UI tests pass.

---

# Layout

+------------------------------------------------------------+
| Folder Selection                                           |
+------------------------------------------------------------+

 Selected Folders

 ----------------------------------------------------------
 | C:\Users\John\Documents                                 |
 | D:\Photos                                               |
 | E:\Downloads                                            |
 ----------------------------------------------------------

 [ Add Folder ]

 [ Remove Folder ]

------------------------------------------------------------

 Recent Locations

 • Documents

 • Downloads

 • Pictures

------------------------------------------------------------

                     [ Start Scan ]

------------------------------------------------------------

 Status

 Ready

---

# Future

Not part of v0.1:

- Drag & Drop folders.
- Favorite folders.
- Network locations.
- Saved scan profiles.
- Folder tags.
- Folder exclusion rules.

---

# Dependencies

Depends on:

- 013 - Main Window
- 012 - Configuration Manager

Required by:

- 001 - File Scanner

flowchart LR

UI["UI Views"]
    --> APP["Application Layer"]

APP --> Scanner["001 File Scanner"]
APP --> Metadata["002 File Metadata"]
APP --> Hasher["003 File Hasher"]
APP --> Classifier["004 File Classifier"]
APP --> Rules["006 Rule Engine"]
APP --> Planner["007 Move Planner"]
APP --> Executor["009 Move Executor"]

---

# Autonomous v0.1 Decisions

## Contract completion

The draft did not define a folder-picker integration, duplicate handling, path normalization, recent-folder retention, or scan-request payload. v0.1 uses manually entered absolute local paths, lexical normalization with `Path.GetFullPath`, platform-aware duplicate comparison, and `Directory.Exists` only to validate a chosen scan root. The Desktop view also offers Avalonia's native folder picker when the platform storage provider supports it; manual absolute-path entry remains the fallback.

## Public UI contract

- `ScanRequest(IReadOnlyList<string> FolderPaths)` is an immutable request emitted after all selected roots are valid.
- `FolderSelectionViewModel` owns selected roots, five process-lifetime recent roots, validation status, and add/remove/request commands.
- Roots retain insertion order; identical normalized paths are rejected using case-insensitive comparison on Windows and ordinal comparison elsewhere.
- `ScanRequested` is emitted by the page. The Desktop shell subscribes to it and forwards the request to `IApplicationController`; the page itself still does not invoke scanner services, create tasks, or navigate.

## Safety and deferred behavior

No folder contents are enumerated, read, modified, or sent externally by this page. The Desktop view provides a native folder picker where its platform storage provider supports one, while manual absolute paths remain available. Network locations, persistence of recent folders, drag and drop, profiles, and exclusions are deferred.
