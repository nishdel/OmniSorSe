# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 014 |
| Component | Dashboard |
| Project | OpenSorSe.Desktop |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The Dashboard is the application's home screen.

It provides users with an overview of OpenSorSe, quick access to common actions, and the latest completed scan summary for the current application session.

---

# Why

The Dashboard gives users a central place to begin using OpenSorSe without needing to navigate through multiple screens.

It should answer:

- What can I do?
- What did the latest completed scan find?
- What should I do next?

---

# Responsibilities

The Dashboard shall:

- Display application status.
- Display the latest completed in-memory scan summary when available.
- Display quick actions.
- Display basic statistics.
- Navigate to other parts of the application.

---

# Does NOT

The Dashboard must NOT:

- Scan folders.
- Execute business logic.
- Move files.
- Calculate hashes.
- Execute organization rules.
- Modify configuration.

---

# Inputs

- Application state.
- Latest completed scan summary for the current application session.
- Statistics.
- User interactions.

---

# Outputs

The Dashboard provides:

- Navigation requests.
- User actions.
- Dashboard updates.

---

# Workflow

1. Load dashboard.
2. Display application summary.
3. Display the latest completed scan summary or the no-scan-yet explanation.
4. Display quick actions.
5. Handle user interaction.
6. Navigate to selected feature.

---

# Assumptions

- Application has started successfully.
- Configuration has been loaded.

---

# Acceptance Criteria

The implementation is complete when:

- Dashboard loads successfully.
- The latest completed scan summary is displayed when available.
- Quick actions function correctly.
- Statistics update correctly.
- Navigation works.
- UI tests pass.

---

# Layout

+------------------------------------------------------------+
|                     Dashboard                              |
+------------------------------------------------------------+

  Recent Activity

  • Last Scan
  • Files Organized
  • Duplicate Files Found

--------------------------------------------------------------

 Quick Actions

 [ Scan Folder ]

 [ View Results ]

 [ Settings ]

--------------------------------------------------------------

 Statistics

 Files Scanned
 Files Organized
 Duplicate Files
 Storage Saved

--------------------------------------------------------------

 Status

 Ready

---

## Corrected v0.1 layout

The implemented Dashboard replaces the illustrative draft layout above. It contains a status line, a no-completed-scan explanation when appropriate, and a **Latest completed scan** section with files scanned, folders discovered, exact duplicate files, and warnings. It has only Scan folder, View results, and Settings quick actions; View results is enabled only after a completed in-memory scan. It does not show Files organized, Storage saved, or unorganized-file metrics.

# Future

Not part of v0.1:

- Widgets.
- Custom dashboard.
- AI recommendations.
- Charts.
- Productivity statistics.
- Themes.

---

# Dependencies

Depends on:

- 013 - Main Window

Required by:

- None

---

# Autonomous v0.1 Decisions

Dashboard state is read-only and starts with a clear no-completed-scan explanation. After the existing shell receives a completed processing result, it replaces the Dashboard's in-memory latest-scan summary with files scanned, folders discovered, exact duplicate files, and warnings. That state belongs to the existing shell for the rest of the application session, so navigation does not reset it; it is not persistent history and closing the application discards it.

The three quick actions navigate only to Scan, Results, and Settings; they do not invoke scanners, executors, or configuration services. View Results is unavailable until the current session has a completed result. Files organized, storage saved, and unorganized-file metrics are deliberately not shown because v0.1 is review-only. The view is hosted by the existing Main Window only when Dashboard is selected.
