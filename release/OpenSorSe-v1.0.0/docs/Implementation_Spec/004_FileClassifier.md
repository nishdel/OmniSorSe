# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 004 |
| Component | File Classifier |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The File Classifier determines the category of each discovered file based on predefined classification rules.

The classification is used by downstream components to organize files and apply user-defined rules.

The component is read-only.

---

# Why

OpenSorSe needs to understand what each file represents before it can decide how it should be organized.

Classification provides a consistent category for every file regardless of its location.

---

# Responsibilities

The File Classifier shall:

- Classify every eligible file.
- Assign a category to each `FileEntry`.
- Support configurable classification rules.
- Handle unknown file types.
- Return the updated collection.

---

# Does NOT

The File Classifier must NOT:

- Read file contents.
- Rename files.
- Move files.
- Delete files.
- Calculate hashes.
- Detect duplicates.
- Execute organization rules.
- Use AI services.
- Modify the filesystem.

---

# Inputs

- Collection of `FileEntry` objects with metadata.

---

# Outputs

The component returns:

- Updated collection of `FileEntry` objects.
- Classification statistics.
- Classification errors.

---

# Workflow

1. Receive the collection.
2. Determine the file type.
3. Match the file against classification rules.
4. Assign a category.
5. Mark unknown file types when necessary.
6. Return the updated collection.

---

# Acceptance Criteria

The implementation is complete when:

- Known file types are classified correctly.
- Unknown file types are handled safely.
- Every eligible file receives a category.
- No filesystem modifications occur.
- Unit tests pass.

---

# Future

Not part of v0.1:

- AI-powered classification.
- Content-based classification.
- User-trainable categories.
- Confidence scoring.
- Multiple category assignments.

---

# Dependencies

Depends on:

- 002 - File Metadata

Required by:

- 006 - Rule Engine

---

# v0.1 Classification Contract

`FileClassifier` is a deterministic Scanner-stage metadata classifier, not the future AI content classifier. It uses only `FileMetadata.FileName` and `FileMetadata.Extension`, assigns exactly one `FileCategory`, and produces no AI output, confidence, tags, or evidence.

```csharp
public enum FileCategory { Unknown, Document, Spreadsheet, Presentation, Image, Audio, Video, Archive, Code, Data, Executable, Font }
public enum FileClassificationMatchKind { ExactFileName, Extension }
public sealed record FileClassificationRule(string Id, FileClassificationMatchKind MatchKind, string Pattern, FileCategory Category);
public sealed record FileClassificationOptions(IReadOnlyList<FileClassificationRule> Rules) { public static FileClassificationOptions Default { get; } }
```

`Default` uses case-insensitive, lowercase-invariant leading-dot mappings: Document `.txt .md .rtf .pdf .doc .docx .odt`; Spreadsheet `.csv .xls .xlsx .ods`; Presentation `.ppt .pptx .odp`; Image `.jpg .jpeg .png .gif .bmp .tif .tiff .webp .svg .heic`; Audio `.mp3 .wav .flac .aac .m4a .ogg .wma`; Video `.mp4 .mkv .mov .avi .wmv .webm .m4v`; Archive `.zip .7z .rar .tar .gz .bz2 .xz`; Code `.cs .fs .vb .java .py .js .ts .tsx .jsx .c .cpp .h .hpp .rs .go .php .rb .swift .kt .kts .html .css .scss .xml .json .yaml .yml .toml .sql .sh .ps1`; Data `.db .sqlite .sqlite3 .parquet .avro`; Executable `.exe .msi .dll .bat .cmd .com .appx .msix`; Font `.ttf .otf .woff .woff2`.

Custom options replace defaults. Rules are validated before processing: nonempty unique IDs/patterns, non-Unknown category, extension patterns beginning `.`, and defined match kinds. Rules are evaluated in supplied order; first match wins. A missing metadata value yields Unknown plus one `MetadataUnavailable` issue; an unmatched or extensionless entry yields Unknown without issue. Existing classifications are replaced; path, metadata, hash, order, and duplicates are preserved. Cancellation throws without a partial result. No filesystem access, AI, persistence, events, progress, or later-stage behavior is permitted.
