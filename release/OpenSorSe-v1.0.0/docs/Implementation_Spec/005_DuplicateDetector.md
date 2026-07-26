# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 005 |
| Component | Duplicate Detector |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The Duplicate Detector identifies files that are exact duplicates based on their calculated hash values.

It groups duplicate files together and provides this information to downstream components.

The component is read-only.

---

# Why

Duplicate files waste storage space and can make file organization more difficult.

Identifying duplicates allows OpenSorSe to suggest or perform intelligent cleanup while avoiding false matches based solely on filenames.

---

# Responsibilities

The Duplicate Detector shall:

- Compare file hashes.
- Identify identical files.
- Group duplicate files together.
- Mark duplicate status on each affected `FileEntry`.
- Generate duplicate statistics.
- Return the updated collection.

---

# Does NOT

The Duplicate Detector must NOT:

- Rename files.
- Move files.
- Delete files.
- Calculate hashes.
- Classify files.
- Execute rules.
- Use AI services.
- Modify the filesystem.

---

# Inputs

- Collection of `FileEntry` objects containing hash information.

---

# Outputs

The component returns:

- Updated collection of `FileEntry` objects.
- Duplicate groups.
- Duplicate statistics.
- Duplicate detection errors.

---

# Workflow

1. Receive the collection.
2. Compare hash values.
3. Group matching files.
4. Mark duplicate files.
5. Generate statistics.
6. Return the updated collection.

---

# Acceptance Criteria

The implementation is complete when:

- Files with identical hashes are grouped together.
- Unique files remain unchanged.
- Duplicate groups are generated correctly.
- No filesystem modifications occur.
- Unit tests pass.

---

# Future

Not part of v0.1:

- Similar image detection.
- Similar document detection.
- Duplicate confidence scoring.
- AI-assisted duplicate detection.
- Duplicate preview interface.

---

# Dependencies

Depends on:

- 003 - File Hasher

Required by:

- 006 - Rule Engine

---

# v0.1 Contract

## Identity and boundaries

The Duplicate Detector performs exact-content duplicate detection from already calculated SHA-256 hashes. It does not open files, calculate hashes, compare names, paths, metadata, or file contents, or modify the filesystem. It does not perform fuzzy, perceptual, semantic, or near-duplicate detection.

## Input, output, and supported hashes

The detector accepts `FileEntry` values and returns enriched entries, ordered duplicate groups, statistics, and recoverable issues. It supports only the `SHA-256` algorithm, matched case-insensitively. Hash values must be exactly 64 hexadecimal characters; uppercase values are normalized to lowercase. Every output entry receives a newly calculated duplicate classification, replacing any prior value while preserving its full path, metadata, hash, and file classification.

An absent hash produces `HashUnavailable`; a missing or unsupported algorithm produces `UnsupportedHashAlgorithm`; an empty or malformed value produces `InvalidHashValue`. Each affected entry receives exactly one recoverable issue and an `Unknown` classification. Valid singleton hashes are `Unique`; valid hashes shared by two or more entries are `Duplicate` and receive group ID `sha256:<normalized-hash>`. Groups contain enriched entries, preserve input order, and are ordered by the first occurrence of their hash.

The public model is `DuplicateStatus` (`Unknown`, `Unique`, `Duplicate`), `DuplicateClassification(DuplicateStatus Status, string? GroupId = null)`, `DuplicateGroup(string GroupId, string Algorithm, string HashValue, IReadOnlyList<FileEntry> Files)`, `DuplicateDetectionIssueKind` (`HashUnavailable`, `UnsupportedHashAlgorithm`, `InvalidHashValue`), `DuplicateDetectionIssue(string FilePath, DuplicateDetectionIssueKind Kind, string Message)`, `DuplicateDetectionStatistics(long FilesProcessed, long FilesUnique, long FilesDuplicate, long FilesUnknown, long DuplicateGroups, long IssuesEncountered)`, and `DuplicateDetectionResult(IReadOnlyList<FileEntry> Files, IReadOnlyList<DuplicateGroup> Groups, DuplicateDetectionStatistics Statistics, IReadOnlyList<DuplicateDetectionIssue> Issues)`. `FileEntry` is immutably enriched with optional `DuplicateClassification? Duplicate = null`.

The public service contract is `Task<DuplicateDetectionResult> IDuplicateDetector.DetectAsync(IReadOnlyCollection<FileEntry> files, CancellationToken cancellationToken = default)`. The implementation is constructed with `ILoggingService` and `IErrorHandler`, is registered as `AddSingleton<IDuplicateDetector, DuplicateDetector>()`, and has no other subsystem dependency.

## Processing and cancellation

The detector validates the collection and null entries before processing and returns no partial result for validation errors. It checks cancellation before processing, before each entry, and while creating output entries and groups; cancellation throws `OperationCanceledException` without error reporting or a partial result. Processing is sequential and synchronous; no `Task.Run` or parallelism is used.

Unexpected operation-level failures are logged, reported once through `IErrorHandler`, and rethrown. Recoverable hash-validation issues are returned and may be logged, but are not reported through the global error handler. Empty input returns empty files, groups, and issues with all-zero statistics.

## Exclusions

v0.1 excludes hash calculation, filesystem access, persistence, events, progress reporting, UI behavior, AI, rules, planning, deletion, cleanup, preferred-copy selection, and any similarity detection.
