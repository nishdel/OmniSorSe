# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 003 |
| Component | File Hasher |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The File Hasher enriches discovered files with deterministic SHA-256 fingerprints. It is the third Scanner pipeline stage and receives `FileEntry` objects after filesystem metadata collection.

The component reads file contents only to calculate hashes. It is read-only and must never modify user files.

---

# Responsibilities

The File Hasher shall:

- Accept a collection of `FileEntry` objects.
- Hash regular files with SHA-256.
- Read file content through asynchronous streaming with a bounded buffer.
- Produce lowercase hexadecimal SHA-256 strings without separators.
- Enrich `FileEntry` objects immutably with optional hash information.
- Preserve the exact input order and number of entries, including duplicates.
- Handle per-file recoverable failures and continue with subsequent files.
- Support cooperative cancellation through `CancellationToken`.
- Return enriched entries, hashing statistics, and hashing issues.

---

# Does NOT

The File Hasher must **NOT**:

- Detect, compare, group, or label duplicates.
- Read filesystem metadata beyond checks necessary to confirm that an entry is a regular file.
- Discover files or directories.
- Classify files or inspect document structure.
- Rename, move, delete, create, truncate, or otherwise modify files.
- Persist hashes or results to a database.
- Publish events.
- Invoke rules, AI, readers, plugins, or user interface code.
- Hash directories, symbolic links, junctions, or other reparse points.
- Load complete file contents into memory.
- Add an external dependency.

---

# Inputs

The hasher receives:

- A collection of `FileEntry` objects, normally produced by Specification 002.
- An optional `CancellationToken`.

`FileEntry` values may have metadata or may have `Metadata == null`; metadata is not a precondition for hashing in v0.1.

---

# Outputs

The component returns:

- A collection of `FileEntry` objects in the same order and with the same multiplicity as the input.
- Optional `FileHash` information on each successfully hashed entry.
- `FileHashStatistics` using `long` counts and byte totals.
- A collection of recoverable `FileHashIssue` values.

---

# Public Models and Contracts

The implementation shall expose these public contracts in `OpenSorSe.Scanner` and `OpenSorSe.Scanner.Models`:

```csharp
public sealed record FileHash(string Algorithm, string Value);

public sealed record FileEntry(
    string FullPath,
    FileMetadata? Metadata = null,
    FileHash? Hash = null);

public sealed record FileHashStatistics(
    long FilesProcessed,
    long FilesHashed,
    long BytesHashed,
    long IssuesEncountered);

public enum FileHashIssueKind
{
    FileUnavailable,
    AccessDenied,
    FileChangedDuringHashing,
    FileUnreadable,
    NonRegularFileSkipped,
    ReparsePointSkipped,
}

public sealed record FileHashIssue(
    string FilePath,
    FileHashIssueKind Kind,
    string Message);

public sealed record FileHashResult(
    IReadOnlyList<FileEntry> Files,
    FileHashStatistics Statistics,
    IReadOnlyList<FileHashIssue> Issues);

public interface IFileHasher
{
    Task<FileHashResult> HashAsync(
        IReadOnlyCollection<FileEntry> files,
        CancellationToken cancellationToken = default);
}
```

`FileHasher` shall implement `IFileHasher`. All public members shall have XML documentation comments.

---

# Statistics Semantics

`FileHashStatistics` values have these exact meanings:

- `FilesProcessed` counts every input entry examined, including duplicates, unavailable files, directories, and skipped reparse points.
- `FilesHashed` counts only entries that receive a successful new SHA-256 hash.
- `BytesHashed` counts bytes successfully read from eligible streams, including bytes read from a file later rejected by best-effort change detection.
- `IssuesEncountered` equals the number of returned `FileHashIssue` values.

---

# Algorithm and Output Normalization

- The only v0.1 algorithm is SHA-256 using .NET cryptography APIs.
- `FileHash.Algorithm` shall be exactly `"SHA-256"`.
- `FileHash.Value` shall be exactly 64 lowercase ASCII hexadecimal characters with no prefix, whitespace, or separators.
- The same byte sequence shall produce the same normalized hash regardless of file path or name.
- The component shall not compare hash values or infer duplicate status.

---

# Processing Behavior

1. Validate that the input collection is non-null.
2. Check cancellation before queuing work.
3. Process entries sequentially in collection order.
4. Check cancellation before each entry.
5. Validate the entry path and inspect its attributes.
6. Skip directories and unsupported non-file entries with `NonRegularFileSkipped` and skip symbolic links, junctions, and all other reparse points with `ReparsePointSkipped`.
7. Open each eligible file for read-only asynchronous streaming with `FileShare.ReadWrite | FileShare.Delete`, permitting observational access while other processes read, write, rename, or delete the file.
8. Hash bytes incrementally with SHA-256 using a bounded rented buffer.
9. Pass the supplied cancellation token directly to every asynchronous stream read and check cancellation between reads.
10. Validate that the file did not change during hashing.
11. Return an immutable enriched copy of the entry when successful; leave `Hash` unchanged or null when hashing fails.
12. Check cancellation after each recoverable failure before continuing.
13. Return a result only when all entries complete; cancellation does not return a partial result.

Every eligible input entry is hashed again in v0.1. Existing hash values are not trusted or reused. If the current attempt fails or the file changes, the returned entry shall have `Hash == null`; stale input hashes must not be retained.

