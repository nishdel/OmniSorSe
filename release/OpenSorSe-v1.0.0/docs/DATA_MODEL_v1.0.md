# OpenSorSe 1.0 Data Model

## Extracted content

`ExtractedMetadataField` stores name, bounded value, `ContentProvenance`, and confidence. `PdfPageText` stores page number, bounded native text, and deterministic quality state. `OcrPageResult` stores page number, `NativeText`/`Ocr`/`NativeAndOcrFallback`/`Skipped`/`Failed` provenance, bounded text, optional confidence, status, and a safe message.

`ContentRecord` stores normalized absolute path, source length/last-write fingerprint, indexed time, metadata, bounded native/OCR text, page-level OCR provenance, OCR state/engine, extraction fingerprint, warnings, and provenance-aware tags. The extraction fingerprint includes schema, content settings, language, engine/version, and rasterizer/version so stale records remain readable but are reprocessed.

## Tags

`TagAssociation` stores identity, file identity, display/normalized name, category, `TagSource`, `TagAcceptanceState`, confidence, timestamps, source fingerprint, system status, and provenance detail.

## Semantic index

`SemanticIndexEntry` stores normalized path, source/index fingerprints, filename, bounded tags, metadata/native/OCR terms, a 256-dimensional normalized deterministic vector, and indexed time. No raw file bytes are stored.

## Folder structure

`FolderStructureSnapshot` contains root identity, normalized root path, captured time, stable structure fingerprint, and up to 4,000 nodes. Each `StructureNode` has a relative path, directory flag, length, last-write time, and content-independent identity fingerprint. `RestructuringHistoryRecord` contains source/proposed/optional applied snapshots, approval/result state, up to 500 relative moves and outcomes, algorithm version, explicit-override flag, summary, and previous applied record link. The schema-1 envelope stores at most 250 records.

## Compatibility

v0.9.1 settings and catalog models retain their existing JSON names. New properties are optional. Existing accepted tags project into the v1 provenance model without rewriting catalog data merely by reading it.
