# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 017 |
| Component | Results View |
| Project | OpenSorSe.Desktop |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The Results View presents the outcome of the completed read-only analysis phase.

No file operation may be executed from this view in v0.1.

---

# Why

Users should be able to understand what a scan found without risking a change to their files.

The Results View provides a readable, local, review-only summary of discovered files, exact duplicates, warnings, and informational planned-operation counts.

---

# Responsibilities

The Results View shall:

- Display scan results.
- Display a concise scan summary.
- Display discovered files using meaningful file details rather than raw model text.
- Display exact-duplicate status and recoverable warnings.
- Keep long local paths usable through trimming and a tooltip.
- Make the read-only safety boundary clear.

---

# Does NOT

The Results View must NOT:

- Scan files.
- Execute file operations.
- Modify the filesystem.
- Calculate hashes.
- Evaluate organization rules.
- Resolve conflicts.

---

# Inputs

- ScanResult
- Conflict-resolution result
- Duplicate information
- Rule Engine results

---

# Outputs

The component provides:

- Read-only results presentation state.

---

# Workflow

1. Load analysis results.
2. Display the summary and friendly file rows.
3. Display warnings and the informational planned-operation count.
4. Let the user review results or use normal shell navigation.

---

# Assumptions

- The analysis pipeline has completed successfully.
- A completed pipeline result is available in memory.

---

# Acceptance Criteria

The implementation is complete when:

- Results are displayed correctly.
- Files, folders, exact duplicates, warnings, and planned-operation counts are visible.
- Long paths remain usable and the results list can scroll independently of its footer.
- The page exposes no file-operation control.
- UI tests pass.

---

# Layout

+------------------------------------------------------------+
| Results                                                    |
+------------------------------------------------------------+

Summary

Files Scanned:        5,243

Files To Move:          814

Duplicates Found:        52

Warnings:                 3

------------------------------------------------------------

Planned Operations

------------------------------------------------------------

📄 Invoice.pdf

Documents → Documents/Invoices

------------------------------------------------------------

🎵 Song.mp3

Downloads → Music

------------------------------------------------------------

⚠ Duplicate

photo.jpg

------------------------------------------------------------

[ Back ]

[ Cancel ]

[ Execute ]

------------------------------------------------------------

Status

Waiting for user confirmation

---

## Corrected v0.1 layout

The implemented page replaces the illustrative draft layout above. A five-value summary reports files scanned, folders discovered, exact duplicates, warnings, and planned operations. A scrollable file list uses separate columns for file name, containing folder, extension, available size, and duplicate status; it never displays a raw `FileEntry` value. Long paths are trimmed with a full-path tooltip. Warnings are separated from file rows, and a fixed footer says that results are review-only. There are no Back, Cancel, Approve, or Execute file-operation controls in the Desktop view.

# Future

Not part of v0.1:

- Search.
- Filters.
- Sorting.
- Grouping.
- Side-by-side comparison.
- AI explanations.
- Manual editing of individual operations.

---

# Dependencies

Depends on:

- 007 - Move Planner
- 008 - Conflict Resolver

Required by:

- 009 - Move Executor

---

# Autonomous v0.1 Decisions

The draft references an undefined `MovePlan` and assumes a mutation workflow that v0.1 does not implement. v0.1 consumes existing `ConflictResolutionResult` plus explicitly supplied ordered `FileEntry` and `DirectoryEntry` collections. It projects files into immutable presentation rows containing a file name, containing folder/full path, extension, available size, and exact-duplicate status; it never renders a raw model `ToString()` value.

The page has a separated summary, a scrollable file list, warnings, and a footer that remains outside the scrollable content. The summary reports files scanned, folders discovered, exact duplicate files, warnings, and the count of existing planned operations. Long paths are trimmed in the list and available through a tooltip. An empty page explains that a completed scan is required.

The v0.1 Desktop view deliberately exposes no approval, execution, cancel, move, rename, delete, or other file-operation control. Any `PlannedOperation` count is informational only. Loading copies collection membership and creates presentation rows without modifying `FileEntry`, `DirectoryEntry`, or `PlannedOperation` records. The persistent footer states that results are review-only and OpenSorSe will not change files. Search, sorting, filtering, previews, manual plan editing, AI explanations, execution orchestration, and navigation policy are deferred.
