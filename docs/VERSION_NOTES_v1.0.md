# OpenSorSe 1.0.0 Version Notes

OpenSorSe 1.0.0 is the first integrated local-understanding release candidate.

- The everyday navigation is now Home, Scan, Files, Duplicates, Saved scans, and Settings. Advanced tools are disclosed separately.
- The official OpenSorSe mark now identifies the window and roomier sidebar alongside “Open Sort and Search” and “Find clarity in your files.”
- The Files table/details divider supports pointer and keyboard resizing, protects both panes, and remembers its validated local ratio; file columns also resize from accessible header handles.
- Saved scan library, history search, and comparison share one Saved scans area.
- Meaning Search (Beta) opens from Files and clearly identifies its local index controls and match explanations.
- Files uses one search field, a filter drawer, and a right panel that appears only after a file is selected.
- A persistent status bar reports scan, Meaning Search, and File Assistant activity and offers cancellation when supported.
- The File Assistant has explicit Ollama/model readiness, connection retry, exact model switching, actual-model display, and recovery after failure or cancellation.
- File Assistant generation now uses compact single-task prompts designed for small local instruction models, exact Ollama JSON Schemas, temperature `0.0`, application-owned extension preservation, opaque folder identities, fail-closed evidence/relationship validation, and at most one related shape-repair attempt. Real-model compatibility is not claimed until the manual 2B/4B/7B-8B matrix is completed.
- Folder-structure AI requests accept at most 12 selected files; a larger selection is rejected in full before Ollama access with its exact count shown, so no source is silently omitted and no partial plan is presented.
- Advanced testers can opt into one live, non-modal diagnostics viewer for AI, OCR/text extraction, and scanning. It provides correlated bounded sessions, stage timing, seven shared tabs, filters, copy/export/clear actions, redacted defaults, and a separate unredacted-content warning switch. Duplicate, search/indexing, rules/organisation, file-operation, and performance categories are registered but honestly marked not yet instrumented.
- Home uses friendly, understandable metric tiles and the UI uses reusable light/dark semantic color resources.
- Results filtering remains visible while rows scroll.
- Duplicate details open in a responsive right drawer.
- AI and Advanced switches are persisted in Settings and continue to gate visible and executable features centrally.
- Bounded metadata extraction is local and read-only.
- OCR is Beta: PdfPig reads native PDF text by page, PDFtoImage/PDFium renders only insufficient pages, and an optional detected Tesseract CLI recognizes images or rendered pages.
- Tesseract language capability is checked explicitly for configured English (`eng`) and/or German (`deu`) data; Tesseract remains externally installed.
- Optional bounded AI interpretation of extracted document text has its own default-off gate and creates only an unverified review proposal.
- Semantic Search is Beta, local, deterministic, explainable, and independent of AI.
- Provenance-aware tags connect metadata, OCR, search, and organization.
- Structure history, repeat protection, and read-only diagrams retain organization context.
- A separately confirmed deterministic restructuring plan can move only reviewed root-confined files; it never uses AI output, overwrites, or deletes.

Index quality may evolve and indexes may need rebuilding after future upgrades. OCR, AI interpretation, and indexing never modify source files. The cache fingerprint includes OCR settings and engine/rasterizer versions so legacy records are safely reprocessed. GPU acceleration, a bundled Tesseract distribution, live Tesseract recognition, and cross-platform packaging are not claimed as verified by this Windows development environment.
