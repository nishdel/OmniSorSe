# OpenSorSe Roadmap

> This roadmap distinguishes completed releases from future ideas. It is not a commitment to implement every future capability.

## Current direction

OpenSorSe is a local-first, review-oriented desktop application for analyzing selected folders and organizing explicitly reviewed disposable/user-approved roots. The project is implemented in .NET 8, C#, Avalonia UI, and MVVM.

OpenSorSe 1.0 keeps scanning, extraction, page-aware OCR, indexing, duplicate review, diagrams, and AI suggestions non-mutating. Its only new source mutation is a deterministic, separately confirmed, root-confined restructuring plan. OCR Beta and Semantic Search Beta are local and independent of AI.

## Completed releases

### v0.1 — Read-only processing foundation

Complete.

- Folder scanning and recursive directory traversal.
- Read-only metadata extraction and SHA-256 hashing.
- Deterministic file classification and exact duplicate detection.
- Rule evaluation, planning, and conflict-resolution infrastructure.
- Dashboard, Scan workflow, Settings, Diagnostics, and a review-only Operation History foundation (no execution sessions or undo UI).
- Application orchestration, in-memory sessions, logging, error handling, and automated test coverage.

### v0.2 — Read-only results exploration

Complete.

- Immutable in-memory results snapshots.
- Results Explorer with text filtering, categorical filters, deterministic sorting, and bounded paging.
- Read-only result details.
- Exact SHA-256 duplicate-group review and theoretical reclaimable-space presentation.
- Safe group-to-results navigation and clear empty, loading, limitation, and error states.

## v0.3 — Usability improvements and workflow enhancements

Implemented. v0.3 retains the read-only workflow while adding optional local assistance only through validated, review-only proposals.

- Optional Ollama endpoint configuration, health check, installed-model discovery, model selection, timeout, cancellation, redacted diagnostics, and safe unavailable states.
- Validated rename, tag, category, destination, and bounded folder-structure previews using minimal metadata context.
- Local JSON decision history and optional approved-pattern reuse; no model training or fine-tuning.
- Application-owned in-memory tags and deterministic ranked search over filename, path, extension, deterministic category, and accepted tags.
- Results and Settings product-polish pass with match explanations, explicit safety text, and clearer status/disabled states.

The release does not enable filesystem mutation, persistent scan catalogs, content extraction, embedding-based semantic search, or automatic organization.

## v0.4 — Opt-in local catalog

Complete.

- User-controlled local JSON catalog for completed display-safe snapshots and accepted non-deterministic tags.
- Atomic versioned persistence in OpenSorSe application data, disabled by default.
- Ten-entry and 2,000-file-per-entry bounds; oversized snapshots are not silently truncated.
- Historical Catalog page for reopening a saved snapshot without accessing the filesystem.

## v0.5 — Catalog search and maintenance

Complete.

- Deterministic catalog-wide metadata search over saved filenames, paths, extensions, categories, and accepted tags.
- Stable ranked hit list with explanations and a 200-row presentation cap.
- Explicit selected-snapshot removal and two-step clear for application-owned catalog data only.
- No live filesystem refresh, content search, semantic search, or selected-user-file mutation.

## v0.6 — User-managed result tags

Complete.

- Add and remove normalized application-owned tags for a selected result without requiring an AI provider.
- Twelve accepted non-deterministic tags per file, immediate ranked-search/detail refresh, and protected deterministic extension tags.
- Existing opt-in catalog persistence for catalog-backed snapshots; other tags remain session-only.
- No embedded metadata edits, sidecars, bulk propagation, taxonomy database, or selected-user-file mutation.

## v0.7 — Saved catalog searches

Complete.

- Up to 25 named catalog query presets in a separate atomic, versioned local JSON file.
- Explicit rerun against the current deterministic catalog search, including v0.6 user-managed tags; hits are never persisted.
- Selected removal and two-step reset, including explicit recovery from malformed saved-query data.
- No scheduler, background search, persistent index, content search, semantic search, or selected-user-file mutation.

## v0.8 - Identifiable, scope-aware catalog snapshots

Complete.

- Backward-compatible catalog schema 2 with read-only schema-1 migration compatibility.
- Optional user-controlled snapshot names and up to 32 captured selected source roots.
- Explicit unnamed and legacy unknown-scope states; stored roots are never checked against the live filesystem.
- Set, replace, and clear-name workflow plus snapshot-name context in catalog search.
- No arbitrary notes, live verification, database migration, export, monitoring, or selected-user-file mutation.

## v0.9 - Historical snapshot comparison

Complete.