---

# File-Change-During-Hashing Behavior

Before opening a regular file, the hasher shall capture its length and last-write timestamp. After hashing and before assigning the result, it shall read those values again. This is best-effort change detection only; it does not provide or guarantee an immutable filesystem snapshot.

If either value differs, the hasher shall:

- Add one `FileChangedDuringHashing` issue for that entry.
- Not attach the calculated hash.
- Not retry automatically.
- Continue with subsequent entries.

If the file becomes unavailable, access is denied, or reading fails at any point, the component shall record the corresponding recoverable issue and continue.

---

# Cancellation Semantics

Cancellation is cooperative:

- `HashAsync` checks the token before starting its worker operation.
- The worker checks before each file, between streamed reads, and after recoverable exceptions.
- Cancellation closes/disposes the active stream and releases the rented buffer.
- Cancellation throws `OperationCanceledException` and does not return a partial `FileHashResult`.
- No pause, resume, queueing, or task orchestration is part of this specification.

---

# Error Handling

The hasher shall isolate known filesystem failures to the affected entry:

| Condition | Result |
|-----------|--------|
| Missing or invalid path | `FileUnavailable` issue; continue. |
| Permission denied | `AccessDenied` issue; continue. |
| Directory or unsupported non-file entry | `NonRegularFileSkipped` issue; continue. |
| Symbolic link, junction, or other reparse point | `ReparsePointSkipped` issue; continue. |
| Stream-opening or read failure | `FileUnreadable` issue; continue. |
| File changes while being hashed | `FileChangedDuringHashing` issue; continue. |

Each recoverable failure produces at most one issue for its file and is logged once through the existing `Scanner` logging category. Recoverable per-file failures are not sent to the global error handler.

Unexpected operation-level exceptions shall be reported through the existing global error handler and rethrown. `OperationCanceledException` is not reported as an error.

---

# Performance and Memory Constraints

- v0.1 processes one file at a time; it does not parallelize hashing.
- File data shall be streamed asynchronously in fixed-size bounded buffers. A 64 KiB buffer is the v0.1 implementation default.
- Buffers shall be rented from `ArrayPool<byte>` and returned in a `finally` block with `ArrayPool<byte>.Shared.Return(buffer, clearArray: true)` because they contain user file content.
- The implementation shall use incremental hashing and shall not load an entire file into memory.
- Statistics use `long` for file counts, issue counts, and bytes hashed.
- Input order and duplicates are intentionally preserved; no deduplication or sorting occurs.

---

# Security Considerations

- Hashing is local-only and uses no network or AI service.
- File contents exist only transiently in the bounded buffer and are not logged, stored, or returned.
- Logs must not include file contents or calculated hash values.
- Paths should be omitted or masked in diagnostic logs where practical; the result issue retains the affected path for callers.
- SHA-256 is used for deterministic fingerprinting, not for password storage, encryption, or proof of file origin.

---

# Acceptance Criteria

The implementation is complete when:

- SHA-256 hashes are generated for accessible regular files.
- Hash values are lowercase, separator-free hexadecimal strings of length 64.
- The same content produces the same hash.
- Selected deterministic test inputs with different content produce different hashes.
- Large files are streamed without whole-file buffering.
- Input order and duplicate entries are preserved.
- Directories, symbolic links, and reparse points are not hashed.
- Missing, inaccessible, unreadable, and changed files do not stop later files from being processed.
- Cancellation throws `OperationCanceledException` and no partial result is returned.
- No filesystem modifications occur.
- Unit tests pass.

---

# Test Requirements

Unit tests shall cover:

- A known SHA-256 test vector, including exact algorithm label and normalized lowercase hexadecimal output.
- Equal content at different paths producing equal hashes.
- Selected deterministic test inputs with different content producing different hashes.
- Empty-file hashing.
- Input order and duplicate entries being preserved.
- Original immutable `FileEntry` records remaining unchanged.
- Missing and invalid files producing one recoverable issue while later files are hashed.
- Directories and reparse points being skipped when the test environment supports creating them.
- File content and directory membership remaining unchanged.
- Cancellation requested before execution throwing deterministically.
- Cancellation during streamed hashing using a deterministic test seam or controlled stream, not timing assumptions.
- File-change detection using a controlled test seam or deterministic file replacement.
- Large-file hashing using bounded-buffer behavior without asserting filesystem enumeration order.

Integration tests are not required in a separate project for v0.1. The Scanner test project may use isolated temporary files to exercise .NET file streaming and cryptography. Tests that require symbolic-link creation or Windows ACL changes must be conditional on environment capabilities and must restore all state.

---

# Dependencies

Depends on:

- 001 - File Scanner
- 002 - File Metadata
- .NET built-in `System.Security.Cryptography` and `System.Buffers` APIs

Required by:

- 005 - Duplicate Detector

No new NuGet package is permitted or required.

---

# Deferred Behavior

Not part of v0.1:

- Multiple or configurable hash algorithms.
- Parallel hashing.
- Incremental or cached hashing.
- Automatic retries.
- Hash verification workflows.
- Perceptual, fuzzy, image, audio, or near-duplicate hashing.
- Duplicate detection, grouping, status assignment, or persistence.
- Progress reporting, events, UI integration, task orchestration, or database storage.
