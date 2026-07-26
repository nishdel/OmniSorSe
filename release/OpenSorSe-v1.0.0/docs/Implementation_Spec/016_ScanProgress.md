# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 016 |
| Component | Scan Progress |
| Project | OpenSorSe.Desktop |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The Scan Progress view displays the real-time progress of an active scan.

It keeps the user informed about the current operation and provides control over long-running scans.

---

# Why

Scanning large directories can take time.

The user should always know:

- What is happening.
- Which folder is currently being scanned.
- How much progress has been made.
- Whether they can cancel the operation.

---

# Responsibilities

The Scan Progress view shall:

- Display overall scan progress.
- Display the current folder being scanned.
- Display the number of files discovered.
- Display the elapsed scan time.
- Display the current processing stage.
- Allow the user to cancel the scan.

---

# Does NOT

The Scan Progress view must NOT:

- Scan files.
- Read the filesystem.
- Move files.
- Execute organization rules.
- Calculate hashes.
- Process metadata.
- Access AI services.

---

# Inputs

- Scan progress updates.
- Scan statistics.
- Scan status.
- User interaction.

---

# Outputs

The component provides:

- Cancel scan request.
- Progress updates.
- Status updates.

---

# Workflow

1. Open when a scan starts.
2. Display scan progress.
3. Update statistics in real time.
4. Display the current processing stage.
5. Handle user cancellation.
6. Close when the scan finishes.

---

# Assumptions

- A scan is currently running.
- Progress information is available.
- Cancellation is supported.

---

# Acceptance Criteria

The implementation is complete when:

- Progress updates correctly.
- Statistics update in real time.
- Current folder is displayed.
- Cancel button works.
- The view closes automatically when the scan completes.
- UI tests pass.

---

# Layout

+------------------------------------------------------------+
| Scanning...                                                |
+------------------------------------------------------------+

Progress

██████████████░░░░░░░░░░░░░░░░░░ 45%

------------------------------------------------------------

Current Stage

Scanning Files

------------------------------------------------------------

Current Folder

C:\Users\John\Documents\Projects

------------------------------------------------------------

Files Found

2,154

Folders Scanned

387

Elapsed Time

00:01:42

------------------------------------------------------------

               [ Cancel Scan ]

------------------------------------------------------------

Status

Scanning...

---

# Future

Not part of v0.1:

- Estimated remaining time.
- Scan speed.
- Live file preview.
- Pause / Resume.
- Multiple simultaneous scans.

---

# Dependencies

Depends on:

- 013 - Main Window
- 001 - File Scanner

Required by:

- 017 - Results View

---

# Autonomous v0.1 Decisions

The draft does not define progress percentages, a scanner-to-view lifetime contract, or automatic navigation. The v0.1 view model consumes the existing immutable `OpenSorSe.Scanner.Models.ScanProgress` snapshots directly and uses an indeterminate progress indicator because scanner discovery has no reliable total-work estimate. It exposes `Start`, `ApplyProgress`, and terminal `Complete(ScanStatus)` presentation methods plus a cancellation event. It never calls a scanner or task manager.

The Desktop shell owns navigation and scan lifetime through `IApplicationController`. Completion changes presentation stage and, for a completed read-only pipeline, loads Results, updates the Dashboard's in-memory latest-scan summary, and navigates to Results without discarding results. Pause/resume, estimates, rates, previews, and concurrent scans remain deferred.
