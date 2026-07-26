# OpenSorSe 1.0 System Goals

## Goals

1. Analyze selected folders safely with read-only traversal, metadata, hashes, classification, exact duplicates, progress, and cancellation.
2. Keep Results usable through fixed filters, bounded paging, independent scrolling, explanations, provenance tags, and a responsive duplicate drawer.
3. Add local understanding through defensive metadata extraction, optional OCR Beta, and independently enabled local Semantic Search Beta.
4. Preserve user control: AI remains optional and suggestion-only; restructuring remains deterministic, preview-first, separately confirmed, root-confined, bounded, and auditable.
5. Avoid repeated organization by activating protection only after a successful apply, while allowing incremental new-file proposals, material-change detection, and explicit override.
6. Keep data local, stores bounded/atomic/versioned, logs privacy-aware, and v0.9.1 settings/catalog/tags/searches backward compatible.
7. Maintain responsive asynchronous MVVM workflows with cancellation, bounded memory, lazy/bounded presentation, and failure isolation.

## Non-goals

OpenSorSe 1.0 does not provide:

- Autonomous or AI-driven filesystem control.
- Duplicate deletion or automatic cleanup.
- Generic rule execution/undo from the Desktop.
- Bundled Tesseract executables/language data, GPU acceleration, or externally learned embeddings.
- Plugins, broad localization, packaging overhaul, live monitoring, cloud indexing, or report export.
- Claims of cross-platform packaging validation beyond portable architecture and current Windows build/test validation.

## Safety invariant

No operation may infer mutation authority from scanning, indexing, an AI response, a preview, or a history record. Only the explicit exact-plan restructuring confirmation grants bounded authority for that one operation.