- Deterministic comparison of two explicit stored snapshots with added, removed, modified, and unchanged classifications.
- Stored metadata and accepted tag-set change detection, same/different/unknown source-scope status, and deterministic duplicate-path warnings.
- Cancellation, change-kind/path filtering, a 4,000-change service bound, and a 500-row presentation bound.
- Baseline/current opening reuses the existing historical Results workflow and never accesses a stored file path.
- No live monitoring, rename inference, content/hash comparison, reports/export, database, or file mutation.

## v0.9.1 - Optional AI and progressive feature visibility

Complete.

- Independent default-off **Enable AI features** and **Show advanced features** settings with centralized feature requirements for views, navigation, commands, and application services.
- Independent default-off rename and folder-structure capability switches; disabled capabilities cannot reach the provider even through direct internal calls.
- Deterministic, bounded, metadata-only prompts for one-file rename and selected-file logical hierarchy proposals.
- Strict JSON parsing and whole-response validation for identities, fields, counts, confidence, filenames, path components, hierarchy graphs, and duplicate suggestions.
- Review/edit/accept/reject decision workflow only; no AI proposal creates a folder or renames, moves, deletes, overwrites, or edits a file.
- Advanced classification for historical comparison, detailed diagnostics, operation-history internals, logging detail, and raw provider/request diagnostics while core workflows and essential Ollama setup remain regular.
- Ollama remains optional, externally managed, and isolated from all non-AI workflows.
- A corrective reliability pass normalizes Ollama endpoints, uses one 5-300 second request timeout, preserves exact model selection, reports typed request progress, and uses request-local item identities with exact-once folder validation.
- Diagnostics retains a bounded inspectable event buffer and an independently enabled, redacted, session-only AI request buffer.
- Contextual Help, a simplified Catalog Search hierarchy, scroll-stable Settings, reusable status feedback, and a responsive Duplicate View improve normal desktop usability.
- Duplicate View can explicitly open a bounded set of known current-scan files or containing folders for comparison through the operating system, without adding any OpenSorSe mutation action.

This refinement is not the v1.0 milestone.

## v1.0 - Integrated local understanding and structure history

Release candidate; automated validation complete where the environment permits and manual GUI verification pending.

- Results has a permanently visible filter toolbar and independently scrolling rows.
- Duplicate details use a responsive right-side drawer while groups remain visible.
- AI and Advanced switches are globally available from the shell.
- Defensive local metadata extraction supports filesystem, PDF, Open XML, and image fields with provenance.
- OCR Beta uses PdfPig plus built-in PDFtoImage/PDFium page rendering and a detected external Tesseract CLI for supported images and scanned/mixed PDFs; only pages with insufficient native text are rasterized.
- English/German Tesseract language support, explicit capability refresh, cache fingerprints, bounded temporary storage, and page-level provenance are implemented.
- Optional AI interpretation of bounded extracted text has a separate default-off capability and remains an unverified one-document review proposal.
- Provenance-aware tags can be accepted or rejected without modifying embedded file metadata.
- Semantic Search Beta builds a bounded local deterministic hybrid index and explains filename, tag, metadata, native-text, OCR, and similarity signals.
- Structure history stores bounded source, proposed, and applied snapshots, plus per-item outcomes.
- Only successful confirmed applies activate repeat protection; new files can receive incremental proposals, material changes are detected, and explicit override remains available.
- The advanced Structure history page provides current capture, filters, read-only diagrams, and accessible change summaries.
- Versioned new stores and safe defaults preserve v0.9.1 settings, catalogs, tags, saved searches, and AI decisions.

The release does not add plugins, broad localization, installers, cloud indexing, live monitoring, reports/export, autonomous AI file control, or generic rule execution.

## Future release ideas

The following are longer-term ideas, not current capabilities or committed release scope:

- Rich full-fidelity document/content readers beyond the bounded 1.0 extractors.
- Bundled Tesseract executables or OCR language/model packaging.
- Learned or external semantic embedding models and GPU acceleration.
- Database-backed scan catalogs, tags, and search indexes.
- Generic rule execution and undo integration beyond the narrow restructuring apply workflow.
- Plugin system.
- Live filesystem monitoring or scheduled comparisons.
- Exportable reports and rename inference.

Any future feature that could modify user files requires a separate safety design covering explicit authorization, live preflight, preview, failure handling, and recovery expectations.

## Release principles

- Preserve the current read-only safety boundary unless a dedicated release explicitly changes it.
- Keep local-first privacy and transparent user control central to every proposal.
- Treat the architecture documents for unimplemented subsystems as design intent, not evidence of a shipped feature.
- Validate build, tests, and relevant UI behavior before declaring a release complete.

## Related documentation

- [Release Status](RELEASE_STATUS.md)
- [Project Philosophy](project_philosophy.md)
- [System Overview](Architecture/00_System/00_Overview.md)
- [Technology Stack](Architecture/99_Appendix/Technology_Stack.md)
