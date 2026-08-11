# Readers Overview

> This document distinguishes the bounded released v2.2 content pipeline from broader future readers.

---

## Implementation Status

OpenSorSe 1.0 implements `IMetadataExtractor` and `IMetadataExtractionPipeline` for filesystem metadata, page-aware PdfPig PDF fields/native text, bounded DOCX/XLSX core properties/native text, and PNG/JPEG dimensions. It also provides separately enabled OCR Beta through built-in PDFtoImage/PDFium page rendering and a capability-detected external Tesseract CLI for PNG/JPEG/TIFF and insufficient scanned/mixed-PDF pages. Extractors are read-only, bounded, cancellable, never execute macros, and never fetch remote resources.

v2.2 adds provider-neutral media evidence, deterministic
image headers/EXIF, bounded lazy thumbnails, existing OCR reuse, optional
`ffprobe` audio/video metadata, and optional bounded `ffmpeg` representative
frames. It defines transcription and visual-description contracts but bundles
no implementation. The authoritative scope and format list are in
[Media Intelligence v2.2](../../MEDIA_INTELLIGENCE_v2.2.md).

Rich document layout, handwriting/table recognition, archive readers, formula
evaluation, embedded-object execution, full-fidelity parsing, broad codec
support, and whole-video understanding remain future work. The older
format-specific reader documents are design history where they exceed the
v1.0 or v2.2 implementation guides.

---

## Purpose

The Readers/content boundary provides consistent defensive extraction while keeping format handling separate from scanning, rules, semantic indexing, and presentation.

---

## Prospective Responsibilities

The implemented narrow subsystem is responsible for:

* Selecting a reader for a supported file type.
* Extracting content and format-specific metadata without modifying the source file.
* Isolating failures to the affected file.
* Reporting extraction outcomes through the application's diagnostics model.

It would not own filesystem traversal, result presentation, persistence, AI inference, or file-changing operations.

---

## Relationship to the Current Release

The Scanner remains responsible for recursive traversal, basic metadata, hashing, errors, progress, and cancellation. After scan enrichment, the optional application content stage caches supported extracted metadata/text by source fingerprint and isolates every per-file failure.

---

## Related Documents

* [Scanner Overview](../02_Scanner/00_Overview.md)
* [System Overview](../00_System/00_Overview.md)
* [Release Status](../../RELEASE_STATUS.md)
